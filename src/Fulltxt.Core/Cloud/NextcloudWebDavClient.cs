using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Fulltxt.Core.Cloud;

/// <summary>
/// Minimaler WebDAV-Client für Nextcloud/ownCloud. Läuft komplett gegen die vom Nutzer
/// angegebene Serveradresse - es gibt keinen Umweg über einen Dienst von uns. Zugangsdaten
/// (App-Passwort) werden nur im Arbeitsspeicher gehalten, nie geloggt.
/// </summary>
public sealed class NextcloudWebDavClient : IDisposable
{
    private static readonly HttpMethod PropFind = new("PROPFIND");

    private const string PropfindBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:">
          <d:prop>
            <d:displayname/>
            <d:getcontentlength/>
            <d:getlastmodified/>
            <d:getetag/>
            <d:resourcetype/>
          </d:prop>
        </d:propfind>
        """;

    private static readonly XNamespace Dav = "DAV:";

    private readonly HttpClient _http;
    private readonly string _davBaseUrl;

    public NextcloudWebDavClient(string serverUrl, string username, string appPassword)
    {
        _davBaseUrl = $"{serverUrl.TrimEnd('/')}/remote.php/dav/files/{Uri.EscapeDataString(username)}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var authBytes = Encoding.UTF8.GetBytes($"{username}:{appPassword}");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    /// <summary>Wirft bei falscher URL/Zugangsdaten - zum Prüfen einer neuen Verbindung im UI.</summary>
    public Task TestConnectionAsync(CancellationToken ct = default) => ListDirectoryAsync("/", ct);

    public async Task<IReadOnlyList<WebDavItem>> ListRecursiveAsync(string rootPath, CancellationToken ct = default)
    {
        var result = new List<WebDavItem>();
        var pending = new Stack<string>();
        pending.Push(NormalizeDirPath(rootPath));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            List<WebDavItem> children;
            try
            {
                children = await ListDirectoryAsync(dir, ct);
            }
            catch (HttpRequestException)
            {
                // Einzelner Ordner nicht erreichbar (z.B. Berechtigungen) - Rest des Baums weiter scannen.
                continue;
            }

            foreach (var child in children)
            {
                if (child.IsCollection)
                {
                    pending.Push(child.RelativePath);
                }
                else
                {
                    result.Add(child);
                }
            }
        }

        return result;
    }

    public async Task<Stream> DownloadAsync(WebDavItem item, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(_davBaseUrl + item.Href, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private async Task<List<WebDavItem>> ListDirectoryAsync(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(PropFind, _davBaseUrl + Uri.EscapeDataString(path).Replace("%2F", "/"))
        {
            Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
        };
        // Depth:infinity liefert bei Nextcloud HTTP 500 statt einer Fehlerantwort - immer nur
        // eine Ebene abfragen und selbst rekursiv weitergehen (Lektion aus der Android-App).
        request.Headers.Add("Depth", "1");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct);
        return ParseMultiStatus(xml, path);
    }

    private List<WebDavItem> ParseMultiStatus(string xml, string requestedPath)
    {
        var doc = XDocument.Parse(xml);
        var items = new List<WebDavItem>();

        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            var relativePath = ToRelativePath(href);
            if (string.Equals(TrimSlash(relativePath), TrimSlash(requestedPath), StringComparison.OrdinalIgnoreCase))
            {
                continue; // erster Eintrag ist der abgefragte Ordner selbst
            }

            var propStat = response.Elements(Dav + "propstat")
                .FirstOrDefault(ps => (ps.Element(Dav + "status")?.Value ?? "").Contains("200"));
            var prop = propStat?.Element(Dav + "prop");
            if (prop is null) continue;

            var isCollection = prop.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null;
            var displayName = prop.Element(Dav + "displayname")?.Value;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = Uri.UnescapeDataString(TrimSlash(relativePath).Split('/').LastOrDefault() ?? "");
            }
            var contentLength = long.TryParse(prop.Element(Dav + "getcontentlength")?.Value, out var len) ? len : 0;
            var lastModified = DateTime.TryParse(prop.Element(Dav + "getlastmodified")?.Value, out var mod)
                ? mod.ToUniversalTime()
                : DateTime.UnixEpoch;
            var etag = prop.Element(Dav + "getetag")?.Value?.Trim('"');

            items.Add(new WebDavItem(
                Href: href,
                RelativePath: Uri.UnescapeDataString(relativePath),
                DisplayName: displayName,
                IsCollection: isCollection,
                ContentLength: contentLength,
                LastModifiedUtc: lastModified,
                ETag: etag));
        }

        return items;
    }

    private string ToRelativePath(string href)
    {
        var davPathPrefix = new Uri(_davBaseUrl).AbsolutePath;
        var hrefPath = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(href).AbsolutePath : href;
        if (hrefPath.StartsWith(davPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            hrefPath = hrefPath[davPathPrefix.Length..];
        }
        return hrefPath;
    }

    private static string NormalizeDirPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        return "/" + path.Trim('/') + "/";
    }

    private static string TrimSlash(string path) => path.Trim('/');

    public void Dispose() => _http.Dispose();
}
