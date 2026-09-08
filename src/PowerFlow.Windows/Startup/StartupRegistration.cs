using Microsoft.Win32;

namespace PowerFlow.Windows.Startup;

public interface IStartupRegistry
{
    bool Contains(string name);
    void Set(string name, string value);
    void Delete(string name);
}

public sealed class StartupRegistration
{
    private const string ValueName = "PowerFlow";
    private readonly IStartupRegistry _registry;
    private readonly string _executablePath;

    public StartupRegistration(string executablePath) : this(new CurrentUserRunRegistry(), executablePath) { }

    public StartupRegistration(IStartupRegistry registry, string executablePath)
    {
        _registry = registry;
        _executablePath = executablePath;
    }

    public bool IsEnabled => _registry.Contains(ValueName);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
            _registry.Set(ValueName, $"\"{_executablePath}\" --background");
        else
            _registry.Delete(ValueName);
    }

    private sealed class CurrentUserRunRegistry : IStartupRegistry
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public bool Contains(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(name) is not null;
        }

        public void Set(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
                ?? throw new InvalidOperationException("Unable to open HKCU Run key.");
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void Delete(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
