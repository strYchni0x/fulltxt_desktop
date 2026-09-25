using System.Net;

namespace Fulltxt.Core.Cloud.OAuth;

/// <summary>Liefert gültige Zugriffstoken und erneuert sie bei Ablauf über den Refresh-Token.
/// Erneuerte Tokens werden über <c>onTokensChanged</c> sofort (verschlüsselt) persistiert.</summary>
public sealed class OAuthTokenManager(
    OAuthClientConfig config,
    HttpClient http,
    OAuthTokens tokens,
    Action<OAuthTokens>? onTokensChanged = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private OAuthTokens current = tokens;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(current.AccessToken) && current.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(60))
            {
                return current.AccessToken;
            }
            await RefreshAsync(ct).ConfigureAwait(false);
            return current.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Erzwingt eine Erneuerung, z.B. nachdem der Server das Token mit 401 abgelehnt hat.</summary>
    public async Task<string> ForceRefreshAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RefreshAsync(ct).ConfigureAwait(false);
            return current.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            throw new OAuthException("Die Anmeldung ist abgelaufen. Bitte das Konto neu verbinden.", reauthRequired: true);
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
            ["client_id"] = config.ClientId,
        };
        if (config.SendScopeOnRefresh) form["scope"] = config.Scope;

        using var response = await http.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var reauth = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
            throw new OAuthException(
                reauth
                    ? "Die Anmeldung ist abgelaufen oder wurde widerrufen. Bitte das Konto neu verbinden."
                    : $"Token-Erneuerung fehlgeschlagen: {OAuthTokenResponse.DescribeError(body)}",
                reauthRequired: reauth);
        }

        var refreshed = OAuthTokenResponse.Parse(body, current.RefreshToken);
        refreshed.AccountId ??= current.AccountId;
        current = refreshed;
        onTokensChanged?.Invoke(current);
    }
}
