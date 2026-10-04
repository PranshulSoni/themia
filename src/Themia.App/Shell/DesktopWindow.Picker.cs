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

/// <summary>Adding a widget: the picker, the ghost and the click that places it.</summary>
internal sealed partial class DesktopWindow
{
    /// <summary>
    /// Room a group keeps free at the top of its area for its own header when a widget is created
    /// inside it, so the new member does not cover the group's title and collapse button.
    /// </summary>
    private const double GroupHeaderAllowancePx = 40;

    /// <summary>
    /// [S-420] The widget picker: a flyout rather than a page in the settings window, because
    /// choosing a widget is a moment's decision made while looking at the desktop, and the settings
    /// window covers the desktop.
    /// <para>
    /// [S-955] Its content is rebuilt every time it opens, because what it offers depends on things
    /// that change while it is closed: which containers this display shows, which widget is
    /// selected, and whether developer mode is on.
    /// </para>
    /// </summary>
    private Flyout BuildAddFlyout()
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,

            // The presenter caps itself at 456px by default, sized for a menu of one column. Left at
            // that, the picker showed two and a half of its four columns and clipped the rest.
            FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
            {
                Setters =
                {
                    new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity),
                    new Setter(FrameworkElement.MaxHeightProperty, double.PositiveInfinity),
                },
            },
        };

        flyout.Opening += (_, _) =>
        {
            var (targets, initial) = PickerTargets();
            flyout.Content = new WidgetPicker(
                _host.State.Preferences.DevMode,
                targets,
                initial,
                (definition, target) => OnPicked(flyout, definition, target),
                flyout.Hide);
        };

        // [S-949] Typing searches at once: the caret is in the box as soon as the picker is open.
        flyout.Opened += (_, _) => (flyout.Content as WidgetPicker)?.FocusSearch();
        return flyout;
    }

    /// <summary>
    /// [S-955] The desktop, then every container on this display that will take one more widget,
    /// in view order. A container that is selected when the picker opens is where it starts: the
    /// user pointed at it before asking to add something.
    /// </summary>
    private (List<PickerTarget> Targets, int Initial) PickerTargets()
    {
        var targets = new List<PickerTarget> { new(null, "Desktop") };
        var initial = 0;
        var view = ViewForThisMonitor(_host.State);
        var seen = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var widget in view.Widgets)
        {
            if (!WidgetTree.IsContainer(widget.Kind) || !view.CanAddInto(widget.Id))
            {
                continue;
            }

            // Two untitled groups would otherwise both read "Group".
            var name = string.IsNullOrWhiteSpace(widget.Label) ? WidgetCatalog.Definition(widget.Kind).Label : widget.Label.Trim();
            seen[name] = seen.TryGetValue(name, out var count) ? count + 1 : 1;
            if (seen[name] > 1)
            {
                name = $"{name} {seen[name]}";
            }

            if (string.Equals(widget.Id, _host.SelectedWidgetId, StringComparison.Ordinal))
            {
                initial = targets.Count;
            }

            targets.Add(new PickerTarget(widget.Id, name));
        }

        return (targets, initial);
    }

    /// <summary>
    /// [S-421, S-956] A widget was chosen. For the desktop that starts click-to-place; for a
    /// container it is created inside it at once, since the container decides where it goes.
    /// </summary>
    private void OnPicked(Flyout flyout, WidgetDefinition definition, PickerTarget target)
    {
        flyout.Hide();

        if (target.ContainerId is not { } containerId)
        {
            _host.BeginPlacement(definition.Kind);
            return;
        }

        AddIntoContainer(containerId, definition);
    }

    /// <summary>
    /// [S-956] Create a widget inside a container in one write, then select it. The settings window
    /// is not opened, for the reason [S-421] gives: adding and configuring are two decisions.
    /// </summary>
    private void AddIntoContainer(string containerId, WidgetDefinition definition)
    {
        var metrics = Metrics;

        // The default size is in pixels; a group member keeps its own rect on this display's grid,
        // so it is converted against this display, as a placement would be.
        var size = metrics.IsUsable
            ? FracRect.FromPixels(0, 0, definition.DefaultWidthPx, definition.DefaultHeightPx, metrics)
            : new FracRect(0, 0, 0.2, 0.2);
        var headerRoom = metrics.IsUsable ? GroupHeaderAllowancePx / metrics.Height : 0;

        if (!ViewForThisMonitor(_host.State).CanAddInto(containerId))
        {
            // The tree changed between opening the picker and choosing (another display, a paste).
            Log.Warn(Scope, $"refused to add a {definition.Kind} into '{containerId}'");
            return;
        }

        string? addedId = null;
        _host.Mutate(state =>
            addedId = ViewForThisMonitor(state).AddInto(containerId, definition.Kind, size.W, size.H, headerRoom)?.Id);

        if (addedId is null)
        {
            Log.Warn(Scope, $"could not add a {definition.Kind} into '{containerId}'");
            return;
        }

        _host.Select(addedId);
        Log.Info(Scope, $"added a {definition.Kind} into '{containerId}'");
    }

    private static Border BuildPlacementHint(TextBlock text) => new()
    {
        Child = text,
        Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
        Margin = new Thickness(0, 24, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        CornerRadius = new CornerRadius(8),
        Background = Brush("AcrylicBackgroundFillColorDefaultBrush", Colors.Black),
        BorderBrush = Brush("SurfaceStrokeColorDefaultBrush", Colors.Gray),
        BorderThickness = new Thickness(1),

        // Words, not a control: anything clickable here would have to be told apart from a click
        // meant to place the widget underneath it.
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private static Grid BuildGhost(Rectangle frame, FontIcon icon, TextBlock label)
    {
        // The fill is the stroke's colour at low alpha: the same accent, so it reads as one shape
        // rather than a box inside a box.
        var accent = (frame.Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        frame.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, accent.R, accent.G, accent.B));

        icon.HorizontalAlignment = HorizontalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;

        var content = new StackPanel
        {
            Spacing = Fluent.Space2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, label },
        };

        return new Grid
        {
            Children = { frame, content },
            Visibility = Visibility.Collapsed,
        };
    }

    /// <summary>
    /// [S-423] Show a widget-shaped outline at a rect on this display: where a widget being placed,
    /// or dragged in from another display, will land. It traces the painted card, inside the gap,
    /// because that is what the user will see once it is there.
    /// </summary>
    public void ShowGhost(FracRect rect, WidgetKind kind)
    {
        var metrics = Metrics;
        if (_closed || !metrics.IsUsable)
        {
            return;
        }

        var box = GridGeometry.Inset(GridGeometry.ToPixels(rect, metrics), Design.WidgetGapPx);
        var definition = WidgetCatalog.Definition(kind);

        _ghostIcon.Glyph = definition.Glyph;
        _ghostLabel.Text = definition.Label;
        _ghostFrame.RadiusX = Design.CornerRadiusPx;
        _ghostFrame.RadiusY = Design.CornerRadiusPx;

        Canvas.SetLeft(_ghost, box.X);
        Canvas.SetTop(_ghost, box.Y);
        _ghost.Width = Math.Max(0, box.Width);
        _ghost.Height = Math.Max(0, box.Height);
        _ghost.Visibility = Visibility.Visible;
    }

    public void HideGhost() => _ghost.Visibility = Visibility.Collapsed;

    /// <summary>
    /// [S-421] Enter or leave placing. The cards stop taking input while a widget is being placed,
    /// so a click over an existing widget places the new one there instead of grabbing the old one,
    /// and the toolbar steps aside for a line saying what the click will do.
    /// </summary>
    private void OnPlacementChanged()
    {
        if (_closed)
        {
            return;
        }

        var placing = _host.PlacingKind;
        _widgetLayer.IsHitTestVisible = placing is null;

        if (placing is { } kind)
        {
            _placementHintText.Text =
                $"Click to place {WidgetCatalog.Definition(kind).Label}  ·  Right-click or Esc to cancel";
            _placementHint.Visibility = Visibility.Visible;
            _toolbar.Visibility = Visibility.Collapsed;

            // The picker was opened on one display, so the pointer is already somewhere: show the
            // ghost there at once rather than waiting for the first move.
            if (IsUnderCursor && Win32.GetCursorPos(out var cursor))
            {
                var (x, y) = FractionOf(cursor);
                ShowGhost(PlacementRect(kind, x, y), kind);
            }
            else
            {
                HideGhost();
            }

            return;
        }

        _placementHint.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = EditMode ? Visibility.Visible : Visibility.Collapsed;
        HideGhost();
    }

    /// <summary>[S-11] Centred on the pointer, then snapped and clamped. Alt bypasses the snap, as when dragging.</summary>
    private FracRect PlacementRect(WidgetKind kind, double xFrac, double yFrac)
    {
        var definition = WidgetCatalog.Definition(kind);
        var snap = Design.GridSnap && !AltHeld();
        return GridGeometry.PlaceAt(xFrac, yFrac, definition.DefaultWidthPx, definition.DefaultHeightPx, Metrics, snap);
    }

    /// <summary>Where a point in this surface's own coordinates falls, as a fraction of it.</summary>
    private (double X, double Y) FractionOfDip(Point dip)
    {
        var metrics = Metrics;
        return metrics.IsUsable
            ? (Math.Clamp(dip.X / metrics.Width, 0, 1), Math.Clamp(dip.Y / metrics.Height, 0, 1))
            : (0.5, 0.5);
    }

    private static bool AltHeld() => InputKeyboardSource
        .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_host.PlacingKind is not { } kind)
        {
            return;
        }

        var (x, y) = FractionOfDip(e.GetCurrentPoint(_root).Position);
        ShowGhost(PlacementRect(kind, x, y), kind);
    }

    /// <summary>The pointer went to another display: the ghost goes with it, so it leaves here.</summary>
    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_host.PlacingKind is not null)
        {
            HideGhost();
        }
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!EditMode)
        {
            return;
        }

        var point = e.GetCurrentPoint(_root);

        if (_host.PlacingKind is { } kind)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                // Placed where the click is, not where the last move put the ghost: the two differ
                // whenever the pointer moved and clicked inside one frame.
                var (x, y) = FractionOfDip(point.Position);
                _host.CompletePlacement(this, PlacementRect(kind, x, y));
            }
            else
            {
                _host.CancelPlacement();
            }

            _root.Focus(FocusState.Pointer);
            e.Handled = true;
            return;
        }

        // Clicking empty surface also gives the surface focus, or the shortcuts stop working as soon
        // as the user clicks away from a widget.
        _host.Select(null);
        _root.Focus(FocusState.Pointer);
    }

    // [S-822] Through the appearance engine, so the ghost and the grid follow the theme and accent.
    private static Brush Brush(string key, Windows.UI.Color fallback) => DesignApplier.Brush(key, fallback);
}
