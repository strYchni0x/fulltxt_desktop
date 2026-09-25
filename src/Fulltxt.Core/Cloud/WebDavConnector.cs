using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Fulltxt.Core.Cloud;

/// <summary>Konfiguration eines WebDAV-Endpunkts (Nextcloud, ownCloud, MagentaCloud, Strato HiDrive, Yandex).</summary>
/// <param name="ServerUrl">Basis-URL des Servers, ggf. mit Unterpfad (z.B. https://host/nextcloud).</param>
/// <param name="DavRootPath">WebDAV-Wurzel relativ zur Server-URL, z.B. /remote.php/dav/files/user/ (URL-kodiert).</param>
/// <param name="StartFolder">Startordner innerhalb der Wurzel, "/" = alles.</param>
/// <param name="FileIdWebLinks">True bei Nextcloud/ownCloud: Datei-IDs (oc:fileid) ergeben direkte Browser-Links.</param>
public sealed record WebDavEndpoint(
    string ServerUrl,
    string DavRootPath,
    string StartFolder,
    string Username,
    string Password,
    bool FileIdWebLinks);

/// <summary>
/// WebDAV-Client für alle Passwort-Anbieter. Listet den Baum, ohne Dateiinhalte zu laden, und lädt Inhalte
/// nur gezielt einzeln. Zugangsdaten bleiben im Arbeitsspeicher und werden nie geloggt.
/// </summary>
public sealed class WebDavConnector : ICloudConnector
{
    private static readonly HttpMethod PropFind = new("PROPFIND");
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace Oc = "http://owncloud.org/ns";

    private const string PropfindBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:oc="http://owncloud.org/ns">
          <d:prop>
            <d:resourcetype/>
            <d:getcontentlength/>
            <d:getetag/>
            <d:getlastmodified/>
            <oc:fileid/>
          </d:prop>
        </d:propfind>
        """;

    private readonly WebDavEndpoint endpoint;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly string origin;
    private readonly string davBasePath;         // URL-kodiert, ohne abschließenden "/"
    private readonly string davBasePathDecoded;  // dekodiert, ohne abschließenden "/"
    private readonly string authHeader;

    public WebDavConnector(WebDavEndpoint endpoint, HttpClient? httpClient = null)
    {
        this.endpoint = endpoint;
        if (httpClient is null)
        {
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            ownsHttp = true;
        }
        else
        {
            http = httpClient;
        }

        var serverUri = new Uri(endpoint.ServerUrl.TrimEnd('/') + "/");
        origin = serverUri.GetLeftPart(UriPartial.Authority);
        davBasePath = (serverUri.AbsolutePath.TrimEnd('/') + "/" + endpoint.DavRootPath.Trim('/')).TrimEnd('/');
        if (!davBasePath.StartsWith('/')) davBasePath = "/" + davBasePath;
        davBasePathDecoded = Uri.UnescapeDataString(davBasePath);

        authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{endpoint.Username}:{endpoint.Password}"));
    }

    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await PropfindAsync(StartUrlPath(), "0", ct).ConfigureAwait(false);
    }

    public async Task<CloudChanges> GetChangesAsync(string? cursor, CancellationToken ct = default)
    {
        // WebDAV kennt keine Änderungsliste: immer die vollständige Liste holen (nur Metadaten, keine Inhalte).
        var files = await ListFilesAsync(ct).ConfigureAwait(false);
        return new CloudChanges(files, [], [], IsFullListing: true, NextCursor: null);
    }

    public async Task<Stream> OpenReadAsync(CloudFileRef file, CancellationToken ct = default)
    {
        var url = file.RemoteId.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? file.RemoteId : origin + file.RemoteId;
        var response = await HttpRetry.SendAsync(http, () => Authorized(new HttpRequestMessage(HttpMethod.Get, url)), ct,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        try
        {
            EnsureSuccess(response, "Download");
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new ResponseOwningStream(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<List<CloudItem>> ListFilesAsync(CancellationToken ct)
    {
        var start = StartUrlPath();
        List<Entry>? entries = null;

        // Ein Aufruf für den ganzen Baum (Depth: infinity) ist viel schneller, viele Server lehnen ihn aber ab
        // (Nextcloud: 403, teils 500 oder Timeout). Bei JEDEM Fehler fällt es auf Ordner für Ordner zurück.
        try
        {
            using var infinityCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            infinityCts.CancelAfter(TimeSpan.FromSeconds(90));
            entries = await PropfindAsync(start, "infinity", infinityCts.Token).ConfigureAwait(false);
        }
        catch (CloudException ex) when (ex.IsAuthError)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            entries = null;
        }

        var items = new List<CloudItem>();
        if (entries is not null)
        {
            items.AddRange(entries.Where(e => !e.IsCollection).Select(ToItem));
            return items;
        }

        var pending = new Stack<string>();
        pending.Push(start);
        var first = true;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = pending.Pop();

            List<Entry> children;
            try
            {
                children = await PropfindAsync(folder, "1", ct).ConfigureAwait(false);
            }
            catch (CloudException) when (!first)
            {
                continue; // einzelner nicht lesbarer Unterordner - Rest des Baums weiter scannen
            }
            first = false;

            foreach (var child in children)
            {
                if (child.IsCollection) pending.Push(child.EncodedHref);
                else items.Add(ToItem(child));
            }
        }
        return items;
    }

    private async Task<List<Entry>> PropfindAsync(string encodedPath, string depth, CancellationToken ct)
    {
        var url = origin + encodedPath;
        using var response = await HttpRetry.SendAsync(http, () =>
        {
            var request = Authorized(new HttpRequestMessage(PropFind, url)
            {
                Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
            });
            request.Headers.Add("Depth", depth);
            return request;
        }, ct).ConfigureAwait(false);

        EnsureSuccess(response, "Verbindung");
        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseMultiStatus(xml, encodedPath);
    }

    private List<Entry> ParseMultiStatus(string xml, string requestedEncodedPath)
    {
        var requested = TrimSlashes(Uri.UnescapeDataString(requestedEncodedPath));
        var entries = new List<Entry>();

        foreach (var response in XDocument.Parse(xml).Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            var encodedHref = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(href).AbsolutePath : href;
            var decodedPath = Uri.UnescapeDataString(encodedHref);
            if (string.Equals(TrimSlashes(decodedPath), requested, StringComparison.OrdinalIgnoreCase)) continue; // der Ordner selbst

            var prop = response.Elements(Dav + "propstat")
                .FirstOrDefault(ps => (ps.Element(Dav + "status")?.Value ?? "200").Contains("200"))
                ?.Element(Dav + "prop");
            if (prop is null) continue;

            var relative = decodedPath.StartsWith(davBasePathDecoded, StringComparison.OrdinalIgnoreCase)
                ? decodedPath[davBasePathDecoded.Length..]
                : decodedPath;
            relative = "/" + TrimSlashes(relative);

            entries.Add(new Entry(
                EncodedHref: encodedHref,
                RelativePath: relative,
                Name: relative[(relative.LastIndexOf('/') + 1)..],
                IsCollection: prop.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null,
                Size: long.TryParse(prop.Element(Dav + "getcontentlength")?.Value, out var size) ? size : 0,
                ETag: prop.Element(Dav + "getetag")?.Value?.Trim().Trim('"'),
                ModifiedUtc: DateTime.TryParse(prop.Element(Dav + "getlastmodified")?.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var modified)
                    ? modified
                    : DateTime.UnixEpoch,
                FileId: prop.Element(Oc + "fileid")?.Value));
        }
        return entries;
    }

    private CloudItem ToItem(Entry entry) => new(
        RemoteId: entry.EncodedHref,
        Path: entry.RelativePath,
        Name: entry.Name,
        Size: entry.Size,
        ModifiedUtc: entry.ModifiedUtc,
        ETag: string.IsNullOrEmpty(entry.ETag) ? null : entry.ETag,
        WebUrl: BuildWebUrl(entry));

    private string? BuildWebUrl(Entry entry)
    {
        if (!endpoint.FileIdWebLinks) return null;
        var server = endpoint.ServerUrl.TrimEnd('/');
        if (!string.IsNullOrEmpty(entry.FileId)) return $"{server}/index.php/f/{entry.FileId}";

        var dir = entry.RelativePath[..entry.RelativePath.LastIndexOf('/')];
        return $"{server}/apps/files/?dir={Uri.EscapeDataString(dir.Length == 0 ? "/" : dir).Replace("%2F", "/")}";
    }

    private string StartUrlPath()
    {
        var folder = endpoint.StartFolder.Trim('/');
        return folder.Length == 0
            ? davBasePath + "/"
            : davBasePath + "/" + string.Join('/', folder.Split('/').Select(Uri.EscapeDataString)) + "/";
    }

    private HttpRequestMessage Authorized(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;

        var code = (int)response.StatusCode;
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new CloudException("Anmeldung fehlgeschlagen: Benutzername oder Passwort stimmen nicht.", isAuthError: true),
            HttpStatusCode.Forbidden =>
                new CloudException("Zugriff verweigert (HTTP 403)."),
            HttpStatusCode.NotFound =>
                new CloudException("Nicht gefunden: Server-Adresse oder Ordner prüfen."),
            _ => new CloudException($"{what} fehlgeschlagen (HTTP {code})."),
        };
    }

    private static string TrimSlashes(string path) => path.Trim('/');

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
    }

    private sealed record Entry(
        string EncodedHref,
        string RelativePath,
        string Name,
        bool IsCollection,
        long Size,
        string? ETag,
        DateTime ModifiedUtc,
        string? FileId);
}
