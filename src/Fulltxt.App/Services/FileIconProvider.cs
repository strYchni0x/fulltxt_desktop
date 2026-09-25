using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fulltxt.App.Services;

/// <summary>Liefert das Windows-Shell-Symbol zu einer Dateiendung. Die Datei muss nicht existieren
/// (wichtig für Cloud-Treffer), gecacht wird pro Endung.</summary>
public static class FileIconProvider
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static ImageSource? GetIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return Cache.GetOrAdd(extension, Load);
    }

    private static ImageSource? Load(string extension)
    {
        var info = new ShFileInfo();
        var probe = extension.Length > 0 ? extension : ".file";
        var result = SHGetFileInfo(probe, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
