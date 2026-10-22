using System.Globalization;
using System.Windows.Data;

namespace RoninVitals.Converters;

/// <summary>ConverterParameter format: "WhenTrue|WhenFalse".</summary>
public sealed class BoolToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "|").Split('|');
        var isTrue = value is true;
        return isTrue ? parts[0] : (parts.Length > 1 ? parts[1] : string.Empty);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
