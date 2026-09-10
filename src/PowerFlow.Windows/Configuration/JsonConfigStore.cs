using System.Text.Json;
using PowerFlow.Core.Rules;

namespace PowerFlow.Windows.Configuration;

public sealed class JsonConfigStore
{
    private readonly string _directory;
    private readonly string _primary;
    private readonly string _lastGood;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonConfigStore(string directory)
    {
        _directory = directory;
        _primary = Path.Combine(directory, "config.json");
        _lastGood = Path.Combine(directory, "config.json.lastgood");
    }

    public bool LastLoadUsedFallback { get; private set; }
    public string? LastDiagnostic { get; private set; }

    public async Task<PowerFlowConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        LastLoadUsedFallback = false;
        LastDiagnostic = null;
        if (!File.Exists(_primary)) return PowerFlowConfig.Default;

        try
        {
            return Validate(await ReadAsync(_primary, cancellationToken));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            LastDiagnostic = $"Primary config invalid: {ex.Message}";
            if (!File.Exists(_lastGood)) throw;
            var fallback = Validate(await ReadAsync(_lastGood, cancellationToken));
            LastLoadUsedFallback = true;
            return fallback;
        }
    }

    public async Task SaveAsync(PowerFlowConfig config, CancellationToken cancellationToken = default)
    {
        Validate(config);
        Directory.CreateDirectory(_directory);
        var temp = Path.Combine(_directory, "config.json.tmp");
        var json = JsonSerializer.Serialize(config, _json);
        await File.WriteAllTextAsync(temp, json, cancellationToken);

        if (File.Exists(_primary))
        {
            File.Replace(temp, _primary, _lastGood, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _primary);
        }
    }

    private async Task<PowerFlowConfig> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PowerFlowConfig>(stream, _json, cancellationToken)
            ?? throw new InvalidDataException("Config deserialized to null.");
    }

    private static PowerFlowConfig Validate(PowerFlowConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException($"Unsupported schema version {config.SchemaVersion}.");
        if (config.CpuPromotionWindow <= TimeSpan.Zero || config.QuietWindow <= TimeSpan.Zero || config.PostGameCooldown < TimeSpan.Zero)
            throw new InvalidDataException("Timing values are invalid.");
        if (config.CpuPromotionThresholdPercent is < 0 or > 100 || config.QuietThresholdPercent is < 0 or > 100)
            throw new InvalidDataException("CPU thresholds must be between 0 and 100.");
        var adaptive = config.EffectiveAdaptiveGovernorSettings;
        if (adaptive.LearningPaused && (adaptive.FrozenLearnedEnvelope is null || adaptive.FrozenConfidence is null))
            throw new InvalidDataException("Paused adaptive learning requires a frozen learned envelope and confidence.");
        return config;
    }
}
