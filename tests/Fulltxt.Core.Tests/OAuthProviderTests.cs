using System.Net;
using System.Text;
using System.Text.Json;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Cloud.OAuth;

namespace Fulltxt.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body, string? Auth)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class OAuthProviderTests
{
    private static OAuthTokenManager FreshTokens(HttpClient http, OAuthClientConfig config) =>
        new(config, http, new OAuthTokens { AccessToken = "access-1", RefreshToken = "refresh-1", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });

    [Fact]
    public async Task OneDrive_Delta_ReturnsFilesDeletionsAndCursor_ResolvingPathsFromFolders()
    {
        var handler = new FakeHandler((request, _) =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/root/delta") && !url.Contains("page2"))
            {
                return FakeHandler.Json("""
                {"value":[
                  {"id":"root","name":"root","folder":{},"parentReference":{}},
                  {"id":"f1","name":"Büro","folder":{},"parentReference":{"path":"/drive/root:"}},
                  {"id":"a","name":"Plan.pdf","size":10,"eTag":"\"tag-a\"","lastModifiedDateTime":"2026-05-01T10:00:00Z",
                   "file":{"mimeType":"application/pdf"},"parentReference":{"path":"/drive/root:/B%C3%BCro","id":"f1"},"webUrl":"https://1drv.ms/a"},
                  {"id":"b","name":"ohne-pfad.txt","size":5,"eTag":"tag-b","lastModifiedDateTime":"2026-05-02T10:00:00Z",
                   "file":{},"parentReference":{"id":"f1"},"webUrl":"https://1drv.ms/b"}
                ],"@odata.nextLink":"https://graph.test/page2"}
                """);
            }
            return FakeHandler.Json("""
            {"value":[{"id":"gone","deleted":{}}],"@odata.deltaLink":"https://graph.test/delta?token=NEW"}
            """);
        });
        using var http = new HttpClient(handler);
        using var connector = new OneDriveConnector(http, FreshTokens(http, OAuthClients.OneDrive), "https://graph.test");

        var changes = await connector.GetChangesAsync(null);

        Assert.True(changes.IsFullListing);
        Assert.Equal("https://graph.test/delta?token=NEW", changes.NextCursor);
        Assert.Equal(["gone"], changes.DeletedRemoteIds);
        var plan = changes.Changed.Single(i => i.RemoteId == "a");
        Assert.Equal("/Büro/Plan.pdf", plan.Path);
        Assert.Equal("https://1drv.ms/a", plan.WebUrl);
        Assert.Equal("/Büro/ohne-pfad.txt", changes.Changed.Single(i => i.RemoteId == "b").Path); // Pfad über Ordner-ID aufgelöst
        Assert.All(handler.Calls, c => Assert.Equal("Bearer access-1", c.Auth));
    }

    [Fact]
    public async Task OneDrive_UsesStoredCursor_AndFallsBackToFullListing_WhenGone()
    {
        var handler = new FakeHandler((request, _) =>
        {
            var url = request.RequestUri!.ToString();
            return url.Contains("old-cursor")
                ? FakeHandler.Json("{}", HttpStatusCode.Gone)
                : FakeHandler.Json("""{"value":[],"@odata.deltaLink":"https://graph.test/delta?token=FRESH"}""");
        });
        using var http = new HttpClient(handler);
        using var connector = new OneDriveConnector(http, FreshTokens(http, OAuthClients.OneDrive), "https://graph.test");

        var changes = await connector.GetChangesAsync("https://graph.test/delta?token=old-cursor");

        Assert.True(changes.IsFullListing);
        Assert.Equal("https://graph.test/delta?token=FRESH", changes.NextCursor);
    }

    [Fact]
    public async Task OneDrive_Download_FollowsRedirectWithoutBearerHeader()
    {
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "graph.test")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://cdn.test/file123");
                return redirect;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Dateiinhalt") };
        });
        using var http = new HttpClient(handler);
        using var connector = new OneDriveConnector(http, FreshTokens(http, OAuthClients.OneDrive), "https://graph.test");

        await using var stream = await connector.OpenReadAsync(new CloudFileRef("item-1", "/x.txt", "x.txt"));
        using var reader = new StreamReader(stream);

        Assert.Equal("Dateiinhalt", await reader.ReadToEndAsync());
        Assert.Contains(handler.Calls, c => c.Url == "https://graph.test/me/drive/items/item-1/content" && c.Auth == "Bearer access-1");
        Assert.Contains(handler.Calls, c => c.Url == "https://cdn.test/file123" && c.Auth is null);
    }

    [Fact]
    public async Task Dropbox_ListsPagedFiles_AndReportsDeletionsWithPathLower()
    {
        var handler = new FakeHandler((request, body) =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/files/list_folder"))
            {
                return FakeHandler.Json("""
                {"entries":[
                  {".tag":"folder","name":"Docs","path_display":"/Docs"},
                  {".tag":"file","id":"id:1","name":"Bericht Ü.pdf","path_display":"/Docs/Bericht Ü.pdf","size":12,
                   "server_modified":"2026-06-01T08:00:00Z","rev":"rev1","is_downloadable":true}
                ],"cursor":"c1","has_more":true}
                """);
            }
            // list_folder/continue
            return body.Contains("\"c1\"")
                ? FakeHandler.Json("""
                {"entries":[
                  {".tag":"file","id":"id:2","name":"a.txt","path_display":"/a.txt","size":1,"server_modified":"2026-06-02T08:00:00Z","rev":"rev2"},
                  {".tag":"file","id":"id:3","name":"nodl.txt","path_display":"/nodl.txt","size":1,"is_downloadable":false,"rev":"r"},
                  {".tag":"deleted","name":"alt.txt","path_lower":"/docs/alt.txt"}
                ],"cursor":"c2","has_more":false}
                """)
                : FakeHandler.Json("""{"entries":[],"cursor":"c3","has_more":false}""");
        });
        using var http = new HttpClient(handler);
        using var connector = new DropboxConnector(http, FreshTokens(http, OAuthClients.Dropbox), "https://api.test/2", "https://content.test/2");

        var full = await connector.GetChangesAsync(null);

        Assert.True(full.IsFullListing);
        Assert.Equal("c2", full.NextCursor);
        Assert.Equal(["id:1", "id:2"], full.Changed.Select(i => i.RemoteId)); // nicht herunterladbare Datei übersprungen
        Assert.Equal("/Docs/Bericht Ü.pdf", full.Changed[0].Path);
        Assert.Equal("https://www.dropbox.com/preview/Docs/Bericht%20%C3%9C.pdf", full.Changed[0].WebUrl);
        Assert.Equal(["/docs/alt.txt"], full.DeletedPaths);

        var delta = await connector.GetChangesAsync("c2");
        Assert.False(delta.IsFullListing);
        Assert.Equal("c3", delta.NextCursor);
    }

    [Fact]
    public async Task Dropbox_Download_SendsIdInApiArgHeader()
    {
        string? apiArg = null;
        var handler = new FakeHandler((request, _) =>
        {
            apiArg = request.Headers.GetValues("Dropbox-API-Arg").Single();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("PDF-Bytes") };
        });
        using var http = new HttpClient(handler);
        using var connector = new DropboxConnector(http, FreshTokens(http, OAuthClients.Dropbox), "https://api.test/2", "https://content.test/2");

        await using var stream = await connector.OpenReadAsync(new CloudFileRef("id:42", "/x.pdf", "x.pdf"));

        Assert.Equal("""{"path":"id:42"}""", apiArg);
    }

    [Fact]
    public async Task TokenManager_RefreshesExpiredToken_PersistsNewTokens_AndKeepsRefreshToken()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{"access_token":"access-2","expires_in":3600}"""));
        using var http = new HttpClient(handler);
        OAuthTokens? saved = null;
        var manager = new OAuthTokenManager(OAuthClients.OneDrive, http,
            new OAuthTokens { AccessToken = "old", RefreshToken = "refresh-1", ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-5) },
            tokens => saved = tokens);

        var token = await manager.GetAccessTokenAsync();

        Assert.Equal("access-2", token);
        Assert.Equal("refresh-1", saved!.RefreshToken); // kein neuer Refresh-Token geliefert -> alter bleibt
        var call = Assert.Single(handler.Calls);
        Assert.Contains("grant_type=refresh_token", call.Body);
        Assert.Contains("scope=Files.Read", call.Body); // Microsoft verlangt den Scope auch beim Erneuern
    }

    [Fact]
    public async Task TokenManager_InvalidGrant_RequiresReauth()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        var manager = new OAuthTokenManager(OAuthClients.Dropbox, http,
            new OAuthTokens { AccessToken = "", RefreshToken = "widerrufen", ExpiresAtUtc = DateTime.MinValue });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => manager.GetAccessTokenAsync());

        Assert.True(ex.ReauthRequired);
        Assert.True(ex.IsAuthError);
    }

    [Fact]
    public async Task LoopbackFlow_ExchangesCodeWithPkce_AndSendsVerifierMatchingChallenge()
    {
        const int port = 53697;
        Uri? authorizeUri = null;
        var handler = new FakeHandler((_, _) =>
            FakeHandler.Json("""{"access_token":"tok","refresh_token":"ref","expires_in":1800,"account_id":"dbid:1"}"""));
        using var http = new HttpClient(handler);

        var tokens = await LoopbackOAuthFlow.AuthorizeAsync(OAuthClients.Dropbox, http,
            async uri =>
            {
                authorizeUri = uri;
                // Simuliert den Browser: nach der "Anmeldung" leitet der Anbieter mit Code + state zurück.
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                using var browser = new HttpClient();
                var redirect = $"{query["redirect_uri"]}?code=CODE123&state={query["state"]}";
                using var response = await browser.GetAsync(redirect);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            },
            timeout: TimeSpan.FromSeconds(20), port: port);

        Assert.Equal("tok", tokens.AccessToken);
        Assert.Equal("ref", tokens.RefreshToken);
        Assert.Equal("dbid:1", tokens.AccountId);

        var query = System.Web.HttpUtility.ParseQueryString(authorizeUri!.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["token_access_type"]);
        Assert.Equal(OAuthClients.Dropbox.ClientId, query["client_id"]);

        var form = System.Web.HttpUtility.ParseQueryString(Assert.Single(handler.Calls).Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("CODE123", form["code"]);
        var expectedChallenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedChallenge, query["code_challenge"]);
    }

    [Fact]
    public async Task LoopbackFlow_RejectsWrongState_WithoutCallingTokenEndpoint()
    {
        const int port = 53698;
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<OAuthException>(() => LoopbackOAuthFlow.AuthorizeAsync(OAuthClients.OneDrive, http,
            async uri =>
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                using var browser = new HttpClient();
                using var _ = await browser.GetAsync($"{query["redirect_uri"]}?code=X&state=gefaelscht");
            },
            timeout: TimeSpan.FromSeconds(20), port: port));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task HttpRetry_WaitsAndRetries_OnThrottling()
    {
        var attempts = 0;
        var handler = new FakeHandler((_, _) =>
        {
            attempts++;
            if (attempts >= 3) return new HttpResponseMessage(HttpStatusCode.OK);
            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return throttled;
        });
        using var http = new HttpClient(handler);
        var waits = new List<TimeSpan>();
        HttpRetry.Delay = (wait, _) => { waits.Add(wait); return Task.CompletedTask; };
        try
        {
            using var response = await HttpRetry.SendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, "https://x.test"), CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            HttpRetry.Delay = Task.Delay;
        }

        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], waits);
    }
}
