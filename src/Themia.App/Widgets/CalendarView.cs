using System.Globalization;
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
/// What is coming up, read from the hearth hub.
/// <para>
/// No account, token or provider, and no connection of its own: hearth owns sign-in, sync and
/// consent, and the app's one connection is reached through <see cref="AppServices.Data"/>. This
/// lists the calendars hearth granted and refreshes when the hub says something changed.
/// </para>
/// <para>
/// Times are shown on a 24-hour clock. The app builds with <c>InvariantGlobalization</c>, so there
/// is no culture to read a 12-hour preference from and no setting that offers one; 24-hour is the
/// form that cannot be misread for the other one.
/// </para>
/// </summary>
internal sealed partial class CalendarView : Grid, IWidgetView, IWidgetHeaderContent
{
    private const string Scope = "calendar-widget";

    /// <summary>
    /// The time column's width: thirteen steps of the 4px grid, which is what "All day" — the
    /// longest thing the column ever holds — needs at the caption step. Fixed rather than derived
    /// from the tile, so every row's title starts in the same place whatever size the widget is.
    /// </summary>
    private const double TimeColumn = 52;

    // Written out rather than read from a culture: the app builds with InvariantGlobalization, and
    // Formatting.Ago only looks backwards, so a calendar that looks forwards needs its own names.
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

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

    // The rows on screen, so a resize can switch them between the two row heights Fluent defines
    // without asking the hub for the same records again. Nothing else needs holding: every size in
    // a row is a ramp step or a grid step, so a resize cannot change it.
    private readonly List<Border> _rows = [];

    // One per row, so the hover handlers come off with the row they light. A leaked one keeps the
    // row alive and re-lights it after teardown.
    private readonly List<IDisposable> _hovers = [];

    private HearthDataService? _data;
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CalendarConfig _config = new();
    private IReadOnlyList<EventRecord> _records = [];
    private bool _refreshing;
    private bool _requeryWanted;
    private bool _loaded;
    private bool _detached;

    public CalendarView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // The count is Fluent's InfoBadge shape, which the design system owns.
        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        // The busy ring and the count ride on the card's header. The card reads this once when it
        // builds the header, so it exists from construction rather than on first use.
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
            // it. The gap above the first row is the card header's; this keeps the last one off the
            // edge.
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 0);

        // A Windows empty state: an icon over a line of body text in the secondary colour, centred.
        // Not a shrunken caption, which reads as an error the app is embarrassed about.
        _empty = Fluent.EmptyState(Glyphs.Calendar, _emptyText, out _emptyIcon);
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
        var next = config as CalendarConfig ?? new CalendarConfig();

        // Only the fields that change what the hub is asked for warrant a round trip. Without this
        // check, dragging an unrelated settings slider fired a query per frame.
        var needsQuery = _config.CollectionId != next.CollectionId
            || _config.EffectiveMaxCount != next.EffectiveMaxCount
            || _config.EffectiveDaysAhead != next.EffectiveDaysAhead;

        // These two change only how the records already in hand are drawn - all-day events are
        // filtered out of the reply, not out of the query - so they re-render instead.
        var redraws = _config.ShowLocation != next.ShowLocation
            || _config.ShowAllDay != next.ShowAllDay;

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
    /// The hub pushes changes, but a calendar goes stale on its own: the window starts at "now", a
    /// meeting becomes "Now" and then past, and "Today" becomes yesterday at midnight. None of that
    /// is a change the hub would report, so the tick re-asks.
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
        // the window or the calendar mid-query, and swallowing that would leave the old one shown.
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
            // Hearth's limit counts the records it returns, before this drops the cancelled ones
            // and - when they are not wanted - the all-day ones. Asking for a little more keeps a
            // full list full; the service caps the page itself, so this cannot ask for the world.
            var request = Math.Min(100, _config.EffectiveMaxCount * 2);

            var events = await _data.UpcomingAsync(
                _config.CollectionId,
                DateTimeOffset.Now,
                TimeSpan.FromDays(_config.EffectiveDaysAhead),
                request).ConfigureAwait(true);

            if (_detached)
            {
                return;
            }

            _loaded = true;
            _records = events;
            Render(events);
        }
        catch (Exception ex)
        {
            // Never let a hub problem take the surface down; say so in the widget instead.
            Log.Error(Scope, "could not refresh the calendar", ex);
            ShowMessage("Could not read the calendar. See themia.log.");
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

    private void Render(IReadOnlyList<EventRecord> events)
    {
        ClearRows();

        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var shown = Visible(events);

        if (shown.Count == 0)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        // A day heading only when the day changes, so a list of four meetings on one afternoon is
        // not four headings deep. Grouped in order because the query sorted by start.
        DateOnly? heading = null;
        var compact = Compact;

        foreach (var item in shown)
        {
            var day = DayOf(item, today);

            if (heading != day)
            {
                heading = day;
                _list.Children.Add(BuildDayHeading(DayLabel(day, today)));
            }

            var row = BuildRow(item, now, compact);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _badge.Text = shown.Count.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// What the list actually shows: never a cancelled occurrence, which is a record like any other
    /// and would otherwise read as a meeting that is happening, and all-day events only when they
    /// were asked for.
    /// </summary>
    private List<EventRecord> Visible(IReadOnlyList<EventRecord> events)
    {
        var shown = new List<EventRecord>(Math.Min(events.Count, _config.EffectiveMaxCount));

        foreach (var item in events)
        {
            if (item.Cancelled || (item.AllDay && !_config.ShowAllDay))
            {
                continue;
            }

            shown.Add(item);

            if (shown.Count == _config.EffectiveMaxCount)
            {
                break;
            }
        }

        return shown;
    }

    /// <summary>
    /// A day heading is a label, not a row: it keeps the inset the rows give up, so it lines up with
    /// the widget's own title above it rather than with the rows' text.
    /// </summary>
    private static Border BuildDayHeading(string label)
    {
        var text = Fluent.Text(FluentType.BodyStrong, "TextFillColorSecondaryBrush");
        text.Text = label;

        return new Border
        {
            Child = text,
            Padding = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space1),
        };
    }

    private Border BuildRow(EventRecord item, DateTimeOffset now, bool compact)
    {
        // A fixed column, so the titles line up instead of each starting wherever its own time
        // happened to end. An event under way says "Now" in the strong step of the same line.
        var running = InProgress(item, now);
        var when = Fluent.Text(
            running ? FluentType.BodyStrong : FluentType.Body,
            "TextFillColorSecondaryBrush");

        when.Text = TimeLabel(item, now);
        when.Width = TimeColumn;
        when.VerticalAlignment = VerticalAlignment.Top;
        when.Margin = new Thickness(0, 0, Fluent.Space2, 0);

        var title = Fluent.Text(FluentType.BodyStrong);
        title.Text = string.IsNullOrWhiteSpace(item.Title) ? "(no title)" : item.Title;

        // No spacing between the lines of a row: each one already carries the line height Fluent
        // pairs with its size, and a gap on top of that is the web app's look.
        var text = new StackPanel { Spacing = 0 };
        text.Children.Add(title);

        if (_config.ShowLocation && !string.IsNullOrWhiteSpace(item.Location))
        {
            var place = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            place.Text = item.Location;
            text.Children.Add(place);
        }

        Grid.SetColumn(when, 0);
        Grid.SetColumn(text, 1);

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { when, text },
        };

        var row = Fluent.Row(body, compact);
        _hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, Tooltip(item, now));
        return row;
    }

    private static string Tooltip(EventRecord item, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(item.Title) ? "(no title)" : item.Title,
            Span(item, now),
        };

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            lines.Add(item.Location);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The full span, for the tooltip: what the row's one-line time label had to leave out.</summary>
    private static string Span(EventRecord item, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var day = DayLabel(DayOf(item, today), today);

        if (item.AllDay || item.StartsAt is not { } start)
        {
            return $"{day} · All day";
        }

        return item.EndsAt is { } end && end > start
            ? $"{day} · {Clock(start)}–{Clock(end)} ({Formatting.Duration(end - start)})"
            : $"{day} · {Clock(start)}";
    }

    /// <summary>The day a row is filed under. See <see cref="AgendaRules.FiledUnder"/>.</summary>
    private static DateOnly DayOf(EventRecord item, DateOnly today)
        => item.StartsAt is { } start
            ? AgendaRules.FiledUnder(DateOnly.FromDateTime(start.LocalDateTime), today)
            : today;

    private static string DayLabel(DateOnly day, DateOnly today) => Formatting.DayHeading(day, today);

    /// <summary>
    /// The row's time: "All day", "Now" for one already under way, or the start on a 24-hour clock.
    /// </summary>
    private static string TimeLabel(EventRecord item, DateTimeOffset now)
    {
        if (item.AllDay)
        {
            return "All day";
        }

        if (item.StartsAt is not { } start)
        {
            return "--:--";
        }

        return InProgress(item, now) ? "Now" : Clock(start);
    }

    private static bool InProgress(EventRecord item, DateTimeOffset now)
        => AgendaRules.IsInProgress(item.AllDay, item.StartsAt, item.EndsAt, now);

    private static string Clock(DateTimeOffset moment) => Formatting.Clock24(moment);

    /// <summary>
    /// Re-fit what is already drawn to the tile: the row height, and whether the empty state has
    /// room for its icon. No query, and no font size derived from the tile — every size in a row is
    /// a step of the ramp or of the 4px grid, so a resize has nothing else to change.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = Compact;

        foreach (var row in _rows)
        {
            Fluent.SetDensity(row, compact);
        }

        _emptyIcon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Say what is actually true, because every one of these needs a different action.</summary>
    private string EmptyMessage()
    {
        var granted = _data?.CalendarCollections.Count ?? 0;
        var days = _config.EffectiveDaysAhead;
        var window = days == 1 ? "the next day" : $"the next {days} days";

        return _data?.Status switch
        {
            null => "Themia is still starting.",
            HearthStatus.NotRunning => "hearth is not running.\nStart it to see your calendar here.",
            HearthStatus.Denied => "hearth has not granted Themia access to any calendar.",
            HearthStatus.Failed => "Could not read from hearth. See themia.log.",
            HearthStatus.Connecting or HearthStatus.Idle => "Connecting to hearth…",
            _ when granted == 0 => "hearth has granted no calendars.",
            _ when _records.Count > 0 && !_config.ShowAllDay => $"Nothing but all-day events in {window}.",
            _ => $"Nothing in {window}.",
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
