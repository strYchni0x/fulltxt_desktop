using System.Net;
using System.Net.Sockets;

namespace Fulltxt.Core.Cloud;

/// <summary>Prüfung und Normalisierung der Eingaben beim Hinzufügen eines Cloud-Kontos.</summary>
public static class CloudAccountInput
{
    public static string? NormalizeServerUrl(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) return null;
        if (!text.Contains("://")) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? text.TrimEnd('/')
            : null;
    }

    /// <summary>Klartext-HTTP ist nur im eigenen Netz (Loopback, private Adressen, *.local, einfache Hostnamen) vertretbar.</summary>
    public static bool IsInsecureRemote(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps) return false;
        if (uri.IsLoopback || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
        }
        return true;
    }
}
