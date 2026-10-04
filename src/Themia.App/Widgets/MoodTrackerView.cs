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
/// How you felt, logged in a tap, read back as a calendar heatmap. [S-320]
/// <para>
/// The heatmap's scale is the <b>accent brush</b> at rising opacity — the one place in this app a
/// colour ramp is legitimate, and it is still the shared accent object with an opacity on the
/// <i>shape</i>, never a hex value this file invented. So it follows the user's accent colour and
/// the light/dark and high-contrast themes like everything else on the desktop.
/// </para>
/// <para>
/// And it never relies on that colour alone. [S-322] Each square carries the value three more ways:
/// a bar whose <i>height</i> is the level, a digit when the square is big enough to hold one, and
/// the whole day as a sentence in its tooltip and its accessible name.
/// </para>
/// <para>
/// [S-1452] [S-1453] The tile follows <see cref="MoodLayout.Plan"/>: stacked (faces, map with weeks
/// as rows, thought box, footer) up to a regular width; split on a wide tile — a left pane with the
/// faces, the thought box, the footer and the insights ([S-1454]), and the map beside it with weeks
/// as columns, recent thoughts under it. The drawing (cells, faces, insights) is in
/// <c>MoodTrackerView.Drawing.cs</c>.
/// </para>
/// </summary>
internal sealed partial class MoodTrackerView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetSizeAware, IWidgetKeys, IWidgetHeaderContent
{
    private const string Scope = "mood";

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The opacity of the wash at each of the five levels, and zero for a day with nothing on it.
    /// Applied to the square, never to the accent brush (shared app-wide). The bottom of the ramp is
    /// 0.22 because below about a fifth the wash cannot be told from the empty square.
    /// </summary>
    private static readonly double[] Wash = [0, 0.22, 0.40, 0.58, 0.78, 1.0];

    private readonly Grid _picker = new();
    private readonly LevelButton[] _levels = new LevelButton[MoodRules.MaxLevel];
    private readonly StackPanel _mapColumn = new() { Spacing = 0 };
    private readonly Grid _cells = new();
    private readonly List<DayCell> _dayCells = [];
    private readonly TextBox _note = new();
    private readonly TextBlock _stats = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _insights = new() { Spacing = MoodLayout.Gap };
    private readonly StackPanel _recent = new() { Spacing = MoodLayout.CellGap };
    private readonly DispatcherQueueTimer? _saveTimer;

    // Looked up once: a grid of three hundred squares re-resolving brushes per redraw is wasted work.
    private readonly Brush _accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _emptyFill = FluentLayout.Themed("ControlAltFillColorSecondaryBrush", Colors.Gray);
    private readonly Brush _onAccent = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White);
    private readonly Brush _onCard = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _ring = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _noRing = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray);
    private readonly Brush _focus = FluentLayout.Themed("FocusStrokeColorOuterBrush", Colors.White);

    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private MoodTrackerConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private WidgetArea _area;
    private MoodPlan _plan;
    private LocalDay _today;
    private MoodGrid? _grid;
    private string _shape = string.Empty;
    private int? _cursor;
    private bool _dirty;
    private bool _applyingNote;
    private bool _detached;

    public MoodTrackerView()
    {
        for (var i = 0; i < 4; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        Padding = new Thickness(0, MoodLayout.Top, 0, MoodLayout.Bottom);

        BuildPicker();
        BuildNote();

        _cells.RowSpacing = MoodLayout.CellGap;
        _cells.ColumnSpacing = MoodLayout.CellGap;
        _mapColumn.Children.Add(_cells);
        _mapColumn.Children.Add(_recent);

        _stats.TextAlignment = TextAlignment.Center;
        _stats.MaxLines = 1;
        _stats.TextTrimming = TextTrimming.CharacterEllipsis;

        Children.Add(_picker);
        Children.Add(_mapColumn);
        Children.Add(_note);
        Children.Add(_stats);
        Children.Add(_insights);

        _saveTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_saveTimer is null)
        {
            Log.Warn(Scope, "no dispatcher queue on this thread; moods save on close only");
        }
        else
        {
            _saveTimer.Interval = SaveDebounce;
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += OnSaveTick;
        }
    }

    /// <summary>
    /// [S-1449] The five points of the scale as five faces, awful to great, on the same wash the
    /// heatmap uses — so the row of buttons is also the legend. The digit and the level's name are the
    /// accessible name and tooltip.
    /// </summary>
    private void BuildPicker()
    {
        _picker.ColumnSpacing = Fluent.Space1;
        _picker.Height = MoodLayout.PickerHeight;

        for (var level = MoodRules.MinLevel; level <= MoodRules.MaxLevel; level++)
        {
            _picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var button = new LevelButton(level, _accent, _emptyFill, _onAccent, _onCard, Wash[level], OnLevelPicked);
            _levels[level - MoodRules.MinLevel] = button;

            SetColumn(button.Host, level - MoodRules.MinLevel);
            _picker.Children.Add(button.Host);
        }
    }

    private void BuildNote()
    {
        // The same chrome-free field the note widget uses: no border at rest, the stock subtle fill
        // under the pointer, and the stock accent line when it has focus.
        var clear = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        var hover = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);

        _note.Resources["TextControlBackground"] = clear;
        _note.Resources["TextControlBackgroundPointerOver"] = hover;
        _note.Resources["TextControlBackgroundFocused"] = hover;
        _note.Resources["TextControlBackgroundDisabled"] = clear;
        _note.Resources["TextControlBorderBrush"] = clear;
        _note.Resources["TextControlBorderBrushPointerOver"] = clear;
        _note.Resources["TextControlBorderBrushDisabled"] = clear;
        _note.Resources["TextControlBorderBrushFocused"] = _accent;

        _note.AcceptsReturn = false;
        _note.TextWrapping = TextWrapping.NoWrap;
        _note.IsSpellCheckEnabled = false;
        _note.IsTextPredictionEnabled = false;
        _note.BorderThickness = new Thickness(0);
        _note.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _note.FontSize = FluentType.Body.FontSize;
        _note.Height = MoodLayout.NoteHeight;
        _note.MinHeight = MoodLayout.NoteHeight;
        _note.Padding = new Thickness(Fluent.Space1);

        _note.GotFocus += OnNoteGotFocus;
        _note.LostFocus += OnNoteLostFocus;
        _note.KeyDown += OnNoteKeyDown;
    }

    /// <summary>[S-1573] What the map shows when it is a month: its name, or "This week" / "Last N weeks".</summary>
    public FrameworkElement? HeaderTrailing => _caption;

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    /// <summary>
    /// Re-read the config. A tap and a committed thought are written into the config immediately and
    /// only the <i>save</i> is deferred, so an incoming config cannot be carrying an older version of
    /// either. A genuinely different bag is adopted after flushing.
    /// </summary>
    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as MoodTrackerConfig ?? new MoodTrackerConfig();

        if (!ReferenceEquals(next, _config))
        {
            Flush();
        }

        _config = next;
        Resubscribe();
        Render();
    }

    /// <inheritdoc />
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Render();
    }

    private void Resubscribe()
    {
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
    /// The only reason this widget wakes: the window has to roll over at midnight without being
    /// touched. Nothing is redrawn unless the calendar day actually changed. [S-13]
    /// </summary>
    private void OnTick()
    {
        if (!_detached && LocalDay.From(DateTimeOffset.Now) != _today)
        {
            Render();
        }
    }

    /// <summary>
    /// Draw everything. The clock is read <b>once</b> here and passed down, so the picker, the
    /// heatmap and the footer cannot disagree about which day is today in a frame that straddles
    /// midnight.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _today = LocalDay.From(DateTimeOffset.Now);

        var monthWeeks = MoodGrid.Month(_today.Year, _today.Month, _config.FirstDayOfWeek).Rows.Count;
        _plan = MoodLayout.Plan(_area, _config, monthWeeks);

        Arrange();

        var entries = MoodRules.Sanitise(_config.Entries);
        var byDay = MoodRules.ByDay(entries);
        var latest = MoodRules.LatestOn(entries, _today);

        DrawPicker(latest?.Level ?? 0);
        DrawMap(entries, byDay);
        DrawNote(latest);
        DrawStats(entries, byDay);
        DrawInsights(entries);
    }

    /// <summary>
    /// Put each part where the plan says. Stacked: one column, the map in the star row. Split: the
    /// left pane's parts down column 0 (insights in the star row), the map spanning column 1.
    /// </summary>
    private void Arrange()
    {
        var split = _plan.Arrangement == MoodArrangement.Split;
        var inset = MoodLayout.Inset;
        var gap = MoodLayout.Gap;

        ColumnDefinitions[0].Width = split ? new GridLength(inset + _plan.LeftWidth) : new GridLength(1, GridUnitType.Star);
        ColumnDefinitions[1].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        RowDefinitions[0].Height = GridLength.Auto;
        RowDefinitions[1].Height = split ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        RowDefinitions[2].Height = GridLength.Auto;
        RowDefinitions[3].Height = split ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

        var right = split ? 0 : inset;

        Place(_picker, 0, 0, 1);
        _picker.Margin = new Thickness(inset, 0, right, 0);

        Place(_note, split ? 1 : 2, 0, 1);
        _note.Margin = new Thickness(inset - Fluent.Space1, gap, right == 0 ? 0 : right - Fluent.Space1, 0);
        _note.Visibility = _plan.Note ? Visibility.Visible : Visibility.Collapsed;

        Place(_stats, split ? 2 : 3, 0, 1);
        _stats.Margin = new Thickness(inset, gap, right, 0);
        _stats.TextAlignment = split ? TextAlignment.Left : TextAlignment.Center;
        _stats.Visibility = _plan.Stats ? Visibility.Visible : Visibility.Collapsed;

        Place(_insights, 3, 0, 1);
        _insights.Margin = new Thickness(inset, gap, 0, 0);
        _insights.Visibility = split && (_plan.Distribution || _plan.Weekdays || _plan.Summary) ? Visibility.Visible : Visibility.Collapsed;

        if (split)
        {
            SetRow(_mapColumn, 0);
            SetRowSpan(_mapColumn, 4);
            SetColumn(_mapColumn, 1);
            _mapColumn.Margin = new Thickness(2 * gap, 0, inset, 0);
            _mapColumn.HorizontalAlignment = HorizontalAlignment.Left;
            _mapColumn.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            Place(_mapColumn, 1, 0, 1);
            _mapColumn.Margin = new Thickness(inset, gap, inset, 0);
            _mapColumn.HorizontalAlignment = HorizontalAlignment.Center;
            _mapColumn.VerticalAlignment = VerticalAlignment.Center;
        }

        _recent.Visibility = split && _plan.RecentNotes > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Place(FrameworkElement element, int row, int column, int rowSpan)
    {
        SetRow(element, row);
        SetRowSpan(element, rowSpan);
        SetColumn(element, column);
        SetColumnSpan(element, 1);
    }

    private void DrawPicker(int selected)
    {
        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i].SetState(_levels[i].Level == selected, _cursor == i, _ring, _noRing, _focus);
        }
    }

    private void DrawNote(MoodEntry? latest)
    {
        // Disabled until something is logged today, with a placeholder that says why. [S-05]
        _note.IsEnabled = latest is not null;
        _note.PlaceholderText = latest is null ? "Log how you feel to add a thought" : "What was behind it?";

        var text = latest?.Note ?? string.Empty;

        // Never while it is being typed in: the box is the live copy until it commits.
        if (_note.FocusState != FocusState.Unfocused || string.Equals(_note.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _applyingNote = true;

        try
        {
            _note.Text = text;
        }
        finally
        {
            _applyingNote = false;
        }
    }

    private void DrawStats(IReadOnlyList<MoodEntry> entries, IReadOnlyDictionary<LocalDay, MoodDay> byDay)
    {
        if (_grid is null)
        {
            _stats.Text = string.Empty;
            return;
        }

        var streak = MoodRules.Streak(byDay, _today);
        var average = MoodRules.Average(entries, _grid.First, _grid.Last);

        if (average is null)
        {
            // Nothing in the window at all: no average, rather than a misleading zero. [S-09]
            _stats.Text = "Nothing logged yet";
            AutomationProperties.SetName(_stats, "Nothing logged in this window yet");
            return;
        }

        var run = streak switch
        {
            0 => "no run",
            1 => "1 day run",
            _ => $"{streak.ToString(CultureInfo.InvariantCulture)} day run",
        };

        var mean = average.Value.ToString("0.0", CultureInfo.InvariantCulture);

        _stats.Text = $"{run}  ·  average {mean}";
        AutomationProperties.SetName(_stats, $"{run}, average {mean} out of {MoodRules.MaxLevel}");
    }

    // -----------------------------------------------------------------------------------------
    // Logging, deleting, the thought box
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A level was tapped. The instant is read once here and stored with its offset, so the entry
    /// keeps the day it was actually logged on whatever the machine's zone does afterwards. [S-03]
    /// </summary>
    private void OnLevelPicked(int level)
    {
        if (_detached)
        {
            return;
        }

        _config.Entries.Add(new MoodEntry
        {
            Id = MoodEntry.NewId(),
            At = DateTimeOffset.Now,
            Level = level,
            Note = string.Empty,
        });

        // Bounded at the moment it grows: the log lives in the state file every drag rewrites.
        MoodRules.Prune(_config.Entries, MoodTrackerConfig.MaxEntries);

        MarkDirty();
        Render();
    }

    /// <summary>[S-1451] Delete one entry from the day menu's submenu.</summary>
    private void DeleteEntry(string id)
    {
        if (_detached || !MoodInsights.Delete(_config.Entries, id))
        {
            return;
        }

        MarkDirty();
        Render();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer?.Start();
    }

    private void OnNoteGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnNoteLostFocus(object sender, RoutedEventArgs args)
    {
        CommitNote();
        _surface?.RequestTextInput(false);
    }

    private void OnNoteKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        CommitNote();
    }

    /// <summary>
    /// Write the thought onto today's most recent entry. On commit, not per keystroke: the box is the
    /// live copy while it has focus. [S-05]
    /// </summary>
    private void CommitNote()
    {
        if (_applyingNote || _detached)
        {
            return;
        }

        var entries = MoodRules.Sanitise(_config.Entries);
        var latest = MoodRules.LatestOn(entries, LocalDay.From(DateTimeOffset.Now));

        if (latest is null)
        {
            return; // ignore: nothing logged today, so there is nothing to attach the thought to.
        }

        var text = _note.Text.Trim();

        if (string.Equals(latest.Note, text, StringComparison.Ordinal))
        {
            return;
        }

        latest.Note = text;
        MarkDirty();
    }

    private void OnSaveTick(DispatcherQueueTimer sender, object args) => Flush();

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

    // -----------------------------------------------------------------------------------------
    // Keyboard ([S-1455])
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1455] [S-1562] A key from the shell's keyboard lease. Left/Right (and Up/Down) and Home/End
    /// move a cursor over the five faces (<see cref="ItemCursor"/>), Enter logs the face under it,
    /// Escape — also sent whenever the lease ends — drops the cursor. Returns whether the key was used.
    /// </summary>
    public bool HandleKey(WidgetKey key)
    {
        if (_detached)
        {
            return false;
        }

        if (key == WidgetKey.Enter)
        {
            if (_cursor is { } at)
            {
                OnLevelPicked(MoodRules.MinLevel + at);
                return true;
            }

            return false;
        }

        var next = ItemCursor.Move(_levels.Length, _cursor, key);
        var used = next != _cursor || key == WidgetKey.Escape;
        _cursor = next;
        Render();
        return used;
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        // Before anything is unhooked: the last chance today's mood and its thought have.
        CommitNote();
        Flush();
        _detached = true;

        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTick;
        }

        _note.GotFocus -= OnNoteGotFocus;
        _note.LostFocus -= OnNoteLostFocus;
        _note.KeyDown -= OnNoteKeyDown;
        _surface?.RequestTextInput(false);

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        foreach (var level in _levels)
        {
            level?.Detach();
        }

        ClearCells();
        _ticker = null;
    }
}
