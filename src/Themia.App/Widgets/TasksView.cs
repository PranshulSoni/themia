using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Open tasks, read from the hearth hub.
/// <para>
/// No account, token or provider, and no connection of its own: hearth owns sign-in, sync and
/// consent, and the app's one connection is reached through <see cref="AppServices.Data"/>. This
/// lists the task lists hearth granted and refreshes when the hub says something changed.
/// </para>
/// <para>
/// Read-only, like every hearth widget here: the marker beside a task says what its state is, and
/// clicking it does nothing, because the scope asked for cannot write one back.
/// </para>
/// </summary>
internal sealed partial class TasksView : Grid, IWidgetView, IWidgetHeaderContent
{
    private const string Scope = "tasks-widget";

    // Written out rather than read from a culture: the app builds with InvariantGlobalization, and
    // Formatting.Ago only looks backwards, while a due date mostly looks forwards.
    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _trailing;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();
    private readonly StackPanel _empty;

    // What a resize has to re-fit: the rows themselves, and the state marker, which is sized from
    // the row height rather than from the tile. Everything else in a row is a step of the ramp or
    // of the 4px grid, so a resize cannot change it.
    private readonly List<Border> _markers = [];
    private readonly List<FontIcon> _markerIcons = [];
    private readonly List<Border> _rows = [];

    // One per row, so the hover handlers come off with the row they light. A leaked one keeps the
    // row alive and re-lights it after teardown.
    private readonly List<IDisposable> _hovers = [];

    private HearthDataService? _data;
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private TasksConfig _config = new();
    private IReadOnlyList<TaskRecord> _records = [];
    private bool _refreshing;
    private bool _requeryWanted;
    private bool _loaded;
    private bool _detached;

    public TasksView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // The open count is Fluent's InfoBadge shape, which the design system owns.
        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        // The busy ring and the open count ride on the card's header. The card reads this once when
        // it builds the header, so it exists from construction rather than on first use.
        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _trailing.Children.Add(_busy);
        _trailing.Children.Add(_badgeHost);

        _scroll = new ScrollViewer
        {
            // Nothing either side, so a row reaches both edges of the card and its highlight with
            // it. The gap above the first row is the card header's; this keeps the last one off
            // the edge.
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 0);

        // A Windows empty state: an icon over a line of body text in the secondary colour, centred.
        // Not a shrunken caption, which reads as an error the app is embarrassed about.
        _empty = Fluent.EmptyState(Glyphs.Task, _emptyText, out _emptyIcon);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 0);

        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += (_, _) => ApplyDensity();
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _trailing;

    /// <summary>
    /// Whether the tile takes the compact row. Fluent has two list-row heights and a tile shorter
    /// than five standard rows reads better with the shorter one: this is a density switch between
    /// two fixed heights, not a row that grows with the tile.
    /// </summary>
    private bool Compact => ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _data = AppServices.Data;

        if (_data is not null)
        {
            _data.Changed += OnHubChanged;
        }

        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as TasksConfig ?? new TasksConfig();

        // Only the fields that change what the hub is asked for warrant a round trip. Without this
        // check, dragging an unrelated settings slider fired a query per frame. Completed tasks are
        // a filter on the query - hearth would not have sent them - so that one does re-ask.
        var needsQuery = _config.CollectionId != next.CollectionId
            || _config.EffectiveMaxCount != next.EffectiveMaxCount
            || _config.ShowCompleted != next.ShowCompleted;

        // The due date changes only how the records already in hand are drawn, so it re-renders.
        var redraws = _config.ShowDueDate != next.ShowDueDate;

        _config = next;
        Resubscribe();

        if (needsQuery || !_loaded)
        {
            _ = RefreshAsync();
        }
        else if (redraws)
        {
            Render(_records);
        }
    }

    private void OnHubChanged() => _ = RefreshAsync();

    /// <summary>
    /// Re-subscribe only when the cadence actually changed. Dropping and remaking the subscription
    /// on every config touch made dragging a settings slider stutter, because each frame tore down
    /// the timer subscription for every widget on the desktop and built it again.
    /// </summary>
    private void Resubscribe()
    {
        if (_detached)
        {
            return;
        }

        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, OnTick);
    }

    /// <summary>
    /// The hub pushes changes, but "overdue" and "Today" are answers about the current date, not
    /// about the record, and nothing in the store changes at midnight. So the tick re-renders.
    /// </summary>
    private void OnTick() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_detached)
        {
            return;
        }

        // The host builds the connection before any surface exists, so this is only reachable in a
        // teardown race - but a widget that finds no service must still say so rather than sit
        // blank with no explanation.
        if (_data is null)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        // A second request while one is in flight is not dropped: the settings window can change
        // the list mid-query, and swallowing that would leave the old one shown.
        if (_refreshing)
        {
            _requeryWanted = true;
            return;
        }

        _refreshing = true;
        _busy.IsActive = true;
        _busy.Visibility = Visibility.Visible;

        if (!_loaded)
        {
            ShowMessage("Loading…");
        }

        try
        {
            var tasks = await _data.TasksAsync(
                _config.CollectionId,
                _config.ShowCompleted,
                null,
                _config.EffectiveMaxCount).ConfigureAwait(true);

            if (_detached)
            {
                return;
            }

            _loaded = true;
            _records = tasks;
            Render(tasks);
        }
        catch (Exception ex)
        {
            // Never let a hub problem take the surface down; say so in the widget instead.
            Log.Error(Scope, "could not refresh the task list", ex);
            ShowMessage("Could not read tasks. See themia.log.");
        }
        finally
        {
            _refreshing = false;
            _busy.IsActive = false;
            _busy.Visibility = Visibility.Collapsed;

            if (_requeryWanted && !_detached)
            {
                _requeryWanted = false;
                _ = RefreshAsync();
            }
        }
    }

    private void Render(IReadOnlyList<TaskRecord> tasks)
    {
        ClearRows();

        if (tasks.Count == 0)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        // One "today" for the whole render rather than one per row, so a list drawn across midnight
        // cannot call the same date overdue on one row and due today on the next.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var compact = Compact;
        var open = 0;

        foreach (var task in tasks)
        {
            if (!task.Completed)
            {
                open++;
            }

            var row = BuildRow(task, today, compact);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        // The open count, not the row count: with completed tasks shown, "how much is left" is the
        // number worth putting in the header, and it is the one the list is otherwise silent about.
        _badge.Text = open.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = open > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The markers have no size until this runs, and a first render need not be followed by a
        // resize, so it is called here rather than left to SizeChanged alone.
        ApplyDensity();
    }

    private Border BuildRow(TaskRecord task, DateOnly today, bool compact)
    {
        var marker = BuildMarker(task);

        // Hearth's "high" is the only priority worth marking, and weight marks it the way an unread
        // message is marked - no extra glyph, no legend. Both steps are the same 14/20 line, so
        // nothing moves when a task is important.
        var title = Fluent.Text(
            task.Important && !task.Completed ? FluentType.BodyStrong : FluentType.Body,
            task.Completed ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");

        title.Text = string.IsNullOrWhiteSpace(task.Title) ? "(untitled task)" : task.Title;

        // A finished task is struck through and dimmed, not merely dimmed: strike-through is the
        // one mark that says "done" without a legend and without relying on colour.
        title.TextDecorations = task.Completed
            ? Windows.UI.Text.TextDecorations.Strikethrough
            : Windows.UI.Text.TextDecorations.None;

        title.VerticalAlignment = VerticalAlignment.Center;

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(marker, 0);
        Grid.SetColumn(title, 1);
        body.Children.Add(marker);
        body.Children.Add(title);

        if (_config.ShowDueDate && task.DueDate is { } due)
        {
            var overdue = task.IsOverdue(today);

            // Overdue says so in words as well as in colour: a red date alone is invisible to
            // anyone who cannot see the red. The critical colour is a named Fluent brush, so it
            // tracks the theme rather than being a red this widget chose. Both states take the same
            // ramp step, so the column does not change width when one task falls behind.
            var when = Fluent.Text(
                FluentType.Caption,
                overdue ? "SystemFillColorCriticalBrush" : "TextFillColorTertiaryBrush");

            when.Text = DueLabel(due, today, overdue);
            when.VerticalAlignment = VerticalAlignment.Center;
            when.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            Grid.SetColumn(when, 2);
            body.Children.Add(when);
        }

        var row = Fluent.Row(body, compact);
        _hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, Tooltip(task, today));
        return row;
    }

    /// <summary>
    /// The state marker: an empty ring for open work, a filled tick for finished. Drawn rather than
    /// taken from the icon font because there is no hollow-circle glyph in the set, and an outline
    /// next to a filled tick is what makes the two states tell apart at a glance.
    /// <para>
    /// Sized in <see cref="ApplyDensity"/> from the row height, which is fixed, rather than from the
    /// tile: a checkbox that grows with the window is a web control.
    /// </para>
    /// </summary>
    private Border BuildMarker(TaskRecord task)
    {
        var marker = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space3, 0),
            BorderBrush = FluentLayout.Themed("ControlStrongStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(task.Completed ? 0 : 1),
            Background = task.Completed
                ? FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue)
                : Fluent.RowRest(),
        };

        if (task.Completed)
        {
            var tick = new FontIcon
            {
                Glyph = Glyphs.Done,
                Foreground = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _markerIcons.Add(tick);
            marker.Child = tick;
        }

        _markers.Add(marker);
        return marker;
    }

    private static string Tooltip(TaskRecord task, DateOnly today)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(task.Title) ? "(untitled task)" : task.Title,
            task.Completed ? "Completed" : "Open",
        };

        if (task.DueDate is { } due)
        {
            lines.Add($"Due {DueLabel(due, today, task.IsOverdue(today))}");
        }

        if (task.Important)
        {
            lines.Add("High priority");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string DueLabel(DateOnly due, DateOnly today, bool overdue)
        => Formatting.DueDate(due, today, overdue);

    private static string ShortDate(DateOnly day) => Formatting.ShortDate(day);

    /// <summary>
    /// Re-fit what is already drawn to the tile: the row height, the marker that is sized from it,
    /// and whether the empty state has room for its icon. No query, and no font size derived from
    /// the tile — every size in a row is a step of the ramp or of the 4px grid.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = Compact;

        // Fluent's own checkbox is 20 square on a 40 row; the compact row takes the 16 an inline
        // icon uses. Both come from the row, not from the tile.
        var markerSize = compact ? Fluent.IconSize : Fluent.Space5;

        foreach (var row in _rows)
        {
            Fluent.SetDensity(row, compact);
        }

        foreach (var marker in _markers)
        {
            marker.Width = markerSize;
            marker.Height = markerSize;
            marker.CornerRadius = new CornerRadius(markerSize / 2);
        }

        foreach (var tick in _markerIcons)
        {
            // The tick sits inside the ring, so it is a fraction of the marker - which is itself a
            // fixed size - and never of the tile.
            tick.FontSize = Math.Round(markerSize * 0.6);
        }

        _emptyIcon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Say what is actually true, because every one of these needs a different action.</summary>
    private string EmptyMessage()
    {
        var granted = _data?.TaskCollections.Count ?? 0;

        return _data?.Status switch
        {
            null => "Themia is still starting.",
            HearthStatus.NotRunning => "hearth is not running.\nStart it to see your tasks here.",
            HearthStatus.Denied => "hearth has not granted Themia access to any task list.",
            HearthStatus.Failed => "Could not read from hearth. See themia.log.",
            HearthStatus.Connecting or HearthStatus.Idle => "Connecting to hearth…",
            _ when granted == 0 => "hearth has granted no task lists.",
            _ when _config.ShowCompleted => "Nothing on the list.",
            _ => "Nothing open. All done.",
        };
    }

    private void ShowMessage(string text)
    {
        _emptyText.Text = text;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        _badgeHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Drop the rows and, with them, their hover handlers: a handler left hooked keeps the row it
    /// lights alive and re-lights it after the list has moved on.
    /// </summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
        _rows.Clear();
        _markers.Clear();
        _markerIcons.Clear();
    }

    public void Detach()
    {
        _detached = true;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        if (_data is not null)
        {
            _data.Changed -= OnHubChanged;
            _data = null;
        }

        _ticker = null;
        _records = [];
        ClearRows();
    }
}
