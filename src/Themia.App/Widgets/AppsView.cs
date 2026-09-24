using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The applications this PC has, as a list you can launch from.
/// <para>
/// A Windows 11 list rather than a tile wall: the compact 32px row, the Body step for the name, the
/// Start-menu folder as a right-hand detail column, and no rule between rows. The row carries the
/// 16px inset as its own padding, so the fill under the pointer reaches both edges of the card the
/// way a row's does.
/// </para>
/// <para>
/// The enumeration is disk work and runs off the UI thread, discarded when a newer one has started.
/// Everything that is opened goes through <see cref="ShellLauncher"/>, which is the app's one launch
/// path and the only thing that decides what may be launched.
/// </para>
/// </summary>
internal sealed partial class AppsView : Grid, IWidgetView, IWidgetSurfaceAware
{
    /// <summary>Below this width the folder column is dropped: the name is what a launcher is for.</summary>
    private const double CategoryMinimumWidth = 320;

    /// <summary>
    /// Below this height the filter box is dropped. At 180x120 a 32px box and its margins are most
    /// of the widget, and a filter over two visible rows is not worth the two rows.
    /// </summary>
    private const double FilterHidesBelow = 168;

    /// <summary>How many characters of a folder name the detail column shows.</summary>
    private const double CategoryWidth = 96;

    private readonly TextBlock _title = Fluent.Text(FluentType.BodyStrong);
    private readonly Grid _header;
    private readonly ProgressRing _busy = new();
    private readonly TextBox _filter = new();
    private readonly Border _filterHost;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();

    /// <summary>One per row, so the hover handlers come off with the rows they light.</summary>
    private readonly List<IDisposable> _hovers = [];

    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private AppsConfig _config = new();
    private IDisposable? _subscription;
    private CancellationTokenSource? _inFlight;

    private IReadOnlyList<InstalledApp> _apps = [];

    /// <summary>
    /// Which scan is current. A reply carrying an older number was started against a config nobody
    /// is looking at any more, and is dropped rather than drawn.
    /// </summary>
    private int _generation;

    private bool _everLoaded;
    private bool _detached;
    private bool _compact;
    private bool _categoryVisible = true;

    public AppsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "Apps";

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        _header = Fluent.Header(Glyphs.Apps, _title, _busy);
        _header.Margin = Fluent.HeaderMargin;
        SetRow(_header, 0);

        _filter.PlaceholderText = "Filter";
        _filter.IsSpellCheckEnabled = false;
        _filter.IsTextPredictionEnabled = false;
        _filter.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _filter.TextChanged += OnFilterChanged;
        _filter.KeyDown += OnFilterKeyDown;

        // The desktop surface carries WS_EX_NOACTIVATE and never becomes the foreground window, so
        // a text box on it shows a caret and receives nothing. The surface lifts that while the box
        // has focus; without these two lines the filter looks like it works and swallows every key.
        _filter.GotFocus += OnFilterGotFocus;
        _filter.LostFocus += OnFilterLostFocus;

        _filterHost = new Border
        {
            Child = _filter,
            Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2),
        };

        SetRow(_filterHost, 1);

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        SetRow(_scroll, 2);

        _empty = Fluent.EmptyState(Glyphs.Apps, _emptyText, out _emptyIcon);
        _empty.Visibility = Visibility.Collapsed;
        SetRow(_empty, 2);

        Children.Add(_header);
        Children.Add(_filterHost);
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
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as AppsConfig ?? new AppsConfig();
        var previous = _config;

        _config = next;

        // Only a change to what comes back off the disk is worth a re-scan. Dragging the count
        // slider must not fire a Start-menu walk per frame.
        var needsRead = !_everLoaded || previous.EffectiveMaxCount != next.EffectiveMaxCount;

        // Subscribing fires a tick of its own, and that tick re-reads with the config already in
        // place above, so there is nothing left for this call to do.
        if (Resubscribe())
        {
            return;
        }

        if (needsRead)
        {
            Reload();
        }
        else
        {
            ApplyLayout();
            Render();
        }
    }

    private bool Resubscribe()
    {
        if (_subscription is not null)
        {
            return false;
        }

        _subscription = _ticker?.Subscribe(_config.TickInterval, Reload);
        return _subscription is not null;
    }

    // -----------------------------------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------------------------------

    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        _everLoaded = true;

        // The previous scan was started for a config nobody is looking at any more.
        _inFlight?.Cancel();

        var cts = new CancellationTokenSource();
        _inFlight = cts;

        var generation = ++_generation;
        var maxCount = _config.EffectiveMaxCount;

        // Say something immediately, but only when there is nothing on screen to keep. A tick that
        // re-reads the same Start menu must not blank the list it is about to redraw.
        if (_apps.Count == 0)
        {
            ApplyLayout();
            Render();
        }

        _ = LoadAsync(maxCount, generation, cts);
    }

    private async Task LoadAsync(int maxCount, int generation, CancellationTokenSource cts)
    {
        try
        {
            SetBusy(true);

            var apps = await Task.Run(() => InstalledApps.Read(maxCount, cts.Token), cts.Token)
                .ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            _apps = apps;
            ApplyLayout();
            Render();
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer scan, which will draw in this one's place.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
                SetBusy(false);
            }

            cts.Dispose();
        }
    }

    private void SetBusy(bool busy)
    {
        if (_detached)
        {
            return;
        }

        _busy.IsActive = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    private void OnFilterChanged(object sender, TextChangedEventArgs args) => Render();

    private void OnFilterKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        args.Handled = true;
        _filter.Text = string.Empty;
    }

    private void OnFilterGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnFilterLostFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(false);

    // -----------------------------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Height < Fluent.CompactBelow;
        var category = args.NewSize.Width >= CategoryMinimumWidth;

        ApplyLayout();

        if (compact == _compact && category == _categoryVisible)
        {
            return;
        }

        _compact = compact;
        _categoryVisible = category;
        Render();
    }

    /// <summary>
    /// What a tile this size shows. Nothing here is a font size: every piece of type is a fixed ramp
    /// step and the rows are the standard heights, so a tile that grows shows more rows rather than
    /// larger ones.
    /// </summary>
    private void ApplyLayout()
    {
        var height = ActualHeight;
        var measured = height > 0;

        _header.Visibility = measured && height < Fluent.HeaderHidesBelow
            ? Visibility.Collapsed
            : Visibility.Visible;

        var room = !measured || height >= FilterHidesBelow;

        _filterHost.Visibility = _config.ShowFilter && room ? Visibility.Visible : Visibility.Collapsed;

        // The icon is dropped on a tile too short to carry it: a tile showing only an icon says
        // less than one showing only the sentence.
        _emptyIcon.Visibility = measured && height < 180 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render()
    {
        ClearRows();

        // The filter is only applied when its box is actually on screen. A tile shrunk below the
        // threshold with a query still typed into it would otherwise show a list narrowed by
        // something the reader cannot see.
        var query = _filterHost.Visibility == Visibility.Visible ? _filter.Text : string.Empty;
        var visible = InstalledAppRules.Filter(_apps, query);

        if (visible.Count == 0)
        {
            // Each of these needs a different thing from the reader, so each says which it is: an
            // app list showing a blank tile is indistinguishable from a broken one.
            _emptyText.Text = !_everLoaded || _inFlight is not null
                ? "Looking for your apps…"
                : _apps.Count == 0
                    ? "No apps found in your Start menu."
                    : "Nothing matches that.";

            _empty.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var detail = _config.ShowCategory && _categoryVisible;

        foreach (var app in visible)
        {
            _list.Children.Add(BuildRow(app, detail));
        }
    }

    private FrameworkElement BuildRow(InstalledApp app, bool detail)
    {
        // A Segoe glyph rather than the shortcut's own icon. Extracting an HICON and turning it
        // into a XAML brush is a GDI handle dance this port has no AOT-safe path for yet, and a
        // stock glyph is an honest placeholder where a wrong or missing icon would not be.
        var icon = Fluent.Icon(Glyphs.Apps);
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = app.Name;

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

        if (detail && app.Category.Length > 0)
        {
            var category = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            category.Text = app.Category;
            category.TextAlignment = TextAlignment.Right;
            category.MaxWidth = CategoryWidth;
            category.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            SetColumn(category, 2);
            grid.Children.Add(category);
        }

        var row = Fluent.Row(grid, _compact);
        row.Tag = app.Target;
        _hovers.Add(Fluent.Hover(row));

        // Double-click by default. A widget sits under the pointer's path across the desktop, and a
        // stray single click launching an application is how a glanceable list becomes a hazard.
        if (_config.OpenOnSingleClick)
        {
            row.Tapped += OnRowActivated;
        }
        else
        {
            row.DoubleTapped += OnRowOpened;
        }

        ToolTipService.SetToolTip(row, app.Category.Length > 0 ? $"{app.Name} — {app.Category}" : app.Name);
        return row;
    }

    private void OnRowActivated(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        args.Handled = true;
        Open(sender);
    }

    private void OnRowOpened(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs args)
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

        // The one launch path in the app. It re-checks the target rather than trusting that this
        // list built it.
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
        _filter.TextChanged -= OnFilterChanged;
        _filter.KeyDown -= OnFilterKeyDown;
        _filter.GotFocus -= OnFilterGotFocus;
        _filter.LostFocus -= OnFilterLostFocus;
        _surface?.RequestTextInput(false);

        ClearRows();

        _subscription?.Dispose();
        _subscription = null;

        // Cancel but do not dispose: the in-flight scan owns its source and frees it on the way
        // out, so there is exactly one owner however many times this is called.
        _inFlight?.Cancel();
        _inFlight = null;

        _apps = [];
        _ticker = null;
    }
}
