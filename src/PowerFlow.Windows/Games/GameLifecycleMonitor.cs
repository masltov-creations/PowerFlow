using System.Diagnostics;
using PowerFlow.Core.Rules;

namespace PowerFlow.Windows.Games;

public sealed class GameLifecycleMonitor : IGameLifecycleMonitor
{
    private readonly IProcessEventSource _source;
    private readonly IProcessHandleFactory _handles;
    private readonly Dictionary<int, ITrackedProcessHandle> _tracked = new();
    private IReadOnlyList<AppRule> _rules = Array.Empty<AppRule>();
    private bool _started;
    private bool _waitingForLauncherChild;

    public GameLifecycleMonitor() : this(new ForegroundProcessEventSource(), new ProcessHandleFactory()) { }

    public GameLifecycleMonitor(IProcessEventSource source, IProcessHandleFactory handles)
    {
        _source = source;
        _handles = handles;
        _source.ProcessStarted += OnProcessStarted;
    }

    public event EventHandler<GameDetectedEventArgs>? GameDetected;
    public event EventHandler<GameDetectedEventArgs>? GameProcessAdded;
    public event EventHandler? GameLatchReleased;

    public bool IsLatched => _tracked.Count > 0;
    public int TrackedCount => _tracked.Count;
    public string? LatchReason { get; private set; }

    public void UpdateRules(IReadOnlyList<AppRule> rules) => _rules = rules;

    public void Start()
    {
        _started = true;
        if (!IsLatched)
        {
            foreach (var existing in _source.SnapshotExisting())
            {
                OnProcessStarted(this, existing);
                if (IsLatched && !_waitingForLauncherChild) break;
            }
        }
        if (!IsLatched || _waitingForLauncherChild) _source.Start();
    }

    public void Stop()
    {
        _started = false;
        _source.Stop();
    }

    public void AddRelatedProcess(GameProcess process)
    {
        Track(process, $"Related game process: {Path.GetFileName(process.ExecutablePath)}", isInitial: !IsLatched);
    }

    public void Dispose()
    {
        Stop();
        _source.ProcessStarted -= OnProcessStarted;
        foreach (var handle in _tracked.Values.ToArray())
        {
            handle.Exited -= OnTrackedExited;
            handle.Dispose();
        }
        _tracked.Clear();
        _source.Dispose();
    }

    private void OnProcessStarted(object? sender, ProcessStartEvent e)
    {
        if (IsLatched && _waitingForLauncherChild && e.ParentProcessId is int parent && _tracked.ContainsKey(parent))
        {
            var child = new GameProcess(e.ProcessId, e.ExecutablePath, e.ParentProcessId, e.StartTime, "launcher-child");
            Track(child, $"Launcher handoff -> {Path.GetFileName(e.ExecutablePath)}", isInitial: false);
            _waitingForLauncherChild = false;
            _source.Stop();
            return;
        }

        if (IsLatched) return;

        var rule = FindPerformanceRule(e.ExecutablePath);
        if (rule is null) return;

        var process = new GameProcess(e.ProcessId, e.ExecutablePath, e.ParentProcessId, e.StartTime, $"explicit:{rule.DisplayName ?? Path.GetFileName(rule.ExecutablePath)}");
        var reason = $"Explicit Performance rule - {rule.DisplayName ?? Path.GetFileName(e.ExecutablePath)}";
        Track(process, reason, isInitial: true);

        _waitingForLauncherChild = rule.FollowChildren && IsLauncherExecutable(e.ExecutablePath);
        if (_waitingForLauncherChild)
            _source.Start();
        else
            _source.Stop();
    }

    private AppRule? FindPerformanceRule(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        foreach (var rule in _rules)
        {
            if (rule.Mode != AppRuleMode.Performance) continue;
            var rulePath = rule.ExecutablePath;
            if (rulePath.Contains('\\') || rulePath.Contains('/'))
            {
                if (string.Equals(Path.GetFullPath(rulePath), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)) return rule;
            }
            else if (string.Equals(Path.GetFileName(rulePath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }
        return null;
    }

    private void Track(GameProcess process, string reason, bool isInitial)
    {
        if (_tracked.TryGetValue(process.ProcessId, out var existing))
        {
            if (existing.Process.StartTime == process.StartTime) return;
            existing.Exited -= OnTrackedExited;
            existing.Dispose();
            _tracked.Remove(process.ProcessId);
        }

        var handle = _handles.Open(process);
        handle.Exited += OnTrackedExited;
        _tracked[process.ProcessId] = handle;
        handle.EnableExitEvents();
        LatchReason = reason;
        var args = new GameDetectedEventArgs(process, reason);
        if (isInitial) GameDetected?.Invoke(this, args);
        else GameProcessAdded?.Invoke(this, args);
    }

    private void OnTrackedExited(object? sender, EventArgs e)
    {
        if (sender is not ITrackedProcessHandle handle) return;
        if (!_tracked.TryGetValue(handle.Process.ProcessId, out var current) || current.Process.StartTime != handle.Process.StartTime)
            return;

        current.Exited -= OnTrackedExited;
        current.Dispose();
        _tracked.Remove(handle.Process.ProcessId);

        if (_tracked.Count != 0) return;

        _waitingForLauncherChild = false;
        LatchReason = null;
        GameLatchReleased?.Invoke(this, EventArgs.Empty);
        if (_started) _source.Start();
    }

    private static bool IsLauncherExecutable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name.Contains("launcher") || name is "steam" or "epicgameslauncher" or "battle.net" or "ea" or "eaapp" or "goggalaxy" or "ubisoftconnect" or "playnite.desktopapp" or "playnite.fullscreenapp";
    }

    private sealed class ProcessHandleFactory : IProcessHandleFactory
    {
        public ITrackedProcessHandle Open(GameProcess process) => new ProcessHandle(process);
    }

    private sealed class ProcessHandle : ITrackedProcessHandle
    {
        private readonly Process _process;
        public ProcessHandle(GameProcess process)
        {
            Process = process;
            _process = System.Diagnostics.Process.GetProcessById(process.ProcessId);
            _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        }
        public GameProcess Process { get; }
        public event EventHandler? Exited;
        public void EnableExitEvents()
        {
            _process.EnableRaisingEvents = true;
            if (_process.HasExited) Exited?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose() => _process.Dispose();
    }

}

