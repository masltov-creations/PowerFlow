using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AppEntitlementPresentationTests
{
    [Fact]
    public void LegacyRulesRemainBackwardCompatibleWhileExplicitImportanceOwnsNewRules()
    {
        var balanced = new AppRule("legacy-balanced.exe", AppRuleMode.Balanced);
        var performance = new AppRule("legacy-performance.exe", AppRuleMode.Performance);
        Assert.Equal(EnvelopeZone.Efficient, balanced.EffectiveEntitlement.MaximumZone);
        Assert.Equal(EnvelopeZone.Boost, performance.EffectiveEntitlement.MaximumZone);

        var low = performance with { Entitlement = null, Importance = AppImportance.Low };
        var normal = performance with { Entitlement = null, Importance = AppImportance.Normal };
        var high = performance with { Entitlement = null, Importance = AppImportance.High };
        Assert.Equal(EnvelopeZone.Efficient, low.EffectiveEntitlement.MaximumZone);
        Assert.Equal(EnvelopeZone.Boost, normal.EffectiveEntitlement.MaximumZone);
        Assert.Equal(EnvelopeZone.Boost, high.EffectiveEntitlement.MaximumZone);
        Assert.True(high.EffectiveEntitlement.QualificationDuration < normal.EffectiveEntitlement.QualificationDuration);
    }

    [Fact]
    public void RulesSurface_ExposesOnlyLowNormalHighImportance()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");
        Assert.Contains("Change importance", xaml, StringComparison.Ordinal);
        Assert.Contains("AppImportanceBox", xaml, StringComparison.Ordinal);
        Assert.Contains("LOW · EFFICIENCY ONLY", xaml, StringComparison.Ordinal);
        Assert.Contains("NORMAL · QUALIFIED BOOST", xaml, StringComparison.Ordinal);
        Assert.Contains("HIGH · FAST BOOST", xaml, StringComparison.Ordinal);
        Assert.Contains("Importance = importance", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumZoneBox", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("QualificationSecondsBox", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaseSecondsBox", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseHysteresisSecondsBox", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOM", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdaptiveActuation_IsOwnedByAutoAndHasNoUserToggle()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        Assert.DoesNotContain("AdaptiveActuationToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("AdaptiveActuationEnabled = true", app, StringComparison.Ordinal);
        Assert.Contains("AdaptiveGovernor = canonicalAdaptive", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Workloads_DoNotExposeWindowsPlanOrServicePolicyEditors()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");
        Assert.DoesNotContain("ServicePolicy", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceRepeater", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("EXPERT", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("High Performance", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Power Saver", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}