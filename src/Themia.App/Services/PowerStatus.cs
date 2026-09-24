using System.Runtime.InteropServices;
using Themia.App.Interop;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// Where the machine's power is coming from, and which plan it is running.
/// <para>
/// Two Win32 calls and no WMI: <c>Win32_Battery</c> and <c>Win32_PowerPlan</c> would answer the
/// same questions and would pull <c>System.Management</c>, a COM server and a reflection-based
/// object model into a widget that samples a struct once every fifteen seconds. Neither call here
/// can block, so both run on the tick.
/// </para>
/// <para>
/// The interpretation lives in <see cref="PowerRules"/> — this file's whole job is to get the bytes
/// and free what it allocated.
/// </para>
/// </summary>
internal static unsafe partial class PowerStatus
{
    private const string Scope = "power";

    /// <summary>ERROR_SUCCESS. The power API returns Win32 codes, not HRESULTs.</summary>
    private const uint Success = 0;

    /// <summary>ERROR_MORE_DATA: the buffer was too small, and the required size came back in the size.</summary>
    private const uint MoreData = 234;

    /// <summary>
    /// A friendly name longer than this is a scheme somebody has been creative with. Capped so a
    /// hostile registry value cannot make the widget allocate without bound; the display rule caps
    /// it again, much shorter, for the row itself.
    /// </summary>
    private const uint MaxNameBytes = 4096;

    private static uint _lastPlanFailure;

    /// <summary>Read the whole power state. Never throws: it runs on the shared ticker.</summary>
    public static PowerSnapshot Read()
    {
        if (!Win32.GetSystemPowerStatus(out var status))
        {
            var error = (uint)Marshal.GetLastWin32Error();
            Log.Warn(Scope, $"GetSystemPowerStatus failed: {error}");
            return PowerSnapshot.Unreadable;
        }

        return PowerRules.Read(
            status.ACLineStatus,
            status.BatteryFlag,
            status.BatteryLifePercent,
            status.SystemStatusFlag,
            status.BatteryLifeTime,
            ActivePlanName());
    }

    /// <summary>
    /// The active power plan's friendly name, or null.
    /// <para>
    /// <c>PowerGetActiveScheme</c> hands back a GUID it allocated with <c>LocalAlloc</c> and the
    /// caller owns it. Freed on every path, including the one where reading the name fails: this
    /// runs on a tick, so a leak here is a few bytes every fifteen seconds for as long as the
    /// desktop is up.
    /// </para>
    /// </summary>
    private static string? ActivePlanName()
    {
        nint scheme = 0;

        try
        {
            var active = PowerGetActiveScheme(0, &scheme);

            if (active != Success || scheme == 0)
            {
                NotePlanFailure(active, "could not read the active power scheme");
                return null;
            }

            uint bytes = 0;

            // Asked for twice on purpose: once with no buffer to learn the size, once to fill it.
            // Guessing a size and truncating would put half a plan name in the row.
            var sized = PowerReadFriendlyName(0, scheme, 0, 0, null, &bytes);

            if (sized is not (Success or MoreData) || bytes == 0 || bytes > MaxNameBytes)
            {
                NotePlanFailure(sized, "could not size the power plan name");
                return null;
            }

            var buffer = new byte[bytes];

            fixed (byte* target = buffer)
            {
                var read = PowerReadFriendlyName(0, scheme, 0, 0, target, &bytes);

                if (read != Success)
                {
                    NotePlanFailure(read, "could not read the power plan name");
                    return null;
                }

                NotePlanFailure(Success, null);

                // A counted UTF-16 string with a terminator inside the count. Read to the first NUL
                // rather than trusting the length, because the value comes out of the registry.
                return new string((char*)target, 0, (int)(bytes / sizeof(char))).TrimEnd('\0');
            }
        }
        finally
        {
            if (scheme != 0)
            {
                LocalFree(scheme);
            }
        }
    }

    /// <summary>
    /// Log a plan-read failure once rather than once per tick. A machine whose power registry is
    /// locked down answers identically forever.
    /// </summary>
    private static void NotePlanFailure(uint error, string? message)
    {
        if (error == _lastPlanFailure)
        {
            return;
        }

        var recovered = error == Success;
        _lastPlanFailure = error;

        if (recovered)
        {
            Log.Info(Scope, "the power plan is readable again");
            return;
        }

        if (message is not null)
        {
            Log.Warn(Scope, $"{message}: {error}");
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(nint userRootPowerKey, nint* activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        nint schemeGuid,
        nint subGroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        byte* buffer,
        uint* bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);
}
