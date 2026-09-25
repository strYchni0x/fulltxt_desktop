using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fulltxt.Core.Cloud.OAuth;

public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Anbieter-Konto-ID (Dropbox liefert sie mit der Token-Antwort).</summary>
    public string? AccountId { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static OAuthTokens FromJson(string json) =>
        JsonSerializer.Deserialize<OAuthTokens>(json) ?? throw new InvalidDataException("Token-Daten ungültig.");
}

/// <summary>Öffentlicher OAuth-Client (PKCE, kein Client-Secret).</summary>
public sealed record OAuthClientConfig(
    string ClientId,
    string AuthorizeEndpoint,
    string TokenEndpoint,
    string Scope,
    IReadOnlyDictionary<string, string>? ExtraAuthorizeParameters = null,
    bool SendScopeOnRefresh = false);

public static class OAuthClients
{
    public static readonly OAuthClientConfig OneDrive = new(
        ClientId: "dc50994a-bc9d-438e-a071-044c75fb4fac",
        AuthorizeEndpoint: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint: "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        Scope: "Files.Read offline_access",
        SendScopeOnRefresh: true);

    public static readonly OAuthClientConfig Dropbox = new(
        ClientId: "1moz3vfpd93mwsq",
        AuthorizeEndpoint: "https://www.dropbox.com/oauth2/authorize",
        TokenEndpoint: "https://api.dropboxapi.com/oauth2/token",
        Scope: "files.metadata.read files.content.read account_info.read",
        ExtraAuthorizeParameters: new Dictionary<string, string> { ["token_access_type"] = "offline" });
}

public sealed class OAuthException(string message, bool reauthRequired = false) : CloudException(message, isAuthError: true)
{
    /// <summary>True, wenn der Refresh-Token ungültig ist und das Konto neu verbunden werden muss.</summary>
    public bool ReauthRequired { get; } = reauthRequired;
}

internal static class OAuthTokenResponse
{
    /// <summary>Wertet eine Token-Antwort aus. Fehlt ein neuer Refresh-Token, bleibt der bisherige erhalten.</summary>
    public static OAuthTokens Parse(string json, string? previousRefreshToken)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access)) throw new OAuthException("Die Token-Antwort enthielt kein Zugriffstoken.");

        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return new OAuthTokens
        {
            AccessToken = access,
            RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? previousRefreshToken : previousRefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn),
            AccountId = root.TryGetProperty("account_id", out var id) ? id.GetString() : null,
        };
    }

    public static string DescribeError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return description is { Length: > 0 } ? description.Split('\n')[0] : error ?? "Unbekannter Fehler";
        }
        catch (JsonException)
        {
            return "Unbekannter Fehler";
        }
    }
}
