using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Fulltxt.Core.Cloud.OAuth;

/// <summary>OAuth-2.0-Anmeldung für Desktop-Apps: Autorisierungscode + PKCE, Umleitung an einen kurzlebigen
/// lokalen Listener (127.0.0.1). Das Passwort wird nur beim Anbieter im Browser eingegeben - diese App sieht es nie.</summary>
public static class LoopbackOAuthFlow
{
    /// <summary>Fester Port, weil z.B. Dropbox die Umleitungs-URI exakt (inkl. Port) registriert haben will.</summary>
    public const int DefaultPort = 53682;

    public static string RedirectUri(int port = DefaultPort) => $"http://localhost:{port}/";

    public static async Task<OAuthTokens> AuthorizeAsync(
        OAuthClientConfig config,
        HttpClient http,
        Func<Uri, Task> openBrowser,
        CancellationToken ct = default,
        TimeSpan? timeout = null,
        int port = DefaultPort)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var redirectUri = RedirectUri(port);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = config.ClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri;
        query["scope"] = config.Scope;
        query["state"] = state;
        query["code_challenge"] = challenge;
        query["code_challenge_method"] = "S256";
        if (config.ExtraAuthorizeParameters is not null)
        {
            foreach (var (key, value) in config.ExtraAuthorizeParameters) query[key] = value;
        }
        var authorizeUri = new Uri($"{config.AuthorizeEndpoint}?{query}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new OAuthException($"Der lokale Anmelde-Port {port} ist belegt oder gesperrt: {ex.Message}");
        }

        // Erst auf die Umleitung warten, dann den Browser öffnen - so geht keine Antwort verloren,
        // auch wenn der Browser (oder ein Test) sofort zurückspringt.
        var codeTask = WaitForCodeAsync(listener, state, timeout ?? TimeSpan.FromMinutes(3), ct);
        try
        {
            await openBrowser(authorizeUri).ConfigureAwait(false);
        }
        catch
        {
            listener.Close();
            try { await codeTask.ConfigureAwait(false); } catch (Exception) { /* Folgefehler des Abbruchs ignorieren */ }
            throw;
        }

        var code = await codeTask.ConfigureAwait(false);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = config.ClientId,
            ["code_verifier"] = verifier,
        };
        using var response = await http.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException($"Anmeldung fehlgeschlagen: {OAuthTokenResponse.DescribeError(body)}");
        }
        return OAuthTokenResponse.Parse(body, previousRefreshToken: null);
    }

    private static async Task<string> WaitForCodeAsync(HttpListener listener, string expectedState, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        // GetContextAsync ist nicht abbrechbar - beim Abbruch den Listener schließen, das beendet die Wartezeit.
        using var registration = timeoutCts.Token.Register(listener.Close);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (timeoutCts.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                throw new OAuthException("Zeitüberschreitung: Die Anmeldung im Browser wurde nicht abgeschlossen.");
            }

            var parameters = context.Request.QueryString;
            var error = parameters["error"];
            var code = parameters["code"];

            if (error is null && code is null)
            {
                // z.B. favicon.ico - ignorieren und weiter warten.
                await RespondAsync(context, 404, "Nicht gefunden").ConfigureAwait(false);
                continue;
            }

            if (error is not null)
            {
                await RespondAsync(context, 400, Page("Anmeldung abgebrochen", "Du kannst dieses Fenster schließen und zu FullTXT zurückkehren.")).ConfigureAwait(false);
                throw new OAuthException($"Anmeldung abgebrochen: {parameters["error_description"] ?? error}");
            }

            if (!string.Equals(parameters["state"], expectedState, StringComparison.Ordinal))
            {
                await RespondAsync(context, 400, Page("Ungültige Anfrage", "Die Anmeldung wurde aus Sicherheitsgründen abgelehnt.")).ConfigureAwait(false);
                throw new OAuthException("Die Anmeldeantwort passt nicht zur Anfrage (state). Vorgang abgebrochen.");
            }

            await RespondAsync(context, 200, Page("Anmeldung erfolgreich", "Du kannst dieses Fenster schließen und zu FullTXT zurückkehren.")).ConfigureAwait(false);
            return code!;
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, int status, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static string Page(string title, string text) =>
        $"<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><title>FullTXT</title>" +
        "<body style=\"margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;" +
        "background:#0c0c0c;color:#f0f0f0;font-family:Segoe UI,sans-serif\">" +
        "<div style=\"text-align:center;padding:32px\">" +
        $"<h1 style=\"color:#22c55e;margin:0 0 12px\">{WebUtility.HtmlEncode(title)}</h1>" +
        $"<p style=\"color:#888\">{WebUtility.HtmlEncode(text)}</p></div></body></html>";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
