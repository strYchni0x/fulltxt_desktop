using System.Globalization;
using System.Windows.Data;

namespace Fulltxt.App.Converters;

/// <summary>Bindet eine Zahl an eine RadioButton-Gruppe: ConverterParameter = Zahlenwert der Option.</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int number && int.TryParse(parameter?.ToString(), CultureInfo.InvariantCulture, out var option) && number == option;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter?.ToString(), CultureInfo.InvariantCulture, out var option) ? option : Binding.DoNothing;
}
