using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Envelope;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed class AtlasSelectionChangedEventArgs(IReadOnlyList<int> observationIndices) : EventArgs
{
    public IReadOnlyList<int> ObservationIndices { get; } = observationIndices;
}

public sealed class AtlasDimensionsChangedEventArgs(PerformanceAtlasDimension x, PerformanceAtlasDimension y) : EventArgs
{
    public PerformanceAtlasDimension X { get; } = x;
    public PerformanceAtlasDimension Y { get; } = y;
}

public sealed partial class PerformanceAtlasControl : UserControl
{
    private IReadOnlyList<OperatingObservation> _observations = Array.Empty<OperatingObservation>();
    private OperatingEnvelope? _envelope;
    private PerformanceAtlasData? _data;
    private PerformanceAtlasDimension _x = PerformanceAtlasDimension.PackagePower;
    private PerformanceAtlasDimension _y = PerformanceAtlasDimension.EffectiveClock;
    private HashSet<int> _selectedObservationIndices = [];
    private bool _suppressDimensionEvents;
    private const int BinCount = 12;

    public PerformanceAtlasControl()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Redraw();
    }

    public event EventHandler<AtlasSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<AtlasDimensionsChangedEventArgs>? DimensionsChanged;

    public void Apply(
        IReadOnlyList<OperatingObservation> observations,
        OperatingEnvelope? envelope,
        Func<OperatingObservation, double?>? efficiencySelector = null)
    {
        _observations = observations?.ToArray() ?? Array.Empty<OperatingObservation>();
        _envelope = envelope;
        _data = PerformanceAtlasProjection.Build(_observations, _x, _y, BinCount, efficiencySelector);
        Redraw();
    }

    public void SetSelectedObservationIndices(IEnumerable<int>? observationIndices)
    {
        _selectedObservationIndices = observationIndices is null
            ? []
            : observationIndices.Where(index => index >= 0).ToHashSet();
        RedrawSelection();
    }

    private void OnDimensionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDimensionEvents || XDimensionSelector is null || YDimensionSelector is null) return;
        var requestedX = SelectedDimension(XDimensionSelector, _x);
        var requestedY = SelectedDimension(YDimensionSelector, _y);
        if (requestedX == requestedY)
        {
            _suppressDimensionEvents = true;
            if (ReferenceEquals(sender, XDimensionSelector))
                XDimensionSelector.SelectedIndex = IndexOf(_x);
            else
                YDimensionSelector.SelectedIndex = IndexOf(_y);
            _suppressDimensionEvents = false;
            return;
        }

        _x = requestedX;
        _y = requestedY;
        _data = PerformanceAtlasProjection.Build(_observations, _x, _y, BinCount);
        DimensionsChanged?.Invoke(this, new AtlasDimensionsChangedEventArgs(_x, _y));
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        AtlasCanvas.Children.Clear();
        FrontierLayer.Children.Clear();
        SelectionLayer.Children.Clear();
        if (_data is null)
        {
            UpdateLabels();
            ResidencySummary.Text = "Waiting for operating history";
            SelectionCard.Visibility = Visibility.Collapsed;
            return;
        }

        var width = AtlasCanvas.ActualWidth;
        var height = AtlasCanvas.ActualHeight;
        UpdateLabels();
        ResidencySummary.Text = _data.IncludedObservationIndices.Count == 0
            ? "No observations contain both selected dimensions"
            : $"{_data.IncludedObservationIndices.Count} observations · {_data.Cells.Count} occupied regions";
        if (width < 80 || height < 80) return;

        DrawGrid(width, height);
        DrawCells(width, height);
        DrawFrontier(width, height);
        RedrawSelection();
    }

    private void DrawGrid(double width, double height)
    {
        var grid = Brush(255, 255, 255, ActualTheme == ElementTheme.Light ? (byte)24 : (byte)16);
        for (var i = 0; i <= BinCount; i += 3)
        {
            var x = width * i / BinCount;
            var y = height * i / BinCount;
            AddLine(AtlasCanvas, x, 0, x, height, grid, 1);
            AddLine(AtlasCanvas, 0, y, width, y, grid, 1);
        }
    }

    private void DrawCells(double width, double height)
    {
        if (_data is null) return;
        var cellWidth = width / _data.XAxis.BinCount;
        var cellHeight = height / _data.YAxis.BinCount;
        foreach (var cell in _data.Cells)
        {
            var alpha = (byte)Math.Clamp(24 + cell.Density * 190, 24, 214);
            var rectangle = new Rectangle
            {
                Width = Math.Max(1, cellWidth - 1),
                Height = Math.Max(1, cellHeight - 1),
                RadiusX = 2,
                RadiusY = 2,
                Fill = Brush(52, 214, 238, alpha),
                Stroke = Brush(150, 240, 255, (byte)Math.Clamp(alpha + 20, 0, 235)),
                StrokeThickness = cell.Density > .66 ? 1 : .5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rectangle, cell.XBin * cellWidth + .5);
            Canvas.SetTop(rectangle, height - (cell.YBin + 1) * cellHeight + .5);
            AtlasCanvas.Children.Add(rectangle);
        }
    }

    private void DrawFrontier(double width, double height)
    {
        if (_data is null || _envelope is null) return;
        var pressurePowerPair = (_x == PerformanceAtlasDimension.CpuPressure && _y == PerformanceAtlasDimension.PackagePower)
            || (_x == PerformanceAtlasDimension.PackagePower && _y == PerformanceAtlasDimension.CpuPressure);
        if (!pressurePowerPair) return;

        var candidates = new[]
        {
            (_envelope.EcoCeilingPressure, _envelope.EcoPowerFrontierWatts),
            (_envelope.EfficientCeilingPressure, _envelope.EfficientPowerFrontierWatts),
            (_envelope.ResponsiveCeilingPressure, _envelope.ResponsivePowerFrontierWatts)
        };
        var points = candidates
            .Where(candidate => candidate.Item2 is double watts && double.IsFinite(watts))
            .Select(candidate => AtlasPoint(candidate.Item1, candidate.Item2!.Value, width, height))
            .ToArray();
        if (points.Length == 0) return;

        var frontierBrush = Brush(181, 139, 255, 220);
        if (points.Length > 1)
        {
            var line = new Polyline { Stroke = frontierBrush, StrokeThickness = 2, IsHitTestVisible = false };
            foreach (var point in points) line.Points.Add(point);
            FrontierLayer.Children.Add(line);
        }
        foreach (var point in points)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = frontierBrush, IsHitTestVisible = false };
            Canvas.SetLeft(dot, point.X - 3);
            Canvas.SetTop(dot, point.Y - 3);
            FrontierLayer.Children.Add(dot);
        }
        var label = new TextBlock { Text = "LEARNED FRONTIER", FontSize = 11, Foreground = frontierBrush, Opacity = .82, IsHitTestVisible = false };
        Canvas.SetLeft(label, 6);
        Canvas.SetTop(label, 4);
        FrontierLayer.Children.Add(label);
    }

    private Point AtlasPoint(double pressure, double power, double width, double height)
    {
        if (_data is null) return new Point();
        var xValue = _x == PerformanceAtlasDimension.CpuPressure ? pressure : power;
        var yValue = _y == PerformanceAtlasDimension.CpuPressure ? pressure : power;
        var x = Normalize(xValue, _data.XAxis) * width;
        var y = height - Normalize(yValue, _data.YAxis) * height;
        return new Point(x, y);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_data is null || InteractionLayer.ActualWidth <= 0 || InteractionLayer.ActualHeight <= 0) return;
        var point = e.GetCurrentPoint(InteractionLayer).Position;
        var xBin = Math.Clamp((int)Math.Floor(point.X / InteractionLayer.ActualWidth * _data.XAxis.BinCount), 0, _data.XAxis.BinCount - 1);
        var yBinFromTop = Math.Clamp((int)Math.Floor(point.Y / InteractionLayer.ActualHeight * _data.YAxis.BinCount), 0, _data.YAxis.BinCount - 1);
        var yBin = _data.YAxis.BinCount - 1 - yBinFromTop;
        var cell = _data.Cells.FirstOrDefault(candidate => candidate.XBin == xBin && candidate.YBin == yBin);
        if (cell is null)
        {
            _selectedObservationIndices.Clear();
            SelectionCard.Visibility = Visibility.Collapsed;
            RedrawSelection();
            SelectionChanged?.Invoke(this, new AtlasSelectionChangedEventArgs(Array.Empty<int>()));
            return;
        }

        _selectedObservationIndices = cell.ObservationIndices.ToHashSet();
        SelectionReadout.Text = $"{cell.SampleCount} observations · density {cell.Density:P0}" +
            (cell.EfficiencyValue is double efficiency ? $" · evidence {efficiency:0.##}" : string.Empty);
        SelectionCard.Visibility = Visibility.Visible;
        RedrawSelection();
        SelectionChanged?.Invoke(this, new AtlasSelectionChangedEventArgs(cell.ObservationIndices.ToArray()));
    }

    private void RedrawSelection()
    {
        SelectionLayer.Children.Clear();
        if (_data is null || _selectedObservationIndices.Count == 0 || SelectionLayer.ActualWidth <= 0 || SelectionLayer.ActualHeight <= 0) return;
        var cellWidth = SelectionLayer.ActualWidth / _data.XAxis.BinCount;
        var cellHeight = SelectionLayer.ActualHeight / _data.YAxis.BinCount;
        foreach (var cell in _data.Cells.Where(cell => cell.ObservationIndices.Any(_selectedObservationIndices.Contains)))
        {
            var outline = new Rectangle
            {
                Width = Math.Max(1, cellWidth - 2),
                Height = Math.Max(1, cellHeight - 2),
                Stroke = Brush(244, 250, 255, 245),
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, cell.XBin * cellWidth + 1);
            Canvas.SetTop(outline, SelectionLayer.ActualHeight - (cell.YBin + 1) * cellHeight + 1);
            SelectionLayer.Children.Add(outline);
        }
    }

    private void UpdateLabels()
    {
        if (_data is null)
        {
            XAxisLabel.Text = AxisText(_x, null);
            YAxisLabel.Text = AxisText(_y, null);
            return;
        }
        XAxisLabel.Text = AxisText(_x, _data.XAxis);
        YAxisLabel.Text = AxisText(_y, _data.YAxis);
    }

    private static string AxisText(PerformanceAtlasDimension dimension, PerformanceAtlasAxis? axis)
    {
        var label = dimension switch
        {
            PerformanceAtlasDimension.PackagePower => "PACKAGE POWER",
            PerformanceAtlasDimension.EffectiveClock => "EFFECTIVE CLOCK",
            PerformanceAtlasDimension.ActiveCores => "ACTIVE CORES",
            _ => "CPU PRESSURE"
        };
        return axis is null || string.IsNullOrWhiteSpace(axis.Unit) ? label : $"{label} ({axis.Unit})";
    }

    private static PerformanceAtlasDimension SelectedDimension(ComboBox box, PerformanceAtlasDimension fallback)
    {
        if (box.SelectedItem is not ComboBoxItem item || item.Tag is null) return fallback;
        return Enum.TryParse<PerformanceAtlasDimension>(item.Tag.ToString(), out var value) ? value : fallback;
    }

    private static int IndexOf(PerformanceAtlasDimension dimension) => dimension switch
    {
        PerformanceAtlasDimension.PackagePower => 0,
        PerformanceAtlasDimension.EffectiveClock => 1,
        PerformanceAtlasDimension.ActiveCores => 2,
        _ => 3
    };

    private static double Normalize(double value, PerformanceAtlasAxis axis)
    {
        var span = axis.DomainMax - axis.DomainMin;
        return span <= 0 ? 0 : Math.Clamp((value - axis.DomainMin) / span, 0, 1);
    }

    private static Line AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false };
        canvas.Children.Add(line);
        return line;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}