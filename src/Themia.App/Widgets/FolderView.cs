using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// What is in a folder on this PC, as a Windows 11 file list.
/// <para>
/// Built to sit next to Explorer rather than next to a web dashboard: Fluent's 32px compact list
/// row, the Body step for the name, the 16px Segoe folder and file glyphs, and no rule between
/// rows. The row spans the full width of the card and carries the 16px inset as its own padding, so
/// the highlight under the pointer reaches both edges the way a row's does and not the way a box's
/// does.
/// </para>
/// <para>
/// <b>Read-only.</b> Double-clicking a row hands it to the shell and that is the whole of what this
/// widget can do to a file. A rename or a delete one mis-click away, on a surface that sits under
/// every other window and has no undo, is not a feature — those belong in Explorer, which has both
/// a confirmation and a recycle bin.
/// </para>
/// </summary>
internal sealed partial class FolderView : Grid, IWidgetView
{
    /// <summary>
    /// Below this the tile is too narrow for a name and two detail columns, so the detail is
    /// dropped rather than squeezed: a name trimmed to three characters to make room for a date is
    /// a list that has stopped being readable. The name is what a folder listing is for.
    /// </summary>
    private const double DetailMinimumWidth = 300;

    /// <summary>
    /// What an empty <see cref="FolderConfig.Path"/> means, resolved once. Read here rather than in
    /// <see cref="FolderPath"/> so the rule itself stays a pure function.
    /// </summary>
    private static readonly string ProfileFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly TextBlock _title = Fluent.Text(FluentType.BodyStrong);
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _message;
    private readonly Border _messageHost;

    // One per row, so the hover handlers come off with the rows they light. A leaked one keeps the
    // row alive and re-lights it after the listing has been redrawn or the widget torn down.
    private readonly List<IDisposable> _hovers = [];

    private Ticker? _ticker;
    private FolderConfig _config = new();
    private FolderPathResult _resolved;
    private FolderSnapshot _snapshot = FolderSnapshot.Of(FolderListingStatus.Loading);
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;

    /// <summary>
    /// Which listing is current. A reply carrying an older number is a folder the widget has
    /// stopped pointing at — a network share that took thirty seconds to answer must not overwrite
    /// the folder the user has since chosen.
    /// </summary>
    private int _generation;

    private bool _detailVisible = true;
    private bool _everLoaded;
    private bool _detached;

    public FolderView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        var header = Fluent.Header(Glyphs.Folder, _title, _busy);
        header.Margin = Fluent.HeaderMargin;
        SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        SetRow(_scroll, 1);

        _message = Fluent.Text(FluentType.Body, "TextFillColorTertiaryBrush");
        _message.TextWrapping = TextWrapping.Wrap;
        _message.TextTrimming = TextTrimming.None;
        _message.TextAlignment = TextAlignment.Center;

        _messageHost = new Border
        {
            Child = _message,
            Padding = WidgetLayout.SideInset,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        SetRow(_messageHost, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_messageHost);

        // Event-driven, not measured on a timer: the detail columns come and go with the tile's
        // actual width, and only a change of answer costs a redraw.
        SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as FolderConfig ?? new FolderConfig();
        var previous = _config;

        _config = next;

        var resolved = FolderPath.Resolve(next.Path, ProfileFolder);
        var pathChanged = resolved.Path != _resolved.Path || resolved.Problem != _resolved.Problem;
        _resolved = resolved;

        _title.Text = resolved.IsValid ? FolderPath.DisplayName(resolved.Path) : "Folder";

        // Only the fields that change what comes back off the disk are worth a re-read. Everything
        // else re-draws what is already in hand — dragging the row-count slider must not fire a
        // directory enumeration per frame.
        var needsRead = !_everLoaded
            || pathChanged
            || previous.Sort != next.Sort
            || previous.Descending != next.Descending
            || previous.FoldersFirst != next.FoldersFirst
            || previous.ShowHidden != next.ShowHidden
            || previous.EffectiveMaxCount != next.EffectiveMaxCount;

        // Subscribing fires a tick of its own, and that tick reloads with the config already in
        // place above. Reloading again here would be a second enumeration of the same folder.
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
            Render();
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed, and say whether it
    /// did — a new subscription ticks immediately, so the caller has nothing left to do.
    /// <para>
    /// Tearing it down and remaking it on every config touch made dragging a settings slider
    /// stutter, because each frame rebuilt the shared timer for every widget on the desktop.
    /// </para>
    /// </summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Reload);
        return _subscription is not null;
    }

    /// <summary>
    /// Start a listing. The read itself happens on the thread pool: a mapped drive whose server has
    /// gone away can leave a single directory call sitting for most of a minute, and on the UI
    /// thread that is every widget on the desktop frozen with it.
    /// </summary>
    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        _everLoaded = true;

        if (!_resolved.IsValid)
        {
            _snapshot = FolderSnapshot.Of(FolderListingStatus.InvalidPath);
            Render();
            return;
        }

        var request = new FolderRequest(
            _resolved.Path,
            _config.Sort,
            _config.Descending,
            _config.FoldersFirst,
            _config.ShowHidden,
            _config.EffectiveMaxCount);

        // The previous listing is now for a folder or an order nobody is looking at.
        _inFlight?.Cancel();

        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        // Say something immediately, but only when there is nothing on screen to keep. A tick that
        // re-reads the same folder should not blank the list it is about to redraw.
        if (_snapshot.Entries.Count == 0)
        {
            _snapshot = FolderSnapshot.Of(FolderListingStatus.Loading);
            Render();
        }

        _ = LoadAsync(request, generation, cts);
    }

    private async Task LoadAsync(FolderRequest request, int generation, CancellationTokenSource cts)
    {
        try
        {
            SetBusy(true);

            var snapshot = await Task.Run(() => FolderListing.Read(request, cts.Token), cts.Token)
                .ConfigureAwait(true);

            // A late reply from a folder the widget has since left. Discarded rather than drawn.
            if (_detached || generation != _generation)
            {
                return;
            }

            // A re-read that found exactly what the last one found must not rebuild the list:
            // replacing the rows would throw away the scroll position and the row under the
            // pointer, which on a long listing is the widget scrolling itself back to the top
            // every thirty seconds while somebody is reading it.
            var unchanged = snapshot.Status == _snapshot.Status
                && FolderRules.SameContent(snapshot.Entries, _snapshot.Entries);

            _snapshot = snapshot;

            if (!unchanged)
            {
                Render();
            }
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer listing, which will draw in this one's place.
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wanted = e.NewSize.Width >= DetailMinimumWidth;

        if (wanted == _detailVisible)
        {
            return;
        }

        _detailVisible = wanted;
        Render();
    }

    /// <summary>
    /// Draw the snapshot in hand. Every state that has no rows says which state it is: a folder
    /// widget showing a blank tile is indistinguishable from a broken one, and each of these needs
    /// a different thing from the reader.
    /// </summary>
    private void Render()
    {
        ClearRows();

        if (_snapshot.Status != FolderListingStatus.Ok || _snapshot.Entries.Count == 0)
        {
            _message.Text = FolderFormatting.StatusMessage(_snapshot.Status, _resolved.Problem);
            _messageHost.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _messageHost.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        // One clock reading for the whole list, so a listing drawn across midnight cannot show two
        // different ideas of "today".
        var now = DateTimeOffset.Now;
        var detail = _config.ShowDetail && _detailVisible;

        foreach (var entry in _snapshot.Entries)
        {
            _list.Children.Add(BuildRow(entry, now, detail));
        }
    }

    private FrameworkElement BuildRow(FolderEntry entry, DateTimeOffset now, bool detail)
    {
        // The only hierarchy in the row: a folder leads somewhere, a file does not. Carried by the
        // two stock text colours rather than by a colour this widget invented.
        var icon = Fluent.Icon(
            entry.IsDirectory ? Glyphs.Folder : Glyphs.File,
            entry.IsDirectory ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = entry.Name;
        name.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(icon, 0);
        SetColumn(name, 1);
        grid.Children.Add(icon);
        grid.Children.Add(name);

        if (detail)
        {
            var size = Detail(FolderFormatting.Size(entry), 56);
            var modified = Detail(FolderFormatting.Modified(entry.Modified, now), 60);

            SetColumn(size, 2);
            SetColumn(modified, 3);
            grid.Children.Add(size);
            grid.Children.Add(modified);
        }

        var row = Fluent.Row(grid, compact: true);
        _hovers.Add(Fluent.Hover(row));

        // Double-click, not single: a widget sits under the pointer's path across the desktop, and
        // one stray click launching something is how a glanceable surface becomes a hazard.
        row.DoubleTapped += (_, _) => FolderListing.Open(entry.FullPath);

        ToolTipService.SetToolTip(row, entry.FullPath);
        return row;
    }

    /// <summary>
    /// A right-aligned detail column: the secondary text colour, the Caption step, and tabular
    /// figures so the sizes and dates line up down the list instead of jittering per row.
    /// </summary>
    private static TextBlock Detail(string text, double width)
    {
        var block = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        block.Text = text;
        block.TextAlignment = TextAlignment.Right;
        block.MinWidth = width;
        block.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
        block.VerticalAlignment = VerticalAlignment.Center;

        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// <summary>
    /// Drop the rows and, with them, their hover handlers: a handler left hooked keeps the row it
    /// lights alive and re-lights it after the listing has moved on.
    /// </summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        ClearRows();

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        // Cancel but do not dispose: the in-flight load owns its source and frees it on the way out,
        // so there is exactly one owner however many times this is called.
        _inFlight?.Cancel();
        _inFlight = null;

        _ticker = null;
    }
}
