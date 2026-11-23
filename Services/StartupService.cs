using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace CoTab.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CoTab";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null)
            {
                error = "无法打开系统启动项注册表。";
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Keeps the registered command pointed at the current executable after an update/move.</summary>
    public static void RefreshIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var current = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(current)) return;
            var expected = BuildCommand();
            if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            {
                using var writable = Registry.CurrentUser.CreateSubKey(RunKey, true);
                writable?.SetValue(ValueName, expected, RegistryValueKind.String);
            }
        }
        catch
        {
            // Non-critical.
        }
    }

    public static string BuildCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            processPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        var fileName = Path.GetFileName(processPath);
        if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(assembly))
                return $"\"{processPath}\" \"{assembly}\"";
        }
        return $"\"{processPath}\"";
    }
}
