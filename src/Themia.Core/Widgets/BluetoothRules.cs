namespace Themia.Core.Widgets;

/// <summary>
/// What a paired device is, as far as its icon is concerned. A closed set rather than the original's
/// free-form string: the glyph table has to cover every case at compile time, and a typo in a
/// category name is otherwise a device that silently draws the generic icon.
/// </summary>
public enum BluetoothDeviceKind
{
    Unknown = 0,
    Audio = 1,
    Mouse = 2,
    Keyboard = 3,
    Phone = 4,
    Gamepad = 5,
    Pen = 6,
    Watch = 7,
}

/// <summary>Why a Bluetooth list has no rows. Each one wants a different sentence, and none of them is blank.</summary>
public enum BluetoothStatus
{
    /// <summary>Before the first enumeration comes back.</summary>
    Loading = 0,

    Ok = 1,

    /// <summary>This machine has no Bluetooth radio at all. Not an error — a fact about the hardware.</summary>
    NoRadio = 2,

    /// <summary>There is a radio and it is switched off.</summary>
    RadioOff = 3,

    /// <summary>Windows refused the app access to the radios.</summary>
    PermissionDenied = 4,

    /// <summary>The enumeration itself failed. The only one of these that is a fault.</summary>
    Failed = 5,
}

/// <summary>One paired device, as the widget draws it.</summary>
/// <param name="Name">The device's own name. Attacker-influenced in principle, so it is displayed and nothing more.</param>
/// <param name="Id">The Windows device-interface id. Used to re-read battery, never shown.</param>
/// <param name="Connected">Whether it is connected right now.</param>
/// <param name="Kind">Which glyph it gets.</param>
/// <param name="BatteryPercent">0..100 where the device reports it over GATT, else null.</param>
public readonly record struct BluetoothDeviceEntry(
    string Name,
    string Id,
    bool Connected,
    BluetoothDeviceKind Kind,
    int? BatteryPercent);

/// <summary>
/// The rules the Bluetooth widget draws by: what a device is called, what it is, and in what order.
/// <para>
/// Pure, and kept out of the enumeration code, because the enumeration needs a radio and a machine
/// to run on and these are the parts that are easy to get quietly wrong — a device whose name is
/// empty, two radios reporting the same headset, a battery percentage of 255 from a device that
/// means "I do not know".
/// </para>
/// </summary>
public static class BluetoothRules
{
    /// <summary>
    /// Guess what a device is from its name.
    /// <para>
    /// A guess, and deliberately only used to pick an icon. Windows exposes a class-of-device for
    /// Classic devices and an appearance value for LE ones, but neither is populated for every
    /// paired device, and a list where half the rows have an icon and half do not looks broken in a
    /// way that a slightly wrong icon does not.
    /// </para>
    /// </summary>
    public static BluetoothDeviceKind Classify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BluetoothDeviceKind.Unknown;
        }

        var lower = name.ToLowerInvariant();

        if (Contains(lower, "headphone", "headset", "speaker", "earbud", "airpod", "buds", "audio", "soundbar", "soundcore"))
        {
            return BluetoothDeviceKind.Audio;
        }

        if (Contains(lower, "mouse", "trackpad", "magic trackpad"))
        {
            return BluetoothDeviceKind.Mouse;
        }

        if (Contains(lower, "keyboard", "keychron"))
        {
            return BluetoothDeviceKind.Keyboard;
        }

        // Before the phone brands, not after: a "Galaxy Watch" and a "Pixel Watch" both carry a
        // phone maker's name and are not phones. The more specific word wins.
        if (Contains(lower, "watch", "band", "fitbit", "garmin"))
        {
            return BluetoothDeviceKind.Watch;
        }

        if (Contains(lower, "phone", "iphone", "galaxy", "pixel"))
        {
            return BluetoothDeviceKind.Phone;
        }

        if (Contains(lower, "controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con"))
        {
            return BluetoothDeviceKind.Gamepad;
        }

        if (Contains(lower, "pen", "stylus"))
        {
            return BluetoothDeviceKind.Pen;
        }

        return BluetoothDeviceKind.Unknown;
    }

    private static bool Contains(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What to call a device. A paired device with no name is common enough — some LE peripherals
    /// simply do not advertise one — and an empty row is indistinguishable from a rendering bug.
    /// <para>
    /// Collapsed to a single line: the name comes off the radio, so it is not this widget's to
    /// trust, and a row is one line tall whatever arrives in it.
    /// </para>
    /// </summary>
    public static string DisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Unknown device";
        }

        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "Unknown device";
        }

        var collapsed = string.Join(' ', words);
        return collapsed.Length <= 64 ? collapsed : collapsed[..64];
    }

    /// <summary>
    /// The battery column, or null when the device does not report one — which is most of them.
    /// <para>
    /// 255 is the Battery Service's own "unknown", and anything above 100 is a device answering
    /// out of spec. Both become no column rather than a percentage nobody should act on.
    /// </para>
    /// </summary>
    public static string? BatteryLabel(int? percent)
        => percent is >= 0 and <= 100 ? $"{percent}%" : null;

    /// <summary>The connection state, in words. Carried by text as well as by the dot beside it.</summary>
    public static string ConnectionLabel(bool connected) => connected ? "Connected" : "Paired";

    /// <summary>The sentence a tile with no rows shows. Every state names itself.</summary>
    public static string StatusMessage(BluetoothStatus status) => status switch
    {
        BluetoothStatus.Loading => "Reading paired devices",
        BluetoothStatus.NoRadio => "This PC has no Bluetooth radio",
        BluetoothStatus.RadioOff => "Bluetooth is turned off",
        BluetoothStatus.PermissionDenied => "Windows has not granted access to the radios",
        BluetoothStatus.Failed => "Could not read the paired devices",
        _ => "No paired devices",
    };

    /// <summary>
    /// Merge, filter and order what the two enumerations returned.
    /// <para>
    /// The merge is the part that matters: a headset that supports both Classic and LE is enumerated
    /// twice, under one name, and showing it twice makes the list look like it is double-counting.
    /// The LE entry is the one that carries a GATT battery reading, so where the two disagree the
    /// richer answer wins rather than whichever happened to be enumerated first.
    /// </para>
    /// <para>
    /// Connected devices first, then by name, so the order is stable between polls — a list that
    /// reshuffles every thirty seconds cannot be read.
    /// </para>
    /// </summary>
    public static List<BluetoothDeviceEntry> Arrange(
        IEnumerable<BluetoothDeviceEntry> devices,
        bool connectedOnly,
        int maxCount)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var merged = new List<BluetoothDeviceEntry>();

        foreach (var device in devices)
        {
            var name = DisplayName(device.Name);
            var normalised = device with { Name = name, Kind = Classify(name) };

            // Nameless devices are not merged with each other: they all normalise to the same
            // string, and folding four unnamed peripherals into one row would hide three devices.
            var index = string.IsNullOrWhiteSpace(device.Name)
                ? -1
                : merged.FindIndex(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                merged.Add(normalised);
                continue;
            }

            var current = merged[index];

            merged[index] = current with
            {
                Connected = current.Connected || normalised.Connected,
                BatteryPercent = normalised.BatteryPercent ?? current.BatteryPercent,
            };
        }

        if (connectedOnly)
        {
            merged.RemoveAll(device => !device.Connected);
        }

        merged.Sort(static (left, right) =>
        {
            if (left.Connected != right.Connected)
            {
                return left.Connected ? -1 : 1;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        var limit = Math.Clamp(maxCount, 1, 50);

        if (merged.Count > limit)
        {
            merged.RemoveRange(limit, merged.Count - limit);
        }

        return merged;
    }
}
