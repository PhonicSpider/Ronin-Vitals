using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

/// <summary>
/// One tile in the vitals strip, backed by a live sampler (see SystemMetricsService) rather
/// than mocked data. Sample() re-invokes the delegate each timer tick and pushes the result
/// into both the sparkline and the mono value line.
/// </summary>
public partial class VitalMetricViewModel : ObservableObject
{
    private const int SampleCount = 28;
    private const double SparkWidth = 64;
    private const double SparkHeight = 20;

    private readonly Func<VitalSample> _sample;
    private readonly double[] _values;

    public VitalMetricViewModel(string label, string brushKey, Func<VitalSample> sample)
    {
        Label = label;
        BrushKey = brushKey;
        _sample = sample;
        _values = new double[SampleCount];

        var initial = sample();
        Array.Fill(_values, initial.GaugeValue);
        _valueText = initial.DisplayText;
        Points = BuildPoints();
    }

    public string Label { get; }
    public string BrushKey { get; }

    [ObservableProperty] private string _valueText;
    [ObservableProperty] private PointCollection _points;

    public void Sample()
    {
        var result = _sample();
        for (var index = 0; index < _values.Length - 1; index++) _values[index] = _values[index + 1];
        _values[^1] = Math.Clamp(result.GaugeValue, 0, 100);

        ValueText = result.DisplayText;
        Points = BuildPoints();
    }

    private PointCollection BuildPoints()
    {
        var points = new PointCollection(SampleCount);
        for (var index = 0; index < _values.Length; index++)
        {
            var pointX = index / (double)(_values.Length - 1) * SparkWidth;
            var pointY = SparkHeight - _values[index] / 100.0 * SparkHeight;
            points.Add(new Point(pointX, pointY));
        }
        return points;
    }
}
