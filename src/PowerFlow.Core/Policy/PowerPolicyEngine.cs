namespace PowerFlow.Core.Policy;

public sealed class PowerPolicyEngine
{
    private PolicyConfig _config;
    private readonly HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);
    private PowerState _state;
    private PowerState? _manualState;
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

    public void Reconfigure(PolicyConfig config)
    {
        _config = config;
        _highDemandSince = null;
        _quietSince = null;

        if (_games.Count > 0)
        {
            _state = PowerState.HighPerformance;
            _reason = "Performance locked - Game";
        }
        else if (_manualState is PowerState manual)
        {
            _state = manual;
            _reason = $"{DisplayState(manual)} locked - Manual";
        }
        else
        {
            _reason = $"{DisplayState(_state)} - policy updated";
        }
    }
    public void SynchronizeObservedState(PowerState state)
    {
        _state = state;
        _highDemandSince = null;
        _quietSince = null;
        _coolingDown = false;
        _reason = state switch
        {
            PowerState.PowerSaver => "Power Saver - observed Windows state",
            PowerState.Balanced => "Balanced - observed Windows state",
            PowerState.HighPerformance => "High Performance - observed Windows state",
            _ => state.ToString()
        };
    }
    public PolicyDecision Evaluate(PolicyEvent evt)
    {
        var before = _state;

        switch (evt)
        {
            case ManualStateRequested manual:
                _manualState = manual.State;
                _coolingDown = false;
                _highDemandSince = null;
                _quietSince = null;
                if (_games.Count == 0 || manual.State == PowerState.HighPerformance)
                {
                    _state = manual.State;
                    _reason = ManualReason(manual.State);
                }
                break;

            case ManualPerformanceRequested:
                _manualState = PowerState.HighPerformance;
                _coolingDown = false;
                _highDemandSince = null;
                _quietSince = null;
                _state = PowerState.HighPerformance;
                _reason = ManualReason(PowerState.HighPerformance);
                break;

            case ManualStateReleased:
            case ManualPerformanceReleased:
                _manualState = null;
                if (_games.Count > 0)
                {
                    _state = PowerState.HighPerformance;
                    _reason = "Performance locked - Game";
                }
                else if (_state == PowerState.HighPerformance)
                {
                    _coolingDown = false;
                    _state = PowerState.Balanced;
                    _reason = "Manual lock released - Balanced";
                }
                else
                {
                    _coolingDown = false;
                    _highDemandSince = null;
                    _quietSince = null;
                    _reason = "Manual lock released - automatic policy resumed";
                }
                break;
            case GameStarted game:
                _games.Add(game.ProcessKey);
                if (_manualState != PowerState.HighPerformance)
                {
                    _coolingDown = false;
                    _state = PowerState.HighPerformance;
                    _reason = $"Performance locked - Game ({game.ProcessKey})";
                }
                break;

            case GameExited game:
                _games.Remove(game.ProcessKey);
                if (_games.Count == 0 && game.AllTrackedGameProcessesExited && _manualState is PowerState manualState)
                {
                    _coolingDown = false;
                    _state = manualState;
                    _reason = ManualReason(manualState);
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

            case CooldownExpired when _coolingDown && _manualState is null && _games.Count == 0:
                _coolingDown = false;
                _state = PowerState.Balanced;
                _quietSince = null;
                _reason = "Game cooldown complete - Balanced";
                break;

            case ExplicitBalancedActivated balanced:
                _explicitBalancedRule = balanced.RuleName;
                if (_manualState is null && _games.Count == 0 && !_coolingDown)
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

        if (_games.Count > 0)
        {
            _state = PowerState.HighPerformance;
            _reason = _manualState == PowerState.HighPerformance ? ManualReason(PowerState.HighPerformance) : "Performance locked - Game";
        }
        else if (_manualState is PowerState manualState)
        {
            _state = manualState;
            _reason = ManualReason(manualState);
        }
        return new PolicyDecision(_state, before != _state, _reason, _manualState is not null || _games.Count > 0, evt.At);
    }

    private void EvaluateCpu(CpuSample cpu)
    {
        if (_manualState is not null || _games.Count > 0 || _coolingDown)
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

    private static string ManualReason(PowerState state) => state switch
    {
        PowerState.PowerSaver => "Power Saver locked - Manual",
        PowerState.Balanced => "Balanced locked - Manual",
        PowerState.HighPerformance => "Performance locked - Manual",
        _ => $"{state} locked - Manual"
    };    private static string DisplayState(PowerState state) => state switch
    {
        PowerState.PowerSaver => "Power Saver",
        PowerState.Balanced => "Balanced",
        PowerState.HighPerformance => "High Performance",
        _ => state.ToString()
    };
}
