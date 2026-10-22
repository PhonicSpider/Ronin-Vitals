using System.Globalization;
using System.Windows.Data;

namespace RoninVitals.Converters;

/// <summary>Widens the symptom-content column's MaxWidth when the Tech Mode console panel is
/// showing, so the checklist+console split has room to sit side by side instead of being
/// squeezed into the normal single-panel 760px cap.</summary>
public sealed class TechConsoleWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1360.0 : 760.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
