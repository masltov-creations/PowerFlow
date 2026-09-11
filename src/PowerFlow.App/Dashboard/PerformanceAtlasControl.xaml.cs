using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Envelope;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed class AtlasSelectionChangedEventArgs(IReadOnlyList<int> observationIndices) : EventArgs
{
    public IReadOnlyList<int> ObservationIndices { get; } = observationIndices;
}

public sealed class AtlasHoverChangedEventArgs(IReadOnlyList<int> observationIndices) : EventArgs
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
    private HashSet<int> _linkedHoveredObservationIndices = [];
    private Func<OperatingObservation, double?>? _efficiencySelector;
    private ModelExplanation? _modelExplanation;
    private bool _suppressDimensionEvents;
    private bool _initialized;
    private bool _reducedMotion;
    private readonly Dictionary<UIElement, Storyboard> _transientAnimations = [];
    private bool _redrawQueued;
    private const int BinCount = 12;

    public PerformanceAtlasControl()
    {
        _suppressDimensionEvents = true;
        InitializeComponent();
        _initialized = true;
        _suppressDimensionEvents = false;
        ActualThemeChanged += (_, _) => RequestRedraw();
    }

    public event EventHandler<AtlasSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<AtlasDimensionsChangedEventArgs>? DimensionsChanged;
    public event EventHandler<AtlasHoverChangedEventArgs>? HoverChanged;

    public void Apply(IReadOnlyList<OperatingObservation> observations, OperatingEnvelope? envelope, Func<OperatingObservation, double?>? efficiencySelector = null)
    {
        _observations = observations?.ToArray() ?? Array.Empty<OperatingObservation>();
        _envelope = envelope;
        _efficiencySelector = efficiencySelector;
        UpdateDimensionAvailability();
        Redraw();
    }

    public void SetModelExplanation(ModelExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        _modelExplanation = explanation;
        WhatLearnedText.Text = explanation.WhatLearned;
        WhatDoingNowText.Text = explanation.WhatDoingNow;
        ConfidenceExplanationText.Text = explanation.ConfidenceExplanation;
        DrawCurrentPoint();
    }

    public void SetSelectedObservationIndices(IEnumerable<int>? observationIndices)
    {
        _selectedObservationIndices = observationIndices is null ? [] : observationIndices.Where(index => index >= 0).ToHashSet();
        RedrawSelection();
    }
    public void SetHoveredObservationIndices(IEnumerable<int>? observationIndices)
    {
        _linkedHoveredObservationIndices = observationIndices is null
            ? []
            : observationIndices.Where(index => index >= 0 && index < _observations.Count).ToHashSet();
        RedrawLinkedHover();
    }

    public void SetReducedMotion(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        if (!reducedMotion) return;
        foreach (var animation in _transientAnimations.Values) animation.Stop();
        _transientAnimations.Clear();
        AtlasHoverCard.Opacity = AtlasHoverCard.Visibility == Visibility.Visible ? 1 : 0;
    }

    private void UpdateDimensionAvailability()
    {
        var available = PerformanceAtlasProjection.AvailableDimensions(_observations);
        UpdateSelectorAvailability(XDimensionSelector, available);
        UpdateSelectorAvailability(YDimensionSelector, available);
        DimensionAvailabilityText.Text = available.Count == 0
            ? "No live Atlas metrics are available yet"
            : $"Available now: {string.Join(" · ", available.Select(PerformanceAtlasProjection.FriendlyDimension))}";

        var pair = PerformanceAtlasProjection.ResolveAvailablePair(_observations, _x, _y);
        if (!pair.IsAvailable)
        {
            _data = null;
            AtlasUnavailableText.Text = pair.Explanation ?? "No usable observations are available for the Atlas yet.";
            AtlasUnavailableMessage.Visibility = Visibility.Visible;
            return;
        }

        if (pair.Changed || pair.X != _x || pair.Y != _y)
        {
            _x = pair.X;
            _y = pair.Y;
            _suppressDimensionEvents = true;
            XDimensionSelector.SelectedIndex = IndexOf(_x);
            YDimensionSelector.SelectedIndex = IndexOf(_y);
            _suppressDimensionEvents = false;
            DimensionAvailabilityText.Text = pair.Explanation ?? DimensionAvailabilityText.Text;
        }
        _data = PerformanceAtlasProjection.Build(_observations, _x, _y, BinCount, _efficiencySelector);
        AtlasUnavailableMessage.Visibility = _data.IncludedObservationIndices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_data.IncludedObservationIndices.Count == 0)
            AtlasUnavailableText.Text = "No usable observations contain both selected metrics yet.";
    }

    private static void UpdateSelectorAvailability(ComboBox selector, IReadOnlyList<PerformanceAtlasDimension> available)
    {
        foreach (var item in selector.Items.OfType<ComboBoxItem>())
        {
            if (!Enum.TryParse<PerformanceAtlasDimension>(item.Tag?.ToString(), out var dimension)) continue;
            item.IsEnabled = available.Contains(dimension);
            ToolTipService.SetToolTip(item, item.IsEnabled ? $"Plot {PerformanceAtlasProjection.FriendlyDimension(dimension)}" : $"No usable {PerformanceAtlasProjection.FriendlyDimension(dimension)} observations yet");
        }
    }

    private void OnDimensionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _suppressDimensionEvents || XDimensionSelector is null || YDimensionSelector is null) return;
        var requestedX = SelectedDimension(XDimensionSelector, _x);
        var requestedY = SelectedDimension(YDimensionSelector, _y);
        if (requestedX == requestedY)
        {
            _suppressDimensionEvents = true;
            if (ReferenceEquals(sender, XDimensionSelector)) XDimensionSelector.SelectedIndex = IndexOf(_x);
            else YDimensionSelector.SelectedIndex = IndexOf(_y);
            _suppressDimensionEvents = false;
            return;
        }
        var pair = PerformanceAtlasProjection.ResolveAvailablePair(_observations, requestedX, requestedY);
        if (!pair.IsAvailable)
        {
            AtlasUnavailableText.Text = pair.Explanation ?? "No usable observations are available for that view.";
            AtlasUnavailableMessage.Visibility = Visibility.Visible;
            return;
        }
        _x = pair.X;
        _y = pair.Y;
        _suppressDimensionEvents = true;
        XDimensionSelector.SelectedIndex = IndexOf(_x);
        YDimensionSelector.SelectedIndex = IndexOf(_y);
        _suppressDimensionEvents = false;
        _data = PerformanceAtlasProjection.Build(_observations, _x, _y, BinCount, _efficiencySelector);
        DimensionAvailabilityText.Text = pair.Explanation ?? $"Showing {PerformanceAtlasProjection.FriendlyDimension(_x)} × {PerformanceAtlasProjection.FriendlyDimension(_y)}";
        DimensionsChanged?.Invoke(this, new AtlasDimensionsChangedEventArgs(_x, _y));
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) { if (_initialized) RequestRedraw(); }

    private void RequestRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
            {
                _redrawQueued = false;
                Redraw();
            })) return;
        _redrawQueued = false;
        Redraw();
    }

    private void Redraw()
    {
        AtlasCanvas.Children.Clear();
        FrontierLayer.Children.Clear();
        SelectionLayer.Children.Clear();
        if (_data is null)
        {
            UpdateLabels();
            ResidencySummary.Text = "Waiting for usable operating history";
            SelectionCard.Visibility = Visibility.Collapsed;
            HideCurrentPoint();
            return;
        }
        var width = AtlasCanvas.ActualWidth;
        var height = AtlasCanvas.ActualHeight;
        UpdateLabels();
        ResidencySummary.Text = _data.IncludedObservationIndices.Count == 0
            ? "No usable observations contain both selected metrics"
            : $"{_data.IncludedObservationIndices.Count} observations · {_data.Cells.Count} occupied regions";
        if (width < 80 || height < 80) return;
        DrawGrid(width, height);
        DrawCells(width, height);
        DrawFrontier(width, height);
        RedrawSelection();
        DrawCurrentPoint();
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
            var rectangle = new Rectangle { Width = Math.Max(1, cellWidth - 1), Height = Math.Max(1, cellHeight - 1), RadiusX = 2, RadiusY = 2, Fill = Brush(52, 214, 238, alpha), Stroke = Brush(150, 240, 255, (byte)Math.Clamp(alpha + 20, 0, 235)), StrokeThickness = cell.Density > .66 ? 1 : .5, IsHitTestVisible = false };
            Canvas.SetLeft(rectangle, cell.XBin * cellWidth + .5);
            Canvas.SetTop(rectangle, height - (cell.YBin + 1) * cellHeight + .5);
            AtlasCanvas.Children.Add(rectangle);
        }
    }

    private void DrawFrontier(double width, double height)
    {
        if (_data is null || _envelope is null) return;
        var pressurePowerPair = (_x == PerformanceAtlasDimension.CpuPressure && _y == PerformanceAtlasDimension.PackagePower) || (_x == PerformanceAtlasDimension.PackagePower && _y == PerformanceAtlasDimension.CpuPressure);
        if (!pressurePowerPair) return;
        var candidates = new[] { (_envelope.EcoCeilingPressure, _envelope.EcoPowerFrontierWatts), (_envelope.EfficientCeilingPressure, _envelope.EfficientPowerFrontierWatts), (_envelope.ResponsiveCeilingPressure, _envelope.ResponsivePowerFrontierWatts) };
        var points = candidates.Where(candidate => candidate.Item2 is double watts && double.IsFinite(watts)).Select(candidate => AtlasPoint(candidate.Item1, candidate.Item2!.Value, width, height)).ToArray();
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
            Canvas.SetLeft(dot, point.X - 3); Canvas.SetTop(dot, point.Y - 3); FrontierLayer.Children.Add(dot);
        }
        var label = new TextBlock { Text = "LEARNED FRONTIER", FontSize = 11, Foreground = frontierBrush, Opacity = .82, IsHitTestVisible = false };
        Canvas.SetLeft(label, 6); Canvas.SetTop(label, 4); FrontierLayer.Children.Add(label);
    }

    private void DrawCurrentPoint()
    {
        if (_data is null || _modelExplanation?.CurrentPoint is not ModelCurrentPoint point || CurrentPointLayer.ActualWidth <= 0 || CurrentPointLayer.ActualHeight <= 0)
        {
            HideCurrentPoint();
            return;
        }
        var xValue = CurrentValue(point, _data.XAxis.Dimension);
        var yValue = CurrentValue(point, _data.YAxis.Dimension);
        if (xValue is not double xRaw || yValue is not double yRaw || !double.IsFinite(xRaw) || !double.IsFinite(yRaw))
        {
            HideCurrentPoint();
            return;
        }
        var x = Normalize(xRaw, _data.XAxis) * CurrentPointLayer.ActualWidth;
        var y = CurrentPointLayer.ActualHeight - Normalize(yRaw, _data.YAxis) * CurrentPointLayer.ActualHeight;
        Canvas.SetLeft(CurrentPointHalo, Math.Clamp(x - 10, 0, Math.Max(0, CurrentPointLayer.ActualWidth - 20)));
        Canvas.SetTop(CurrentPointHalo, Math.Clamp(y - 10, 0, Math.Max(0, CurrentPointLayer.ActualHeight - 20)));
        Canvas.SetLeft(CurrentPointDot, Math.Clamp(x - 4.5, 0, Math.Max(0, CurrentPointLayer.ActualWidth - 9)));
        Canvas.SetTop(CurrentPointDot, Math.Clamp(y - 4.5, 0, Math.Max(0, CurrentPointLayer.ActualHeight - 9)));
        YouAreHereLabel.Text = "YOU ARE HERE";
        Canvas.SetLeft(YouAreHereLabel, Math.Clamp(x + 8, 4, Math.Max(4, CurrentPointLayer.ActualWidth - 92)));
        Canvas.SetTop(YouAreHereLabel, Math.Clamp(y - 18, 2, Math.Max(2, CurrentPointLayer.ActualHeight - 20)));
        CurrentPointHalo.Opacity = .72; CurrentPointDot.Opacity = 1; YouAreHereLabel.Opacity = 1;
    }

    private void HideCurrentPoint() { CurrentPointHalo.Opacity = 0; CurrentPointDot.Opacity = 0; YouAreHereLabel.Opacity = 0; }

    private static double? CurrentValue(ModelCurrentPoint point, PerformanceAtlasDimension dimension) => dimension switch
    {
        PerformanceAtlasDimension.CpuPressure => point.CpuPressurePercent,
        PerformanceAtlasDimension.PackagePower => point.PackageWatts,
        PerformanceAtlasDimension.EffectiveClock => point.EffectiveClockMhz,
        PerformanceAtlasDimension.ActiveCores => point.AwakeCores,
        _ => null
    };

    private Point AtlasPoint(double pressure, double power, double width, double height)
    {
        if (_data is null) return new Point();
        var xValue = _x == PerformanceAtlasDimension.CpuPressure ? pressure : power;
        var yValue = _y == PerformanceAtlasDimension.CpuPressure ? pressure : power;
        return new Point(Normalize(xValue, _data.XAxis) * width, height - Normalize(yValue, _data.YAxis) * height);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_data is null || InteractionLayer.ActualWidth <= 0 || InteractionLayer.ActualHeight <= 0)
        {
            HideAtlasHover(true);
            return;
        }
        var point = e.GetCurrentPoint(InteractionLayer).Position;
        var cell = CellAtPoint(point);
        if (cell is null)
        {
            HideAtlasHover(true);
            return;
        }
        AtlasPointerHoverLayer.Children.Clear();
        DrawHoverCell(AtlasPointerHoverLayer, cell, Brush(127, 226, 249, 230), 2.2);
        var observations = cell.ObservationIndices.Where(i => i >= 0 && i < _observations.Count).Select(i => _observations[i]).ToArray();
        AtlasHoverReadout.Text = InspectionExplanationProjection.ForAtlasCell(observations, cell.Density).AsPlainText();
        SetTransient(AtlasHoverCard, true);
        HoverChanged?.Invoke(this, new AtlasHoverChangedEventArgs(cell.ObservationIndices.ToArray()));
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HideAtlasHover(true);

    private void HideAtlasHover(bool notify)
    {
        AtlasPointerHoverLayer.Children.Clear();
        SetTransient(AtlasHoverCard, false);
        if (notify) HoverChanged?.Invoke(this, new AtlasHoverChangedEventArgs(Array.Empty<int>()));
    }

    private void SetTransient(UIElement element, bool visible)
    {
        if (_transientAnimations.Remove(element, out var running)) running.Stop();
        if (_reducedMotion)
        {
            element.Opacity = visible ? 1 : 0;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (visible) element.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation
        {
            From = element.Opacity,
            To = visible ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(90))
        };
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        _transientAnimations[element] = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!_transientAnimations.TryGetValue(element, out var current) || !ReferenceEquals(current, storyboard)) return;
            _transientAnimations.Remove(element);
            element.Opacity = visible ? 1 : 0;
            if (!visible) element.Visibility = Visibility.Collapsed;
        };
        storyboard.Begin();
    }
    private PerformanceAtlasCell? CellAtPoint(Point point)
    {
        if (_data is null || InteractionLayer.ActualWidth <= 0 || InteractionLayer.ActualHeight <= 0) return null;
        if (point.X < 0 || point.X > InteractionLayer.ActualWidth || point.Y < 0 || point.Y > InteractionLayer.ActualHeight) return null;
        var xBin = Math.Clamp((int)Math.Floor(point.X / InteractionLayer.ActualWidth * _data.XAxis.BinCount), 0, _data.XAxis.BinCount - 1);
        var yBinFromTop = Math.Clamp((int)Math.Floor(point.Y / InteractionLayer.ActualHeight * _data.YAxis.BinCount), 0, _data.YAxis.BinCount - 1);
        var yBin = _data.YAxis.BinCount - 1 - yBinFromTop;
        return _data.Cells.FirstOrDefault(candidate => candidate.XBin == xBin && candidate.YBin == yBin);
    }

    private void RedrawLinkedHover()
    {
        AtlasLinkedHoverLayer.Children.Clear();
        if (_data is null || _linkedHoveredObservationIndices.Count == 0 || AtlasHoverLayer.ActualWidth <= 0 || AtlasHoverLayer.ActualHeight <= 0) return;
        foreach (var cell in _data.Cells.Where(cell => cell.ObservationIndices.Any(_linkedHoveredObservationIndices.Contains)))
            DrawHoverCell(AtlasLinkedHoverLayer, cell, Brush(110, 224, 245, 135), 1.4);
    }

    private void DrawHoverCell(Canvas layer, PerformanceAtlasCell cell, Brush stroke, double thickness)
    {
        if (_data is null) return;
        var width = AtlasPlotHost.ActualWidth;
        var height = AtlasPlotHost.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var cellWidth = width / _data.XAxis.BinCount;
        var cellHeight = height / _data.YAxis.BinCount;
        var rectangle = new Rectangle
        {
            Width = Math.Max(1, cellWidth - 2),
            Height = Math.Max(1, cellHeight - 2),
            Stroke = stroke,
            StrokeThickness = thickness,
            RadiusX = 4,
            RadiusY = 4,
            Fill = Brush(95, 220, 245, 18),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, cell.XBin * cellWidth + 1);
        Canvas.SetTop(rectangle, height - (cell.YBin + 1) * cellHeight + 1);
        layer.Children.Add(rectangle);
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_data is null || InteractionLayer.ActualWidth <= 0 || InteractionLayer.ActualHeight <= 0) return;
        var point = e.GetCurrentPoint(InteractionLayer).Position;
        var cell = CellAtPoint(point);
        if (cell is null)
        {
            _selectedObservationIndices.Clear();
            SelectionCard.Visibility = Visibility.Collapsed;
            RedrawSelection();
            SelectionChanged?.Invoke(this, new AtlasSelectionChangedEventArgs(Array.Empty<int>()));
            return;
        }
        _selectedObservationIndices = cell.ObservationIndices.ToHashSet();
        SelectionReadout.Text = InspectionExplanationProjection.ForAtlasCell(
            cell.ObservationIndices.Where(i => i >= 0 && i < _observations.Count).Select(i => _observations[i]).ToArray(),
            cell.Density).AsPlainText();
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
            var outline = new Rectangle { Width = Math.Max(1, cellWidth - 2), Height = Math.Max(1, cellHeight - 2), Stroke = Brush(244, 250, 255, 245), StrokeThickness = 2, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false };
            Canvas.SetLeft(outline, cell.XBin * cellWidth + 1); Canvas.SetTop(outline, SelectionLayer.ActualHeight - (cell.YBin + 1) * cellHeight + 1); SelectionLayer.Children.Add(outline);
        }
    }

    private void UpdateLabels()
    {
        if (_data is null) { XAxisLabel.Text = AxisText(_x, null); YAxisLabel.Text = AxisText(_y, null); return; }
        XAxisLabel.Text = AxisText(_x, _data.XAxis); YAxisLabel.Text = AxisText(_y, _data.YAxis);
    }

    private static string AxisText(PerformanceAtlasDimension dimension, PerformanceAtlasAxis? axis)
    {
        var label = dimension switch { PerformanceAtlasDimension.PackagePower => "PACKAGE POWER", PerformanceAtlasDimension.EffectiveClock => "CPU PERFORMANCE", PerformanceAtlasDimension.ActiveCores => "CORES AWAKE", _ => "CPU PRESSURE" };
        return axis is null || string.IsNullOrWhiteSpace(axis.Unit) ? label : $"{label} ({axis.Unit})";
    }

    private static PerformanceAtlasDimension SelectedDimension(ComboBox box, PerformanceAtlasDimension fallback)
    {
        if (box.SelectedItem is not ComboBoxItem item || item.Tag is null) return fallback;
        return Enum.TryParse<PerformanceAtlasDimension>(item.Tag.ToString(), out var value) ? value : fallback;
    }
    private static int IndexOf(PerformanceAtlasDimension dimension) => dimension switch { PerformanceAtlasDimension.PackagePower => 0, PerformanceAtlasDimension.EffectiveClock => 1, PerformanceAtlasDimension.ActiveCores => 2, _ => 3 };
    private static double Normalize(double value, PerformanceAtlasAxis axis) { var span = axis.DomainMax - axis.DomainMin; return span <= 0 ? 0 : Math.Clamp((value - axis.DomainMin) / span, 0, 1); }
    private static Line AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness) { var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false }; canvas.Children.Add(line); return line; }
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}
