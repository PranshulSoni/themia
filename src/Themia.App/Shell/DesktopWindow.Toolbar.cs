using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Shell.Config;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;
using Windows.Graphics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Themia.App.Shell;

/// <summary>The edit toolbar shown along the top of the surface in edit mode, and its menus.</summary>
internal sealed partial class DesktopWindow
{
    /// <summary>[S-905] The toolbar's grid-snap switch.</summary>
    private ToggleButton? _snapToggle;

    private Border BuildToolbar()
    {
        var views = new DropDownButton
        {
            Content = "View",
        };

        // Refilled every time it opens. Built once, it kept listing the views that existed when the
        // surface was created and ticking the one that was active then.
        var viewMenu = new MenuFlyout();
        viewMenu.Opening += (_, _) => FillViewFlyout(viewMenu);
        views.Flyout = viewMenu;

        var add = new DropDownButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = Glyphs.Add, FontSize = 14 }, new TextBlock { Text = "Add widget" } },
            },
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "Add widget");
        add.Flyout = BuildAddFlyout();

        // Quitting lives behind a second click, and away from Done. With no tray entry this is the
        // only place it is offered, but it is also the one button on the toolbar that cannot be
        // undone, and Done is the one right next to it that the user presses constantly.
        var more = new DropDownButton { Content = new FontIcon { Glyph = "\uE712", FontSize = 14 } };
        ToolTipService.SetToolTip(more, "More");

        // An icon-only button has no text for a screen reader to read, and none for anything
        // driving the app from outside to find it by either.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "More");
        more.Loaded += (_, _) => more.Flyout = BuildMoreFlyout();

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                add,
                views,
                BuildSnapToggle(),
                ToolbarButton("", "Settings", _host.ShowSettings),
                more,
                new AppBarSeparator(),
                AccentButton("", "Done", () => _host.SetEditMode(false)),
            },
        };

        return new Border
        {
            Child = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8),
            Background = Brush("AcrylicBackgroundFillColorDefaultBrush", Colors.Black),
            BorderBrush = Brush("SurfaceStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
    }

    /// <summary>
    /// [S-905] Grid snap, on the toolbar as in the original: the one setting the user reaches for in
    /// the middle of arranging widgets. Kept in step with Settings by <see cref="SyncToolbar"/>.
    /// </summary>
    private ToggleButton BuildSnapToggle()
    {
        var toggle = new ToggleButton
        {
            IsChecked = _host.State.Design.GridSnap,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = Glyphs.Grid, FontSize = 14 }, new TextBlock { Text = "Snap" } },
            },
        };

        ToolTipService.SetToolTip(toggle, "Snap widgets to the grid while moving and resizing");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Snap to grid");
        toggle.Click += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            _host.Mutate(state => state.Design.GridSnap = on);
        };

        _snapToggle = toggle;
        return toggle;
    }

    /// <summary>[S-905] The toolbar's switches follow a change made elsewhere (Settings, an import).</summary>
    private void SyncToolbar()
    {
        if (_snapToggle is not null && _snapToggle.IsChecked != Design.GridSnap)
        {
            _snapToggle.IsChecked = Design.GridSnap;
        }
    }

    /// <summary>
    /// The toolbar's overflow. One row today - quitting - which has nowhere else to live now that
    /// there is no tray menu.
    /// </summary>
    private MenuFlyout BuildMoreFlyout()
    {
        var flyout = new MenuFlyout();

#if DEBUG
        // [S-957] Debug builds only, not "release behind developer mode": the gallery's sample data
        // and its PNG command line are debug-only and live in the same files, and a release gallery
        // without them would show nothing but empty states.
        var gallery = new MenuFlyoutItem { Text = "Widget gallery", Icon = new FontIcon { Glyph = Glyphs.Apps } };
        gallery.Click += (_, _) => Gallery.WidgetGallery.Open(WidgetKind.Clock);
        flyout.Items.Add(gallery);
        flyout.Items.Add(new MenuFlyoutSeparator());
#endif

        var exit = new MenuFlyoutItem { Text = "Exit Themia", Icon = new FontIcon { Glyph = "\uE7E8" } };
        exit.Click += (_, _) => _host.RequestExit();
        flyout.Items.Add(exit);

        return flyout;
    }

    private void FillViewFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        // The view THIS display shows, not the app-wide active one: each display has its own.
        var shownId = CurrentViewId;

        foreach (var view in _host.State.Views)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = view.Name,
                IsChecked = view.Id == shownId,

                // [S-903] The view's own icon where it has one.
                Icon = string.IsNullOrEmpty(view.Icon) ? null : new FontIcon { Glyph = view.Icon },
            };

            var id = view.Id;
            item.Click += (_, _) => _host.ShowView(_monitor.Id, id);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var add = new MenuFlyoutItem { Text = "New view", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) =>
        {
            // [S-1065] The free-tier view limit; a refusal says why in a notice ([S-881]).
            if (!_host.AllowCreate(0, 1))
            {
                return;
            }

            _host.Mutate(state =>
            {
                var created = ViewState.Create($"View {state.Views.Count + 1}");
                state.Views.Add(created);
                state.ShowViewOn(_monitor.Id, created.Id);
            }, undo: UndoAction.AddView);
        };

        flyout.Items.Add(add);

        // [S-900] A copy of the view this display shows, shown here in its place.
        var duplicate = new MenuFlyoutItem { Text = "Duplicate this view", Icon = new FontIcon { Glyph = Glyphs.Copy } };
        duplicate.Click += (_, _) => _host.DuplicateView(shownId, _monitor.Id);
        flyout.Items.Add(duplicate);
    }

    private static Button ToolbarButton(string glyph, string text, Action action)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
            },
        };

        button.Click += (_, _) => action();
        ToolTipService.SetToolTip(button, text);
        return button;
    }

    private static Button AccentButton(string glyph, string text, Action action)
    {
        var button = ToolbarButton(glyph, text, action);
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style accent)
        {
            button.Style = accent;
        }

        return button;
    }
}
