using PowerFlow.Core.Rules;
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

    private static string NewTemp()
    {
        var p = Path.Combine(Path.GetTempPath(), "PowerFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }
}
