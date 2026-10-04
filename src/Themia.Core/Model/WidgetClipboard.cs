using Themia.Core.Geometry;

namespace Themia.Core.Model;

/// <summary>Whether a clipboard entry duplicates its widget or moves it.</summary>
public enum ClipboardMode
{
    Copy = 0,
    Cut = 1,
}

/// <summary>
/// What a paste should do, worked out before anything is mutated.
/// </summary>
/// <param name="Widget">The instance to add to the target view. Always a fresh object with a new id.</param>
/// <param name="RemoveFromViewId">For a cut, the view the original must be removed from; null for a copy.</param>
/// <param name="RemoveWidgetId">For a cut, the id of the original; null for a copy.</param>
/// <param name="Members">
/// [S-876] What the widget holds, fresh copies with fresh ids, their parents pointing into the paste;
/// added after <paramref name="Widget"/>. Null or empty for a widget that holds nothing.
/// </param>
public readonly record struct PastePlan(
    WidgetInstance Widget,
    string? RemoveFromViewId,
    string? RemoveWidgetId,
    IReadOnlyList<WidgetInstance>? Members = null);

/// <summary>
/// Copy / cut / paste for widgets, across views and across displays.
/// <para>
/// In-process rather than on the system clipboard: the payload is a live widget configuration, not
/// text, and putting it on the system clipboard would both clobber whatever the user had there and
/// invite other apps to paste something that is not a widget.
/// </para>
/// <para>
/// A cut is a <b>mark</b>, not an immediate removal — the original stays put until a paste actually
/// lands, so cutting and then changing your mind costs nothing.
/// </para>
/// </summary>
public sealed class WidgetClipboard
{
    private WidgetInstance? _payload;

    /// <summary>[S-876] Snapshots of everything the payload held, with their original ids.</summary>
    private List<WidgetInstance> _members = [];

    public ClipboardMode Mode { get; private set; }

    /// <summary>The view the entry was taken from. Needed to remove the original when a cut is pasted.</summary>
    public string? SourceViewId { get; private set; }

    /// <summary>The id of the widget the entry was taken from.</summary>
    public string? SourceWidgetId { get; private set; }

    public bool HasContent => _payload is not null;

    /// <summary>Raised whenever the contents change, so menus can enable or disable Paste.</summary>
    public event Action? Changed;

    /// <param name="members">[S-876] Everything inside the widget (a container's members, at any depth), in view order.</param>
    public void Copy(WidgetInstance widget, string viewId, IEnumerable<WidgetInstance>? members = null)
        => Set(widget, viewId, ClipboardMode.Copy, members);

    /// <param name="members">[S-876] As for <see cref="Copy"/>: a container is cut with what it holds.</param>
    public void Cut(WidgetInstance widget, string viewId, IEnumerable<WidgetInstance>? members = null)
        => Set(widget, viewId, ClipboardMode.Cut, members);

    private void Set(WidgetInstance widget, string viewId, ClipboardMode mode, IEnumerable<WidgetInstance>? members)
    {
        ArgumentNullException.ThrowIfNull(widget);

        // Snapshot it: the clipboard must survive the original being moved, reconfigured or deleted.
        _payload = widget.Clone();

        // Members keep their original ids in the snapshot, so the paste can map each parent to its copy.
        _members = [.. (members ?? []).Select(m =>
        {
            var snapshot = m.Clone();
            snapshot.Id = m.Id;
            return snapshot;
        })];
        SourceViewId = viewId;
        SourceWidgetId = widget.Id;
        Mode = mode;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_payload is null)
        {
            return;
        }

        _payload = null;
        _members = [];
        SourceViewId = null;
        SourceWidgetId = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Work out what pasting at <paramref name="at"/> on a screen of <paramref name="screen"/> would
    /// do, without mutating anything. Returns null when the clipboard is empty.
    /// <para>
    /// The pasted widget keeps its size and is centred on the drop point, then snapped and clamped,
    /// so pasting onto a differently-shaped display still lands a sane, fully on-screen widget.
    /// </para>
    /// </summary>
    public PastePlan? PlanPaste(FracRect at, ScreenMetrics screen, bool snapEnabled)
    {
        if (_payload is null)
        {
            return null;
        }

        var copy = _payload.Clone(); // Clone() already assigns a fresh id
        var centred = new FracRect(
            at.X - (_payload.W / 2),
            at.Y - (_payload.H / 2),
            _payload.W,
            _payload.H);

        copy.Rect = GridGeometry.Settle(centred, screen, snapEnabled);
        var members = PlanMembers(copy, screen);

        return Mode == ClipboardMode.Cut
            ? new PastePlan(copy, SourceViewId, SourceWidgetId, members)
            : new PastePlan(copy, null, null, members);
    }

    /// <summary>
    /// [S-876] Fresh copies of the members for one paste: new ids (a copy can be pasted twice), each
    /// parent pointed at its copy, and each rect moved by as much as the container moved, so a
    /// group's members land in the pasted area exactly where they stood in the original.
    /// </summary>
    private List<WidgetInstance> PlanMembers(WidgetInstance root, ScreenMetrics screen)
    {
        if (_payload is null || _members.Count == 0)
        {
            return [];
        }

        var ids = new Dictionary<string, string>(StringComparer.Ordinal) { [SourceWidgetId ?? string.Empty] = root.Id };
        foreach (var member in _members)
        {
            ids[member.Id] = Guid.NewGuid().ToString("n");
        }

        var dx = root.X - _payload.X;
        var dy = root.Y - _payload.Y;

        return [.. _members.Select(member =>
        {
            var copy = member.Clone();
            copy.Id = ids[member.Id];
            copy.ParentId = member.ParentId is { } parent && ids.TryGetValue(parent, out var mapped) ? mapped : root.Id;
            copy.Rect = GridGeometry.Clamp(member.Rect with { X = member.X + dx, Y = member.Y + dy }, screen);
            return copy;
        })];
    }

    /// <summary>
    /// Called once a paste has been applied. A cut is consumed — pasting it twice would mean moving
    /// one widget to two places — while a copy stays available for repeat pastes.
    /// </summary>
    public void OnPasted()
    {
        if (Mode == ClipboardMode.Cut)
        {
            Clear();
        }
    }
}
