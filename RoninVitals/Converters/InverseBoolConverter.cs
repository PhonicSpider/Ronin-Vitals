using System.Globalization;
using System.Windows.Data;

namespace RoninVitals.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => !(value is true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => !(value is true);
}
