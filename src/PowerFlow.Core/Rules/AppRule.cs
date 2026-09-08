namespace PowerFlow.Core.Rules;

public enum AppRuleMode { Balanced, Performance }

public sealed record AppRule(
    string ExecutablePath,
    AppRuleMode Mode,
    string? DisplayName = null,
    bool FollowChildren = true);
