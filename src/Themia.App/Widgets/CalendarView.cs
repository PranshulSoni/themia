using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
internal sealed partial class CalendarView : Grid, IWidgetView
{
    private const string Scope = "calendar-widget";

    /// <summary>
    /// A shared transparent brush for the rows' rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so a row would never
    /// raise the pointer events its hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    // Written out rather than read from a culture: the app builds with InvariantGlobalization, and
    // Formatting.Ago only looks backwards, so a calendar that looks forwards needs its own names.
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private readonly FontIcon _icon = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _emptyText = new();
    private readonly StackPanel _empty = new();

    // What a resize has to re-size, kept by role. A widget is dragged to any size, so the rows are
    // measured from the tile rather than fixed - and holding the pieces means a resize re-sizes
    // what is already on screen instead of asking the hub for the same records again.
    private readonly List<TextBlock> _primaryText = [];
    private readonly List<TextBlock> _secondaryText = [];
    private readonly List<TextBlock> _dayHeadings = [];
    private readonly List<TextBlock> _timeLabels = [];
    private readonly List<Border> _rows = [];
    private readonly List<Border> _dayHeadingHosts = [];

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
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var muted = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);

        _title.Text = "Upcoming";
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = muted;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _icon.Glyph = Glyphs.Calendar;
        _icon.Foreground = muted;
        _icon.Margin = new Thickness(0, 0, 6, 0);

        _badge.FontWeight = FontWeights.Bold;
        _badge.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush", Colors.White);

        _badgeHost = new Border
        {
            Child = _badge,
            Background = ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        right.Children.Add(_busy);
        right.Children.Add(_badgeHost);

        var header = new Grid
        {
            Margin = new Thickness(WidgetLayout.ContentInset, WidgetLayout.ContentInset, WidgetLayout.ContentInset, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(right, 2);
        header.Children.Add(_icon);
        header.Children.Add(_title);
        header.Children.Add(right);
        Grid.SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            Padding = WidgetLayout.ListInset,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 1);

        _emptyText.TextWrapping = TextWrapping.Wrap;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.TextAlignment = TextAlignment.Center;
        _emptyText.Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray);

        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.Children.Add(_emptyText);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += (_, _) => ApplyTypeScale();
    }

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

        foreach (var item in shown)
        {
            var day = DayOf(item, today);

            if (heading != day)
            {
                heading = day;
                _list.Children.Add(BuildDayHeading(DayLabel(day, today)));
            }

            var row = BuildRow(item, now);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _badge.Text = shown.Count.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = Visibility.Visible;

        ApplyTypeScale();
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

    private Border BuildDayHeading(string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        _dayHeadings.Add(text);

        var host = new Border { Child = text };
        _dayHeadingHosts.Add(host);
        return host;
    }

    private Border BuildRow(EventRecord item, DateTimeOffset now)
    {
        var when = new TextBlock
        {
            Text = TimeLabel(item, now),
            Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
            FontWeight = InProgress(item, now) ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        // Every row's time block gets the same width in ApplyTypeScale, so the titles line up in a
        // column instead of each starting wherever its own time happened to end.
        _timeLabels.Add(when);
        _secondaryText.Add(when);

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Title) ? "(no title)" : item.Title,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush", Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        _primaryText.Add(title);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(title);

        if (_config.ShowLocation && !string.IsNullOrWhiteSpace(item.Location))
        {
            var place = new TextBlock
            {
                Text = item.Location,
                Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };

            _secondaryText.Add(place);
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

        var row = new Border
        {
            Child = body,
            Background = Transparent,
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        // Hover, the way every list on Windows does it. Done by hand rather than with a ListView
        // because the row is a custom layout, but the brush is the stock one so it tracks the theme
        // and the accent. PointerExited alone is not enough: the pointer can leave without it when
        // the list scrolls or the widget is torn down mid-hover, stranding a row lit.
        row.PointerEntered += (_, _) => row.Background = ThemeBrush("SubtleFillColorSecondaryBrush", Colors.Gray);
        row.PointerExited += (_, _) => row.Background = Transparent;
        row.PointerCaptureLost += (_, _) => row.Background = Transparent;
        row.PointerCanceled += (_, _) => row.Background = Transparent;

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
    /// Size the rows to the tile. A widget is dragged to any size, so a fixed row would leave a
    /// large tile mostly empty and clip a small one.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var primary = TypeScale.Fit(width, height, 0.055, 0.085, 11, 20);
        var secondary = TypeScale.Fit(width, height, 0.046, 0.072, 10, 17);
        var heading = TypeScale.Fit(width, height, 0.050, 0.080, 10, 18);
        var pad = Math.Clamp(primary * 0.55, 4, 12);

        _title.FontSize = heading;
        _icon.FontSize = heading;
        _badge.FontSize = Math.Max(9, heading - 2);
        _emptyText.FontSize = secondary;

        foreach (var text in _primaryText)
        {
            text.FontSize = primary;
        }

        foreach (var text in _secondaryText)
        {
            text.FontSize = secondary;
        }

        foreach (var text in _dayHeadings)
        {
            text.FontSize = secondary;
        }

        // Wide enough for "All day", which is the longest thing the column ever holds.
        var timeColumn = Math.Round(secondary * 4.2);
        foreach (var text in _timeLabels)
        {
            text.MinWidth = timeColumn;
        }

        foreach (var host in _dayHeadingHosts)
        {
            // A day heading is a label, not a row, so it keeps the inset the rows give up.
            host.Padding = new Thickness(WidgetLayout.ContentInset, pad, WidgetLayout.ContentInset, pad * 0.4);
        }

        foreach (var row in _rows)
        {
            // The sides are the standard inset rather than the tile-scaled one, so a row
            // reaches both edges of the widget and its text still lines up with the header.
            row.Padding = new Thickness(WidgetLayout.ContentInset, pad * 0.7, WidgetLayout.ContentInset, pad * 0.7);

            // Negative side margins so the hover highlight reaches the tile's edge rather than
            // stopping short of it in a box of its own.
        }
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

    private void ClearRows()
    {
        _list.Children.Clear();
        _rows.Clear();
        _primaryText.Clear();
        _secondaryText.Clear();
        _dayHeadings.Clear();
        _dayHeadingHosts.Clear();
        _timeLabels.Clear();
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

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
