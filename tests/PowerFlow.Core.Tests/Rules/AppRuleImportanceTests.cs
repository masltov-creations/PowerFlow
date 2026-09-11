using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.Core.Tests.Rules;

public sealed class AppRuleImportanceTests
{
    [Fact]
    public void Legacy_rules_keep_original_mapping()
    {
        var balanced = new AppRule("background.exe", AppRuleMode.Balanced);
        var performance = new AppRule("interactive.exe", AppRuleMode.Performance);
        Assert.Equal(EnvelopeZone.Efficient, balanced.EffectiveEntitlement.MaximumZone);
        Assert.Equal(AppImportance.Low, balanced.EffectiveImportance);
        Assert.Equal(EnvelopeZone.Boost, performance.EffectiveEntitlement.MaximumZone);
        Assert.Equal(TimeSpan.FromSeconds(4), performance.EffectiveEntitlement.QualificationDuration);
        Assert.Equal(AppImportance.Normal, performance.EffectiveImportance);
    }

    [Fact]
    public void Low_caps_at_efficient_while_high_qualifies_faster_than_normal()
    {
        var low = AppRule.EntitlementFor(AppImportance.Low);
        var normal = AppRule.EntitlementFor(AppImportance.Normal);
        var high = AppRule.EntitlementFor(AppImportance.High);
        Assert.Equal(EnvelopeZone.Efficient, low.MaximumZone);
        Assert.Equal(EnvelopeZone.Boost, normal.MaximumZone);
        Assert.Equal(TimeSpan.FromSeconds(4), normal.QualificationDuration);
        Assert.Equal(EnvelopeZone.Boost, high.MaximumZone);
        Assert.Equal(TimeSpan.FromSeconds(.5), high.QualificationDuration);
        Assert.True(high.QualificationDuration < normal.QualificationDuration);
    }

    [Fact]
    public void Explicit_custom_entitlement_remains_authoritative()
    {
        var custom = new PerformanceEntitlement(EnvelopeZone.Responsive, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3), false);
        var rule = new AppRule("custom.exe", AppRuleMode.Performance, FollowChildren: false, Entitlement: custom, Importance: AppImportance.High);
        Assert.Same(custom, rule.EffectiveEntitlement);
        Assert.Equal(AppImportance.Normal, rule.EffectiveImportance);
    }
}