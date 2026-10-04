using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// What a person can do to the gallery while looking at it: the slideshow's hover bar, opening a
/// picture, the picture's own menu and the widget's menu entries.
/// <para>
/// Keyboard ([S-29] in the original: Left / Right / Space while the widget has focus) is not wired:
/// the desktop surface takes no keyboard outside edit mode, and asking for it would pull focus away
/// from whatever the user is working in to move a photo frame on. <see cref="Previous"/>,
/// <see cref="Next"/> and <see cref="TogglePause"/> are the whole of what a key handler would call,
/// so the shell's keyboard lease can attach them when it exists.
/// </para>
/// </summary>
internal sealed partial class GalleryView
{
    private const double ControlSize = 32;

    private readonly Border _controls = new();
    private readonly FontIcon _pauseIcon = new() { Glyph = Glyphs.Pause, FontSize = Fluent.IconSize };
    private Button _pauseButton = null!;
    private bool _hovering;

    private static readonly (GalleryLayout Layout, string Glyph, string Text)[] Layouts =
    [
        (GalleryLayout.Slideshow, Glyphs.Play, "Slideshow"),
        (GalleryLayout.Grid, Glyphs.Grid, "Grid"),
        (GalleryLayout.Scatter, Glyphs.Shuffle, "Scatter"),
        (GalleryLayout.Mosaic, Glyphs.Group, "Mosaic"),
    ];

    private void BuildCommands()
    {
        var previous = ControlButton(new FontIcon { Glyph = Glyphs.Previous, FontSize = Fluent.IconSize }, "Previous picture", Previous);
        _pauseButton = ControlButton(_pauseIcon, "Pause slideshow", TogglePause);
        var next = ControlButton(new FontIcon { Glyph = Glyphs.Next, FontSize = Fluent.IconSize }, "Next picture", Next);

        // [S-769] A pill over the bottom of the picture, shown only while the pointer is over the
        // widget: a photo frame is for looking at, and permanent buttons would sit on every photo.
        _controls.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { previous, _pauseButton, next },
        };
        _controls.Padding = new Thickness(2);
        _controls.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _controls.Background = FluentLayout.Themed("AcrylicInAppFillColorDefaultBrush", Microsoft.UI.Colors.Black);
        _controls.BorderBrush = FluentLayout.Themed("SurfaceStrokeColorFlyoutBrush", Microsoft.UI.Colors.Gray);
        _controls.BorderThickness = new Thickness(1);
        _controls.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_controls);
        Canvas.SetZIndex(_controls, 2000);

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerLeft;
        PointerCanceled += OnPointerCanceled;
        RightTapped += OnRightTapped;
    }

    private static Button ControlButton(FontIcon icon, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = icon,
            Width = ControlSize,
            Height = ControlSize,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };

        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private void DetachCommands()
    {
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerLeft;
        PointerCanceled -= OnPointerCanceled;
        RightTapped -= OnRightTapped;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovering = true;
        UpdateControls();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _hovering = false;
        UpdateControls();
    }

    private void OnPointerLeft(object sender, PointerRoutedEventArgs e)
    {
        // Exited bubbles up from every picture and button inside, so moving from a photo onto the
        // bar would hide the bar. Only leaving this view's own bounds counts.
        var at = e.GetCurrentPoint(this).Position;

        if (at.X >= 0 && at.Y >= 0 && at.X < ActualWidth && at.Y < ActualHeight)
        {
            return;
        }

        _hovering = false;
        UpdateControls();
    }

    /// <summary>Put the hover bar at the bottom centre of the main picture.</summary>
    private void PlaceControls(GalleryPlan plan)
    {
        var width = (3 * ControlSize) + (2 * 2) + 6;
        var height = ControlSize + 6;
        Canvas.SetLeft(_controls, Math.Max(0, (plan.MainWidth - width) / 2));
        Canvas.SetTop(_controls, Math.Max(0, plan.MainHeight - height - Fluent.Space2));
        UpdateControls();
    }

    private void HideControls()
    {
        _controls.Visibility = Visibility.Collapsed;
    }

    private void UpdateControls()
    {
        var show = _hovering && IsSlideshow && _plan is { Controls: true, Mode: GalleryMode.Slideshow };
        _controls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _pauseIcon.Glyph = _paused ? Glyphs.Play : Glyphs.Pause;
        ToolTipService.SetToolTip(_pauseButton, _paused ? "Resume slideshow" : "Pause slideshow");
    }

    /// <summary>Give a new frame its pointer behaviour: open on double-tap, jump on a thumbnail tap.</summary>
    private void WireFrame(GalleryFrame frame)
    {
        // [S-771] Double, not single: a single click on the desktop layer is how a card is selected
        // and dragged, and opening a photo viewer on every stray click is the opposite of calm.
        frame.Host.DoubleTapped += (_, args) =>
        {
            if (frame.Path is { } path && Picture.IsLocal(path))
            {
                ShellLauncher.Open(path);
                args.Handled = true;
            }
        };

        if (frame.Thumbnail)
        {
            frame.Host.Tapped += (_, args) =>
            {
                JumpTo(frame.Index);
                args.Handled = true;
            };
        }
    }

    /// <summary>[S-768] Step back one slide and restart the hold, so the picture chosen gets its full time.</summary>
    private void Previous() => JumpTo(_index - 1);

    private void Next() => JumpTo(_index + 1);

    private void JumpTo(int index)
    {
        var count = _scan.Paths.Count;

        if (count == 0)
        {
            return;
        }

        _index = ((index % count) + count) % count;
        _slideTimer?.Stop();
        ApplySlideCadence();
        Lay();
    }

    /// <summary>[S-769] Pause or resume the slideshow. The picture stays where it is.</summary>
    private void TogglePause()
    {
        _paused = !_paused;
        ApplySlideCadence();
        UpdateControls();
    }

    private void SetLayout(GalleryLayout layout)
    {
        if (_config.Layout == layout)
        {
            return;
        }

        _config.Layout = layout;
        _surface?.RequestSave();
        ApplyConfig(_config);
    }

    /// <summary>[S-773] Read the source again now, and deal a fresh scatter or mosaic.</summary>
    private void RefreshNow() => Rescan(reroll: true);

    /// <summary>
    /// [S-771] The menu of the picture under the pointer — open it, show it in Explorer — followed by
    /// the layout switch and refresh, which on a card covered edge to edge in pictures would
    /// otherwise only be reachable from the header.
    /// </summary>
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        // An empty state has nothing of its own to offer: the card's menu (Configure first) opens.
        if (_plan is null)
        {
            return;
        }

        var frame = FrameUnder(args.OriginalSource as DependencyObject);
        ItemMenu.Show(this, args, MenuEntries(frame?.Path));
    }

    private GalleryFrame? FrameUnder(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
        {
            if (node is Border { Tag: GalleryFrame frame })
            {
                return frame;
            }
        }

        return null;
    }

    private IEnumerable<ItemMenuEntry> MenuEntries(string? path)
    {
        if (path is not null && Picture.IsLocal(path))
        {
            yield return new ItemMenuEntry(Glyphs.Open, "Open", () => ShellLauncher.Open(path));
            yield return new ItemMenuEntry(Glyphs.Folder, "Show in Explorer", () => ShellLauncher.OpenContainingFolder(path));
            yield return ItemMenuEntry.Separator;
        }

        // [S-36] The layouts are a folder's; a file and the photo of the day are one picture.
        if (!_config.IsSingleSource)
        {
            foreach (var (layout, glyph, text) in Layouts)
            {
                var chosen = layout;
                yield return new ItemMenuEntry(glyph, text, () => SetLayout(chosen), Checked: _config.Layout == layout);
            }

            yield return ItemMenuEntry.Separator;
        }

        if (IsSlideshow)
        {
            yield return new ItemMenuEntry(_paused ? Glyphs.Play : Glyphs.Pause, _paused ? "Resume slideshow" : "Pause slideshow", TogglePause);
        }

        yield return new ItemMenuEntry(Glyphs.Refresh, "Refresh images", RefreshNow);
    }

    /// <summary>[S-773] The card's own menu: the other layouts, pause, and refresh.</summary>
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            var actions = new List<WidgetAction>();

            if (IsSlideshow)
            {
                actions.Add(new WidgetAction(_paused ? Glyphs.Play : Glyphs.Pause, _paused ? "Resume slideshow" : "Pause slideshow", TogglePause));
            }

            actions.Add(new WidgetAction(Glyphs.Refresh, "Refresh images", RefreshNow));

            if (!_config.IsSingleSource)
            {
                foreach (var (layout, glyph, text) in Layouts)
                {
                    if (layout != _config.Layout)
                    {
                        var chosen = layout;
                        actions.Add(new WidgetAction(glyph, $"Show as {text.ToLowerInvariant()}", () => SetLayout(chosen)));
                    }
                }
            }

            return actions;
        }
    }
}
