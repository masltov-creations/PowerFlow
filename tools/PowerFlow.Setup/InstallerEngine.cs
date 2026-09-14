using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerFlow.Setup;

internal static class InstallerEngine
{
    private const string PayloadResourceName = "PowerFlow.Payload.zip";
    private const string AppExeName = "PowerFlow.App.exe";
    private const string SetupExeName = "PowerFlow-Setup.exe";
    private const string RunValueName = "PowerFlow";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerFlow";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PowerFlow");
    internal static string UserDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerFlow");
    internal static string LegacyReleaseRoot => Path.Combine(UserDataRoot, "App", "releases");
    internal static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "PowerFlow.lnk");
    internal static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PowerFlow.lnk");

    internal static void VerifyEmbeddedPayload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("This setup executable does not contain the PowerFlow application payload.");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (zip.GetEntry(AppExeName) is null)
        {
            throw new InvalidDataException($"Embedded PowerFlow payload is missing {AppExeName}.");
        }
    }

    internal static async Task InstallAsync(bool createDesktopShortcut, bool launchAfterInstall, IProgress<string>? progress = null)
    {
        progress?.Report("Preparing PowerFlow...");
        VerifyEmbeddedPayload();

        var parent = Directory.GetParent(InstallRoot)?.FullName ?? throw new InvalidOperationException("Unable to resolve the PowerFlow install parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".PowerFlow-install-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".PowerFlow-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        var switched = false;
        try
        {
            ExtractPayload(staging);
            var stagedApp = Path.Combine(staging, AppExeName);
            if (!File.Exists(stagedApp)) throw new InvalidDataException("PowerFlow payload is incomplete.");

            var running = GetRunningPowerFlowPaths();
            var unmanaged = running.Where(path => !IsManagedRuntimePath(path)).ToArray();
            if (unmanaged.Length > 0)
            {
                throw new InvalidOperationException("Another PowerFlow copy is running outside the installed location. Close that development/portable copy, then run Setup again.");
            }

            if (running.Length > 0)
            {
                progress?.Report("Closing the installed PowerFlow copy...");
                await RequestManagedShutdownAsync(running[0]);
            }

            var currentSetup = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentSetup) || !File.Exists(currentSetup)) throw new InvalidOperationException("Setup cannot locate its own executable.");
            File.Copy(currentSetup, Path.Combine(staging, SetupExeName), overwrite: true);

            progress?.Report(Directory.Exists(InstallRoot) ? "Updating PowerFlow..." : "Installing PowerFlow...");
            if (Directory.Exists(InstallRoot)) Directory.Move(InstallRoot, backup);
            Directory.Move(staging, InstallRoot);
            switched = true;

            ConfigureShell(createDesktopShortcut);
            ConfigureStartup();
            ConfigureUninstall();

            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);

            if (launchAfterInstall)
            {
                progress?.Report("Starting PowerFlow...");
                Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot, AppExeName), "--dashboard") { UseShellExecute = true });
            }

            progress?.Report("PowerFlow is installed.");
        }
        catch
        {
            if (!switched && Directory.Exists(staging)) TryDeleteDirectory(staging);
            if (switched && Directory.Exists(backup))
            {
                TryDeleteDirectory(InstallRoot);
                Directory.Move(backup, InstallRoot);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) TryDeleteDirectory(staging);
        }
    }

    internal static int BeginUninstall(bool removeUserData, bool quiet)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Uninstaller cannot locate its executable.");
        if (IsPathUnder(current, InstallRoot))
        {
            var tempExe = Path.Combine(Path.GetTempPath(), $"PowerFlow-Uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(current, tempExe, overwrite: true);
            var arguments = "--uninstall-stage2" + (removeUserData ? " --remove-user-data" : "") + (quiet ? " --quiet" : "");
            Process.Start(new ProcessStartInfo(tempExe, arguments) { UseShellExecute = true });
            return 0;
        }

        UninstallAsync(removeUserData).GetAwaiter().GetResult();
        return 0;
    }

    internal static async Task UninstallAsync(bool removeUserData)
    {
        var running = GetRunningPowerFlowPaths().Where(IsManagedRuntimePath).ToArray();
        if (running.Length > 0) await RequestManagedShutdownAsync(running[0]);

        RemoveStartupIfOwned();
        TryDeleteFile(StartMenuShortcut);
        TryDeleteFile(DesktopShortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        TryDeleteDirectory(InstallRoot);
        if (removeUserData) TryDeleteDirectory(UserDataRoot);
    }

    internal static void ScheduleSelfDeleteIfTemporary()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current) || IsPathUnder(current, InstallRoot)) return;
        if (!Path.GetFileName(current).StartsWith("PowerFlow-Uninstall-", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c ping 127.0.0.1 -n 2 > nul & del /f /q \"{current}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void ExtractPayload(string destination)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Setup payload is missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Setup payload contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string[] GetRunningPowerFlowPaths()
    {
        var paths = new List<string>();
        foreach (var process in Process.GetProcessesByName("PowerFlow.App"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path)) paths.Add(Path.GetFullPath(path));
                }
                catch
                {
                    throw new InvalidOperationException("PowerFlow is running, but Setup could not verify which copy is active. Close PowerFlow and try again.");
                }
            }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsManagedRuntimePath(string path) => IsPathUnder(path, InstallRoot) || IsPathUnder(path, LegacyReleaseRoot);

    private static bool IsPathUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RequestManagedShutdownAsync(string executablePath)
    {
        using var relay = Process.Start(new ProcessStartInfo(executablePath, "--shutdown") { UseShellExecute = true })
            ?? throw new InvalidOperationException("Unable to ask PowerFlow to close.");
        await relay.WaitForExitAsync();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (GetRunningPowerFlowPaths().All(path => !IsManagedRuntimePath(path))) return;
            await Task.Delay(250);
        }
        throw new InvalidOperationException("PowerFlow did not close cleanly. Setup stopped without force-killing it.");
    }

    private static void ConfigureShell(bool createDesktopShortcut)
    {
        var app = Path.Combine(InstallRoot, AppExeName);
        CreateShortcut(StartMenuShortcut, app, "--dashboard");
        if (createDesktopShortcut) CreateShortcut(DesktopShortcut, app, "--dashboard");
        else TryDeleteFile(DesktopShortcut);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut service is unavailable.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic dShell = shell ?? throw new InvalidOperationException("Unable to create the Windows shortcut service.");
            shortcut = dShell.CreateShortcut(shortcutPath);
            dynamic dShortcut = shortcut;
            dShortcut.TargetPath = targetPath;
            dShortcut.Arguments = arguments;
            dShortcut.WorkingDirectory = InstallRoot;
            dShortcut.IconLocation = targetPath + ",0";
            dShortcut.Description = "Open PowerFlow";
            dShortcut.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void ConfigureStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");
        key.SetValue(RunValueName, $"\"{Path.Combine(InstallRoot, AppExeName)}\" --background", RegistryValueKind.String);
    }

    private static void ConfigureUninstall()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true) ?? throw new InvalidOperationException("Unable to register PowerFlow in Installed apps.");
        var setup = Path.Combine(InstallRoot, SetupExeName);
        var app = Path.Combine(InstallRoot, AppExeName);
        var version = GetProductVersion();
        key.SetValue("DisplayName", "PowerFlow");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "PowerFlow");
        key.SetValue("InstallLocation", InstallRoot);
        key.SetValue("DisplayIcon", app);
        key.SetValue("UninstallString", $"\"{setup}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string GetProductVersion()
    {
        var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational)) return "0.1.0";
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }

    private static void RemoveStartupIfOwned()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        var current = key?.GetValue(RunValueName) as string;
        if (current is not null && current.Contains(InstallRoot, StringComparison.OrdinalIgnoreCase)) key!.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
