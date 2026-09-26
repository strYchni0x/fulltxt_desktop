using System.Globalization;
using Avalonia.Data.Converters;

namespace Fulltxt.Linux.Converters;

/// <summary>Kurzes Dateityp-Kürzel für das Symbol der Trefferkarte (z.B. "PDF", "DOCX"). Systemweite
/// Dateisymbole gibt es unter Linux nicht einheitlich, deshalb ein neutrales, themenfähiges Zeichen.</summary>
public sealed class FileTypeLabelConverter : IValueConverter
{
    public static readonly FileTypeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var extension = Path.GetExtension(value as string ?? string.Empty).TrimStart('.');
        if (extension.Length == 0) return "FILE";
        return extension.Length <= 4 ? extension.ToUpperInvariant() : extension[..4].ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
