using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Status-glyph foreground. Uses the "Light" tints (originally built for text on a
/// tinted summary bar) rather than the base Teal/Yellow/Red brushes - those read as too
/// low-contrast once paired with a same-hue Dim badge background (see the Dim converter below).</summary>
public sealed class CheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckStatus.Pass => "Vitals.TealLight.Brush",
            CheckStatus.Warn => "Vitals.TealLight.Brush",
            CheckStatus.Bad => "Vitals.TealLight.Brush",
            CheckStatus.Running => "Vitals.Blue.Brush",
            _ => "Vitals.MutedDim.Brush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Status-glyph badge background - a tinted circle per status (green/yellow/red/blue),
/// paired with a single consistent TealLight glyph on top (see the Brush converter above) rather
/// than a same-hue glyph-on-badge pairing, which is what made the glyph itself hard to read.</summary>
public sealed class CheckStatusToDimBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value switch
            {
                CheckStatus.Pass => "Vitals.GreenDim.Brush",
                CheckStatus.Warn => "Vitals.YellowDim.Brush",
                CheckStatus.Bad => "Vitals.RedDim.Brush",
                CheckStatus.Running => "Vitals.BlueDim.Brush",
                _ => "Vitals.Surface3.Brush",
            };
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CheckStatusToGlyphConverter : IValueConverter
{
    // Segoe MDL2 Assets: E73E = CheckMark, E783 = Warning, E711 = Cancel(X)
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            CheckStatus.Pass => "",
            CheckStatus.Warn => "",
            CheckStatus.Bad => "",
            _ => string.Empty,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
