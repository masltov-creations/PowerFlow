using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Dashboard;

public sealed partial class FlowFieldControl : UserControl
{
    private readonly List<Storyboard> _storyboards = [];
    private FrameworkElement[]? _particles;
    private bool _loaded;
    private bool _reducedMotion;
    private ControllerSnapshot? _snapshot;

    public FlowFieldControl() => InitializeComponent();

    public void ApplySnapshot(ControllerSnapshot snapshot, bool reducedMotion)
    {
        _snapshot = snapshot;
        _reducedMotion = reducedMotion;
        UpdateGravity(snapshot);
        if (_loaded) RestartMotion();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _particles ??= [Particle01, Particle02, Particle03, Particle04, Particle05, Particle06, Particle07, Particle08, Particle09, Particle10];
        RestartMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        StopMotion();
    }

    private void UpdateGravity(ControllerSnapshot snapshot)
    {
        SetAnchor(SaverAnchor, SaverHalo, snapshot.State == PowerState.PowerSaver);
        SetAnchor(BalancedAnchor, BalancedHalo, snapshot.State == PowerState.Balanced);
        SetAnchor(PerformanceAnchor, PerformanceHalo, snapshot.State == PowerState.HighPerformance);
        EnergyPath.Opacity = snapshot.IsLatched ? 0.95 : 0.35 + Math.Min(0.55, snapshot.ThresholdProgress * 0.55);
        if (snapshot.IsLatched && snapshot.State == PowerState.HighPerformance)
            PerformanceHalo.Opacity = 1;
    }

    private static void SetAnchor(FrameworkElement anchor, UIElement halo, bool active)
    {
        anchor.Opacity = active ? 1 : 0.48;
        halo.Opacity = active ? 0.95 : 0.28;
        if (anchor.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = active ? 1.18 : 1;
            transform.ScaleY = active ? 1.18 : 1;
        }
    }

    private void RestartMotion()
    {
        StopMotion();
        if (!_loaded || _snapshot is null || _particles is null) return;
        if (_reducedMotion)
        {
            foreach (var particle in _particles) particle.Opacity = 0.18;
            return;
        }

        var reverse = _snapshot.CooldownRemaining is not null;
        var end = _snapshot.State switch
        {
            PowerState.PowerSaver => 235d,
            PowerState.Balanced => 615d,
            PowerState.HighPerformance => 790d,
            _ => 400d
        };
        if (reverse) (end) = 30d;
        var baseSeconds = _snapshot.State switch
        {
            PowerState.HighPerformance => 2.6,
            PowerState.Balanced => 4.0,
            _ => 6.3
        };
        if (_snapshot.IsLatched) baseSeconds *= 0.9;
        var pressure = Math.Clamp(_snapshot.ThresholdProgress, 0, 1);
        baseSeconds *= 1.0 - pressure * 0.25;

        for (var i = 0; i < _particles.Length; i++)
        {
            var particle = _particles[i];
            particle.Opacity = 0.35 + (i % 4) * 0.12;
            if (particle.RenderTransform is not CompositeTransform) particle.RenderTransform = new CompositeTransform();
            var animation = new DoubleAnimation
            {
                From = reverse ? 760 : 0,
                To = end,
                Duration = new Duration(TimeSpan.FromSeconds(baseSeconds + (i % 5) * 0.45)),
                BeginTime = TimeSpan.FromMilliseconds(i * 145),
                RepeatBehavior = RepeatBehavior.Forever,
                EnableDependentAnimation = false,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animation, particle);
            Storyboard.SetTargetProperty(animation, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            _storyboards.Add(storyboard);
            storyboard.Begin();
        }
    }

    private void StopMotion()
    {
        foreach (var storyboard in _storyboards)
        {
            try { storyboard.Stop(); } catch { }
        }
        _storyboards.Clear();
    }
}
