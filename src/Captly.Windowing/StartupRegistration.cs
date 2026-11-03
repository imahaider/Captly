using Captly.Core.Shell;
using Microsoft.Win32;

namespace Captly.Windowing;

/// <summary>
/// "Start Captly when Windows starts" (PLAN.md Phase 8 step 5): a value under the current user's
/// Run key, so no admin rights are needed.
/// </summary>
public sealed class StartupRegistration(string command, string keyPath = StartupRegistration.RunKey, string valueName = "Captly") : IStartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is string value && value == command;
        }
    }

    public void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (enabled) key.SetValue(valueName, command);
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>The command Windows runs at sign-in: this exe, starting quietly in the tray.</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\" --startup";
}
