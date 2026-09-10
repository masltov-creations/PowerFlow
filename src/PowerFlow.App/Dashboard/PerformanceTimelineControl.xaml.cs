using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Envelope;
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
        LaneLabelColumn.Width = new GridLength(glance ? 0 : presentation == TimelinePresentation.Compact ? 96 : 112);
        TimelineRoot.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 72,
            TimelinePresentation.Compact => 170,
            TimelinePresentation.Expanded => 280,
            _ => 340
        };
        TimelinePlotHost.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 58,
            TimelinePresentation.Compact => 132,
            TimelinePresentation.Expanded => 238,
            _ => 300
        };
        RequestRedraw();
    }
    public event EventHandler<TimelineCursorChangedEventArgs>? CursorChanged;
    public event EventHandler<TimelineSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<PolicyHandleChangedEventArgs>? PolicyHandleChanged;

    public void SetPolicyContext(OperatingEnvelope learnedEnvelope, PerformanceEntitlement learnedEntitlement, EnvelopeTuning candidateTuning)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(candidateTuning);
        _learnedEnvelope = learnedEnvelope;
        _learnedEntitlement = learnedEntitlement;
        _candidateTuning = candidateTuning;
        _envelope = candidateTuning.ApplyTo(learnedEnvelope);
        RedrawPolicy();
    }

    public void SetTuneMode(bool enabled)
    {
        _tuneMode = enabled;
        PolicyHandleLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) _draggingPolicyHandle = null;
        RedrawPolicy();
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

        var laneHeight = height / 4d;
        var light = ActualTheme == ElementTheme.Light;
        var grid = light ? Brush(17, 34, 51, 24) : Brush(255, 255, 255, 18);
        var text = light ? Brush(17, 34, 51, 118) : Brush(255, 255, 255, 105);
        var series = new[]
        {
            light ? Brush(12, 145, 132, 235) : Brush(80, 224, 207, 240),
            light ? Brush(68, 105, 196, 225) : Brush(123, 168, 255, 235),
            light ? Brush(136, 88, 192, 225) : Brush(190, 143, 255, 235),
            light ? Brush(194, 112, 43, 225) : Brush(255, 177, 92, 235)
        };

        for (var lane = 0; lane < 4; lane++)
        {
            var top = lane * laneHeight;
            if (lane > 0) AddLine(GridLayer, 0, top, width, top, grid, 1);
            AddLine(GridLayer, 0, top + laneHeight * .5, width, top + laneHeight * .5, grid, 1);
            UpdateTracePath(lane, _data.Lanes[lane], top + 4, Math.Max(1, laneHeight - 8), series[lane]);
            AddText(GridLayer, FormatDomain(_data.Lanes[lane]), Math.Max(0, width - 65), top + 2, 11, text);
        }

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
        var maximumGapX = Math.Clamp(10d / Math.Max(1d, _windowSeconds), 0.001d, 1d);
        path.Data = ToPathGeometry(ShapePreservingCurve.BuildSparseObservations(samples, maximumGapX));
    }

    private Microsoft.UI.Xaml.Shapes.Path TracePath(int laneIndex) => laneIndex switch
    {
        0 => CpuTracePath,
        1 => PowerTracePath,
        2 => ClockTracePath,
        3 => CoresTracePath,
        _ => throw new ArgumentOutOfRangeException(nameof(laneIndex))
    };

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
    private void RedrawPolicy()
    {
        if (_plotWidth <= 1 || _plotHeight <= 1) return;
        PolicyValueLayer.Children.Clear();
        PolicyTimeLayer.Children.Clear();
        PolicyHandleLayer.Children.Clear();

        var overlay = TimelinePolicyOverlayProjection.Build(_data, _learnedEnvelope, _learnedEntitlement, _candidateTuning);
        var laneHeight = _plotHeight / 4d;
        foreach (var rail in overlay.ValueRails)
        {
            var laneIndex = LaneIndex(rail.Metric);
            var learnedY = PolicyValueY(laneIndex, laneHeight, rail.LearnedNormalizedY);
            var candidateY = PolicyValueY(laneIndex, laneHeight, rail.CandidateNormalizedY);
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

    private int LaneIndex(PerformanceTimelineMetric metric)
    {
        for (var i = 0; i < _data.Lanes.Count; i++)
            if (_data.Lanes[i].Metric == metric) return i;
        return 0;
    }

    private double PolicyValueY(int laneIndex, double laneHeight, double normalizedY)
        => laneIndex * laneHeight + 4 + Math.Clamp(normalizedY, 0d, 1d) * Math.Max(1, laneHeight - 8);

    private TimelinePolicyHandleKind? HitTestPolicyHandle(Point point)
    {
        if (!_tuneMode || _plotWidth <= 1 || _plotHeight <= 1) return null;
        var overlay = TimelinePolicyOverlayProjection.Build(_data, _learnedEnvelope, _learnedEntitlement, _candidateTuning);
        var laneHeight = _plotHeight / 4d;
        TimelinePolicyHandleKind? winner = null;
        var best = 14d;
        foreach (var rail in overlay.ValueRails.Where(rail => rail.Editable))
        {
            var distance = Math.Abs(point.Y - PolicyValueY(LaneIndex(rail.Metric), laneHeight, rail.CandidateNormalizedY));
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
        var updated = TimelinePolicyInteraction.ApplyDrag(
            _candidateTuning,
            kind,
            point.X / Math.Max(1, _plotWidth),
            point.Y / Math.Max(1, _plotHeight),
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
        CursorReadout.Text = InspectionExplanationProjection.ForObservation(observation).AsPlainText();
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
        var laneHeight = _plotHeight / 4d;
        var brush = PolicyBrush(kind, true);
        var rail = overlay.ValueRails.FirstOrDefault(item => item.Kind == kind);
        if (rail is not null)
        {
            var y = PolicyValueY(LaneIndex(rail.Metric), laneHeight, rail.CandidateNormalizedY);
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
        CpuValueText.Text = $"{latest.CpuPressurePercent:0.0}%";
        PowerValueText.Text = current.PackageWatts is double watts ? $"{watts:0.0} W" : "-";
        ClockValueText.Text = current.EffectiveClockMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "-";
        CoresValueText.Text = current.ActiveCores is int active
            ? current.TotalCores is int total ? $"{active}/{total}" : active.ToString()
            : current.TotalCores is int knownTotal ? $"-/{knownTotal}" : "-";
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

    private static string FormatObservation(OperatingObservation value)
    {
        var watts = value.PackageWatts is double w ? $"{w:0.0} W" : "— W";
        var ghz = value.EffectiveClockMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "— GHz";
        var cores = value.ActiveCores is int active ? value.TotalCores is int total ? $"{active}/{total} cores" : $"{active} cores" : "— cores";
        var actor = string.IsNullOrWhiteSpace(value.Actor) ? string.Empty : $" · {ShortActor(value.Actor)}";
        var decision = value.Decision == EnvelopeDecisionKind.None ? string.Empty : $" · {value.Decision}";
        return $"{value.At:HH:mm:ss} · CPU {value.CpuPressurePercent:0.0}% · {watts} · {ghz} · {cores} · {value.Zone}{actor}{decision}";
    }

    private static string ShortActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return string.Empty;
        try { return System.IO.Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }

    private static string FormatDomain(TimelineLaneProjection lane) => lane.Metric switch
    {
        PerformanceTimelineMetric.CpuPressure => "100%",
        PerformanceTimelineMetric.PackagePower => $"{lane.DomainMax:0} W",
        PerformanceTimelineMetric.EffectiveClock => $"{lane.DomainMax / 1000d:0.0} GHz",
        PerformanceTimelineMetric.ActiveCores => $"{lane.DomainMax:0}",
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
