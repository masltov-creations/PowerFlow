using System.Xml.Linq;
using Xunit;

namespace PowerFlow.App.Tests.Packaging;

public sealed class ManifestSafetyTests
{
    [Fact]
    public void AppProject_DoesNotEmbedCustomWin32Manifest()
    {
        var root = FindRepoRoot();
        var projectPath = Path.Combine(root, "src", "PowerFlow.App", "PowerFlow.App.csproj");
        var project = XDocument.Load(projectPath);
        var applicationManifest = project.Descendants("ApplicationManifest").Select(x => x.Value).FirstOrDefault();
        Assert.True(string.IsNullOrWhiteSpace(applicationManifest), "PowerFlow must not embed a custom Win32 app.manifest; WinUI 3 owns DPI awareness.");
        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "app.manifest")), "Custom app.manifest must remain absent to avoid USER32 DPI parser fail-fast.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow repo root not found.");
    }
}