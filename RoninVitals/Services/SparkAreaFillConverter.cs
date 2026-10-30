using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RoninVitals.Services;

/// <summary>
/// Turns a sparkline's line points into a closed polygon (line points, then down to the
/// canvas floor at the last x, then back to the floor at the first x) so a gradient Fill
/// only ever shades the area under the line, never the empty space above it - a plain
/// Rectangle behind the line shades the same fixed box regardless of where the line sits,
/// which is wrong for anything but a line pinned to the very top.
/// </summary>
public sealed class SparkAreaFillConverter : IValueConverter
{
    public double CanvasHeight { get; set; } = 20;

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PointCollection points || points.Count == 0)
            return new PointCollection();

        var area = new PointCollection(points.Count + 2);
        foreach (var point in points) area.Add(point);
        area.Add(new System.Windows.Point(points[^1].X, CanvasHeight));
        area.Add(new System.Windows.Point(points[0].X, CanvasHeight));
        return area;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
