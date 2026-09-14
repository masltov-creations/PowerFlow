using Xunit;

namespace PowerFlow.App.Tests.Startup;

public sealed class InstallLayoutContractTests
{
    [Fact]
    public void DistributionIncludesOneClickSetupAndScriptedInstallFallback()
    {
        Assert.True(File.Exists(RepoFile("tools", "PowerFlow.Setup", "PowerFlow.Setup.csproj")), "Missing one-click setup project.");
        Assert.True(File.Exists(RepoFile("tools", "install", "Build-PowerFlowDistribution.ps1")), "Missing distribution builder.");
        Assert.True(File.Exists(RepoFile("tools", "install", "Install-PowerFlow.ps1")), "Missing scripted installer fallback.");
        Assert.True(File.Exists(RepoFile("tools", "install", "Uninstall-PowerFlow.ps1")), "Missing scripted uninstaller fallback.");
    }

    [Fact]
    public void InstallerUsesStablePerUserProgramPathAndPreservesUserData()
    {
        var install = ReadRepoFile("tools", "install", "Install-PowerFlow.ps1");
        var uninstall = ReadRepoFile("tools", "install", "Uninstall-PowerFlow.ps1");

        Assert.Contains("Programs\\PowerFlow", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCALAPPDATA", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCALAPPDATA", uninstall, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RemoveUserData", uninstall, StringComparison.Ordinal);
        Assert.Contains("PowerFlow", uninstall, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerCreatesDashboardShortcutsAndRegistersUninstall()
    {
        var install = ReadRepoFile("tools", "install", "Install-PowerFlow.ps1");

        Assert.Contains("Start Menu", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Desktop", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--dashboard", install, StringComparison.Ordinal);
        Assert.Contains("CurrentVersion\\Uninstall", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UninstallString", install, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistributionBuildPublishesSelfContainedAppAndSingleFileSetup()
    {
        var build = ReadRepoFile("tools", "install", "Build-PowerFlowDistribution.ps1");

        Assert.Contains("--self-contained", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win-x64", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PublishSingleFile", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PowerFlow-Setup.exe", build, StringComparison.OrdinalIgnoreCase);
        var appBuildStart = build.IndexOf("Invoke-Checked 'dotnet' @('build', $appProject", StringComparison.OrdinalIgnoreCase);
        var appBuildEnd = build.IndexOf("Compress-Archive", appBuildStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(appBuildStart >= 0 && appBuildEnd > appBuildStart, "Could not isolate the app build section.");
        var appBuild = build[appBuildStart..appBuildEnd];
        Assert.Contains("DebugType=None", appBuild, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DebugSymbols=false", appBuild, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PathMap", appBuild, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistributionBuilderKeepsWinUiXamlResourcesInPayload()
    {
        var build = ReadRepoFile("tools", "install", "Build-PowerFlowDistribution.ps1");

        Assert.Contains("PowerFlow.App.pri", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("App.xbf", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dashboard\\MainWindow.xbf", build, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'publish', $appProject", build, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeLeadsUsersToReleaseInstallerNotSourceBuild()
    {
        var readme = ReadRepoFile("README.md");

        Assert.Contains("PowerFlow-Setup.exe", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Releases", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Install PowerFlow", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Building from source", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupSupportsDeterministicQuietInstallUsingTheSameInstallerEngine()
    {
        var program = ReadRepoFile("tools", "PowerFlow.Setup", "Program.cs");

        Assert.Contains("--install", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--quiet", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstallerEngine.InstallAsync", program, StringComparison.Ordinal);
    }
    private static string ReadRepoFile(params string[] parts) => File.ReadAllText(RepoFile(parts));

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException() }.Concat(parts).ToArray());
    }
}
