using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fulltxt.Core.Cloud.OAuth;

namespace Fulltxt.Core.Cloud;

/// <summary>Dropbox über die HTTP-API v2: rekursive Auflistung mit Cursor (danach nur noch Änderungen), nur Lesezugriff.</summary>
public sealed class DropboxConnector(
    HttpClient http,
    OAuthTokenManager tokens,
    string apiBase = DropboxConnector.DefaultApiBase,
    string contentBase = DropboxConnector.DefaultContentBase) : ICloudConnector
{
    public const string DefaultApiBase = "https://api.dropboxapi.com/2";
    public const string DefaultContentBase = "https://content.dropboxapi.com/2";

    public async Task TestConnectionAsync(CancellationToken ct = default) => await GetAccountNameAsync(ct).ConfigureAwait(false);

    public async Task<string> GetAccountNameAsync(CancellationToken ct = default)
    {
        // Endpunkte ohne Argumente erwarten einen literalen JSON-"null"-Body.
        using var doc = await PostJsonAsync($"{apiBase}/users/get_current_account", "null", ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("email", out var email) && email.GetString() is { Length: > 0 } mail) return mail;
        if (root.TryGetProperty("name", out var name) && name.TryGetProperty("display_name", out var display)
            && display.GetString() is { Length: > 0 } text) return text;
        return "Dropbox";
    }

    public async Task<CloudChanges> GetChangesAsync(string? cursor, CancellationToken ct = default)
    {
        try
        {
            return await CollectAsync(cursor, ct).ConfigureAwait(false);
        }
        catch (CloudException ex) when (cursor is not null && ex.Message.Contains("reset"))
        {
            // Cursor ungültig geworden: komplett neu auflisten.
            return await CollectAsync(null, ct).ConfigureAwait(false);
        }
    }

    private async Task<CloudChanges> CollectAsync(string? cursor, CancellationToken ct)
    {
        var changed = new List<CloudItem>();
        var deletedPaths = new List<string>();
        var full = cursor is null;

        var next = cursor is null
            ? await PostJsonAsync($"{apiBase}/files/list_folder",
                """{"path":"","recursive":true,"include_deleted":false,"limit":2000}""", ct).ConfigureAwait(false)
            : await PostJsonAsync($"{apiBase}/files/list_folder/continue",
                JsonSerializer.Serialize(new { cursor }), ct).ConfigureAwait(false);

        string? lastCursor;
        while (true)
        {
            using (next)
            {
                var root = next.RootElement;
                foreach (var entry in root.GetProperty("entries").EnumerateArray())
                {
                    var tag = entry.TryGetProperty(".tag", out var t) ? t.GetString() : null;
                    switch (tag)
                    {
                        case "file":
                            if (entry.TryGetProperty("is_downloadable", out var dl) && dl.ValueKind == JsonValueKind.False) break;
                            if (ToItem(entry) is { } item) changed.Add(item);
                            break;
                        case "deleted":
                            if (entry.TryGetProperty("path_lower", out var lower) && lower.GetString() is { } path) deletedPaths.Add(path);
                            break;
                    }
                }

                lastCursor = root.GetProperty("cursor").GetString();
                if (!(root.TryGetProperty("has_more", out var more) && more.GetBoolean())) break;
            }

            next = await PostJsonAsync($"{apiBase}/files/list_folder/continue",
                JsonSerializer.Serialize(new { cursor = lastCursor }), ct).ConfigureAwait(false);
        }

        return new CloudChanges(changed, [], deletedPaths, IsFullListing: full, NextCursor: lastCursor);
    }

    public async Task<Stream> OpenReadAsync(CloudFileRef file, CancellationToken ct = default)
    {
        var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        var apiArg = JsonSerializer.Serialize(new { path = file.RemoteId });
        var response = await HttpRetry.SendAsync(http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{contentBase}/files/download");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Das Argument steht im Header; nicht-ASCII-Zeichen müssten escaped werden (IDs sind reines ASCII).
            request.Headers.Add("Dropbox-API-Arg", apiArg);
            return request;
        }, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        try
        {
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new ResponseOwningStream(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<JsonDocument> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        using var response = await HttpRetry.SendAsync(http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private static async Task<CloudException> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var code = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CloudException("Anmeldung bei Dropbox fehlgeschlagen. Bitte das Konto neu verbinden.", isAuthError: true);
        }

        var summary = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            summary = doc.RootElement.TryGetProperty("error_summary", out var s) ? s.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            // Kein JSON - nur den Statuscode melden.
        }
        return new CloudException($"Dropbox-Anfrage fehlgeschlagen (HTTP {code}){(summary.Length > 0 ? ": " + summary : "")}");
    }

    private static CloudItem? ToItem(JsonElement entry)
    {
        var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var name = entry.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        if (id is null || name is null) return null;

        var path = entry.TryGetProperty("path_display", out var p) ? p.GetString() ?? "/" + name : "/" + name;
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        return new CloudItem(
            RemoteId: id,
            Path: path,
            Name: name,
            Size: entry.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
            ModifiedUtc: entry.TryGetProperty("server_modified", out var mod) && mod.TryGetDateTime(out var when)
                ? when.ToUniversalTime()
                : DateTime.UnixEpoch,
            ETag: entry.TryGetProperty("rev", out var rev) ? rev.GetString() : null,
            WebUrl: "https://www.dropbox.com/preview" + encoded);
    }

    public void Dispose()
    {
        // HttpClient gehört dem Aufrufer (Factory) - hier nichts freizugeben.
    }
}
