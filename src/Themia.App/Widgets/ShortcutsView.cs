using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The links, files, folders and Windows locations somebody keeps to hand.
/// <para>
/// A Windows 11 list: fixed-height rows, the Body step for the name, a 16px Segoe glyph for what
/// kind of thing it is, and no rule between rows. The list itself is the whole widget — there is no
/// editing here, because a rename or a remove one mis-click away on a surface with no undo belongs
/// in the settings window, which is where it is.
/// </para>
/// <para>
/// <b>Every target is re-checked at the moment it is opened</b>, by
/// <see cref="ShellLauncher"/>. The config file it came from lives in the user's profile and
/// anything running as the user can rewrite it, so a shortcut that was valid when it was added is
/// not evidence that it is valid now. A target the rule refuses is drawn as a refused row rather
/// than quietly dropped: an item that has been tampered with is exactly the one worth seeing.
/// </para>
/// <para>
/// The original widget also allowed a shortcut to hold an "app layout" — a tree of panes that opens
/// several applications and snaps each onto a region of a display. That is a window manager rather
/// than a shortcut, and it is deliberately not ported.
/// </para>
/// </summary>
internal sealed partial class ShortcutsView : Grid, IWidgetView, IWidgetSurfaceAware
{
    /// <summary>Below this width the target column is dropped; the name is what the row is for.</summary>
    private const double TargetMinimumWidth = 340;

    /// <summary>How wide the target column may grow before it starts taking room from the name.</summary>
    private const double TargetWidth = 160;

    private readonly TextBlock _title = Fluent.Text(FluentType.BodyStrong);
    private readonly Grid _header;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;

    /// <summary>One per row, so the hover handlers come off with the rows they light.</summary>
    private readonly List<IDisposable> _hovers = [];

    private IWidgetSurface? _surface;
    private ShortcutsConfig _config = new();
    private bool _detached;
    private bool _compact;
    private bool _targetVisible = true;

    public ShortcutsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "Shortcuts";

        _header = Fluent.Header(Glyphs.Shortcut, _title);
        _header.Margin = Fluent.HeaderMargin;
        SetRow(_header, 0);

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        SetRow(_scroll, 1);

        _empty = Fluent.EmptyState(
            Glyphs.Shortcut,
            new TextBlock { Text = "No shortcuts yet. Add them in this widget's settings." },
            out _emptyIcon);

        _empty.Visibility = Visibility.Collapsed;
        SetRow(_empty, 1);

        Children.Add(_header);
        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;

        var label = surface.Label;

        if (!string.IsNullOrWhiteSpace(label))
        {
            _title.Text = label;
        }
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        // No ticker: a shortcut list changes when its settings change and at no other time.
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ShortcutsConfig ?? new ShortcutsConfig();
        ApplyLayout();
        Render();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Height < Fluent.CompactBelow;
        var target = args.NewSize.Width >= TargetMinimumWidth;

        ApplyLayout();

        if (compact == _compact && target == _targetVisible)
        {
            return;
        }

        _compact = compact;
        _targetVisible = target;
        Render();
    }

    private void ApplyLayout()
    {
        var height = ActualHeight;
        var measured = height > 0;

        _header.Visibility = measured && height < Fluent.HeaderHidesBelow
            ? Visibility.Collapsed
            : Visibility.Visible;

        _emptyIcon.Visibility = measured && height < 180 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render()
    {
        ClearRows();

        if (_config.Items.Count == 0)
        {
            _empty.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var detail = _config.ShowTarget && _targetVisible;

        foreach (var item in _config.Items)
        {
            _list.Children.Add(BuildRow(item, detail));
        }
    }

    private FrameworkElement BuildRow(ShortcutItem item, bool detail)
    {
        var classified = LaunchTargets.Classify(item.Target);

        var icon = Fluent.Icon(GlyphFor(classified.Kind), ColorKeyFor(classified.Kind));
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = LaunchTargets.DisplayName(item.Label, item.Target);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(icon, 0);
        SetColumn(name, 1);
        grid.Children.Add(icon);
        grid.Children.Add(name);

        if (detail)
        {
            var target = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            target.Text = item.Target;
            target.TextAlignment = TextAlignment.Right;
            target.MaxWidth = TargetWidth;
            target.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            SetColumn(target, 2);
            grid.Children.Add(target);
        }

        var row = Fluent.Row(grid, _compact);
        row.Tag = item.Target;
        _hovers.Add(Fluent.Hover(row));

        if (classified.IsValid)
        {
            // Double-click by default: this surface sits under the pointer's path across the
            // desktop, and a stray single click opening something is what makes a launcher a hazard.
            if (_config.OpenOnSingleClick)
            {
                row.Tapped += OnRowActivated;
            }
            else
            {
                row.DoubleTapped += OnRowOpened;
            }

            ToolTipService.SetToolTip(row, item.Target);
        }
        else
        {
            // No handler at all, so a refused target cannot be opened by any gesture. The row says
            // what is wrong rather than disappearing, because a shortcut that changed under the
            // user is the one worth showing.
            ToolTipService.SetToolTip(row, $"{item.Target}\n{LaunchTargets.RefusalMessage(classified.Refusal)}");
        }

        return row;
    }

    /// <summary>The 16px Segoe glyph that says what kind of thing a row points at.</summary>
    private static string GlyphFor(LaunchKind kind) => kind switch
    {
        LaunchKind.Web => Glyphs.Link,
        LaunchKind.LocalPath => Glyphs.File,
        LaunchKind.ShellNamespace => Glyphs.Folder,
        _ => Glyphs.Info,
    };

    /// <summary>
    /// A refused row is drawn in the stock "something is wrong" colour, so it reads as a problem
    /// rather than as an ordinary row that happens not to respond.
    /// </summary>
    private static string ColorKeyFor(LaunchKind kind)
        => kind == LaunchKind.Refused ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush";

    private void OnRowActivated(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;
        Open(sender);
    }

    private void OnRowOpened(object sender, DoubleTappedRoutedEventArgs args)
    {
        args.Handled = true;
        Open(sender);
    }

    private void Open(object sender)
    {
        if (_detached || sender is not Border { Tag: string target })
        {
            return;
        }

        // The one launch path in the app. It classifies the target again rather than trusting that
        // this row was drawn from a valid one.
        ShellLauncher.Open(target);
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();

        foreach (var child in _list.Children)
        {
            if (child is Border row)
            {
                row.Tapped -= OnRowActivated;
                row.DoubleTapped -= OnRowOpened;
            }
        }

        _list.Children.Clear();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        SizeChanged -= OnSizeChanged;
        ClearRows();

        _surface = null;
    }
}
