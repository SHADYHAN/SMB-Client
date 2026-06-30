using Microsoft.Win32;

namespace Rynat.WindowsTray.Services;

internal sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RYANT共享网盘";

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RunValueName) is string value
            && value.Contains("Rynat.WindowsTray.exe", StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(RunValueName, BuildStartupCommand(), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static string BuildStartupCommand()
    {
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "Rynat.WindowsTray.exe");
        return $"\"{exePath}\" --startup";
    }
}
