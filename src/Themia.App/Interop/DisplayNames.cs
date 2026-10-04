using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>What the display configuration API says about one GDI display.</summary>
/// <param name="FriendlyName">The monitor's product name, or empty when it reports none.</param>
/// <param name="DevicePath">The monitor's device interface path, or empty when it has none ([S-842]).</param>
internal readonly record struct DisplayDescriptor(string FriendlyName, string DevicePath);

/// <summary>
/// The name a person would recognise for a display, and the path that identifies it.
/// <para>
/// <c>\\.\DISPLAY1</c> identifies nothing: on a two-monitor desk it is a coin flip which is which,
/// and the number is assigned by Windows rather than by anything the user can see. The display
/// configuration API knows the monitor's actual product name — the string printed on the bezel —
/// and its device interface path, which is what Themia keys every per-display setting by ([S-842]).
/// </para>
/// </summary>
internal static partial class DisplayNames
{
    private const string Scope = "displays";

    private const uint QdcOnlyActivePaths = 0x00000002;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetTargetName = 2;
    private const int ErrorSuccess = 0;

    /// <summary>
    /// Map GDI device name (<c>\\.\DISPLAY1</c>) to the monitor's product name and device path.
    /// <para>
    /// Returns an empty map rather than throwing: a failure here degrades every display to its
    /// ordinal ([S-843]), which is logged, and must never stop the surfaces being built.
    /// </para>
    /// </summary>
    public static Dictionary<string, DisplayDescriptor> Describe()
    {
        var names = new Dictionary<string, DisplayDescriptor>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess)
            {
                Log.Warn(Scope, "GetDisplayConfigBufferSizes failed; displays fall back to their ordinal names");
                return names;
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];

            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != ErrorSuccess)
            {
                Log.Warn(Scope, "QueryDisplayConfig failed; displays fall back to their ordinal names");
                return names;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                // Two lookups per path: the source says which \\.\DISPLAYn this is, the target says
                // which monitor it is. Neither alone is enough to build the mapping.
                var source = new SourceDeviceName
                {
                    Header = Header(DeviceInfoGetSourceName, Marshal.SizeOf<SourceDeviceName>(), path.SourceInfo.AdapterId, path.SourceInfo.Id),
                };

                if (DisplayConfigGetDeviceInfo(ref source) != ErrorSuccess)
                {
                    continue;
                }

                var target = new TargetDeviceName
                {
                    Header = Header(DeviceInfoGetTargetName, Marshal.SizeOf<TargetDeviceName>(), path.TargetInfo.AdapterId, path.TargetInfo.Id),
                };

                if (DisplayConfigGetDeviceInfo(ref target) != ErrorSuccess)
                {
                    continue;
                }

                var gdi = source.ViewGdiDeviceName;

                // A cloned source drives two monitors; the first path names it, as Windows lists it.
                if (string.IsNullOrWhiteSpace(gdi) || names.ContainsKey(gdi))
                {
                    continue;
                }

                names[gdi] = new DisplayDescriptor(target.MonitorFriendlyDeviceName.Trim(), target.MonitorDevicePath.Trim());
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not read display names; falling back to device names", ex);
        }

        return names;
    }

    private static DeviceInfoHeader Header(int type, int size, ulong adapterId, uint id) => new()
    {
        Type = type,
        Size = (uint)size,
        AdapterId = adapterId,
        Id = id,
    };

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public ulong AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        public fixed char GdiDeviceName[32];

        public readonly string ViewGdiDeviceName
        {
            get
            {
                fixed (char* name = GdiDeviceName)
                {
                    return ReadFixed(name, 32);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char FriendlyName[64];
        public fixed char DevicePath[128];

        public readonly string MonitorFriendlyDeviceName
        {
            get
            {
                fixed (char* name = FriendlyName)
                {
                    return ReadFixed(name, 64);
                }
            }
        }

        public readonly string MonitorDevicePath
        {
            get
            {
                fixed (char* path = DevicePath)
                {
                    return ReadFixed(path, 128);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public ulong AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public ulong AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public ulong RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    /// <summary>Opaque here: the mode array must be passed through, but nothing in it is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public ulong AdapterId;
        public ModeUnion Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct ModeUnion
    {
        [FieldOffset(0)]
        public ulong Reserved;
    }

    private static unsafe string ReadFixed(char* buffer, int capacity)
    {
        var length = 0;
        while (length < capacity && buffer[length] != '\0')
        {
            length++;
        }

        return new string(buffer, 0, length);
    }
}
