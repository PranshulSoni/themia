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

    /// <summary>ERROR_NO_MORE_ITEMS: the end of an enumeration.</summary>
    private const uint NoMoreItems = 259;

    /// <summary>ACCESS_SCHEME: enumerate the schemes themselves.</summary>
    private const uint AccessScheme = 16;

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
    /// [S-1249] Every power plan on the machine, by friendly name, with the active one marked — the
    /// read-only list the largest power tile shows. Empty when the scheme list cannot be read; a
    /// scheme whose name cannot be read is skipped rather than listed blank.
    /// </summary>
    public static IReadOnlyList<PowerPlanEntry> Plans()
    {
        var plans = new List<PowerPlanEntry>();
        Guid active = default;
        nint scheme = 0;

        try
        {
            if (PowerGetActiveScheme(0, &scheme) == Success && scheme != 0)
            {
                active = *(Guid*)scheme;
            }
        }
        finally
        {
            if (scheme != 0)
            {
                LocalFree(scheme);
            }
        }

        // A machine has a handful of schemes; the cap only stops a corrupt registry from looping.
        for (uint index = 0; index < 32; index++)
        {
            Guid guid;
            var size = (uint)sizeof(Guid);
            var listed = PowerEnumerate(0, null, null, AccessScheme, index, (byte*)&guid, &size);

            if (listed == NoMoreItems)
            {
                break;
            }

            if (listed != Success)
            {
                NotePlanFailure(listed, "could not list the power plans");
                break;
            }

            if (PowerRules.PlanDisplayName(FriendlyName(&guid)) is { } name)
            {
                plans.Add(new PowerPlanEntry(name, guid == active));
            }
        }

        return plans;
    }

    /// <summary>GUID_VIDEO_SUBGROUP / GUID_VIDEO_POWERDOWN_TIMEOUT (winnt.h): "Turn my screen off after".</summary>
    private static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    /// <summary>GUID_SLEEP_SUBGROUP / GUID_STANDBY_TIMEOUT / GUID_HIBERNATE_TIMEOUT: "Make my device sleep / hibernate after".</summary>
    private static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid SleepTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    private static readonly Guid HibernateTimeout = new("9d7815a6-7ee4-497e-8888-515a05f02364");

    /// <summary>
    /// [S-1623] The active plan's screen, sleep and hibernate timeouts for <paramref name="source"/>
    /// (the battery values on battery, the plugged-in ones otherwise — as Settings shows the pair
    /// for the source in use). A value that cannot be read stays null; the scheme GUID the API
    /// allocates is freed on every path.
    /// </summary>
    public static PowerTimeouts Timeouts(PowerSource source)
    {
        nint scheme = 0;

        try
        {
            var active = PowerGetActiveScheme(0, &scheme);

            if (active != Success || scheme == 0)
            {
                NotePlanFailure(active, "could not read the active power scheme");
                return PowerTimeouts.Unknown;
            }

            var battery = source == PowerSource.Battery;
            return new PowerTimeouts(
                ReadIndex(scheme, VideoSubgroup, VideoTimeout, battery),
                ReadIndex(scheme, SleepSubgroup, SleepTimeout, battery),
                ReadIndex(scheme, SleepSubgroup, HibernateTimeout, battery));
        }
        finally
        {
            if (scheme != 0)
            {
                LocalFree(scheme);
            }
        }
    }

    /// <summary>One setting's value index (seconds for a timeout), or null when the plan does not answer.</summary>
    private static uint? ReadIndex(nint scheme, Guid subgroup, Guid setting, bool battery)
    {
        uint value;
        var read = battery
            ? PowerReadDCValueIndex(0, (Guid*)scheme, &subgroup, &setting, &value)
            : PowerReadACValueIndex(0, (Guid*)scheme, &subgroup, &setting, &value);

        return read == Success ? value : null;
    }

    /// <summary>One scheme's friendly name, sized then read, or null.</summary>
    private static string? FriendlyName(Guid* scheme)
    {
        uint bytes = 0;
        var sized = PowerReadFriendlyName(0, (nint)scheme, 0, 0, null, &bytes);

        if (sized is not (Success or MoreData) || bytes == 0 || bytes > MaxNameBytes)
        {
            return null;
        }

        var buffer = new byte[bytes];

        fixed (byte* target = buffer)
        {
            return PowerReadFriendlyName(0, (nint)scheme, 0, 0, target, &bytes) == Success
                ? new string((char*)target, 0, (int)(bytes / sizeof(char))).TrimEnd('\0')
                : null;
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

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(
        nint rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte* buffer,
        uint* bufferSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(nint rootPowerKey, Guid* schemeGuid, Guid* subGroupOfPowerSettingsGuid, Guid* powerSettingGuid, uint* acValueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadDCValueIndex(nint rootPowerKey, Guid* schemeGuid, Guid* subGroupOfPowerSettingsGuid, Guid* powerSettingGuid, uint* dcValueIndex);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);
}

/// <summary>One power plan, by name, and whether it is the active one.</summary>
internal readonly record struct PowerPlanEntry(string Name, bool Active);
