using Fulltxt.Core.Cloud.OAuth;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Cloud;

/// <summary>Erzeugt aus einer gespeicherten Quelle den passenden Connector. Zugangsdaten werden nur hier,
/// nur im Arbeitsspeicher und nur für die Dauer der Nutzung entschlüsselt.</summary>
public sealed class CloudConnectorFactory
{
    // Ein gemeinsamer Client für OAuth-Anbieter: Weiterleitungen werden dort bewusst manuell behandelt
    // (vorab autorisierte Download-URLs dürfen keinen Bearer-Header bekommen).
    private static readonly Lazy<HttpClient> OAuthHttp = new(() =>
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) });

    private static readonly Lazy<HttpClient> TokenHttp = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

    public static HttpClient SharedTokenClient => TokenHttp.Value;

    /// <param name="persistCredential">Wird mit neuen, bereits verschlüsselten Zugangsdaten aufgerufen
    /// (z.B. wenn ein OAuth-Refresh-Token rotiert wurde).</param>
    public ICloudConnector Create(FileSource source, Action<byte[]>? persistCredential = null)
    {
        if (source.ProtectedCredential is null)
        {
            throw new CloudException("Für dieses Konto sind keine Zugangsdaten gespeichert.", isAuthError: true);
        }

        var secret = SecretProtector.UnprotectString(source.ProtectedCredential);
        return source.Type switch
        {
            SourceType.OneDrive => new OneDriveConnector(OAuthHttp.Value, Tokens(OAuthClients.OneDrive, secret, persistCredential)),
            SourceType.Dropbox => new DropboxConnector(OAuthHttp.Value, Tokens(OAuthClients.Dropbox, secret, persistCredential)),
            SourceType.Nextcloud or SourceType.OwnCloud or SourceType.MagentaCloud => new WebDavConnector(new WebDavEndpoint(
                ServerUrl: Require(source.ServerUrl, "Server-Adresse"),
                DavRootPath: $"/remote.php/dav/files/{Uri.EscapeDataString(Require(source.Username, "Benutzername"))}/",
                StartFolder: source.RootPath,
                Username: source.Username!,
                Password: secret,
                FileIdWebLinks: true)),
            SourceType.StratoHidrive => new WebDavConnector(new WebDavEndpoint(
                ServerUrl: "https://webdav.hidrive.strato.com",
                DavRootPath: $"/users/{Uri.EscapeDataString(Require(source.Username, "Benutzername"))}/",
                StartFolder: source.RootPath,
                Username: source.Username!,
                Password: secret,
                FileIdWebLinks: false)),
            SourceType.Yandex => new WebDavConnector(new WebDavEndpoint(
                ServerUrl: "https://webdav.yandex.com",
                DavRootPath: "/",
                StartFolder: source.RootPath,
                Username: Require(source.Username, "Benutzername"),
                Password: secret,
                FileIdWebLinks: false)),
            _ => throw new NotSupportedException($"Kein Cloud-Connector für {source.Type}."),
        };
    }

    /// <summary>Connector für ein neues, noch nicht gespeichertes Passwort-Konto (Verbindungstest im Dialog).</summary>
    public static ICloudConnector CreateForNewPasswordAccount(
        SourceType type, string serverUrl, string username, string password, string startFolder = "/")
    {
        var probe = new FileSource
        {
            Type = type,
            DisplayName = "",
            RootPath = startFolder,
            ServerUrl = serverUrl,
            Username = username,
            ProtectedCredential = SecretProtector.ProtectString(password),
        };
        return new CloudConnectorFactory().Create(probe);
    }

    private static OAuthTokenManager Tokens(OAuthClientConfig config, string tokenJson, Action<byte[]>? persist) =>
        new(config, TokenHttp.Value, OAuthTokens.FromJson(tokenJson),
            persist is null ? null : tokens => persist(SecretProtector.ProtectString(tokens.ToJson())));

    private static string Require(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new CloudException($"{what} fehlt.") : value;

    /// <summary>Meldet den Benutzer per Browser bei OneDrive/Dropbox an und liefert Konto-Name + verschlüsselte Token.</summary>
    public static async Task<(string AccountName, byte[] ProtectedTokens)> SignInAsync(
        SourceType type, Func<Uri, Task> openBrowser, CancellationToken ct = default)
    {
        var config = type switch
        {
            SourceType.OneDrive => OAuthClients.OneDrive,
            SourceType.Dropbox => OAuthClients.Dropbox,
            _ => throw new NotSupportedException($"{type} nutzt keine OAuth-Anmeldung."),
        };

        var tokens = await LoopbackOAuthFlow.AuthorizeAsync(config, TokenHttp.Value, openBrowser, ct).ConfigureAwait(false);
        var manager = new OAuthTokenManager(config, TokenHttp.Value, tokens);
        string name = type == SourceType.OneDrive
            ? await new OneDriveConnector(OAuthHttp.Value, manager).GetAccountNameAsync(ct).ConfigureAwait(false)
            : await new DropboxConnector(OAuthHttp.Value, manager).GetAccountNameAsync(ct).ConfigureAwait(false);

        return (name, SecretProtector.ProtectString(tokens.ToJson()));
    }
}
