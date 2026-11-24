using Microsoft.Win32;

namespace LexiPop.Services;

/// <summary>
/// 开机自启：写 HKCU\...\Run，不需要管理员权限。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LexiPop";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Toggle()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null) return;

        if (IsEnabled())
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
    }
}
