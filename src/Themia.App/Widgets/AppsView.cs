using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The applications this PC has, as tiles, rows or compact rows you launch from ([S-1520]), each with
/// the icon Explorer shows for it ([S-1522]), in Start-menu folder sections ([S-1521]).
/// <para>
/// The enumeration is disk work and runs off the UI thread, discarded when a newer one has started.
/// Everything that is opened goes through <see cref="ShellLauncher"/>, which is the app's one launch
/// path and the only thing that decides what may be launched. What the user hid stays in the config
/// ([S-1523]) and is applied before the filter box, so a search never brings it back.
/// </para>
/// <para>
/// Split by concern: this file owns the lifecycle, reading, the ladder ([S-1527]), the card's
/// commands and the keys ([S-1526]); <c>.Cells.cs</c> builds and paints tiles and rows, loads their
/// icons and owns the item menu.
/// </para>
/// </summary>
internal sealed partial class AppsView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetHeaderContent,
    IWidgetContextActions, IWidgetSizeAware, IWidgetKeys
{
    private const string Scope = "apps";

    /// <summary>The filter box's line: the stock box plus the gap under it.</summary>
    private const double FilterLine = Fluent.RowHeightCompact + Fluent.Space2;

    /// <summary>The folder chips' line (XL).</summary>
    private const double ChipsLine = 28 + Fluent.Space2;

    private readonly ProgressRing _busy = new();
    private readonly TextBox _filter = new();
    private readonly Border _filterHost;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };
    private readonly ScrollViewer _chipsHost;
    private readonly Grid _panel = new();
    private readonly ScrollViewer _scroll;
    private readonly ScrollFade _fade;
    private readonly TextBlock _footer;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();

    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private AppsConfig _config = new();

    /// <summary>
    /// [S-13a] A copy of the settings this view last acted on, which is what a change is measured
    /// against. Not <c>_config</c>: the settings page edits the live config object in place, so by
    /// the time a change arrives the "previous" config already holds the new values.
    /// </summary>
    private AppsConfig _applied = new();
    private IDisposable? _subscription;
    private CancellationTokenSource? _inFlight;

    private IReadOnlyList<InstalledApp> _apps = [];
    private WidgetArea _area;

    /// <summary>What the cells were last built for; a different shape, or new data, rebuilds them.</summary>
    private CellShape _shape;
    private bool _dirty = true;

    /// <summary>[S-1527] The folder chip picked at XL (null: every folder). Not saved: a glance, not a setting.</summary>
    private string? _category;

    /// <summary>Which scan is current; a reply carrying an older number is dropped rather than drawn.</summary>
    private int _generation;

    private bool _everLoaded;
    private bool _detached;

    public AppsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        _filter.PlaceholderText = "Filter";
        _filter.IsSpellCheckEnabled = false;
        _filter.IsTextPredictionEnabled = false;
        _filter.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _filter.TextChanged += OnFilterChanged;
        _filter.KeyDown += OnFilterKeyDown;

        // The desktop surface carries WS_EX_NOACTIVATE, so a text box on it shows a caret and
        // receives nothing until the box asks for the keyboard lease ([S-801]).
        _filter.GotFocus += OnFilterGotFocus;
        _filter.LostFocus += OnFilterLostFocus;

        _filterHost = new Border
        {
            Child = _filter,
            Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2),
            Visibility = Visibility.Collapsed,
        };

        _chipsHost = new ScrollViewer
        {
            Content = _chips,
            Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2),
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = Visibility.Collapsed,
        };

        _scroll = new ScrollViewer
        {
            Content = _panel,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        _fade = new ScrollFade(_scroll, _panel);

        _footer = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        _footer.Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2);
        _footer.TextTrimming = TextTrimming.CharacterEllipsis;
        _footer.Visibility = Visibility.Collapsed;

        _empty = Fluent.EmptyState(Glyphs.Apps, _emptyText, out _emptyIcon);
        _empty.Visibility = Visibility.Collapsed;

        SetRow(_filterHost, 0);
        SetRow(_chipsHost, 1);
        SetRow(_scroll, 2);
        SetRow(_footer, 3);
        SetRow(_empty, 2);

        Children.Add(_filterHost);
        Children.Add(_chipsHost);
        Children.Add(_scroll);
        Children.Add(_footer);
        Children.Add(_empty);
    }

    /// <summary>The ring that spins while the Start menu is being read, in the card's header.</summary>
    public FrameworkElement? HeaderTrailing => _busy;

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as AppsConfig ?? new AppsConfig();
        var previous = _applied;

        _config = next;
        _applied = (AppsConfig)next.Clone();
        _dirty = true;

        // Only a change to what comes back off the disk is worth a re-scan. Dragging the count
        // slider must not fire a Start-menu walk per frame.
        var needsRead = !_everLoaded || previous.EffectiveMaxCount != next.EffectiveMaxCount;

        // Subscribing fires a tick of its own, which reads with the config already in place.
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
            Lay();
        }
    }

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
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
            Lay();
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

            // A re-read that found the same apps keeps the cells (and the cursor) it has.
            if (!apps.SequenceEqual(_apps))
            {
                _apps = apps;
                _dirty = true;
            }

            Lay();
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

                // The empty state says "Looking for your apps…" while a scan runs.
                if (_cells.Count == 0)
                {
                    Lay();
                }
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
    // The ladder
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1527] The items first; the filter box (Regular height), the folder chips (XL) and the
    /// count line (L) take their lines only where the tier has them, and the items take the rest,
    /// tiled or rowed by <see cref="AppsLayout"/>. A change of shape or data rebuilds the cells;
    /// anything else repaints them.
    /// </summary>
    private void Lay()
    {
        if (_detached)
        {
            return;
        }

        var area = _area.IsMeasured ? _area : new WidgetArea(320, 196);
        var room = new Disclosure(area);

        var filter = _config.ShowFilter && AppsLayout.Filter(area) && room.Take(FilterLine);
        _filterHost.Visibility = filter ? Visibility.Visible : Visibility.Collapsed;

        var categories = InstalledAppRules.Categories(
            InstalledAppRules.Visible(_apps, _config.HiddenApps, _config.HiddenCategories, null, null));
        var chips = AppsLayout.Chips(area) && categories.Count > 1 && room.Take(ChipsLine);
        _chipsHost.Visibility = chips ? Visibility.Visible : Visibility.Collapsed;

        if (!chips || (_category is { } picked && !categories.Contains(picked, StringComparer.OrdinalIgnoreCase)))
        {
            _category = null;
        }

        var footer = AppsLayout.Footer(area) && room.Take(FluentType.Caption.LineHeight + Fluent.Space2);
        _footer.Visibility = footer ? Visibility.Visible : Visibility.Collapsed;

        // [S-1527] The filter only narrows while its box is on screen: a list narrowed by a query
        // nobody can see is a list that looks broken.
        var query = filter ? _filter.Text : string.Empty;
        var visible = InstalledAppRules.Visible(_apps, _config.HiddenApps, _config.HiddenCategories, _category, query);

        if (visible.Count == 0)
        {
            ShowEmpty(area);
            UpdateFooter(visible.Count);
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var view = _config.View;
        var sections = InstalledAppRules.Sections(visible, _config.GroupByFolder && view != AppsViewMode.Compact);
        var headers = AppsLayout.Headers(area, view, sections.Count);

        var inset = Inset(view);
        var width = Math.Max(0, area.Width - inset.Left - inset.Right);
        var height = Math.Max(0, room.Remaining - inset.Top - inset.Bottom);

        var shape = view == AppsViewMode.Grid
            ? new CellShape(view, AppsLayout.Tiles(area, width, height, visible.Count), default, headers, false, width)
            : RowShape(area, view, headers, width);

        // The filter and the chips change what is listed, so their coming and going is a new shape.
        shape = shape with { Filter = filter, Chips = chips };

        _panel.MinHeight = height;

        if (_dirty || shape != _shape)
        {
            _dirty = false;
            _shape = shape;
            Render(sections, chips ? categories : null);
        }
        else
        {
            PaintAll();
        }

        UpdateFooter(visible.Count);
    }

    private static CellShape RowShape(WidgetArea area, AppsViewMode view, bool headers, double width)
    {
        var rows = AppsLayout.Rows(area, view, headers);
        return new CellShape(view, default, rows, headers, AppsLayout.CategoryColumn(area, view, headers, rows), width);
    }

    /// <summary>The padding around the items: tiles keep a margin from the card edge, rows go edge to edge.</summary>
    private static Thickness Inset(AppsViewMode view) => view == AppsViewMode.Grid
        ? new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space2)
        : WidgetLayout.ListInset;

    /// <summary>
    /// Each of these needs a different thing from the reader, so each says which it is: an app list
    /// showing a blank tile is indistinguishable from a broken one.
    /// </summary>
    private void ShowEmpty(WidgetArea area)
    {
        ClearCells();
        _shape = default;

        _emptyText.Text = !_everLoaded || _inFlight is not null
            ? "Looking for your apps…"
            : _apps.Count == 0
                ? "No apps found in your Start menu."
                : InstalledAppRules.HiddenCount(_apps, _config.HiddenApps, _config.HiddenCategories) == _apps.Count
                    ? "Every app is hidden. Show them again in this widget's settings."
                    : "Nothing matches that.";

        // The icon goes on a tile too short to carry it: an icon alone says less than the sentence.
        _emptyIcon.Visibility = area.HeightClass >= SizeClass.Regular ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
    }

    /// <summary>[S-1527] "120 apps", or "12 of 120" under a filter, and how many are hidden.</summary>
    private void UpdateFooter(int shown)
    {
        if (_footer.Visibility != Visibility.Visible)
        {
            return;
        }

        var hidden = InstalledAppRules.HiddenCount(_apps, _config.HiddenApps, _config.HiddenCategories);
        var total = _apps.Count - hidden;
        var parts = new List<string>
        {
            shown == total ? (total == 1 ? "1 app" : $"{total} apps") : $"{shown} of {total}",
        };

        if (hidden > 0)
        {
            parts.Add($"{hidden} hidden");
        }

        _footer.Text = string.Join(" · ", parts);
    }

    /// <summary>[S-1527] The XL folder chips: "All" and one per folder; the picked one is checked.</summary>
    private void BuildChips(IReadOnlyList<string>? categories)
    {
        foreach (var child in _chips.Children)
        {
            if (child is ToggleButton chip)
            {
                chip.Click -= OnChipClick;
            }
        }

        _chips.Children.Clear();

        if (categories is null)
        {
            return;
        }

        _chips.Children.Add(Chip("All", null));

        foreach (var category in categories)
        {
            _chips.Children.Add(Chip(InstalledAppRules.CategoryTitle(category), category));
        }
    }

    private ToggleButton Chip(string text, string? category)
    {
        var chip = new ToggleButton
        {
            Content = text,
            Tag = category,
            IsChecked = string.Equals(category, _category, StringComparison.OrdinalIgnoreCase),
            Height = 28,
            MinWidth = 0,
            Padding = new Thickness(Fluent.Space3, 0, Fluent.Space3, 0),
            FontSize = FluentType.Caption.FontSize,
            CornerRadius = new CornerRadius(14),
        };

        chip.Click += OnChipClick;
        return chip;
    }

    private void OnChipClick(object sender, RoutedEventArgs args)
    {
        if (sender is ToggleButton chip)
        {
            _category = chip.Tag as string;
            _dirty = true;
            Lay();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    private void OnFilterChanged(object sender, TextChangedEventArgs args)
    {
        _dirty = true;
        _cursor = null;
        Lay();
    }

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case Windows.System.VirtualKey.Escape when _filter.Text.Length > 0:
                args.Handled = true;
                _filter.Text = string.Empty;
                break;

            // [S-1526] Enter in the box opens the app under the cursor, or the first match: type a
            // few letters, press Enter, as in Start.
            case Windows.System.VirtualKey.Enter when _cells.Count > 0:
                args.Handled = true;
                Open(_cells[_cursor is { } at && at < _cells.Count ? at : 0]);
                break;
        }
    }

    private void OnFilterGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnFilterLostFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(false);

    /// <inheritdoc />
    public bool WantsKeys => _cells.Count > 0;

    /// <summary>
    /// [S-1526] The arrows walk the tiles or rows, section by section, keeping the column; Enter
    /// opens, the Menu key opens the app's menu, Escape (and the lease ending) drops the cursor.
    /// </summary>
    public bool HandleKey(WidgetKey key)
    {
        if (_detached)
        {
            return false;
        }

        switch (key)
        {
            case WidgetKey.Enter:
                if (CursorCell() is not { } open)
                {
                    return false;
                }

                Open(open);
                return true;

            case WidgetKey.Menu:
                if (CursorCell() is not { } target)
                {
                    return false;
                }

                ShowMenuAt(target);
                return true;
        }

        var (next, used) = SectionCursor.Move(SectionSizes(), Columns(), _cursor, key, PageRows());

        if (!used)
        {
            return false;
        }

        _cursor = next;
        PaintAll();
        CursorCell()?.Root.StartBringIntoView();
        return true;
    }

    /// <summary>Items per visual row: the tile plan's columns, or the list's.</summary>
    private int Columns() => Math.Max(1, _shape.View == AppsViewMode.Grid ? _shape.Tiles.Columns : _shape.Rows.Columns);

    /// <summary>How many rows a Page key steps: about one screenful.</summary>
    private int PageRows()
    {
        var row = _shape.View == AppsViewMode.Grid ? _shape.Tiles.TileHeight : _shape.Rows.RowHeight;
        return Math.Max(1, Disclosure.WholeRows(_scroll.ActualHeight, Math.Max(1, row)));
    }

    // -----------------------------------------------------------------------------------------
    // The card's commands
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1528] The card's menu (original S-13 / S-14): Refresh, the other two views, grouping and
    /// the click. Each names the state it switches to.
    /// </summary>
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            var actions = new List<WidgetAction> { new(Glyphs.Refresh, "Refresh", Reload) };

            foreach (var mode in (AppsViewMode[])[AppsViewMode.Grid, AppsViewMode.List, AppsViewMode.Compact])
            {
                if (mode != _config.View)
                {
                    actions.Add(new WidgetAction(ModeGlyph(mode), $"Show as {ModeName(mode)}", () => SetConfig(c => c.View = mode)));
                }
            }

            if (_config.View != AppsViewMode.Compact)
            {
                actions.Add(new WidgetAction(Glyphs.Folder, _config.GroupByFolder ? "Don't group by folder" : "Group by folder",
                    () => SetConfig(c => c.GroupByFolder = !c.GroupByFolder)));
            }

            actions.Add(new WidgetAction(Glyphs.Open,
                _config.ClickMode == ItemClickMode.Double ? "Open on single click" : "Open on double click",
                () => SetConfig(c => c.ClickMode = c.ClickMode == ItemClickMode.Double ? ItemClickMode.Single : ItemClickMode.Double)));

            return actions;
        }
    }

    private void SetConfig(Action<AppsConfig> change)
    {
        change(_config);
        _applied = (AppsConfig)_config.Clone();
        _surface?.RequestSave();
        _dirty = true;
        Lay();
    }

    private static string ModeName(AppsViewMode mode) => mode switch
    {
        AppsViewMode.List => "list",
        AppsViewMode.Compact => "compact list",
        _ => "tiles",
    };

    private static string ModeGlyph(AppsViewMode mode) => mode switch
    {
        AppsViewMode.List => Glyphs.ListLayout,
        AppsViewMode.Compact => Glyphs.ColumnsLayout,
        _ => Glyphs.GridLayout,
    };

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        _filter.TextChanged -= OnFilterChanged;
        _filter.KeyDown -= OnFilterKeyDown;
        _filter.GotFocus -= OnFilterGotFocus;
        _filter.LostFocus -= OnFilterLostFocus;
        _surface?.RequestTextInput(false);

        BuildChips(null);
        ClearCells();
        _fade.Dispose();

        _subscription?.Dispose();
        _subscription = null;

        // Cancel but do not dispose: the in-flight scan owns its source and frees it on the way
        // out, so there is exactly one owner however many times this is called.
        _inFlight?.Cancel();
        _inFlight = null;

        _apps = [];
        _ticker = null;
    }

    /// <summary>What the cells were last built for.</summary>
    private readonly record struct CellShape(
        AppsViewMode View,
        ShortcutTilePlan Tiles,
        ShortcutRowPlan Rows,
        bool Headers,
        bool CategoryColumn,
        double Width,
        bool Filter = false,
        bool Chips = false);
}
