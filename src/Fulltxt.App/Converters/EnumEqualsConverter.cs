using System.Globalization;
using System.Windows.Data;

namespace Fulltxt.App.Converters;

/// <summary>Bindet einen Enum-Wert an eine RadioButton-Gruppe: ConverterParameter = Enum-Name.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
