using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>A rectangle in percent of a display's work area (0..100 on each axis).</summary>
public readonly record struct PercentRect(double X, double Y, double Width, double Height);

/// <summary>A rectangle in physical screen pixels (whole pixels, as Win32 takes them).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>[S-1407] One window to open and where it goes: the walk of one assigned pane.</summary>
public sealed record LayoutEntry(LayoutSource Source, string Target, string Label, bool ReuseIfRunning, PercentRect Area);

/// <summary>
/// [S-1407] Turning an app layout into windows (original <c>rootToEntries</c> and the backend's
/// <c>compute_rect</c>): the tree walked against the work area, each assigned pane's rectangle in
/// pixels, the correction for a window's invisible resize border, and when a window has moved off
/// where it was put. Pure; the Win32 half is <c>AppLayoutLauncher</c>.
/// </summary>
public static class LayoutPlan
{
    /// <summary>The smallest window a pane is given, in pixels (original: 100 × 80).</summary>
    public const int MinWidth = 100;

    /// <inheritdoc cref="MinWidth"/>
    public const int MinHeight = 80;

    /// <summary>How far a window may sit from its pane before it is put back (original: 8 px).</summary>
    public const int PositionSlack = 8;

    /// <summary>How far its size may differ (original: 24 px — some apps snap to their own grid).</summary>
    public const int SizeSlack = 24;

    /// <summary>
    /// [S-1407] One entry per assigned pane, in tree order, with its rectangle in percent of the
    /// work area (original S-52: a 50/50 side-by-side split puts the first pane at x 0..50 and the
    /// second at 50..100, both full height). An empty layout walks to nothing (S-53). The same
    /// target twice opens once, in the first pane that names it: a second launch of one app gives
    /// one window, or two that fight for one place. A folder and a web address always open (they
    /// never "reuse a running app").
    /// </summary>
    public static IReadOnlyList<LayoutEntry> Walk(LayoutNode? root)
    {
        var entries = new List<LayoutEntry>();
        Walk(root, new PercentRect(0, 0, 100, 100), entries, 0);
        return entries;
    }

    private static void Walk(LayoutNode? node, PercentRect area, List<LayoutEntry> into, int depth)
    {
        if (node is null || depth > LayoutTree.MaxDepth)
        {
            return;
        }

        if (node.Kind == LayoutNodeKind.Pane)
        {
            if (!LayoutTree.IsAssigned(node) || node.Assignment is not { } a)
            {
                return;
            }

            var target = a.Target.Trim();

            if (into.Any(e => e.Source == a.Source && LaunchTargets.SameTarget(e.Target, target)))
            {
                return;
            }

            var reuse = a.Source is LayoutSource.App or LayoutSource.File && a.ReuseIfRunning;
            into.Add(new LayoutEntry(a.Source, target, a.Label ?? string.Empty, reuse, area));
            return;
        }

        var r = LayoutTree.ClampRatio(node.Ratio);

        if (node.Direction == SplitDirection.Vertical)
        {
            Walk(node.A, area with { Width = area.Width * r }, into, depth + 1);
            Walk(node.B, area with { X = area.X + area.Width * r, Width = area.Width * (1 - r) }, into, depth + 1);
        }
        else
        {
            Walk(node.A, area with { Height = area.Height * r }, into, depth + 1);
            Walk(node.B, area with { Y = area.Y + area.Height * r, Height = area.Height * (1 - r) }, into, depth + 1);
        }
    }

    /// <summary>
    /// [S-1407] A pane's rectangle in pixels on <paramref name="workArea"/> (original
    /// <c>compute_rect</c>): percentages held to 0..100, rounded, and never smaller than
    /// <see cref="MinWidth"/> × <see cref="MinHeight"/>.
    /// </summary>
    public static ScreenRect Place(PercentRect area, ScreenRect workArea)
    {
        static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 100);

        var x = workArea.X + (int)Math.Round(workArea.Width * Clamp(area.X) / 100, MidpointRounding.AwayFromZero);
        var y = workArea.Y + (int)Math.Round(workArea.Height * Clamp(area.Y) / 100, MidpointRounding.AwayFromZero);
        var w = Math.Max(MinWidth, (int)Math.Round(workArea.Width * Clamp(area.Width) / 100, MidpointRounding.AwayFromZero));
        var h = Math.Max(MinHeight, (int)Math.Round(workArea.Height * Clamp(area.Height) / 100, MidpointRounding.AwayFromZero));
        return new ScreenRect(x, y, w, h);
    }

    /// <summary>
    /// [S-1408] The window rectangle that makes a window's <i>visible</i> frame fill
    /// <paramref name="pane"/>: Windows 10/11 windows carry an invisible resize border (about 7 px
    /// left, right and bottom), so a window set to the pane's rectangle shows a gap. The borders are
    /// the window rect less DWM's extended frame bounds; negative ones (none measured) count as 0.
    /// </summary>
    public static ScreenRect WithInvisibleBorders(ScreenRect pane, int left, int top, int right, int bottom)
    {
        left = Math.Max(0, left);
        top = Math.Max(0, top);
        right = Math.Max(0, right);
        bottom = Math.Max(0, bottom);
        return new ScreenRect(pane.X - left, pane.Y - top, pane.Width + left + right, pane.Height + top + bottom);
    }

    /// <summary>
    /// [S-1408] Whether a window has moved off its pane since it was put there (an app that restores
    /// its own last position right after showing): more than <see cref="PositionSlack"/> off in
    /// position or <see cref="SizeSlack"/> in size, judged on the visible frame.
    /// </summary>
    public static bool Drifted(ScreenRect pane, ScreenRect visible)
        => Math.Abs(visible.X - pane.X) > PositionSlack
        || Math.Abs(visible.Y - pane.Y) > PositionSlack
        || Math.Abs(visible.Width - pane.Width) > SizeSlack
        || Math.Abs(visible.Height - pane.Height) > SizeSlack;

    /// <summary>
    /// [S-1405] The assignments a layout's icon previews: up to <paramref name="max"/> assigned
    /// panes in tree order (original S-72).
    /// </summary>
    public static IReadOnlyList<LayoutAssignment> Preview(LayoutNode? root, int max = 4)
        => LayoutTree.Panes(root)
            .Where(LayoutTree.IsAssigned)
            .Select(pane => pane.Assignment!)
            .Take(Math.Max(0, max))
            .ToList();

    /// <summary>[S-1405] A layout's detail line (original S-57): "No apps yet", "1 app", "3 apps".</summary>
    public static string Summary(LayoutNode? root)
    {
        var count = LayoutTree.AssignedCount(root);
        return count switch
        {
            0 => "No apps yet",
            1 => "1 app",
            _ => $"{count} apps",
        };
    }
}

/// <summary>What is known about a top-level window when a layout looks for the one it opened.</summary>
/// <param name="Handle">The HWND.</param>
/// <param name="ProcessId">Its process.</param>
/// <param name="ExeName">Its process's file name (<c>WINWORD.EXE</c>), or empty when it cannot be read.</param>
/// <param name="ClassName">Its window class.</param>
/// <param name="Visible">WS_VISIBLE.</param>
/// <param name="Owned">It has an owner (a dialog, a tool window of another window).</param>
/// <param name="ToolWindow">WS_EX_TOOLWINDOW.</param>
/// <param name="TitleLength">The length of its title: splash screens usually have none.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public readonly record struct WindowFacts(
    nint Handle,
    int ProcessId,
    string ExeName,
    string ClassName,
    bool Visible,
    bool Owned,
    bool ToolWindow,
    int TitleLength,
    int Width,
    int Height);

/// <summary>How well a window answers a launch.</summary>
public enum WindowMatch
{
    /// <summary>Not this launch's window.</summary>
    None = 0,

    /// <summary>The launch's, but not obviously its main window (a splash, untitled, small): kept in case nothing better comes.</summary>
    Fallback = 1,

    /// <summary>The launch's main window: put it in its pane now.</summary>
    Main = 2,
}

/// <summary>
/// What a launch is waiting for: the process it started (0 when the shell handed none back — a URL,
/// a single-instance app), the exe its window belongs to, and the windows that were already there.
/// </summary>
public sealed record WindowWatch(LayoutSource Source, int ProcessId, string? ExeHint, IReadOnlySet<nint> Before, int OwnProcessId);

/// <summary>
/// [S-1409] Which window a layout launch opened (original <c>find_best_window</c> and
/// <c>launch_explorer</c>). Pure, so the rules that keep the desktop and the taskbar from ever being
/// moved are tested.
/// </summary>
public static class LayoutWindows
{
    /// <summary>File Explorer's folder windows. The only <c>explorer.exe</c> windows a layout may move.</summary>
    public const string ExplorerClass = "CabinetWClass";

    /// <summary>A real main window, not a splash or a transient: visible, unowned, not a tool window, titled, at least 400 × 300.</summary>
    public static bool IsMain(WindowFacts window)
        => IsTopLevel(window) && window.TitleLength > 0 && window.Width >= 400 && window.Height >= 300;

    /// <summary>A window a person would call "a window": visible, unowned, not a tool window.</summary>
    public static bool IsTopLevel(WindowFacts window) => window.Visible && !window.Owned && !window.ToolWindow;

    /// <summary>
    /// [S-1409] Judge one window against a launch. A folder pane takes only a File Explorer folder
    /// window (by class, never by exe: <c>explorer.exe</c> also owns the desktop and the taskbar)
    /// that is new, or that came to the front after the launch (the folder was already open). Any
    /// other pane takes a window of the process it started, or of its exe that is new or came to the
    /// front (a single-instance app hands the launch to its running copy). Never one of this app's
    /// own windows; never an <c>explorer.exe</c> window that is not a folder window.
    /// </summary>
    public static WindowMatch Judge(WindowWatch watch, WindowFacts window, bool cameToFront)
    {
        ArgumentNullException.ThrowIfNull(watch);

        if (!IsTopLevel(window) || window.ProcessId == watch.OwnProcessId)
        {
            return WindowMatch.None;
        }

        var fresh = !watch.Before.Contains(window.Handle) || cameToFront;
        bool ours;

        if (watch.Source == LayoutSource.Folder)
        {
            ours = fresh && string.Equals(window.ClassName, ExplorerClass, StringComparison.Ordinal);
        }
        else
        {
            if (IsExplorer(window.ExeName) && !string.Equals(window.ClassName, ExplorerClass, StringComparison.Ordinal))
            {
                return WindowMatch.None;
            }

            var byProcess = watch.ProcessId != 0 && window.ProcessId == watch.ProcessId;
            var byExe = fresh
                && !string.IsNullOrEmpty(watch.ExeHint)
                && string.Equals(window.ExeName, watch.ExeHint, StringComparison.OrdinalIgnoreCase);
            ours = byProcess || byExe;
        }

        if (!ours)
        {
            return WindowMatch.None;
        }

        return IsMain(window) ? WindowMatch.Main : WindowMatch.Fallback;
    }

    /// <summary>
    /// [S-1409] The running window to move when an app is already open (original "reposition if
    /// running"): the first main window of that exe in z-order. None for a folder or a web address,
    /// and never an <c>explorer.exe</c> window.
    /// </summary>
    public static WindowFacts? Running(IEnumerable<WindowFacts> windows, LayoutSource source, string? exeHint, int ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (source is LayoutSource.Folder or LayoutSource.Url || string.IsNullOrEmpty(exeHint) || IsExplorer(exeHint))
        {
            return null;
        }

        foreach (var window in windows)
        {
            if (window.ProcessId != ownProcessId
                && IsMain(window)
                && string.Equals(window.ExeName, exeHint, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return null;
    }

    private static bool IsExplorer(string? exe) => string.Equals(exe, "explorer.exe", StringComparison.OrdinalIgnoreCase);
}
