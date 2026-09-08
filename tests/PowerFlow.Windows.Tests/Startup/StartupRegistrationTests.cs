using PowerFlow.Windows.Startup;
using Xunit;

namespace PowerFlow.Windows.Tests.Startup;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void Enable_WritesQuotedExecutableToCurrentUserRunKey()
    {
        var registry = new FakeStartupRegistry();
        var sut = new StartupRegistration(registry, @"C:\Program Files\PowerFlow\PowerFlow.exe");

        sut.SetEnabled(true);

        Assert.Equal("PowerFlow", registry.LastName);
        Assert.Equal("\"C:\\Program Files\\PowerFlow\\PowerFlow.exe\" --background", registry.LastValue);
    }

    [Fact]
    public void Disable_RemovesOnlyPowerFlowValue()
    {
        var registry = new FakeStartupRegistry { Exists = true };
        var sut = new StartupRegistration(registry, @"C:\PowerFlow\PowerFlow.exe");

        sut.SetEnabled(false);

        Assert.Equal("PowerFlow", registry.DeletedName);
    }

    [Fact]
    public void IsEnabled_ReflectsRegistryValue()
    {
        var registry = new FakeStartupRegistry { Exists = true };
        var sut = new StartupRegistration(registry, @"C:\PowerFlow\PowerFlow.exe");
        Assert.True(sut.IsEnabled);
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public bool Exists { get; set; }
        public string? LastName { get; private set; }
        public string? LastValue { get; private set; }
        public string? DeletedName { get; private set; }
        public bool Contains(string name) => Exists;
        public void Set(string name, string value) { Exists = true; LastName = name; LastValue = value; }
        public void Delete(string name) { Exists = false; DeletedName = name; }
    }
}
