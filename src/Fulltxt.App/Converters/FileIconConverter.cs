using System.Globalization;
using System.Windows.Data;
using Fulltxt.App.Services;

namespace Fulltxt.App.Converters;

public sealed class FileIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string fileName ? FileIconProvider.GetIcon(fileName) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
