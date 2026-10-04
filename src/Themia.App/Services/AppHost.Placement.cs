using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Shell;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>Placing a new widget: the kind being placed, and the click that drops it.</summary>
internal sealed partial class AppHost
{
    /// <summary>The kind being placed, while the user is choosing where it goes; otherwise null.</summary>
    public WidgetKind? PlacingKind { get; private set; }

    /// <summary>Raised when placement starts, ends or is cancelled.</summary>
    public event Action? PlacementChanged;

    /// <summary>
    /// [S-421] Start placing a widget. Nothing is added yet: a ghost follows the pointer across every
    /// display and the widget exists only once the user clicks somewhere.
    /// <para>
    /// The settings window is deliberately not opened. Adding a widget and configuring it are two
    /// decisions, and a window appearing over the very spot the user was about to click is the
    /// fastest way to lose the first one.
    /// </para>
    /// </summary>
    public void BeginPlacement(WidgetKind kind)
    {
        SetEditMode(true);
        Select(null);
        PlacingKind = kind;
        PlacementChanged?.Invoke();
        Log.Info(Scope, $"placing a {kind}");
    }

    /// <summary>Abandon placement without adding anything. Safe to call when nothing is being placed.</summary>
    public void CancelPlacement()
    {
        if (PlacingKind is null)
        {
            return;
        }

        PlacingKind = null;
        PlacementChanged?.Invoke();
    }

    /// <summary>
    /// [S-422] Put the widget being placed into the view of the display it was dropped on, at the
    /// rect its ghost showed. Returns the new widget's id, or null when nothing was being placed.
    /// </summary>
    public string? CompletePlacement(DesktopWindow surface, Themia.Core.Geometry.FracRect rect)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (PlacingKind is not { } kind)
        {
            return null;
        }

        var widget = WidgetInstance.Create(kind, rect);
        var viewId = surface.CurrentViewId;

        // Placement ends before the mutation lands, so the relayout it triggers already sees the
        // surface back in its ordinary edit state rather than drawing a ghost over the new card.
        PlacingKind = null;
        PlacementChanged?.Invoke();

        Mutate(state => (state.FindView(viewId) ?? state.ActiveView()).Widgets.Add(widget));
        Select(widget.Id);
        Log.Info(Scope, $"placed a {kind} on {surface.Monitor.DeviceName}");
        return widget.Id;
    }
}
