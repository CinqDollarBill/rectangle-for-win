using Microsoft.Win32;

namespace RectangleWinPlus;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RectangleWinPlus";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Error("Reading autostart failed", ex);
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            string? exe = Environment.ProcessPath;
            if (exe is null) { Log.Warn("Cannot enable autostart: process path unknown."); return; }
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        catch (Exception ex)
        {
            Log.Error("Writing autostart failed", ex);
        }
    }
}
