using PowerFlow.Core.Rules;
using PowerFlow.Core.Envelope;
using PowerFlow.Windows.Configuration;
using Xunit;

namespace PowerFlow.Windows.Tests.Configuration;

public sealed class JsonConfigStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsAndCreatesLastGoodOnSecondSave()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            await store.SaveAsync(PowerFlowConfig.Default with { StartWithWindows = true });
            await store.SaveAsync(PowerFlowConfig.Default with { StartWithWindows = false });
            Assert.True(File.Exists(Path.Combine(dir, "config.json")));
            Assert.True(File.Exists(Path.Combine(dir, "config.json.lastgood")));
            Assert.False((await store.LoadAsync()).StartWithWindows);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Load_MalformedPrimary_FallsBackToLastGood()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            await store.SaveAsync(PowerFlowConfig.Default with { StartWithWindows = true });
            await store.SaveAsync(PowerFlowConfig.Default with { StartWithWindows = false });
            await File.WriteAllTextAsync(Path.Combine(dir, "config.json"), "{broken");
            var loaded = await store.LoadAsync();
            Assert.True(loaded.StartWithWindows);
            Assert.True(store.LastLoadUsedFallback);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExplicitEntitlement_RoundTrips()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            var entitlement = new PowerFlow.Core.Envelope.PerformanceEntitlement(PowerFlow.Core.Envelope.EnvelopeZone.Efficient, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(20), false);
            var rule = new AppRule("c:\\apps\\browser.exe", AppRuleMode.Balanced, "Browser", true, entitlement);
            await store.SaveAsync(PowerFlowConfig.Default with { AppRules = new[] { rule } });

            var loaded = await store.LoadAsync();

            var loadedRule = Assert.Single(loaded.AppRules);
            Assert.NotNull(loadedRule.Entitlement);
            Assert.Equal(PowerFlow.Core.Envelope.EnvelopeZone.Efficient, loadedRule.Entitlement!.MaximumZone);
            Assert.Equal(TimeSpan.FromSeconds(6), loadedRule.Entitlement.QualificationDuration);
            Assert.False(loadedRule.Entitlement.FollowChildren);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task AdaptiveGovernorSettings_RoundTripTuningAndFrozenLearningModel()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Override, 14, 38, 66, EnvelopeZone.Responsive,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(7), EnvelopeZone.Efficient);
            var frozen = new OperatingEnvelope(18, 44, 70, 28, 52, 84);
            var settings = new AdaptiveGovernorSettings(tuning, true, frozen, EnvelopeConfidence.Medium);
            await store.SaveAsync(PowerFlowConfig.Default with { AdaptiveGovernor = settings });

            var loaded = await store.LoadAsync();
            var actual = loaded.EffectiveAdaptiveGovernorSettings;
            Assert.True(actual.LearningPaused);
            Assert.Equal(EnvelopeTuningLayer.Override, actual.EffectiveTuning.Layer);
            Assert.Equal(38, actual.EffectiveTuning.EfficientCeilingPressure);
            Assert.Equal(EnvelopeZone.Efficient, actual.EffectiveTuning.ManualOverrideZone);
            Assert.Equal(frozen, actual.FrozenLearnedEnvelope);
            Assert.Equal(EnvelopeConfidence.Medium, actual.FrozenConfidence);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task OldSchemaOneConfigWithoutAdaptiveGovernorField_LoadsWithSafeDefaults()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            await store.SaveAsync(PowerFlowConfig.Default);
            var path = Path.Combine(dir, "config.json");
            var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            node.Remove("adaptiveGovernor");
            await File.WriteAllTextAsync(path, node.ToJsonString());

            var loaded = await store.LoadAsync();
            Assert.False(loaded.EffectiveAdaptiveGovernorSettings.LearningPaused);
            Assert.Equal(EnvelopeTuningLayer.Learned, loaded.EffectiveAdaptiveGovernorSettings.EffectiveTuning.Layer);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task PausedLearningWithoutFrozenModel_IsRejected()
    {
        var dir = NewTemp();
        try
        {
            var store = new JsonConfigStore(dir);
            var invalid = new AdaptiveGovernorSettings(EnvelopeTuning.Learned, true, null, null);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(PowerFlowConfig.Default with { AdaptiveGovernor = invalid }));
        }
        finally { Directory.Delete(dir, true); }
    }
    private static string NewTemp()
    {
        var p = Path.Combine(Path.GetTempPath(), "PowerFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }
}
