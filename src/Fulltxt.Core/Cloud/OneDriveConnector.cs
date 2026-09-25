using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Fulltxt.Core.Cloud.OAuth;

namespace Fulltxt.Core.Cloud;

/// <summary>OneDrive über Microsoft Graph: Delta-Abfrage (nur Änderungen seit dem letzten Lauf), nur Lesezugriff.
/// Dateiinhalte werden nicht gespeichert - nur gezielt einzeln gelesen.</summary>
public sealed class OneDriveConnector(
    HttpClient http,
    OAuthTokenManager tokens,
    string graphBase = OneDriveConnector.DefaultGraphBase) : ICloudConnector
{
    public const string DefaultGraphBase = "https://graph.microsoft.com/v1.0";

    private const string DriveRootPrefix = "/drive/root:";
    private const string Select = "id,name,size,lastModifiedDateTime,file,folder,parentReference,eTag,deleted,webUrl";

    public async Task TestConnectionAsync(CancellationToken ct = default) => await GetAccountNameAsync(ct).ConfigureAwait(false);

    /// <summary>Name des angemeldeten Kontos (zur Anzeige in der Quellenliste).</summary>
    public async Task<string> GetAccountNameAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"{graphBase}/me/drive?$select=owner", ct).ConfigureAwait(false);
        var user = doc.RootElement.TryGetProperty("owner", out var owner) && owner.TryGetProperty("user", out var u) ? u : default;
        if (user.ValueKind == JsonValueKind.Object)
        {
            if (user.TryGetProperty("email", out var email) && email.GetString() is { Length: > 0 } mail) return mail;
            if (user.TryGetProperty("displayName", out var name) && name.GetString() is { Length: > 0 } display) return display;
        }
        return "OneDrive";
    }

    public async Task<CloudChanges> GetChangesAsync(string? cursor, CancellationToken ct = default)
    {
        try
        {
            return await CollectAsync(cursor, ct).ConfigureAwait(false);
        }
        catch (CloudException ex) when (cursor is not null && ex.Message.Contains("410"))
        {
            // Delta-Token abgelaufen (resyncRequired): komplett neu auflisten.
            return await CollectAsync(null, ct).ConfigureAwait(false);
        }
    }

    private async Task<CloudChanges> CollectAsync(string? cursor, CancellationToken ct)
    {
        var url = cursor ?? $"{graphBase}/me/drive/root/delta?$select={Select}";
        var changed = new List<CloudItem>();
        var deletedIds = new List<string>();
        var folderPaths = new Dictionary<string, string>();
        string? deltaLink = null;

        while (url is not null)
        {
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (id is null) continue;

                    if (item.TryGetProperty("deleted", out _))
                    {
                        deletedIds.Add(id);
                        continue;
                    }

                    var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    var parentPath = ParentPath(item);
                    var parentId = item.TryGetProperty("parentReference", out var pr) && pr.TryGetProperty("id", out var pid) ? pid.GetString() : null;

                    if (item.TryGetProperty("folder", out _))
                    {
                        // Ordnerpfade merken: manche Delta-Antworten liefern für Dateien keinen parentReference.path.
                        if (parentPath is not null && name.Length > 0) folderPaths[id] = Join(parentPath, name);
                        continue;
                    }
                    if (!item.TryGetProperty("file", out _)) continue;

                    parentPath ??= parentId is not null && folderPaths.TryGetValue(parentId, out var known) ? known : "/";
                    changed.Add(new CloudItem(
                        RemoteId: id,
                        Path: Join(parentPath, name),
                        Name: name,
                        Size: item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                        ModifiedUtc: item.TryGetProperty("lastModifiedDateTime", out var mod) && mod.TryGetDateTime(out var when)
                            ? when.ToUniversalTime()
                            : DateTime.UnixEpoch,
                        ETag: item.TryGetProperty("eTag", out var etag) ? etag.GetString() : null,
                        WebUrl: item.TryGetProperty("webUrl", out var web) ? web.GetString() : null));
                }
            }

            deltaLink = root.TryGetProperty("@odata.deltaLink", out var dl) ? dl.GetString() : deltaLink;
            url = root.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }

        return new CloudChanges(changed, deletedIds, [], IsFullListing: cursor is null, NextCursor: deltaLink);
    }

    public async Task<Stream> OpenReadAsync(CloudFileRef file, CancellationToken ct = default)
    {
        var url = $"{graphBase}/me/drive/items/{Uri.EscapeDataString(file.RemoteId)}/content";
        var response = await SendAuthorizedAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        // Graph antwortet mit einer Weiterleitung auf eine vorab autorisierte Download-URL. Diese darf NICHT
        // zusätzlich unseren Bearer-Header bekommen, deshalb wird sie ohne Anmeldedaten abgerufen.
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
        {
            response.Dispose();
            var target = location.IsAbsoluteUri ? location : new Uri(new Uri(url), location);
            response = await HttpRetry.SendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, target), ct,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        }

        try
        {
            Ensure(response);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new ResponseOwningStream(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await SendAuthorizedAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        Ensure(response);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(string url, HttpCompletionOption completion, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        var response = await HttpRetry.SendAsync(http, () => BearerGet(url, token), ct, completion).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token vorzeitig ungültig geworden: einmal erneuern und wiederholen.
            response.Dispose();
            token = await tokens.ForceRefreshAsync(ct).ConfigureAwait(false);
            response = await HttpRetry.SendAsync(http, () => BearerGet(url, token), ct, completion).ConfigureAwait(false);
        }
        return response;
    }

    private static HttpRequestMessage BearerGet(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static void Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var code = (int)response.StatusCode;
        throw code == 401
            ? new CloudException("Anmeldung bei OneDrive fehlgeschlagen. Bitte das Konto neu verbinden.", isAuthError: true)
            : new CloudException($"OneDrive-Anfrage fehlgeschlagen (HTTP {code}).");
    }

    /// <summary>parentReference.path ("/drive/root:/Ordner%20A") -> "/Ordner A". Null, wenn nicht geliefert.</summary>
    private static string? ParentPath(JsonElement item)
    {
        if (!item.TryGetProperty("parentReference", out var parent) || !parent.TryGetProperty("path", out var pathEl)) return null;
        var path = pathEl.GetString();
        if (path is null) return null;

        var index = path.IndexOf(DriveRootPrefix, StringComparison.OrdinalIgnoreCase);
        var relative = index >= 0 ? path[(index + DriveRootPrefix.Length)..] : path;
        // Graph liefert den Pfad prozentkodiert (z.B. B%C3%BCro).
        var decoded = Uri.UnescapeDataString(relative);
        return decoded.Length == 0 ? "/" : decoded;
    }

    private static string Join(string folder, string name) => (folder.TrimEnd('/') + "/" + name);

    public void Dispose()
    {
        // HttpClient gehört dem Aufrufer (Factory) - hier nichts freizugeben.
    }
}
