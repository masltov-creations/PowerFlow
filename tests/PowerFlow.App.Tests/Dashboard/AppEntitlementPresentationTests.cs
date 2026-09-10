using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AppEntitlementPresentationTests
{
    [Fact]
    public void LegacyRulesRemainBackwardCompatibleWhileEffectiveEntitlementIsSemantic()
    {
        var balanced = new AppRule("legacy-balanced.exe", AppRuleMode.Balanced);
        var performance = new AppRule("legacy-performance.exe", AppRuleMode.Performance);
        Assert.Equal(EnvelopeZone.Efficient, balanced.EffectiveEntitlement.MaximumZone);
        Assert.Equal(EnvelopeZone.Boost, performance.EffectiveEntitlement.MaximumZone);

        var explicitEntitlement = new PerformanceEntitlement(EnvelopeZone.Responsive, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(5), false);
        var migrated = performance with { Entitlement = explicitEntitlement };
        Assert.Equal(AppRuleMode.Performance, migrated.Mode);
        Assert.Equal(EnvelopeZone.Responsive, migrated.EffectiveEntitlement.MaximumZone);
    }

    [Fact]
    public void RulesSurfaceEditsSemanticEntitlementInsteadOfRawActuatorValues()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");

        Assert.Contains("Edit entitlement", xaml, StringComparison.Ordinal);
        Assert.Contains("EntitlementDialog", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumZoneBox", xaml, StringComparison.Ordinal);
        Assert.Contains("QualificationSecondsBox", xaml, StringComparison.Ordinal);
        Assert.Contains("LeaseSecondsBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ReleaseHysteresisSecondsBox", xaml, StringComparison.Ordinal);
        Assert.Contains("MAX ZONE", xaml, StringComparison.Ordinal);
        Assert.Contains("QUALIFICATION", xaml, StringComparison.Ordinal);
        Assert.Contains("BOOST LEASE", xaml, StringComparison.Ordinal);
        Assert.Contains("RELEASE HYSTERESIS", xaml, StringComparison.Ordinal);
        Assert.Contains("PerformanceEntitlement", code, StringComparison.Ordinal);
        Assert.Contains("EffectiveEntitlement", code, StringComparison.Ordinal);
        Assert.Contains("Entitlement = entitlement", code, StringComparison.Ordinal);

        foreach (var forbidden in new[] { "EPP", "P-state", "P State", "per-process watts", "watt cap" })
        {
            Assert.DoesNotContain(forbidden, xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AdaptiveActuationIsExplicitAndAdvisoryByDefaultInRulesSurface()
    {
        Assert.False(PowerFlowConfig.Default.AdaptiveActuationEnabled);
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");
        Assert.Contains("AdaptiveActuationToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("Advisory only", xaml, StringComparison.Ordinal);
        Assert.Contains("AdaptiveActuationEnabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RawWindowsPlanMappingIsExpertDisclosureOnly()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        Assert.Contains("Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("EXPERT", xaml, StringComparison.Ordinal);
        Assert.Contains("Eco", xaml, StringComparison.Ordinal);
        Assert.Contains("Power Saver", xaml, StringComparison.Ordinal);
        Assert.Contains("Efficient / Responsive", xaml, StringComparison.Ordinal);
        Assert.Contains("Balanced", xaml, StringComparison.Ordinal);
        Assert.Contains("Boost", xaml, StringComparison.Ordinal);
        Assert.Contains("High Performance", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCardsPresentSemanticCeilingAndTimingNotLegacyModeAsPrimaryLabel()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");
        Assert.Contains("CeilingLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("TimingLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("RuleCardItem", code, StringComparison.Ordinal);
        Assert.Contains("MAX", code, StringComparison.Ordinal);
        Assert.Contains("QUALIFY", code, StringComparison.Ordinal);
        Assert.Contains("LEASE", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
