using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Power;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed class TimelineCursorChangedEventArgs(int? observationIndex) : EventArgs
{
    public int? ObservationIndex { get; } = observationIndex;
}

public sealed class TimelineSelectionChangedEventArgs(IReadOnlyList<int> observationIndices) : EventArgs
{
    public IReadOnlyList<int> ObservationIndices { get; } = observationIndices;
}

public sealed class PolicyHandleChangedEventArgs(TimelinePolicyHandleKind kind, EnvelopeTuning candidateTuning) : EventArgs
{
    public TimelinePolicyHandleKind Kind { get; } = kind;
    public EnvelopeTuning CandidateTuning { get; } = candidateTuning;
}
public sealed partial class PerformanceTimelineControl : UserControl
{
    private static readonly OperatingEnvelope DefaultEnvelope = new(25, 55, 80, null, null, null);
    private IReadOnlyList<OperatingObservation> _observations = Array.Empty<OperatingObservation>();
    private OperatingEnvelope _envelope = DefaultEnvelope;
    private PerformanceTimelineData _data = PerformanceTimelineProjection.Build(Array.Empty<OperatingObservation>());
    private double _windowSeconds = 60;
    private double _plotWidth = 1;
    private double _plotHeight = 1;
    private static readonly double[] LaneWeights = [1.4d, 1.1d, 1.5d];
    private const double LaneWeightTotal = 4d;
    private const double LegacyPressureLaneFraction = .25d;
    private HashSet<int> _selectedObservationIndices = [];
    private HashSet<int> _linkedHoveredObservationIndices = [];
    private OperatingEnvelope _learnedEnvelope = DefaultEnvelope;
    private PerformanceEntitlement _learnedEntitlement = PerformanceEntitlement.LegacyPerformance;
    private EnvelopeTuning _candidateTuning = EnvelopeTuning.Learned;
    private bool _tuneMode;
    private TimelinePolicyHandleKind? _draggingPolicyHandle;
    private bool _reducedMotion;
    private readonly Dictionary<UIElement, Storyboard> _transientAnimations = [];
    private bool _redrawQueued;
    private bool _presentationApplied;
    private IReadOnlyList<ContinuitySample> _coreThreadHistory = Array.Empty<ContinuitySample>();
    private CoreStateTimelineData _coreStateTimeline = CoreStateTimelineData.Empty;
    private GraduatedCapacityTimelineData _capacityTimeline = GraduatedCapacityTimelineData.Empty;
    private GraduatedCoreActuatorStatus? _coreActuatorStatus;
    private ProcessorPolicySnapshot? _processorPolicySnapshot;

    public PerformanceTimelineControl()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => RequestRedraw();
    }

    public TimelinePresentation Presentation { get; private set; } = TimelinePresentation.Expanded;

    public void SetPresentation(TimelinePresentation presentation)
    {
        if (_presentationApplied && Presentation == presentation) return;
        _presentationApplied = true;
        Presentation = presentation;
        var glance = presentation == TimelinePresentation.Glance;
        TimelineHeader.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        GlanceSummary.Visibility = glance ? Visibility.Visible : Visibility.Collapsed;
        LaneLabels.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        TimelineFooter.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        var labelWidth = glance ? 0d : presentation == TimelinePresentation.Compact ? 92d : 104d;
        LaneLabelColumn.Width = new GridLength(labelWidth);
        TimelineFooter.Margin = new Thickness(glance ? 0d : labelWidth + 8d, 0, 0, 0);
        TimelineRoot.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 72,
            TimelinePresentation.Compact => 156,
            TimelinePresentation.Expanded => 230,
            _ => 280
        };
        TimelinePlotHost.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 58,
            TimelinePresentation.Compact => 120,
            TimelinePresentation.Expanded => 190,
            _ => 235
        };
        RequestRedraw();
    }
    public event EventHandler<TimelineCursorChangedEventArgs>? CursorChanged;
    public event EventHandler<TimelineSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<PolicyHandleChangedEventArgs>? PolicyHandleChanged;

    public void SetCoreThreadHistory(IReadOnlyList<ContinuitySample>? history)
    {
        _coreThreadHistory = history?.ToArray() ?? Array.Empty<ContinuitySample>();
        _coreStateTimeline = CoreStateTimelineProjection.Build(_coreThreadHistory, _data.WindowStart, _data.Latest);
        _capacityTimeline = GraduatedCapacityEntitlementModel.Build(_coreThreadHistory, _data.WindowStart, _data.Latest);
        UpdateCoreStateLabel();
        RequestRedraw();
    }

    public void SetProcessorPolicySnapshot(ProcessorPolicySnapshot? snapshot)
    {
        _processorPolicySnapshot = snapshot;
        UpdateCoreStateLabel();
    }
    public void SetCoreActuatorStatus(GraduatedCoreActuatorStatus? status)
    {
        _coreActuatorStatus = status;
        UpdateCoreStateLabel();
    }
    public void SetPolicyContext(OperatingEnvelope learnedEnvelope, PerformanceEntitlement learnedEntitlement, EnvelopeTuning candidateTuning)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(candidateTuning);
        _learnedEnvelope = learnedEnvelope;
        _learnedEntitlement = learnedEntitlement;
        _candidateTuning = candidateTuning;
        _envelope = candidateTuning.ApplyTo(learnedEnvelope);
        UpdatePressureContextLabel();
        RequestRedraw();
    }

    public void SetTuneMode(bool enabled)
    {
        _tuneMode = enabled;
        PolicyHandleLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) _draggingPolicyHandle = null;
        RequestRedraw();
    }

    public void SetSelectedObservationIndices(IEnumerable<int>? observationIndices)
    {
        _selectedObservationIndices = observationIndices is null
            ? []
            : observationIndices.Where(index => index >= 0 && index < _observations.Count).ToHashSet();
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
        InspectionCursor.Opacity = InspectionCursor.Visibility == Visibility.Visible ? 1 : 0;
        CursorCard.Opacity = CursorCard.Visibility == Visibility.Visible ? 1 : 0;
    }

    public void Apply(
        IReadOnlyList<OperatingObservation> observations,
        OperatingEnvelope? envelope = null,
        double windowSeconds = 60,
        PerformanceTimelineMode mode = PerformanceTimelineMode.Stacked)
    {
        _observations = observations?.ToArray() ?? Array.Empty<OperatingObservation>();
        _envelope = envelope ?? DefaultEnvelope;
        _windowSeconds = Math.Max(1, windowSeconds);
        _data = PerformanceTimelineProjection.Build(_observations, _windowSeconds, mode);
        _coreStateTimeline = CoreStateTimelineProjection.Build(_coreThreadHistory, _data.WindowStart, _data.Latest);
        _capacityTimeline = GraduatedCapacityEntitlementModel.Build(_coreThreadHistory, _data.WindowStart, _data.Latest);
        WindowLabel.Text = mode == PerformanceTimelineMode.NormalizedOverlay ? $"{_windowSeconds:0} SEC · NORMALIZED" : $"{_windowSeconds:0} SEC";
        WindowStartLabel.Text = $"-{_windowSeconds:0}s";
        UpdateLiveLabels();
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => RequestRedraw();

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
        var width = GridLayer.ActualWidth;
        var height = GridLayer.ActualHeight;
        if (width < 80 || height < 80) return;

        _plotWidth = width;
        _plotHeight = height;
        GridLayer.Children.Clear();
        ActorDecisionLayer.Children.Clear();
        SelectionLayer.Children.Clear();

        var pressureLane = LaneBounds(0, height);
        DrawPressureContext(pressureLane.Top, pressureLane.Height);
        var light = ActualTheme == ElementTheme.Light;
        var grid = light ? Brush(17, 34, 51, 24) : Brush(255, 255, 255, 18);
        var text = light ? Brush(17, 34, 51, 118) : Brush(255, 255, 255, 105);
        var baseline = light ? Brush(17, 34, 51, 65) : Brush(255, 255, 255, 58);
        var series = new[]
        {
            light ? Brush(12, 145, 132, 235) : Brush(80, 224, 207, 240),
            light ? Brush(68, 105, 196, 225) : Brush(123, 168, 255, 235),
            light ? Brush(136, 88, 192, 225) : Brush(190, 143, 255, 235),
            light ? Brush(194, 112, 43, 225) : Brush(255, 177, 92, 235)
        };

        var pressureGeometry = LaneBounds(0, height);
        AddLine(GridLayer, 0, pressureGeometry.Top + pressureGeometry.Height * .5, width, pressureGeometry.Top + pressureGeometry.Height * .5, grid, 1).Opacity = .55;
        UpdateTracePath(0, _data.Lanes[0], pressureGeometry.Top + 3, Math.Max(1, pressureGeometry.Height - 6), series[0]);
        AddText(GridLayer, FormatDomain(_data.Lanes[0]), Math.Max(0, width - 86), pressureGeometry.Top + 1, 11, text);

        var powerGeometry = LaneBounds(1, height);
        AddLine(GridLayer, 0, powerGeometry.Top, width, powerGeometry.Top, grid, 1);
        AddLine(GridLayer, 0, powerGeometry.Top + powerGeometry.Height * .5, width, powerGeometry.Top + powerGeometry.Height * .5, grid, 1).Opacity = .55;
        UpdateTracePath(1, _data.Lanes[1], powerGeometry.Top + 3, Math.Max(1, powerGeometry.Height - 6), series[1]);
        UpdateTracePath(2, _data.Lanes[2], powerGeometry.Top + 3, Math.Max(1, powerGeometry.Height - 6), series[2]);
        if (_data.Lanes[2].DomainMin <= 100d && _data.Lanes[2].DomainMax >= 100d)
        {
            var performanceSpan = Math.Max(double.Epsilon, _data.Lanes[2].DomainMax - _data.Lanes[2].DomainMin);
            var normalized = Math.Clamp((100d - _data.Lanes[2].DomainMin) / performanceSpan, 0d, 1d);
            var baselineY = powerGeometry.Top + 3 + (1d - normalized) * Math.Max(1, powerGeometry.Height - 6);
            AddLine(GridLayer, 0, baselineY, width, baselineY, baseline, .8).Opacity = .42;
        }
        AddText(GridLayer, FormatDomain(_data.Lanes[1]), Math.Max(0, width - 86), powerGeometry.Top + 1, 11, text);
        AddText(GridLayer, $"PERF {FormatDomain(_data.Lanes[2])}", Math.Max(0, width - 116), powerGeometry.Top + 14, 11, series[2]);

        var coreLane = LaneBounds(3, height);
        AddLine(GridLayer, 0, coreLane.Top, width, coreLane.Top, grid, 1);
        DrawCoreStateTimeline(coreLane.Top, coreLane.Height, series[3], text);

        RedrawPolicy();
        DrawDecisionEvents(height);
        RedrawSelection();
    }

    private void UpdateTracePath(int laneIndex, TimelineLaneProjection lane, double top, double height, Brush stroke)
    {
        var path = TracePath(laneIndex);
        path.Stroke = stroke;
        path.Clip = new RectangleGeometry { Rect = new Rect(0, top, _plotWidth, height) };
        var samples = lane.Points
            .Select(point => point.Y is double y
                ? (Point?)new Point(point.X * _plotWidth, top + (1 - y) * height)
                : null)
            .ToArray();
        var maximumGapX = Math.Clamp(_plotWidth * 10d / Math.Max(1d, _windowSeconds), 1d, _plotWidth);
        path.Data = ToPathGeometry(ShapePreservingCurve.BuildSparseObservations(samples, maximumGapX));
    }

    private Microsoft.UI.Xaml.Shapes.Path TracePath(int laneIndex) => laneIndex switch
    {
        0 => CpuTracePath,
        1 => PowerTracePath,
        2 => ClockTracePath,
        _ => throw new ArgumentOutOfRangeException(nameof(laneIndex))
    };


    private void DrawCoreStateTimeline(double top, double laneHeight, Brush activeBrush, Brush labelBrush)
    {
        var data = _coreStateTimeline;
        if (data.Samples.Count == 0 || data.TotalCores <= 0)
        {
            CoreActivePath.Data = CoreActiveMediumPath.Data = CoreActiveHighPath.Data = CoreActiveHotPath.Data = CoreAwakePath.Data = CoreParkedPath.Data = null;
            DrawCapacityOverlay(top, laneHeight);
            return;
        }

        var light = ActualTheme == ElementTheme.Light;
        CoreActivePath.Fill = activeBrush;
        CoreActiveMediumPath.Fill = activeBrush;
        CoreActiveHighPath.Fill = activeBrush;
        CoreActiveHotPath.Fill = activeBrush;
        CoreActivePath.Opacity = .38;
        CoreActiveMediumPath.Opacity = .58;
        CoreActiveHighPath.Opacity = .78;
        CoreActiveHotPath.Opacity = .98;
        CoreAwakePath.Fill = light ? Brush(194, 112, 43, 135) : Brush(255, 177, 92, 125);
        CoreParkedPath.Fill = light ? Brush(17, 34, 51, 46) : Brush(255, 255, 255, 34);

        var usableTop = top + 3;
        var usableHeight = Math.Max(8, laneHeight - 6);
        var rowPitch = usableHeight / data.TotalCores;
        var cellGapY = Math.Clamp(rowPitch * .16, .25, .8);
        var activeLow = new List<Rect>();
        var activeMedium = new List<Rect>();
        var activeHigh = new List<Rect>();
        var activeHot = new List<Rect>();
        var awakeCells = new List<Rect>();
        var parkedCells = new List<Rect>();
        var samples = data.Samples;
        var fallbackWidth = samples.Count > 1
            ? Math.Max(1, Median(samples.Zip(samples.Skip(1), (a, b) => (b.At - a.At).TotalSeconds).Where(seconds => seconds > 0).ToArray()) / Math.Max(1d, _data.WindowSeconds) * _plotWidth)
            : Math.Max(2, _plotWidth / Math.Max(1d, _data.WindowSeconds));

        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var x = Math.Clamp((sample.At - _data.WindowStart).TotalSeconds / Math.Max(1d, _data.WindowSeconds), 0d, 1d) * _plotWidth;
            var nextX = i + 1 < samples.Count
                ? Math.Clamp((samples[i + 1].At - _data.WindowStart).TotalSeconds / Math.Max(1d, _data.WindowSeconds), 0d, 1d) * _plotWidth
                : Math.Min(_plotWidth, x + fallbackWidth);
            var sliceSpan = Math.Max(1, nextX - x);
            var cellWidth = Math.Max(1, sliceSpan - Math.Min(.8, sliceSpan * .10));
            var row = 0;
            AddLoadCells(activeLow, activeMedium, activeHigh, activeHot, sample.ActiveCoreLoadsPercent ?? Array.Empty<double>(), ref row, x, cellWidth, usableTop, usableHeight, rowPitch, cellGapY);
            AddStateCells(awakeCells, sample.AwakeIdleCores, ref row, x, cellWidth, usableTop, usableHeight, rowPitch, cellGapY, data.TotalCores);
            AddStateCells(parkedCells, sample.ParkedCores, ref row, x, cellWidth, usableTop, usableHeight, rowPitch, cellGapY, data.TotalCores);
        }

        CoreActivePath.Data = BuildCoreCellGeometry(activeLow);
        CoreActiveMediumPath.Data = BuildCoreCellGeometry(activeMedium);
        CoreActiveHighPath.Data = BuildCoreCellGeometry(activeHigh);
        CoreActiveHotPath.Data = BuildCoreCellGeometry(activeHot);
        CoreAwakePath.Data = BuildCoreCellGeometry(awakeCells);
        CoreParkedPath.Data = BuildCoreCellGeometry(parkedCells);
        DrawCapacityOverlay(top, laneHeight);

        for (var q = 1; q < 4; q++)
        {
            var y = usableTop + usableHeight * (1 - q / 4d);
            AddLine(GridLayer, 0, y, _plotWidth, y, labelBrush, .45).Opacity = .18;
        }
    }

    private void DrawCapacityOverlay(double top, double laneHeight)
    {
        if (_capacityTimeline.Samples.Count == 0)
        {
            RequestedCapacityPath.Data = DeliveredCapacityPath.Data = null;
            return;
        }

        var light = ActualTheme == ElementTheme.Light;
        RequestedCapacityPath.Stroke = light ? Brush(16, 132, 172, 245) : Brush(98, 220, 255, 245);
        DeliveredCapacityPath.Stroke = light ? Brush(72, 82, 96, 155) : Brush(235, 241, 248, 155);
        var innerTop = top + 3d;
        var innerHeight = Math.Max(8d, laneHeight - 6d);
        Point? PointFor(GraduatedCapacitySample sample, double value)
        {
            var x = Math.Clamp((sample.At - _data.WindowStart).TotalSeconds / Math.Max(1d, _data.WindowSeconds), 0d, 1d) * _plotWidth;
            var y = innerTop + (1d - Math.Clamp(value / 100d, 0d, 1d)) * innerHeight;
            return new Point(x, y);
        }

        var requested = _capacityTimeline.Samples.Select(sample => PointFor(sample, sample.RequestedCapacityPercent)).ToArray();
        var delivered = _capacityTimeline.Samples.Select(sample => PointFor(sample, sample.DeliveredCapacityPercent)).ToArray();
        var maximumGapX = Math.Clamp(_plotWidth * 10d / Math.Max(1d, _windowSeconds), 1d, _plotWidth);
        RequestedCapacityPath.Data = ToPathGeometry(ShapePreservingCurve.BuildSparseObservations(requested, maximumGapX));
        DeliveredCapacityPath.Data = ToPathGeometry(ShapePreservingCurve.BuildSparseObservations(delivered, maximumGapX));
    }
    private static void AddLoadCells(
        List<Rect> low,
        List<Rect> medium,
        List<Rect> high,
        List<Rect> hot,
        IReadOnlyList<double> loads,
        ref int row,
        double x,
        double width,
        double top,
        double height,
        double rowPitch,
        double gapY)
    {
        foreach (var raw in loads)
        {
            var load = Math.Clamp(raw, 0d, 100d);
            var y = top + height - (row + 1) * rowPitch + gapY / 2;
            var rect = new Rect(x, y, width, Math.Max(.7, rowPitch - gapY));
            if (load >= 75) hot.Add(rect);
            else if (load >= 50) high.Add(rect);
            else if (load >= 25) medium.Add(rect);
            else low.Add(rect);
            row++;
        }
    }

    private static void AddStateCells(List<Rect> target, int count, ref int row, double x, double width, double top, double height, double rowPitch, double gapY, int total)
    {
        var bounded = Math.Clamp(count, 0, Math.Max(0, total - row));
        for (var i = 0; i < bounded; i++, row++)
        {
            var y = top + height - (row + 1) * rowPitch + gapY / 2;
            target.Add(new Rect(x, y, width, Math.Max(.7, rowPitch - gapY)));
        }
    }

    private static Geometry BuildCoreCellGeometry(IEnumerable<Rect> cells)
    {
        var group = new GeometryGroup();
        foreach (var rect in cells)
            group.Children.Add(new RectangleGeometry { Rect = rect });
        return group;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 1;
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }
    private static PathGeometry ToPathGeometry(IReadOnlyList<CurveFigure> figures)
    {
        var geometry = new PathGeometry();
        foreach (var model in figures)
        {
            var figure = new PathFigure { StartPoint = model.Start, IsClosed = false };
            if (model.Segments.Count == 0)
            {
                figure.Segments.Add(new LineSegment { Point = model.Start });
            }
            else
            {
                foreach (var segment in model.Segments)
                {
                    figure.Segments.Add(new BezierSegment
                    {
                        Point1 = segment.Control1,
                        Point2 = segment.Control2,
                        Point3 = segment.End
                    });
                }
            }
            geometry.Figures.Add(figure);
        }
        return geometry;
    }
    private void DrawPressureContext(double top, double laneHeight)
    {
        if (_plotWidth <= 1 || laneHeight <= 1) return;
        var context = PressureZoneProjection.Build(_learnedEnvelope, _candidateTuning);
        var innerTop = top + 4d;
        var innerHeight = Math.Max(1d, laneHeight - 8d);
        double Y(double percent) => innerTop + (1d - Math.Clamp(percent / 100d, 0d, 1d)) * innerHeight;
        var light = ActualTheme == ElementTheme.Light;
        foreach (var band in context.Bands)
        {
            var y1 = Y(band.MaximumPercent);
            var y2 = Y(band.MinimumPercent);
            var fill = band.Zone switch
            {
                EnvelopeZone.Eco => light ? Brush(40, 132, 82, 15) : Brush(91, 214, 139, 17),
                EnvelopeZone.Efficient => light ? Brush(21, 126, 157, 13) : Brush(75, 202, 235, 15),
                EnvelopeZone.Responsive => light ? Brush(132, 83, 181, 12) : Brush(203, 148, 255, 14),
                _ => light ? Brush(177, 92, 53, 10) : Brush(255, 151, 108, 12)
            };
            var rect = new Rectangle { Width = _plotWidth, Height = Math.Max(0d, y2 - y1), Fill = fill, IsHitTestVisible = false };
            Canvas.SetLeft(rect, 0);
            Canvas.SetTop(rect, y1);
            GridLayer.Children.Add(rect);
        }
        if (_tuneMode) return;
        foreach (var item in new[]
        {
            (TimelinePolicyHandleKind.EcoPressure, context.EcoThreshold),
            (TimelinePolicyHandleKind.EfficientPressure, context.EfficientThreshold),
            (TimelinePolicyHandleKind.ResponsivePressure, context.ResponsiveThreshold)
        })
        {
            var y = Y(item.Item2);
            var line = AddLine(GridLayer, 0, y, _plotWidth, y, PolicyBrush(item.Item1, true), .9);
            line.Opacity = .42;
        }
    }

    private void UpdatePressureContextLabel()
    {
        var context = PressureZoneProjection.Build(_learnedEnvelope, _candidateTuning);
        CpuThresholdText.Text = $"T {context.EcoThreshold:0}/{context.EfficientThreshold:0}/{context.ResponsiveThreshold:0}";
        var explanation = $"Actual governor pressure is current CPU utilization on a 0-100% scale. Thresholds: Eco {context.EcoThreshold:0}%, Efficient {context.EfficientThreshold:0}%, Responsive {context.ResponsiveThreshold:0}%; above Responsive is Boost.";
        ToolTipService.SetToolTip(CpuPressureLabel, explanation);
        ToolTipService.SetToolTip(CpuThresholdText, explanation);
    }
    private void RedrawPolicy()
    {
        if (_plotWidth <= 1 || _plotHeight <= 1) return;
        PolicyValueLayer.Children.Clear();
        PolicyTimeLayer.Children.Clear();
        PolicyHandleLayer.Children.Clear();

        var overlay = TimelinePolicyOverlayProjection.Build(_data, _learnedEnvelope, _learnedEntitlement, _candidateTuning);
        foreach (var rail in overlay.ValueRails)
        {
            if (!_tuneMode && rail.Metric == PerformanceTimelineMetric.CpuPressure) continue;
            var laneIndex = LaneIndex(rail.Metric);
            var learnedY = PolicyValueY(laneIndex, rail.LearnedNormalizedY);
            var candidateY = PolicyValueY(laneIndex, rail.CandidateNormalizedY);
            var learnedBrush = PolicyBrush(rail.Kind, false);
            var candidateBrush = PolicyBrush(rail.Kind, true);

            if (_tuneMode && Math.Abs(learnedY - candidateY) > .5)
            {
                var ghost = AddLine(PolicyValueLayer, 0, learnedY, _plotWidth, learnedY, learnedBrush, .75);
                ghost.StrokeDashArray = new DoubleCollection { 1, 6 };
                ghost.Opacity = .42;
            }

            // Policy is context, not the data. Keep it quiet and solid so telemetry remains dominant.
            var line = AddLine(PolicyValueLayer, 0, candidateY, _plotWidth, candidateY, candidateBrush, rail.Editable ? 1.15 : .7);
            line.Opacity = rail.Editable ? (_tuneMode ? .72 : .30) : .18;

            if (_tuneMode && rail.Editable)
            {
                var handle = new Ellipse { Width = 14, Height = 14, Fill = candidateBrush, Stroke = Brush(8, 20, 32, 190), StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(handle, Math.Max(0, _plotWidth - 14));
                Canvas.SetTop(handle, candidateY - 7);
                PolicyHandleLayer.Children.Add(handle);
            }
        }

        var bandIndex = 0;
        foreach (var band in overlay.TimeBands)
        {
            var learnedX = band.LearnedStartX * _plotWidth;
            var candidateX = band.CandidateStartX * _plotWidth;
            var brush = PolicyBrush(band.Kind, true);
            if (_tuneMode && Math.Abs(learnedX - candidateX) > .5)
            {
                var ghost = AddLine(PolicyTimeLayer, learnedX, 0, learnedX, _plotHeight, PolicyBrush(band.Kind, false), .75);
                ghost.StrokeDashArray = new DoubleCollection { 1, 6 };
                ghost.Opacity = .38;
            }

            // Timing policy is a clean vertical edge. Full-height overlapping fills obscured the traces.
            var edge = AddLine(PolicyTimeLayer, candidateX, 0, candidateX, _plotHeight, brush, _tuneMode ? 1.15 : .8);
            edge.Opacity = _tuneMode ? .62 : .24;

            if (_tuneMode && band.Editable)
            {
                var handle = new Rectangle { Width = 12, Height = 20, RadiusX = 6, RadiusY = 6, Fill = brush, Stroke = Brush(8, 20, 32, 190), StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(handle, Math.Clamp(candidateX - 6, 0, Math.Max(0, _plotWidth - 12)));
                Canvas.SetTop(handle, Math.Max(0, _plotHeight - 23 - bandIndex * 21));
                PolicyHandleLayer.Children.Add(handle);
            }
            bandIndex++;
        }
    }

    private static (double Top, double Height) LaneBounds(int dataLaneIndex, double totalHeight)
    {
        var laneIndex = dataLaneIndex switch
        {
            2 => 1, // CPU performance overlays package power.
            3 => 2, // Capacity / cores is the third visual row.
            _ => dataLaneIndex
        };
        if (laneIndex < 0 || laneIndex >= LaneWeights.Length) return (0d, Math.Max(1d, totalHeight));
        var topWeight = 0d;
        for (var i = 0; i < laneIndex; i++) topWeight += LaneWeights[i];
        return (totalHeight * topWeight / LaneWeightTotal, totalHeight * LaneWeights[laneIndex] / LaneWeightTotal);
    }

    private int LaneIndex(PerformanceTimelineMetric metric)
    {
        for (var i = 0; i < _data.Lanes.Count; i++)
            if (_data.Lanes[i].Metric == metric) return i;
        return 0;
    }

    private double PolicyValueY(int laneIndex, double normalizedY)
    {
        var geometry = LaneBounds(laneIndex, _plotHeight);
        return geometry.Top + 4 + Math.Clamp(normalizedY, 0d, 1d) * Math.Max(1, geometry.Height - 8);
    }

    private TimelinePolicyHandleKind? HitTestPolicyHandle(Point point)
    {
        if (!_tuneMode || _plotWidth <= 1 || _plotHeight <= 1) return null;
        var overlay = TimelinePolicyOverlayProjection.Build(_data, _learnedEnvelope, _learnedEntitlement, _candidateTuning);
        TimelinePolicyHandleKind? winner = null;
        var best = 14d;
        foreach (var rail in overlay.ValueRails.Where(rail => rail.Editable))
        {
            var distance = Math.Abs(point.Y - PolicyValueY(LaneIndex(rail.Metric), rail.CandidateNormalizedY));
            if (distance <= best) { best = distance; winner = rail.Kind; }
        }
        foreach (var band in overlay.TimeBands.Where(band => band.Editable))
        {
            var distance = Math.Abs(point.X - band.CandidateStartX * _plotWidth);
            if (distance <= best) { best = distance; winner = band.Kind; }
        }
        return winner;
    }

    private void ApplyPolicyDrag(Point point)
    {
        if (_draggingPolicyHandle is not TimelinePolicyHandleKind kind) return;
        var normalizedCanvasY = point.Y / Math.Max(1, _plotHeight);
        if (kind is TimelinePolicyHandleKind.EcoPressure or TimelinePolicyHandleKind.EfficientPressure or TimelinePolicyHandleKind.ResponsivePressure)
        {
            var pressureLane = LaneBounds(0, _plotHeight);
            var withinPressureLane = Math.Clamp((point.Y - pressureLane.Top) / Math.Max(1d, pressureLane.Height), 0d, 1d);
            normalizedCanvasY = withinPressureLane * LegacyPressureLaneFraction;
        }
        var updated = TimelinePolicyInteraction.ApplyDrag(
            _candidateTuning,
            kind,
            point.X / Math.Max(1, _plotWidth),
            normalizedCanvasY,
            _learnedEnvelope,
            _learnedEntitlement,
            _data.WindowSeconds);
        if (updated == _candidateTuning) return;
        _candidateTuning = updated;
        _envelope = updated.ApplyTo(_learnedEnvelope);
        RedrawPolicy();
        PolicyHandleChanged?.Invoke(this, new PolicyHandleChangedEventArgs(kind, updated));
    }

    private SolidColorBrush PolicyBrush(TimelinePolicyHandleKind kind, bool strong)
    {
        var light = ActualTheme == ElementTheme.Light;
        var alpha = (byte)(strong ? 210 : 90);
        return kind switch
        {
            TimelinePolicyHandleKind.EcoPressure or TimelinePolicyHandleKind.EcoPowerFrontier => light ? Brush(40, 132, 82, alpha) : Brush(91, 214, 139, alpha),
            TimelinePolicyHandleKind.EfficientPressure or TimelinePolicyHandleKind.EfficientPowerFrontier => light ? Brush(21, 126, 157, alpha) : Brush(75, 202, 235, alpha),
            TimelinePolicyHandleKind.ResponsivePressure or TimelinePolicyHandleKind.ResponsivePowerFrontier => light ? Brush(132, 83, 181, alpha) : Brush(203, 148, 255, alpha),
            TimelinePolicyHandleKind.QualificationDuration => light ? Brush(157, 119, 28, alpha) : Brush(245, 204, 92, alpha),
            TimelinePolicyHandleKind.LeaseDuration => light ? Brush(70, 81, 181, alpha) : Brush(148, 153, 255, alpha),
            _ => light ? Brush(177, 92, 53, alpha) : Brush(255, 151, 108, alpha)
        };
    }

    private SolidColorBrush PolicyFill(TimelinePolicyHandleKind kind)
    {
        var color = PolicyBrush(kind, true).Color;
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(10, color.R, color.G, color.B));
    }
    private void DrawDecisionEvents(double height)
    {
        var light = ActualTheme == ElementTheme.Light;
        foreach (var marker in _data.Events.TakeLast(16))
        {
            var x = marker.X * _plotWidth;
            var brush = marker.Decision switch
            {
                EnvelopeDecisionKind.Brake => light ? Brush(194, 91, 47, 185) : Brush(255, 133, 89, 205),
                EnvelopeDecisionKind.Lease => light ? Brush(80, 85, 190, 185) : Brush(148, 153, 255, 205),
                EnvelopeDecisionKind.Qualifying => light ? Brush(163, 128, 33, 175) : Brush(245, 204, 92, 195),
                _ => light ? Brush(40, 61, 76, 90) : Brush(255, 255, 255, 80)
            };
            // Events remain visible without slicing through every telemetry lane.
            var tickHeight = marker.Decision == EnvelopeDecisionKind.Brake ? 12d : 8d;
            var tick = AddLine(ActorDecisionLayer, x, Math.Max(0, height - tickHeight), x, height, brush, marker.Decision == EnvelopeDecisionKind.Brake ? 2.5 : 2);
            tick.Opacity = marker.Decision == EnvelopeDecisionKind.None ? .38 : .72;
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CursorLayer).Position;
        if (_tuneMode && HitTestPolicyHandle(point) is TimelinePolicyHandleKind handle)
        {
            _draggingPolicyHandle = handle;
            CursorLayer.CapturePointer(e.Pointer);
            ApplyPolicyDrag(point);
            e.Handled = true;
            return;
        }
        var index = ObservationIndexAt(point);
        _selectedObservationIndices = index is int observationIndex ? [observationIndex] : [];
        RedrawSelection();
        SelectionChanged?.Invoke(this, new TimelineSelectionChangedEventArgs(_selectedObservationIndices.OrderBy(value => value).ToArray()));
        e.Handled = true;
    }

    private int? ObservationIndexAt(Point point)
    {
        if (_data.Lanes.Count == 0 || _data.Lanes[0].Points.Count == 0) return null;
        if (point.X < 0 || point.X > _plotWidth || point.Y < 0 || point.Y > _plotHeight) return null;
        var normalized = Math.Clamp(point.X / Math.Max(1, _plotWidth), 0, 1);
        return PerformanceTimelineProjection.FindNearestObservationIndex(_data, normalized);
    }

    private void RedrawSelection()
    {
        SelectionLayer.Children.Clear();
        if (_selectedObservationIndices.Count == 0 || _data.Lanes.Count == 0 || _plotWidth <= 1 || _plotHeight <= 1) return;
        var points = _data.Lanes[0].Points.Where(point => _selectedObservationIndices.Contains(point.ObservationIndex)).OrderBy(point => point.X).ToArray();
        if (points.Length == 0) return;
        var selectionBrush = Brush(244, 250, 255, 220);
        if (points.Length > 1)
        {
            var left = points[0].X * _plotWidth;
            var right = points[^1].X * _plotWidth;
            var band = new Rectangle { Width = Math.Max(3, right - left), Height = _plotHeight, Fill = Brush(85, 214, 242, 22), Stroke = Brush(85, 214, 242, 75), StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetLeft(band, left); Canvas.SetTop(band, 0); SelectionLayer.Children.Add(band);
        }
        foreach (var point in points) { var x = point.X * _plotWidth; AddLine(SelectionLayer, x, 0, x, _plotHeight, selectionBrush, 1.5); }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(CursorLayer).Position;
        if (_draggingPolicyHandle is not null)
        {
            ApplyPolicyDrag(p);
            e.Handled = true;
            return;
        }
        if (p.X < 0 || p.X > _plotWidth || p.Y < 0 || p.Y > _plotHeight)
        {
            HideCursor();
            return;
        }

        if (_tuneMode && HitTestPolicyHandle(p) is TimelinePolicyHandleKind policyHandle)
        {
            ShowPolicyInspection(policyHandle, p);
            return;
        }

        if (_data.Lanes.Count == 0 || _data.Lanes[0].Points.Count == 0)
        {
            HideCursor();
            return;
        }
        var normalized = Math.Clamp(p.X / Math.Max(1, _plotWidth), 0, 1);
        var index = PerformanceTimelineProjection.FindNearestObservationIndex(_data, normalized);
        if (index is not int observationIndex || observationIndex < 0 || observationIndex >= _observations.Count)
        {
            HideCursor();
            return;
        }

        PolicyHoverLayer.Children.Clear();
        var observation = _observations[observationIndex];
        var timelinePoint = _data.Lanes[0].Points.First(point => point.ObservationIndex == observationIndex);
        var x = timelinePoint.X * _plotWidth;
        InspectionCursor.X1 = InspectionCursor.X2 = x;
        InspectionCursor.Y1 = 0;
        InspectionCursor.Y2 = _plotHeight;
        SetTransient(InspectionCursor, true);
        var explanation = InspectionExplanationProjection.ForObservation(observation).AsPlainText();
        var pressure = FindDemandPressureNear(observation.At);
        var capacity = FindCapacityNear(observation.At);
        var pressureText = pressure is null
            ? string.Empty
            : $"\nPressure {pressure.PressurePercent:0}% ({pressure.Driver}) · demand {pressure.DemandPercent:0}% · available {pressure.AvailableCapacityPercent:0}% · saturation {pressure.CapacitySaturationPercent:0}% · queue {pressure.QueueLength:0.##} ({pressure.QueuePressurePercent:0}% pressure)";
        var capacityText = capacity is null
            ? string.Empty
            : $"\nCapacity request {capacity.RequestedCapacityPercent:0}% · delivered {capacity.DeliveredCapacityPercent:0}% · ideal {capacity.IdealCapacityPercent:0}% · target sat {capacity.TargetSaturationPercent:0}% · ramp {capacity.RampPercentPerSecond:+0.0;-0.0;0.0}%/s · sustained {capacity.SustainedPressurePercent:0}% · burst {capacity.BurstAgeSeconds:0.0}s ({capacity.Driver})";
        CursorReadout.Text = explanation + pressureText + capacityText;
        SetTransient(CursorCard, true);
        CursorChanged?.Invoke(this, new TimelineCursorChangedEventArgs(observationIndex));
    }
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingPolicyHandle is null) HideCursor();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingPolicyHandle is null) return;
        ApplyPolicyDrag(e.GetCurrentPoint(CursorLayer).Position);
        _draggingPolicyHandle = null;
        CursorLayer.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _draggingPolicyHandle = null;
        CursorLayer.ReleasePointerCapture(e.Pointer);
    }
    private void ShowPolicyInspection(TimelinePolicyHandleKind kind, Point point)
    {
        PolicyHoverLayer.Children.Clear();
        SetTransient(InspectionCursor, false);
        var envelope = _candidateTuning.ApplyTo(_learnedEnvelope);
        var entitlement = _candidateTuning.ApplyTo(_learnedEntitlement);
        var value = kind switch
        {
            TimelinePolicyHandleKind.EcoPressure => envelope.EcoCeilingPressure,
            TimelinePolicyHandleKind.EfficientPressure => envelope.EfficientCeilingPressure,
            TimelinePolicyHandleKind.ResponsivePressure => envelope.ResponsiveCeilingPressure,
            TimelinePolicyHandleKind.EcoPowerFrontier => envelope.EcoPowerFrontierWatts ?? 0,
            TimelinePolicyHandleKind.EfficientPowerFrontier => envelope.EfficientPowerFrontierWatts ?? 0,
            TimelinePolicyHandleKind.ResponsivePowerFrontier => envelope.ResponsivePowerFrontierWatts ?? 0,
            _ => 0
        };
        var duration = kind switch
        {
            TimelinePolicyHandleKind.QualificationDuration => entitlement.QualificationDuration,
            TimelinePolicyHandleKind.LeaseDuration => entitlement.LeaseDuration,
            TimelinePolicyHandleKind.ReleaseHysteresis => entitlement.ReleaseHysteresis,
            _ => TimeSpan.Zero
        };
        CursorReadout.Text = InspectionExplanationProjection.ForPolicyHandle(kind, value, duration).AsPlainText();
        SetTransient(CursorCard, true);
        DrawPolicyHover(kind);
        CursorChanged?.Invoke(this, new TimelineCursorChangedEventArgs(null));
    }

    private void DrawPolicyHover(TimelinePolicyHandleKind kind)
    {
        if (_plotWidth <= 1 || _plotHeight <= 1) return;
        var overlay = TimelinePolicyOverlayProjection.Build(_data, _learnedEnvelope, _learnedEntitlement, _candidateTuning);
        var brush = PolicyBrush(kind, true);
        var rail = overlay.ValueRails.FirstOrDefault(item => item.Kind == kind);
        if (rail is not null)
        {
            var y = PolicyValueY(LaneIndex(rail.Metric), rail.CandidateNormalizedY);
            AddLine(PolicyHoverLayer, 0, y, _plotWidth, y, brush, 3);
            return;
        }
        var band = overlay.TimeBands.FirstOrDefault(item => item.Kind == kind);
        if (band is not null)
        {
            var x = band.CandidateStartX * _plotWidth;
            AddLine(PolicyHoverLayer, x, 0, x, _plotHeight, brush, 3);
        }
    }

    private void RedrawLinkedHover()
    {
        LinkedHoverLayer.Children.Clear();
        if (_linkedHoveredObservationIndices.Count == 0 || _data.Lanes.Count == 0 || _plotWidth <= 1 || _plotHeight <= 1) return;
        var brush = Brush(110, 224, 245, 120);
        foreach (var point in _data.Lanes[0].Points.Where(point => _linkedHoveredObservationIndices.Contains(point.ObservationIndex)))
        {
            var x = point.X * _plotWidth;
            AddLine(LinkedHoverLayer, x, 0, x, _plotHeight, brush, 1.4);
        }
    }
    private void HideCursor()
    {
        PolicyHoverLayer.Children.Clear();
        SetTransient(InspectionCursor, false);
        SetTransient(CursorCard, false);
        CursorChanged?.Invoke(this, new TimelineCursorChangedEventArgs(null));
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
    private void UpdateLiveLabels()
    {
        var current = CurrentTelemetryProjection.Resolve(_observations, TimeSpan.FromSeconds(10));
        if (current is null)
        {
            CpuValueText.Text = PowerValueText.Text = ClockValueText.Text = CoresValueText.Text = "-";
            EnvelopeBadgeText.Text = "LEARNING";
            GlanceSummary.Text = "LEARNING - waiting for telemetry";
            return;
        }

        var latest = current.Latest;
        var observedPressure = LatestDemandPressure();
        CpuValueText.Text = observedPressure is null
            ? $"{latest.CpuPressurePercent:0.0}%"
            : $"{observedPressure.PressurePercent:0}% {PressureDriverShort(observedPressure.Driver)}";
        if (observedPressure is not null)
            ToolTipService.SetToolTip(CpuValueText, $"Observed pressure is the maximum of machine demand, available-capacity saturation, and runnable-queue contention. Demand {observedPressure.DemandPercent:0.0}%, available capacity {observedPressure.AvailableCapacityPercent:0.0}%, saturation {observedPressure.CapacitySaturationPercent:0.0}%, queue {observedPressure.QueueLength:0.##} ({observedPressure.QueuePressurePercent:0.0}% pressure). Display-only for now; controller actuation still uses legacy CPU busy percent.");
        UpdatePressureContextLabel();
        PowerValueText.Text = current.PackageWatts is double watts ? $"{watts:0.0} W" : "-";
        ClockValueText.Text = current.ProcessorPerformancePercent is double performance ? $"{performance:0}%" : "-";
        CoresValueText.Text = current.ActiveCores is int active
            ? current.TotalCores is int total ? $"{active}/{total} awake" : $"{active} awake"
            : current.TotalCores is int knownTotal ? $"-/{knownTotal} awake" : "-";
        UpdateCoreStateLabel();
        var actor = ShortActor(latest.Actor);
        var actorPart = string.IsNullOrWhiteSpace(actor) ? string.Empty : $" - {actor}";
        var decisionPart = latest.Decision == EnvelopeDecisionKind.None ? string.Empty : $" - {latest.Decision.ToString().ToUpperInvariant()}";
        GlanceSummary.Text = $"{latest.Zone.ToString().ToUpperInvariant()} - {PowerValueText.Text} - {ClockValueText.Text} - {CoresValueText.Text}{actorPart}{decisionPart}";
        EnvelopeBadgeText.Text = latest.Decision switch
        {
            EnvelopeDecisionKind.Brake => $"{latest.Zone.ToString().ToUpperInvariant()} - BRAKING",
            EnvelopeDecisionKind.Lease => $"{latest.Zone.ToString().ToUpperInvariant()} - LEASE",
            EnvelopeDecisionKind.Qualifying => $"{latest.Zone.ToString().ToUpperInvariant()} - QUALIFYING",
            _ => latest.Zone.ToString().ToUpperInvariant()
        };
    }

    private void UpdateCoreStateLabel()
    {
        var latest = _coreStateTimeline.Samples.LastOrDefault();
        var capacity = _capacityTimeline.Samples.LastOrDefault();
        if (latest is null && capacity is null) return;

        CoresValueText.Text = capacity is null
            ? $"{latest!.ActiveCores} / {latest.AwakeIdleCores} / {latest.ParkedCores}"
            : $"R{capacity.RequestedCapacityPercent:0} D{capacity.DeliveredCapacityPercent:0}";

        if (latest is null) return;
        var frequency = latest.AverageAwakeFrequencyMhz is double mhz ? $" · {mhz / 1000d:0.00} GHz avg" : string.Empty;
        var maxFrequency = latest.AverageAwakePercentOfMaximumFrequency is double max ? $" · {max:0}% max freq" : string.Empty;
        var capacityDetail = capacity is null
            ? string.Empty
            : $" Requested {capacity.RequestedCapacityPercent:0.0}%, delivered {capacity.DeliveredCapacityPercent:0.0}%, ideal {capacity.IdealCapacityPercent:0.0}%, target saturation {capacity.TargetSaturationPercent:0.0}%, ramp {capacity.RampPercentPerSecond:+0.0;-0.0;0.0}%/s, sustained {capacity.SustainedPressurePercent:0.0}%, burst age {capacity.BurstAgeSeconds:0.0}s ({capacity.Driver}).";
        var plannerDetail = string.Empty;
        if (_processorPolicySnapshot is not null && capacity is not null)
        {
            var actuatorPlan = GraduatedProcessorPolicyPlanner.Plan(
                capacity.RequestedCapacityPercent,
                capacity.DeliveredCapacityPercent,
                latest.AverageAwakePercentOfMaximumFrequency,
                _processorPolicySnapshot);
            var floorAction = actuatorPlan.NextCoreFloorPercent == actuatorPlan.CurrentCoreFloorPercent
                ? $"core floor HOLD {actuatorPlan.CurrentCoreFloorPercent}%"
                : $"core floor {actuatorPlan.CurrentCoreFloorPercent}% -> {actuatorPlan.NextCoreFloorPercent}% (estimated requirement {actuatorPlan.EstimatedRequiredCoreFloorPercent:0}%)";
            plannerDetail = $" Planner: {floorAction}; EPP HOLD {actuatorPlan.CurrentEnergyPerformancePreferencePercent}%. {actuatorPlan.Reason}";
        }
        var actuatorStatusDetail = _coreActuatorStatus is null
            ? string.Empty
            : $" Actuator {_coreActuatorStatus.State}: {_coreActuatorStatus.Message}";
        ToolTipService.SetToolTip(CoresValueText, $"Active / awake-idle / parked physical cores: {latest.ActiveCores} / {latest.AwakeIdleCores} / {latest.ParkedCores}. Active cells vary in intensity by core load: <25%, 25-49%, 50-74%, 75%+. Current awake-core load averages {latest.AverageAwakeLoadPercent:0}%{frequency}{maxFrequency}.{capacityDetail}{plannerDetail}{actuatorStatusDetail}");
    }

    private DemandPressureTelemetry? LatestDemandPressure()
        => _coreThreadHistory.LastOrDefault(sample => sample.DemandPressure is not null)?.DemandPressure;

    private DemandPressureTelemetry? FindDemandPressureNear(DateTimeOffset at)
        => _coreThreadHistory
            .Where(sample => sample.DemandPressure is not null)
            .OrderBy(sample => Math.Abs((sample.At - at).TotalMilliseconds))
            .FirstOrDefault()?.DemandPressure;

    private GraduatedCapacitySample? FindCapacityNear(DateTimeOffset at)
        => _capacityTimeline.Samples
            .OrderBy(sample => Math.Abs((sample.At - at).TotalMilliseconds))
            .FirstOrDefault();

    private static string PressureDriverShort(string driver) => driver switch
    {
        "SATURATION" => "SAT",
        "QUEUE" => "QUEUE",
        _ => "LOAD"
    };
    private static string FormatObservation(OperatingObservation value)
    {
        var watts = value.PackageWatts is double w ? $"{w:0.0} W" : "- W";
        var performanceText = value.ProcessorPerformancePercent is double performance ? $"PERF {performance:0}%" : "PERF -%";
        var speedText = value.EffectiveClockMhz is double mhz ? $"SPEED {mhz / 1000d:0.00} GHz" : "SPEED - GHz";
        var cores = value.ActiveCores is int active ? value.TotalCores is int total ? $"{active}/{total} cores" : $"{active} cores" : "- cores";
        var actor = string.IsNullOrWhiteSpace(value.Actor) ? string.Empty : $" · {ShortActor(value.Actor)}";
        var decision = value.Decision == EnvelopeDecisionKind.None ? string.Empty : $" · {value.Decision}";
        return $"{value.At:HH:mm:ss} | CPU {value.CpuPressurePercent:0.0}% | {watts} | {performanceText} | {speedText} | {cores} | {value.Zone}{actor}{decision}";
    }

    private static string ShortActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return string.Empty;
        try { return System.IO.Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }

    private static string FormatDomain(TimelineLaneProjection lane) => lane.Metric switch
    {
        PerformanceTimelineMetric.CpuPressure => "0-100%",
        PerformanceTimelineMetric.PackagePower => $"{lane.DomainMin:0}-{lane.DomainMax:0} W",
        PerformanceTimelineMetric.EffectiveClock => $"{lane.DomainMin:0}-{lane.DomainMax:0}%",
        PerformanceTimelineMetric.ActiveCores => $"0-{lane.DomainMax:0}",
        _ => string.Empty
    };

    private static Line AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false };
        canvas.Children.Add(line);
        return line;
    }

    private static void AddText(Canvas canvas, string text, double x, double y, double size, Brush foreground)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = foreground, IsHitTestVisible = false };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}
