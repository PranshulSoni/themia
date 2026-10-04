using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A short list of habits, a box to tick each day, and how long the run is. [S-310]
/// <para>
/// The data <b>is</b> the config, the way a note is. So this is one of the few views that writes its
/// own settings: ticking a box mutates the config it was handed and asks the surface to persist it,
/// on a debounce, flushed on detach. [S-313]
/// </para>
/// <para>
/// It is a Windows 11 list. Rows are a fixed 40px — 32 on a small tile — and the type is the ramp's
/// list-row steps, so a card that grows shows <i>more habits</i> rather than bigger ones, and the
/// squares of history grow in number rather than in size. There is no rule between rows.
/// </para>
/// </summary>
internal sealed partial class HabitTrackerView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetHeaderContent
{
    private const string Scope = "habits";

    /// <summary>
    /// How long a run of taps is collected before the state file is written. The tick itself is
    /// already in the config by then — this only decides when it reaches the disk, and
    /// <see cref="Flush"/> on detach is what guarantees it does.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>Under this the row goes compact and the squares shed: 32px holds a box and a name.</summary>
    private const double CompactWidth = 240;

    private const double CompactHeight = 170;

    /// <summary>Below this there is no room for the history strip beside a readable name.</summary>
    private const double HistoryMinimumWidth = 280;

    /// <summary>And below this the streak is a bare number rather than a number and its unit.</summary>
    private const double StreakWordMinimumWidth = 360;

    private readonly TextBlock _done = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly Border _emptyHost;
    private readonly TextBlock _emptyText;
    private readonly FontIcon _emptyIcon;
    private readonly List<HabitRow> _rows = [];
    private readonly DispatcherQueueTimer? _saveTimer;

    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private HabitTrackerConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private LocalDay _today;
    private bool _compact;
    private bool _dirty;
    private bool _detached;

    public HabitTrackerView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_done, FontNumeralAlignment.Tabular);

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = Fluent.ListPadding,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        // Says what to do about it, not merely that there is nothing: the habits are added in the
        // settings window, which is not otherwise discoverable from an empty card.
        _emptyText = Fluent.Text(FluentType.Body);
        _emptyText.Text = "No habits yet.\nAdd one in this widget's settings.";

        _emptyHost = new Border
        {
            Child = Fluent.EmptyState(Glyphs.Habit, _emptyText, out _emptyIcon),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        Children.Add(_scroll);
        Children.Add(_emptyHost);

        _saveTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_saveTimer is null)
        {
            // Where there is no dispatcher queue the ticks are still written on detach; it is only
            // the save-as-you-go that is lost, which is why this is a warning and not a failure.
            Log.Warn(Scope, "no dispatcher queue on this thread; habits save on close only");
        }
        else
        {
            _saveTimer.Interval = SaveDebounce;
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += OnSaveTick;
        }

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The "3/5 done" count, in the card's header. Built in the constructor because the card reads
    /// this once, when it builds that header.
    /// </summary>
    public FrameworkElement? HeaderTrailing => _done;

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    /// <summary>
    /// Re-read the config.
    /// <para>
    /// There is no unflushed edit to protect here, and that is by construction rather than by luck:
    /// a tick is written <i>into the config</i> the moment the box is tapped, and only the save is
    /// deferred. So an incoming config — the settings window renaming a habit, say — cannot arrive
    /// carrying an older idea of today's boxes, because it is the same bag this view already ticked.
    /// A genuinely different bag (a paste, an undo) is adopted after flushing, so nothing pending is
    /// left owing to a config nobody holds any more.
    /// </para>
    /// </summary>
    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as HabitTrackerConfig ?? new HabitTrackerConfig();

        if (!ReferenceEquals(next, _config))
        {
            Flush();
        }

        _config = next;

        if (Resubscribe())
        {
            return;
        }

        Render();
    }

    /// <summary>
    /// Only when the cadence changed, and says whether it did: a new subscription ticks
    /// immediately, so the caller has nothing left to do. Rebuilding it on every config touch makes
    /// dragging a settings slider stutter for every widget on the desktop.
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
        _subscription = _ticker?.Subscribe(interval, OnTick);
        return _subscription is not null;
    }

    /// <summary>
    /// The only reason this widget wakes: a box ticked yesterday has to clear itself when the day
    /// does. Nothing is redrawn unless the calendar day actually changed, so the cost of the
    /// subscription is one comparison a minute.
    /// </summary>
    private void OnTick()
    {
        if (_detached)
        {
            return;
        }

        var today = LocalDay.From(DateTimeOffset.Now);

        if (today == _today)
        {
            return;
        }

        Render();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = args.NewSize.Width;
        var height = args.NewSize.Height;

        _emptyIcon.Visibility = height >= Fluent.HeaderHidesBelow ? Visibility.Visible : Visibility.Collapsed;

        _compact = width < CompactWidth || height < CompactHeight;

        // Re-lays what is already drawn rather than rebuilding it: a rebuild would drop the row
        // under the pointer and re-run every streak count for a drag that changed only a width.
        var layout = Layout(width);

        foreach (var row in _rows)
        {
            row.SetLayout(_compact, layout);
        }
    }

    private HabitRowLayout Layout(double width) => new(
        width >= HistoryMinimumWidth,
        width >= StreakWordMinimumWidth);

    /// <summary>
    /// Draw the habits. The clock is read <b>once</b> here and passed down: a list drawn across
    /// midnight that read it per row would tick one habit's box against today and the next one's
    /// against yesterday, in the same frame.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _today = LocalDay.From(DateTimeOffset.Now);

        var habits = _config.Habits;
        var empty = habits.Count == 0;

        _emptyHost.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        // Rebuild only when the list itself changed. A tick, a rename or a midnight rollover
        // re-letters what is already there, which keeps the row under the pointer and its hover.
        if (!SameHabits(habits))
        {
            BuildRows(habits);
        }

        var layout = Layout(ActualWidth);
        var doneToday = 0;

        for (var i = 0; i < _rows.Count; i++)
        {
            var habit = habits[i];
            var days = HabitRules.Days(habit.Days, _today);

            if (days.Contains(_today))
            {
                doneToday++;
            }

            _rows[i].Update(habit, days, _today, _config.EffectiveHistoryDays, _config.ShowStreak);
            _rows[i].SetLayout(_compact, layout);
        }

        _done.Text = empty
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{doneToday}/{habits.Count}");

        AutomationProperties.SetName(_done, empty
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{doneToday} of {habits.Count} done today"));
    }

    private bool SameHabits(IReadOnlyList<HabitRecord> habits)
    {
        if (habits.Count != _rows.Count)
        {
            return false;
        }

        for (var i = 0; i < habits.Count; i++)
        {
            // By identity, not by name: a rename must not cost a rebuild, and two habits swapping
            // places must.
            if (!ReferenceEquals(habits[i], _rows[i].Habit))
            {
                return false;
            }
        }

        return true;
    }

    private void BuildRows(IReadOnlyList<HabitRecord> habits)
    {
        foreach (var existing in _rows)
        {
            existing.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();

        foreach (var habit in habits)
        {
            var row = new HabitRow(habit, OnToggled);
            _rows.Add(row);
            _list.Children.Add(row.Host);
        }
    }

    /// <summary>
    /// A box was tapped. The day is read once here, for the same reason it is read once per render:
    /// a tap at 23:59:59.9 must go on one day, not on whichever one each of three reads lands in.
    /// </summary>
    private void OnToggled(HabitRecord habit)
    {
        if (_detached)
        {
            return;
        }

        var today = LocalDay.From(DateTimeOffset.Now);

        HabitRules.Toggle(habit.Days, today);

        _dirty = true;
        _saveTimer?.Start(); // restarts the countdown, so a run of taps is one write

        // A full redraw rather than just this row: the header's count and, on a rollover that
        // happened between the last render and this tap, every other row's idea of today.
        Render();
    }

    private void OnSaveTick(DispatcherQueueTimer sender, object args) => Flush();

    /// <summary>
    /// Ask for the config to be persisted. Idempotent: with nothing pending it does nothing, so
    /// calling it from three places costs nothing.
    /// </summary>
    private void Flush()
    {
        _saveTimer?.Stop();

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _surface?.RequestSave();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        // Before anything is unhooked: this is the last chance today's ticks have.
        Flush();
        _detached = true;

        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTick;
        }

        SizeChanged -= OnSizeChanged;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();
        _ticker = null;
    }

    /// <summary>Which of the row's optional parts this card is wide enough for.</summary>
    /// <param name="History">Show the strip of recent days.</param>
    /// <param name="StreakWord">Spell the streak's unit out rather than showing a bare number.</param>
    private readonly record struct HabitRowLayout(bool History, bool StreakWord);

    /// <summary>
    /// One habit's row: a box, the name, the run, and the last few days as squares.
    /// <para>
    /// The box is the <b>only</b> thing that completes a habit. The row carries no tap handler of
    /// its own, so a click meant for the name or the streak can never tick something off — a widget
    /// sits under the pointer's path across the whole desktop, and a surface with no undo should not
    /// record anything on a stray click.
    /// </para>
    /// </summary>
    private sealed class HabitRow
    {
        private readonly Action<HabitRecord> _onToggled;
        private readonly CheckBox _box = new();
        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly TextBlock _streak = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly StackPanel _history = new() { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };
        private readonly List<Border> _squares = [];
        private readonly IDisposable _hover;

        // Looked up once per row rather than per square per render: a thirty-one square strip on a
        // dozen habits is four hundred resource lookups a minute otherwise.
        private readonly Brush _filled = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        private readonly Brush _blank = FluentLayout.Themed("ControlAltFillColorSecondaryBrush", Colors.Gray);

        private bool _applying;
        private bool _showStreak = true;
        private bool _showHistory = true;

        // The run as a number, kept rather than re-read out of the text block: the label is built
        // from this and the width, and a control that parses its own caption back is a control with
        // two copies of the truth in it.
        private int _streakDays;

        private bool _streakSpelled;

        public HabitRow(HabitRecord habit, Action<HabitRecord> onToggled)
        {
            Habit = habit;
            _onToggled = onToggled;

            // The stock CheckBox reserves about 110px for content it has none of, which would push
            // the name most of the way across a small card.
            _box.MinWidth = 0;
            _box.MinHeight = 0;
            _box.Padding = new Thickness(0);
            _box.Margin = new Thickness(0, 0, Fluent.Space3, 0);
            _box.VerticalAlignment = VerticalAlignment.Center;
            _box.Checked += OnChanged;
            _box.Unchecked += OnChanged;

            _name.VerticalAlignment = VerticalAlignment.Center;

            _streak.VerticalAlignment = VerticalAlignment.Center;
            _streak.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
            _streak.TextAlignment = TextAlignment.Right;
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_streak, FontNumeralAlignment.Tabular);

            _history.VerticalAlignment = VerticalAlignment.Center;
            _history.Margin = new Thickness(Fluent.Space3, 0, 0, 0);

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

            Grid.SetColumn(_box, 0);
            Grid.SetColumn(_name, 1);
            Grid.SetColumn(_streak, 2);
            Grid.SetColumn(_history, 3);

            grid.Children.Add(_box);
            grid.Children.Add(_name);
            grid.Children.Add(_streak);
            grid.Children.Add(_history);

            Host = Fluent.Row(grid);
            _hover = Fluent.Hover(Host);
        }

        public HabitRecord Habit { get; }

        public Border Host { get; }

        public void Update(HabitRecord habit, IReadOnlySet<LocalDay> days, LocalDay today, int historyDays, bool showStreak)
        {
            _name.Text = string.IsNullOrWhiteSpace(habit.Name) ? "Untitled habit" : habit.Name;

            var done = HabitRules.IsDone(days, today);

            // Guarded: assigning IsChecked raises Checked/Unchecked, and without this a redraw
            // would toggle every habit it drew.
            _applying = true;

            try
            {
                _box.IsChecked = done;
            }
            finally
            {
                _applying = false;
            }

            AutomationProperties.SetName(_box, $"{_name.Text}, {(done ? "done today" : "not done today")}");

            _streakDays = HabitRules.Streak(days, today);
            _showStreak = showStreak;
            ApplyStreak();

            DrawHistory(days, today, historyDays);
            ApplyVisibility();
        }

        /// <summary>
        /// The last few days as squares, oldest on the left and today on the right.
        /// <para>
        /// Derived from the same set the streak counts, so the squares and the number beside them
        /// can never disagree about which day is which. Each square carries its date and its state
        /// in words, because a filled square and an empty one are otherwise the same shape.
        /// </para>
        /// </summary>
        private void DrawHistory(IReadOnlySet<LocalDay> days, LocalDay today, int historyDays)
        {
            var recent = HabitRules.Recent(days, today, historyDays);

            // Only when the count changed. Rebuilding a strip that is already the right length
            // would throw away its tooltips for nothing.
            if (_squares.Count != recent.Count)
            {
                _squares.Clear();
                _history.Children.Clear();

                for (var i = 0; i < recent.Count; i++)
                {
                    var square = new Border
                    {
                        Width = Fluent.Space2,
                        Height = Fluent.Space2,
                        CornerRadius = new CornerRadius(2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    _squares.Add(square);
                    _history.Children.Add(square);
                }
            }

            for (var i = 0; i < recent.Count; i++)
            {
                var day = today.AddDays(i - (recent.Count - 1));
                var done = recent[i];

                _squares[i].Background = done ? _filled : _blank;

                var label = $"{CalendarText.FullDate(day)}: {(done ? "done" : "not done")}";
                ToolTipService.SetToolTip(_squares[i], label);
                AutomationProperties.SetName(_squares[i], label);
            }
        }

        /// <summary>
        /// Standard or compact, the two heights a Windows list comes in, plus which optional parts
        /// this card has room for. The squares shed before the name does: the name is what a habit
        /// list is for, and a name trimmed to three characters to make room for a strip of dots is a
        /// list that has stopped being readable.
        /// </summary>
        public void SetLayout(bool compact, HabitRowLayout layout)
        {
            Fluent.SetDensity(Host, compact);
            _showHistory = layout.History && !compact;
            _streakSpelled = layout.StreakWord;
            ApplyStreak();
            ApplyVisibility();
        }

        /// <summary>
        /// The run, as a bare number on a narrow card and as a number with its unit on a wide one.
        /// The spoken and hovered forms are the full sentence at every width, because "12" on its
        /// own is not an answer to anything.
        /// </summary>
        private void ApplyStreak()
        {
            var count = _streakDays.ToString(CultureInfo.InvariantCulture);

            _streak.Text = _streakDays == 0
                ? string.Empty
                : _streakSpelled ? (_streakDays == 1 ? "1 day" : $"{count} days") : count;

            var words = _streakDays switch
            {
                0 => "no run yet",
                1 => "1 day in a row",
                _ => $"{count} days in a row",
            };

            AutomationProperties.SetName(_streak, words);
            ToolTipService.SetToolTip(_streak, words);
        }

        private void ApplyVisibility()
        {
            _streak.Visibility = _showStreak && _streak.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _history.Visibility = _showHistory && _squares.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnChanged(object sender, RoutedEventArgs args)
        {
            if (_applying)
            {
                return;
            }

            _onToggled(Habit);
        }

        /// <summary>Safe to call twice: the hover handle is idempotent and the handlers come off once.</summary>
        public void Detach()
        {
            _hover.Dispose();
            _box.Checked -= OnChanged;
            _box.Unchecked -= OnChanged;
            Host.Child = null;
        }
    }
}
