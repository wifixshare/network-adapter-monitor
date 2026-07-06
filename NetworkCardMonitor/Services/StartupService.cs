using Microsoft.Win32;

namespace NetworkCardMonitor.Services;

internal static class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string SettingsRegistryPath = @"Software\NetworkCardMonitor";
    private const string ValueName = "NetworkCardMonitor";
    private const string InitializedValueName = "StartupChoiceInitialized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 开机启动设置。");

        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法读取程序路径。");
            key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static void EnsureEnabledOnFirstRun()
    {
        using var settingsKey = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法保存程序设置。");

        if (settingsKey.GetValue(InitializedValueName) is not null)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 开机启动设置。");

        if (key.GetValue(ValueName) is null)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法读取程序路径。");
            key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
        }

        settingsKey.SetValue(InitializedValueName, 1, RegistryValueKind.DWord);
    }
}

// END_OF_SOURCE_FILE
