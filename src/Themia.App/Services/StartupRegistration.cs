using Microsoft.Win32;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// "Launch when I sign in", via the per-user Run key.
/// <para>
/// Per-user (HKCU) on purpose: it needs no elevation, and a desktop widget layer has no business
/// writing a machine-wide startup entry.
/// </para>
/// </summary>
internal static class StartupRegistration
{
    private const string Scope = "startup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Themia";

    /// <summary>Whether the Run entry exists and still points at this executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key?.GetValue(ValueName) is not string command)
            {
                return false;
            }

            // A stale entry from a previous install location is not "enabled": it would silently
            // launch nothing, and the toggle would lie.
            return command.Trim('"').Equals(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn(Scope, "could not read the Run key", ex);
            return false;
        }
    }

    /// <summary>Add or remove the Run entry. Returns the state actually achieved, which the UI reflects.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                Log.Error(Scope, "could not open the Run key for writing");
                return IsEnabled();
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath()}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return enabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error(Scope, $"could not {(enabled ? "add" : "remove")} the startup entry", ex);
            return IsEnabled();
        }
    }

    private static string ExecutablePath()
        => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Themia.exe");
}
