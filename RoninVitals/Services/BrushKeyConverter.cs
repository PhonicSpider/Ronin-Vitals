using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RoninVitals.Services;

/// <summary>Looks up a named brush ("Vitals.Teal.Brush") from the merged App resource dictionaries.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current.TryFindResource(key) is Brush brush)
            return brush;
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
