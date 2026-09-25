using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fulltxt.Core.Tests;

public enum InfinityBehavior
{
    Allow,
    Forbidden403,
    ServerError500,
}

/// <summary>Minimaler WebDAV-Server (Nextcloud-Layout) für Tests: PROPFIND (Depth 0/1/infinity) und GET mit Basic-Auth.</summary>
public sealed class FakeWebDavServer : IDisposable
{
    public sealed record FakeFile(string Content, string ETag, long FileId);

    private readonly HttpListener listener = new();
    private readonly Task loop;

    public const string User = "demo";
    public const string Password = "secret";

    public string BaseUrl { get; }
    public string DavRoot => $"/remote.php/dav/files/{User}";
    public Dictionary<string, FakeFile> Files { get; } = new(StringComparer.Ordinal);
    public InfinityBehavior Infinity { get; set; } = InfinityBehavior.Forbidden403;
    public HashSet<string> FailGetFor { get; } = [];
    public List<string> Requests { get; } = [];

    public int GetCount => Requests.Count(r => r.StartsWith("GET "));

    public FakeWebDavServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(BaseUrl + "/");
        listener.Start();
        loop = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            try
            {
                Handle(context);
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = Uri.UnescapeDataString(request.Url!.AbsolutePath);
        var depth = request.Headers["Depth"];
        lock (Requests) Requests.Add($"{request.HttpMethod} {path} depth={depth}");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Password}"));
        if (request.Headers["Authorization"] != $"Basic {expected}")
        {
            response.StatusCode = 401;
            response.Close();
            return;
        }

        if (request.HttpMethod == "PROPFIND")
        {
            if (depth == "infinity" && Infinity != InfinityBehavior.Allow)
            {
                response.StatusCode = Infinity == InfinityBehavior.Forbidden403 ? 403 : 500;
                response.Close();
                return;
            }
            WritePropfind(response, path, depth);
            return;
        }

        if (request.HttpMethod == "GET")
        {
            var relative = path.StartsWith(DavRoot) ? path[DavRoot.Length..] : path;
            if (FailGetFor.Contains(relative))
            {
                response.StatusCode = 500;
                response.Close();
                return;
            }
            if (!Files.TryGetValue(relative, out var file))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(file.Content);
            response.ContentType = "application/octet-stream";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
            response.Close();
            return;
        }

        response.StatusCode = 405;
        response.Close();
    }

    private void WritePropfind(HttpListenerResponse response, string requestPath, string? depth)
    {
        var relative = requestPath.StartsWith(DavRoot) ? requestPath[DavRoot.Length..] : requestPath;
        var folder = "/" + relative.Trim('/');
        if (folder == "/") folder = "";

        var folders = new HashSet<string> { "" };
        foreach (var file in Files.Keys)
        {
            var parts = file.Trim('/').Split('/');
            for (var i = 1; i < parts.Length; i++) folders.Add("/" + string.Join('/', parts.Take(i)));
        }
        if (!folders.Contains(folder))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        var xml = new StringBuilder("""<?xml version="1.0"?><d:multistatus xmlns:d="DAV:" xmlns:oc="http://owncloud.org/ns">""");
        AppendFolder(xml, folder);

        if (depth != "0")
        {
            var recursive = depth == "infinity";
            foreach (var sub in folders.Where(f => f.Length > folder.Length && f.StartsWith(folder + "/")).OrderBy(f => f))
            {
                if (recursive || !sub[(folder.Length + 1)..].Contains('/')) AppendFolder(xml, sub);
            }
            foreach (var (path, file) in Files.OrderBy(f => f.Key))
            {
                var parent = path[..path.LastIndexOf('/')];
                if (parent == folder || (recursive && path.StartsWith(folder + "/"))) AppendFile(xml, path, file);
            }
        }
        xml.Append("</d:multistatus>");

        var bytes = Encoding.UTF8.GetBytes(xml.ToString());
        response.StatusCode = 207;
        response.ContentType = "application/xml; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }

    private void AppendFolder(StringBuilder xml, string folder) =>
        xml.Append($"<d:response><d:href>{Href(folder)}/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype>" +
                   "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");

    private void AppendFile(StringBuilder xml, string path, FakeFile file) =>
        xml.Append($"<d:response><d:href>{Href(path)}</d:href><d:propstat><d:prop><d:resourcetype/>" +
                   $"<d:getcontentlength>{Encoding.UTF8.GetByteCount(file.Content)}</d:getcontentlength>" +
                   $"<d:getetag>&quot;{file.ETag}&quot;</d:getetag>" +
                   "<d:getlastmodified>Wed, 01 Jul 2026 10:00:00 GMT</d:getlastmodified>" +
                   $"<oc:fileid>{file.FileId}</oc:fileid></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");

    private string Href(string relative) =>
        DavRoot + string.Concat(relative.Split('/').Select(s => s.Length == 0 ? "" : "/" + Uri.EscapeDataString(s)));

    public void Dispose()
    {
        listener.Close();
        try { loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}
