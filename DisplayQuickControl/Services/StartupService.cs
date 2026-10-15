using Microsoft.Win32;

namespace DisplayQuickControl.Services;

public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayQuickControl";
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string command && command == Command();
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue(ValueName, Command(), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
    private static string Command() => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.")}\"";
}
