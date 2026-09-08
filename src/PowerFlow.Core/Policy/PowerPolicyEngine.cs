namespace PowerFlow.Core.Policy;

public sealed class PowerPolicyEngine
{
    private readonly PolicyConfig _config;
    private readonly HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);
    private PowerState _state;
    private bool _manualLatch;
    private string? _explicitBalancedRule;
    private DateTimeOffset? _highDemandSince;
    private DateTimeOffset? _quietSince;
    private bool _coolingDown;
    private string _reason;

    public PowerPolicyEngine(PolicyConfig config)
    {
        _config = config;
        _state = config.RestingState;
        _reason = config.RestingState == PowerState.Balanced ? "Balanced resting state" : "Power Saver resting state";
    }

    public PolicyDecision Evaluate(PolicyEvent evt)
    {
        var before = _state;

        switch (evt)
        {
            case ManualPerformanceRequested:
                _manualLatch = true;
                _coolingDown = false;
                _state = PowerState.HighPerformance;
                _reason = "Performance locked - Manual";
                break;

            case ManualPerformanceReleased:
                _manualLatch = false;
                if (_games.Count > 0)
                {
                    _state = PowerState.HighPerformance;
                    _reason = "Performance locked - Game";
                }
                else
                {
                    _coolingDown = false;
                    _state = PowerState.Balanced;
                    _reason = "Manual performance latch released";
                }
                break;

            case GameStarted game:
                _games.Add(game.ProcessKey);
                if (!_manualLatch)
                {
                    _coolingDown = false;
                    _state = PowerState.HighPerformance;
                    _reason = $"Performance locked - Game ({game.ProcessKey})";
                }
                break;

            case GameExited game:
                _games.Remove(game.ProcessKey);
                if (_manualLatch)
                {
                    _state = PowerState.HighPerformance;
                    _reason = "Performance locked - Manual";
                }
                else if (_games.Count > 0 || !game.AllTrackedGameProcessesExited)
                {
                    _state = PowerState.HighPerformance;
                    _reason = "Performance locked - Game";
                }
                else
                {
                    _coolingDown = true;
                    _state = PowerState.HighPerformance;
                    _reason = "Game ended - performance cooldown";
                }
                break;

            case CooldownExpired when _coolingDown && !_manualLatch && _games.Count == 0:
                _coolingDown = false;
                _state = PowerState.Balanced;
                _quietSince = null;
                _reason = "Game cooldown complete - Balanced";
                break;

            case ExplicitBalancedActivated balanced:
                _explicitBalancedRule = balanced.RuleName;
                if (!_manualLatch && _games.Count == 0 && !_coolingDown)
                {
                    _state = PowerState.Balanced;
                    _reason = $"Balanced rule - {balanced.RuleName}";
                }
                break;

            case ExplicitBalancedCleared balanced when string.Equals(_explicitBalancedRule, balanced.RuleName, StringComparison.OrdinalIgnoreCase):
                _explicitBalancedRule = null;
                _quietSince = null;
                _reason = "Balanced rule cleared";
                break;

            case CpuSample cpu:
                EvaluateCpu(cpu);
                break;
        }

        if (_manualLatch)
        {
            _state = PowerState.HighPerformance;
            _reason = "Performance locked - Manual";
        }
        else if (_games.Count > 0)
        {
            _state = PowerState.HighPerformance;
            _reason = "Performance locked - Game";
        }

        return new PolicyDecision(_state, before != _state, _reason, _manualLatch || _games.Count > 0, evt.At);
    }

    private void EvaluateCpu(CpuSample cpu)
    {
        if (_manualLatch || _games.Count > 0 || _coolingDown)
            return;

        if (_explicitBalancedRule is not null)
        {
            _state = PowerState.Balanced;
            _reason = $"Balanced rule - {_explicitBalancedRule}";
            return;
        }

        if (_state == PowerState.PowerSaver)
        {
            _quietSince = null;
            if (cpu.CpuPercent >= _config.CpuPromotionThresholdPercent)
            {
                _highDemandSince ??= cpu.At;
                if (cpu.At - _highDemandSince.Value >= _config.CpuPromotionWindow)
                {
                    _state = PowerState.Balanced;
                    _reason = $"Balanced - sustained CPU >= {_config.CpuPromotionThresholdPercent:0.#}%";
                    _highDemandSince = null;
                }
            }
            else
            {
                _highDemandSince = null;
                _reason = "Power Saver - ordinary demand";
            }
            return;
        }

        if (_state == PowerState.Balanced)
        {
            _highDemandSince = null;
            if (_config.RestingState == PowerState.Balanced)
            {
                _quietSince = null;
                _reason = "Balanced resting state";
                return;
            }

            if (cpu.CpuPercent <= _config.QuietThresholdPercent)
            {
                _quietSince ??= cpu.At;
                if (cpu.At - _quietSince.Value >= _config.QuietWindow)
                {
                    _state = PowerState.PowerSaver;
                    _reason = "Power Saver - quiet hysteresis complete";
                    _quietSince = null;
                }
            }
            else
            {
                _quietSince = null;
                _reason = "Balanced - active workload";
            }
        }
    }
}
