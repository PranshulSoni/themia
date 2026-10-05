using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Shell;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1703]/[S-1704] What the side menu's entries do beyond navigating: views are dragged into a new
/// order, and views, displays and widgets each have a right-click menu through the shared
/// <see cref="ItemMenu"/>, so they look like every other menu in the app.
/// </summary>
internal sealed partial class ConfigWindow
{
    /// <summary>The view being dragged in the side menu, or null. Only a drag that began here is accepted.</summary>
    private string? _draggedViewId;

    /// <summary>
    /// The centre of this window in physical screen pixels, or null when it is closed or hidden. A
    /// minimised window reports a position off every display, which then matches none.
    /// </summary>
    public (int X, int Y)? Centre()
    {
        if (_closed || _window.AppWindow is not { IsVisible: true } appWindow)
        {
            return null;
        }

        var position = appWindow.Position;
        var size = appWindow.Size;
        return (position.X + (size.Width / 2), position.Y + (size.Height / 2));
    }

    // -----------------------------------------------------------------------------------------
    // Views: drag to reorder, and the view menu
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1703] Let a view's entry be dragged onto another view's entry: the dragged view takes the
    /// target's place in the order (<see cref="Services.AppHost.ReorderView"/>, the view bar's own
    /// path), so the pane, the bar, the switchers and Ctrl+n all follow.
    /// </summary>
    private void AttachViewItem(NavigationViewItem node, string viewId)
    {
        node.CanDrag = true;
        node.AllowDrop = true;

        node.DragStarting += (_, args) =>
        {
            _draggedViewId = viewId;
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };

        node.DropCompleted += (_, _) => _draggedViewId = null;

        node.DragOver += (_, args) =>
        {
            if (_draggedViewId is { } dragged && dragged != viewId)
            {
                args.AcceptedOperation = DataPackageOperation.Move;
                args.DragUIOverride.IsGlyphVisible = false;
                args.DragUIOverride.IsCaptionVisible = false;
                args.Handled = true;
            }
        };

        node.Drop += (_, args) =>
        {
            if (_draggedViewId is not { } dragged || dragged == viewId)
            {
                return;
            }

            args.Handled = true;
            _draggedViewId = null;

            // After the drop has finished: the move rebuilds the side menu, and with it the entry
            // this event is still being raised on.
            if (!_window.DispatcherQueue.TryEnqueue(() => Run(() => _host.ReorderView(dragged, viewId))))
            {
                Log.Error(Scope, "could not queue a view reorder from the side menu");
            }
        };

        AttachMenu(node, () => ViewMenu(node, viewId));
    }

    /// <summary>
    /// [S-1704] Show on this display, Edit layout; Rename, Change icon; Move up, Move down;
    /// Duplicate, Delete. "This display" is the one the settings window is on ([S-1702]).
    /// </summary>
    private List<ItemMenuEntry> ViewMenu(NavigationViewItem node, string viewId)
    {
        var views = _host.State.Views;
        var index = views.FindIndex(v => v.Id == viewId);
        if (index < 0)
        {
            return [];
        }

        var surface = _host.SettingsSurface();

        return
        [
            new(Glyphs.View, Str.WidgetsC_ViewSwitcher_Menu_Show.Text(), () => ShowHere(viewId),
                Disabled: surface is null || surface.CurrentViewId == viewId),
            new(Glyphs.Edit, Str.Shell_Card_EditLayout.Text(), () => _host.EditView(viewId)),
            ItemMenuEntry.Separator,
            new(Glyphs.Rename, Str.Common_Rename.Text(), () => RenameView(node, viewId), Quick: QuickCommand.Rename),
            new(Glyphs.Star, Str.Shell_Nav_ChangeIcon.Text(), () => ChangeViewIcon(node, viewId)),
            ItemMenuEntry.Separator,
            new(Glyphs.MoveUp, Str.ChildList_MoveUp.Text(), () => _host.MoveView(viewId, -1), Disabled: index == 0),
            new(Glyphs.MoveDown, Str.ChildList_MoveDown.Text(), () => _host.MoveView(viewId, +1), Disabled: index == views.Count - 1),
            ItemMenuEntry.Separator,
            new(Glyphs.Copy, Str.App_DuplicateView.Text(), () => _host.DuplicateView(viewId, _host.SettingsSurface()?.Monitor.Id), Quick: QuickCommand.Duplicate),
            new(Glyphs.Delete, Str.App_DeleteView.Text(), () => _host.DeleteView(viewId), Danger: true, Disabled: views.Count <= 1, Quick: QuickCommand.Delete),
        ];
    }

    private void ShowHere(string viewId)
    {
        if (_host.SettingsSurface() is { } surface)
        {
            _host.ShowView(surface.Monitor.Id, viewId);
        }
    }

    /// <summary>[S-1707] Rename in a small box beside the entry: Enter or leaving it commits, Escape does not.</summary>
    private void RenameView(NavigationViewItem node, string viewId)
    {
        if (_host.State.FindView(viewId) is not { } view)
        {
            return;
        }

        var box = new TextBox { Text = view.Name, MinWidth = 220 };
        var flyout = new Flyout { Content = box, Placement = FlyoutPlacementMode.RightEdgeAlignedTop };
        var cancelled = false;

        box.KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Escape)
            {
                e.Handled = true;
                cancelled = e.Key == VirtualKey.Escape;
                flyout.Hide();
            }
        };

        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        flyout.Closed += (_, _) =>
        {
            if (!cancelled)
            {
                Run(() =>
                {
                    ConfigPages.RenameView(_host, viewId, box.Text);
                    RefreshIfShowing(viewId);
                });
            }
        };

        flyout.ShowAt(node);
    }

    /// <summary>[S-903] The view page's own icon grid, opened beside the entry.</summary>
    private void ChangeViewIcon(NavigationViewItem node, string viewId)
    {
        if (_host.State.FindView(viewId) is not { } view)
        {
            return;
        }

        var picker = ConfigPages.ViewIconPicker(_host, view, () => RefreshIfShowing(viewId));
        picker.Flyout?.ShowAt(node);
    }

    /// <summary>The view page on screen shows the name and icon it was built with; build it again after a change made here.</summary>
    private void RefreshIfShowing(string viewId)
    {
        if (!_closed && _target is ConfigTarget.View shown && shown.ViewId == viewId)
        {
            RenderBody();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Widgets and displays
    // -----------------------------------------------------------------------------------------

    /// <summary>[S-1704] A widget's entry: Edit layout (its view on this display, the widget selected), Delete widget.</summary>
    private void AttachWidgetItem(NavigationViewItem leaf, string viewId, string widgetId)
        => AttachMenu(leaf, () =>
        [
            new(Glyphs.Edit, Str.Shell_Card_EditLayout.Text(), () => _host.EditView(viewId, widgetId)),
            ItemMenuEntry.Separator,
            new(Glyphs.Delete, Str.CtxMenu_DeleteWidget.Text(), () =>
            {
                _host.Select(widgetId);
                _host.DeleteSelected();
            }, Danger: true, Quick: QuickCommand.Delete),
        ]);

    /// <summary>
    /// [S-1704] A display's entry: which existing view it shows ([S-1706]: never a new one), and
    /// whether Themia is shown there at all (the last display on cannot be switched off, [S-845]).
    /// </summary>
    private void AttachDisplayItem(NavigationViewItem entry, string monitorId)
        => AttachMenu(entry, () =>
        {
            var state = _host.State;
            var shown = ConfigPages.ShownViewId(state, monitorId);
            var views = state.Views
                .Select(v =>
                {
                    var id = v.Id;
                    return new ItemMenuEntry(null, v.Name, () => _host.ShowView(monitorId, id), Checked: id == shown);
                })
                .ToList();

            var enabled = !state.DisabledMonitors.Contains(monitorId);
            var canToggle = !enabled || DisplayPlan.CanDisable([.. _host.Displays.Select(d => d.Info)], state.DisabledMonitors, monitorId);

            return
            [
                new(Glyphs.View, Str.Shell_Display_Shows.Text(), null, Children: views),
                new(Glyphs.Display, Str.Shell_Display_Show.Text(), () => _host.SetDisplayEnabled(monitorId, !enabled),
                    Disabled: !canToggle, Checked: enabled),
            ];
        });

    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Open <paramref name="entries"/> at the pointer on a right-click. Read fresh on every click, so
    /// "Move up" and the checked view follow the state. Selecting the entry is not part of it: a
    /// right-click opens a menu, it does not navigate.
    /// </summary>
    private void AttachMenu(NavigationViewItem item, Func<IEnumerable<ItemMenuEntry>> entries)
    {
        item.RightTapped += (_, args) =>
        {
            if (args.Handled)
            {
                return;
            }

            try
            {
                ItemMenu.Show(item, args, entries());
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "could not open a side menu entry's menu", ex);
            }
        };
    }

    /// <summary>Run a side-menu command from a XAML event, logging rather than throwing.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "a side menu command failed", ex);
        }
    }
}
