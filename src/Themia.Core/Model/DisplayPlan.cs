using Themia.Core.Geometry;

namespace Themia.Core.Model;

/// <summary>
/// One connected display, as the display pass sees it.
/// </summary>
/// <param name="Id">[S-842] The stable id every per-display setting is keyed by.</param>
/// <param name="IdResolved">
/// [S-843] False when <paramref name="Id"/> is only the GDI ordinal because the monitor's device path
/// could not be read (see <see cref="DisplayIdentity.Resolve"/>).
/// </param>
/// <param name="Bounds">The whole monitor, in physical pixels. The surface covers exactly this ([S-840]).</param>
/// <param name="WorkArea">The monitor less the taskbar and other app bars, in physical pixels ([S-841]).</param>
/// <param name="IsPrimary">Whether this is the primary display.</param>
public readonly record struct DisplayInfo(string Id, bool IdResolved, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

/// <summary>
/// [S-842] Which string names a display for good.
/// <para>
/// <c>\\.\DISPLAYn</c> is an ordinal, not an identity: Windows hands the numbers out again on every
/// topology change, so a monitor that was powered off overnight can come back as another number and
/// take the other screen's view, scale and on/off switch with it. The monitor's device interface path
/// (<c>\\?\DISPLAY#DEL40F6#5&amp;…&amp;UID4357#{e6f07b5f-…}</c>) names the panel on its connector, and
/// does not move.
/// </para>
/// </summary>
public static class DisplayIdentity
{
    private const string DevicePathPrefix = @"\\?\";

    /// <summary>
    /// The id for a monitor: its device path when it has one, otherwise its GDI name, flagged as
    /// unresolved ([S-843]). Lower-cased, because the same path has been reported in different cases
    /// by different calls and a key that changes case is a different key.
    /// </summary>
    public static (string Id, bool Resolved) Resolve(string? devicePath, string gdiName)
    {
        var path = devicePath?.Trim();
        if (!string.IsNullOrEmpty(path)
            && path.Length > DevicePathPrefix.Length
            && path.StartsWith(DevicePathPrefix, StringComparison.Ordinal))
        {
            return (path.ToLowerInvariant(), true);
        }

        return (string.IsNullOrWhiteSpace(gdiName) ? "display" : gdiName, false);
    }
}

/// <summary>What one display pass has to do to the surfaces.</summary>
/// <param name="Enabled">The displays that should have a surface, after [S-845].</param>
/// <param name="Close">Ids of surfaces to close: gone, disabled, or re-identified.</param>
/// <param name="Create">Displays that need a surface and have none.</param>
/// <param name="Update">Displays whose surface stays but whose bounds, work area or primary flag changed.</param>
public sealed record DisplayPass(
    IReadOnlyList<DisplayInfo> Enabled,
    IReadOnlyList<string> Close,
    IReadOnlyList<DisplayInfo> Create,
    IReadOnlyList<DisplayInfo> Update)
{
    public bool IsEmpty => Close.Count == 0 && Create.Count == 0 && Update.Count == 0;
}

/// <summary>
/// [S-844] The one plan for which display gets a surface. Startup, a display change, the taskbar
/// moving and the per-display switch all go through <see cref="Reconcile"/>, keyed on the stable id.
/// </summary>
public static class DisplayPlan
{
    /// <summary>
    /// Drop repeated ids, keeping the first. Two monitors reporting one id would otherwise share a
    /// surface slot, and one of them would silently show nothing.
    /// </summary>
    public static List<DisplayInfo> Distinct(IReadOnlyList<DisplayInfo> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. connected.Where(d => !string.IsNullOrEmpty(d.Id) && seen.Add(d.Id))];
    }

    /// <summary>
    /// [S-845] The connected displays Themia shows on: every one not switched off. If that leaves
    /// none — the only enabled display was unplugged — the primary is shown anyway, without changing
    /// the setting: with no surface there is no edit mode and no way back to Settings.
    /// </summary>
    public static List<DisplayInfo> Enabled(IReadOnlyList<DisplayInfo> connected, IReadOnlyCollection<string> disabled)
    {
        ArgumentNullException.ThrowIfNull(disabled);

        var all = Distinct(connected);
        var enabled = all.Where(d => !disabled.Contains(d.Id)).ToList();

        if (enabled.Count == 0 && all.Count > 0)
        {
            enabled.Add(all.Find(d => d.IsPrimary) is { IsPrimary: true } primary ? primary : all[0]);
        }

        return enabled;
    }

    /// <summary>[S-845] Whether a display may be switched off: only while another connected display stays on.</summary>
    public static bool CanDisable(IReadOnlyList<DisplayInfo> connected, IReadOnlyCollection<string> disabled, string id)
    {
        ArgumentNullException.ThrowIfNull(disabled);

        return Distinct(connected).Any(d => d.Id != id && !disabled.Contains(d.Id));
    }

    /// <summary>
    /// Compare the surfaces that exist with the displays that should have one.
    /// </summary>
    /// <param name="surfaces">Each live surface's id and the display it was last placed for.</param>
    /// <param name="connected">The displays Windows reports now. Empty means "unknown", not "none" ([S-846]).</param>
    /// <param name="disabled">The ids switched off ([S-845]).</param>
    public static DisplayPass Reconcile(
        IReadOnlyDictionary<string, DisplayInfo> surfaces,
        IReadOnlyList<DisplayInfo> connected,
        IReadOnlyCollection<string> disabled)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(connected);

        if (connected.Count == 0)
        {
            // [S-846] An empty enumeration is a display driver mid-change, not a desk with no screens.
            // Building nothing and closing nothing leaves it to the next display message.
            return new DisplayPass([], [], [], []);
        }

        var enabled = Enabled(connected, disabled);
        var wanted = enabled.ToDictionary(d => d.Id, StringComparer.Ordinal);

        var close = surfaces.Keys.Where(id => !wanted.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
        var create = enabled.Where(d => !surfaces.ContainsKey(d.Id)).ToList();
        var update = enabled.Where(d => surfaces.TryGetValue(d.Id, out var was) && was != d).ToList();

        return new DisplayPass(enabled, close, create, update);
    }

    /// <summary>
    /// [S-843] The displays that may be given a view of their own. A display whose id did not
    /// resolve is only included when it already has one: a real panel answers with its ordinal for a
    /// moment while it wakes, and interning that ordinal would bind the screen to a fresh, empty view
    /// that the next display change abandons. A permanently path-less display (a virtual or remote
    /// one) keeps the binding it was given.
    /// </summary>
    public static List<string> ViewKeys(IReadOnlyList<DisplayInfo> enabled, IReadOnlyDictionary<string, string> monitorViews)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(monitorViews);

        return [.. enabled.Where(d => d.IdResolved || monitorViews.ContainsKey(d.Id)).Select(d => d.Id)];
    }
}
