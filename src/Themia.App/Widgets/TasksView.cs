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
internal sealed partial class TasksView : Grid, IWidgetView
{
    private const string Scope = "tasks-widget";

    /// <summary>
    /// A shared transparent brush for the rows' rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so a row would never
    /// raise the pointer events its hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    // Written out rather than read from a culture: the app builds with InvariantGlobalization, and
    // Formatting.Ago only looks backwards, while a due date mostly looks forwards.
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
    private readonly List<Border> _markers = [];
    private readonly List<FontIcon> _markerIcons = [];
    private readonly List<Border> _rows = [];

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
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var muted = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);

        _title.Text = "Tasks";
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = muted;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _icon.Glyph = Glyphs.Task;
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
            Margin = new Thickness(0, 0, 0, 6),
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
        var open = 0;

        foreach (var task in tasks)
        {
            if (!task.Completed)
            {
                open++;
            }

            var row = BuildRow(task, today);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        // The open count, not the row count: with completed tasks shown, "how much is left" is the
        // number worth putting in the header, and it is the one the list is otherwise silent about.
        _badge.Text = open.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = open > 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyTypeScale();
    }

    private Border BuildRow(TaskRecord task, DateOnly today)
    {
        var marker = BuildMarker(task);

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(task.Title) ? "(untitled task)" : task.Title,

            // A finished task is struck through and dimmed, not merely dimmed: strike-through is
            // the one mark that says "done" without a legend and without relying on colour.
            TextDecorations = task.Completed
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None,
            Foreground = task.Completed
                ? ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray)
                : ThemeBrush("TextFillColorPrimaryBrush", Colors.White),

            // Hearth's "high" is the only priority worth marking, and weight marks it the way an
            // unread message is marked - no extra glyph, no legend.
            FontWeight = task.Important && !task.Completed ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _primaryText.Add(title);

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

            var when = new TextBlock
            {
                Text = DueLabel(due, today, overdue),

                // Overdue says so in words as well as in colour: a red date alone is invisible to
                // anyone who cannot see the red.
                Foreground = overdue
                    ? ThemeBrush("SystemFillColorCriticalBrush", Colors.Red)
                    : ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray),
                FontWeight = overdue ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };

            _secondaryText.Add(when);
            Grid.SetColumn(when, 2);
            body.Children.Add(when);
        }

        var row = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(4),
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

        ToolTipService.SetToolTip(row, Tooltip(task, today));
        return row;
    }

    /// <summary>
    /// The state marker: an empty ring for open work, a filled tick for finished. Drawn rather than
    /// taken from the icon font because there is no hollow-circle glyph in the set, and an outline
    /// next to a filled tick is what makes the two states tell apart at a glance.
    /// </summary>
    private Border BuildMarker(TaskRecord task)
    {
        var marker = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = ThemeBrush("ControlStrongStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(task.Completed ? 0 : 1.2),
            Background = task.Completed
                ? ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue)
                : Transparent,
        };

        if (task.Completed)
        {
            var tick = new FontIcon
            {
                Glyph = Glyphs.Done,
                Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush", Colors.White),
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

        var markerSize = Math.Round(primary * 1.05);
        foreach (var marker in _markers)
        {
            marker.Width = markerSize;
            marker.Height = markerSize;
            marker.CornerRadius = new CornerRadius(markerSize / 2);
        }

        foreach (var tick in _markerIcons)
        {
            tick.FontSize = Math.Max(8, markerSize * 0.62);
        }

        foreach (var row in _rows)
        {
            row.Padding = new Thickness(pad, pad * 0.7, pad, pad * 0.7);

            // Negative side margins so the hover highlight reaches the tile's edge rather than
            // stopping short of it in a box of its own.
            row.Margin = new Thickness(-pad, 0, -pad, 0);
        }
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

    private void ClearRows()
    {
        _list.Children.Clear();
        _rows.Clear();
        _primaryText.Clear();
        _secondaryText.Clear();
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

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
