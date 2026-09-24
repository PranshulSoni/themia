using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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
/// the whole day as a sentence in its tooltip and its accessible name. A colour-blind reader has the
/// bar and the digit; a screen-reader user has the sentence; nobody has to be able to tell a 40%
/// wash from a 60% one.
/// </para>
/// </summary>
internal sealed partial class MoodTrackerView : Grid, IWidgetView, IWidgetSurfaceAware
{
    private const string Scope = "mood";

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The opacity of the wash at each of the five levels, and zero for a day with nothing on it.
    /// <para>
    /// Applied to the square, never to the accent brush: that brush is the same object every other
    /// accent surface in the app paints with, and setting an opacity on it would dim the lot. The
    /// bottom of the ramp is 0.22 rather than something fainter because below about a fifth the
    /// wash is indistinguishable from the empty square next to it, which is the one distinction
    /// this scale has to keep.
    /// </para>
    /// </summary>
    private static readonly double[] Wash = [0, 0.22, 0.40, 0.58, 0.78, 1.0];

    /// <summary>The smallest square still worth drawing; below it the grid stops adding rows.</summary>
    private const double MinCell = 9;

    /// <summary>
    /// And the largest. A heatmap is read as a texture, and a fortnight of squares the size of
    /// buttons is not one — past this the card holds more weeks instead of bigger days.
    /// </summary>
    private const double MaxCell = 28;

    /// <summary>At or above this a square has room for its level as a digit.</summary>
    private const double DigitCell = 18;

    /// <summary>Below this height the thought box and the footer go; the heatmap is the widget.</summary>
    private const double NoteHidesBelow = 200;

    private const double StatsHideBelow = 160;

    private readonly Grid _picker = new();
    private readonly LevelButton[] _levels = new LevelButton[MoodRules.MaxLevel];
    private readonly Border _mapHost;
    private readonly StackPanel _map = new() { Spacing = Fluent.Space1 };
    private readonly Grid _headers = new();
    private readonly Grid _cells = new();
    private readonly List<DayCell> _dayCells = [];
    private readonly TextBox _note = new();
    private readonly TextBlock _stats = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly DispatcherQueueTimer? _saveTimer;

    // Looked up once. A grid of three hundred squares re-resolving two resource keys each on every
    // redraw is six hundred dictionary hits for two brushes.
    private readonly Brush _accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _emptyFill = FluentLayout.Themed("ControlAltFillColorSecondaryBrush", Colors.Gray);
    private readonly Brush _onAccent = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White);
    private readonly Brush _onCard = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _ring = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _noRing = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray);

    private IWidgetSurface? _surface;
    private Ticker? _ticker;
    private MoodTrackerConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private LocalDay _today;
    private MoodGrid? _grid;
    private double _cellSize;
    private bool _digits;
    private bool _dirty;
    private bool _applyingNote;
    private bool _detached;

    public MoodTrackerView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        BuildPicker();
        SetRow(_picker, 0);

        _headers.HorizontalAlignment = HorizontalAlignment.Center;
        _cells.HorizontalAlignment = HorizontalAlignment.Center;
        _cells.RowSpacing = Fluent.Space1;
        _cells.ColumnSpacing = Fluent.Space1;
        _headers.ColumnSpacing = Fluent.Space1;

        _map.Children.Add(_headers);
        _map.Children.Add(_cells);
        _map.HorizontalAlignment = HorizontalAlignment.Center;
        _map.VerticalAlignment = VerticalAlignment.Center;

        _mapHost = new Border
        {
            Child = _map,
            Padding = WidgetLayout.SideInset,
        };

        SetRow(_mapHost, 1);

        BuildNote();
        SetRow(_note, 2);

        _stats.Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space4);
        _stats.TextAlignment = TextAlignment.Center;
        SetRow(_stats, 3);

        Children.Add(_picker);
        Children.Add(_mapHost);
        Children.Add(_note);
        Children.Add(_stats);

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

        SizeChanged += OnSizeChanged;

        // The map's own size, as well as this view's. The star row's height is only settled once
        // the children have been arranged, and reading it during the parent's own size-changed can
        // still see the previous pass — which would lay the squares out for the old card and leave
        // them there until something else moved.
        _mapHost.SizeChanged += OnMapResized;
    }

    /// <summary>
    /// The five points of the scale, as five buttons that use the same wash the heatmap does — so
    /// the row of buttons is also the legend, and the reader never has to learn the scale twice.
    /// Each carries its number and, in words, its name.
    /// </summary>
    private void BuildPicker()
    {
        _picker.ColumnSpacing = Fluent.Space1;
        _picker.Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space2);

        for (var level = MoodRules.MinLevel; level <= MoodRules.MaxLevel; level++)
        {
            _picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var button = new LevelButton(level, _accent, _emptyFill, _onAccent, _onCard, _ring, _noRing, Wash[level], OnLevelPicked);
            _levels[level - MoodRules.MinLevel] = button;

            Grid.SetColumn(button.Host, level - MoodRules.MinLevel);
            _picker.Children.Add(button.Host);
        }
    }

    private void BuildNote()
    {
        // The same chrome-free field the note widget uses: no border at rest so the thought reads as
        // writing on the card, the stock subtle fill under the pointer, and the stock accent line
        // when it has focus so there is something on screen saying where the caret went.
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
        _note.Margin = new Thickness(Fluent.Space3, 0, Fluent.Space3, Fluent.Space1);
        _note.Padding = new Thickness(Fluent.Space1);

        _note.GotFocus += OnNoteGotFocus;
        _note.LostFocus += OnNoteLostFocus;
        _note.KeyDown += OnNoteKeyDown;
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    /// <summary>
    /// Re-read the config. As with the habits there is no unflushed edit to defend: a tap and a
    /// committed thought are written into the config immediately and only the <i>save</i> is
    /// deferred, so an incoming config cannot be carrying an older version of either. A genuinely
    /// different bag is adopted after flushing.
    /// </summary>
    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as MoodTrackerConfig ?? new MoodTrackerConfig();

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
    /// The only reason this widget wakes: the window has to roll over at midnight without being
    /// touched. Nothing is redrawn unless the calendar day actually changed.
    /// </summary>
    private void OnTick()
    {
        if (_detached)
        {
            return;
        }

        if (LocalDay.From(DateTimeOffset.Now) == _today)
        {
            return;
        }

        Render();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Render();

    private void OnMapResized(object sender, SizeChangedEventArgs args) => Render();

    /// <summary>
    /// Which of the parts around the heatmap this card has room for, and which the settings asked
    /// for. Applied on every render rather than only on a resize, so turning the thought box off
    /// takes effect when the switch moves rather than when the widget is next dragged.
    /// <para>
    /// The thought box and the footer are the first things to go on a small card: the heatmap is
    /// what the widget is <i>for</i>, and squeezing three more controls around it leaves none of
    /// them legible.
    /// </para>
    /// </summary>
    private void ApplyChrome()
    {
        var height = ActualHeight;

        _note.Visibility = _config.ShowNote && height >= NoteHidesBelow ? Visibility.Visible : Visibility.Collapsed;
        _stats.Visibility = _config.ShowStats && height >= StatsHideBelow ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Draw everything. The clock is read <b>once</b> here and passed down, so the picker, the
    /// heatmap and the footer cannot end up disagreeing about which day is today in a frame that
    /// straddles midnight.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _today = LocalDay.From(DateTimeOffset.Now);

        ApplyChrome();

        var entries = MoodRules.Sanitise(_config.Entries);
        var byDay = MoodRules.ByDay(entries);
        var latest = MoodRules.LatestOn(entries, _today);

        DrawPicker(latest?.Level ?? 0);
        DrawMap(byDay);
        DrawNote(latest);
        DrawStats(entries, byDay);
    }

    private void DrawPicker(int selected)
    {
        foreach (var level in _levels)
        {
            level.SetSelected(level.Level == selected);
        }
    }

    /// <summary>
    /// Lay the squares out for this card, then colour them.
    /// <para>
    /// The grid is only rebuilt when its <i>shape</i> changed — the number of rows, the size of a
    /// square, whether the digits fit. Everything else re-letters what is already drawn, because
    /// rebuilding three hundred borders on every tick is the kind of thing that makes a desktop of
    /// widgets feel heavy.
    /// </para>
    /// </summary>
    private void DrawMap(IReadOnlyDictionary<LocalDay, MoodDay> byDay)
    {
        var width = _mapHost.ActualWidth - (WidgetLayout.ContentInset * 2);
        var height = _mapHost.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return; // Not measured yet; the size-changed handler brings us straight back.
        }

        var gap = Fluent.Space1;
        var byWidth = (width - (gap * (MoodGrid.Columns - 1))) / MoodGrid.Columns;
        var cell = Math.Clamp(byWidth, MinCell, MaxCell);
        var digits = cell >= DigitCell;

        // The header row costs one square's worth of height plus a gap, and is dropped when the
        // squares are too small to sit under a two-letter label legibly.
        var headerHeight = digits ? FluentType.Caption.LineHeight + gap : 0;
        var rowsThatFit = (int)Math.Floor((height - headerHeight + gap) / (cell + gap));

        var grid = _config.Span == MoodSpan.Month
            ? MoodGrid.Month(_today.Year, _today.Month, _config.FirstDayOfWeek)
            : MoodGrid.Rolling(_today, Math.Clamp(rowsThatFit, 1, _config.EffectiveHistoryWeeks), _config.FirstDayOfWeek);

        if (_config.Span == MoodSpan.Month)
        {
            // A month is a fixed number of rows, so it is the square that gives instead: the whole
            // month is always shown, at whatever size fits.
            var byHeight = (height - headerHeight - (gap * (grid.Rows.Count - 1))) / grid.Rows.Count;
            cell = Math.Clamp(Math.Min(byWidth, byHeight), MinCell, MaxCell);
            digits = cell >= DigitCell;
        }

        var shapeChanged = _grid is null
            || _grid.Rows.Count != grid.Rows.Count
            || _grid.WeekStart != grid.WeekStart
            || Math.Abs(_cellSize - cell) > 0.5
            || _digits != digits;

        _grid = grid;
        _cellSize = cell;
        _digits = digits;

        if (shapeChanged)
        {
            BuildCells(grid, cell, digits);
        }

        for (var i = 0; i < _dayCells.Count; i++)
        {
            var row = i / MoodGrid.Columns;
            var column = i % MoodGrid.Columns;
            var day = grid.Rows[row][column];

            if (day is null)
            {
                _dayCells[i].ShowBlank();
                continue;
            }

            _dayCells[i].Show(MoodRules.Read(byDay, day.Value), day.Value == _today);
        }
    }

    private void BuildCells(MoodGrid grid, double cell, bool digits)
    {
        _dayCells.Clear();
        _cells.Children.Clear();
        _cells.RowDefinitions.Clear();
        _cells.ColumnDefinitions.Clear();
        _headers.Children.Clear();
        _headers.ColumnDefinitions.Clear();

        for (var column = 0; column < MoodGrid.Columns; column++)
        {
            _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
            _headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        }

        _headers.Visibility = digits ? Visibility.Visible : Visibility.Collapsed;

        if (digits)
        {
            var labels = grid.Headers;

            for (var column = 0; column < MoodGrid.Columns; column++)
            {
                var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                label.Text = labels[column];
                label.TextAlignment = TextAlignment.Center;
                Grid.SetColumn(label, column);
                _headers.Children.Add(label);
            }
        }

        for (var row = 0; row < grid.Rows.Count; row++)
        {
            _cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });

            for (var column = 0; column < MoodGrid.Columns; column++)
            {
                var dayCell = new DayCell(cell, digits, _accent, _emptyFill, _onAccent, _onCard, _ring);
                _dayCells.Add(dayCell);

                Grid.SetRow(dayCell.Host, row);
                Grid.SetColumn(dayCell.Host, column);
                _cells.Children.Add(dayCell.Host);
            }
        }
    }

    private void DrawNote(MoodEntry? latest)
    {
        // Disabled until something is logged today, with a placeholder that says why rather than
        // one that invites typing into a box that has nothing to attach the words to.
        _note.IsEnabled = latest is not null;
        _note.PlaceholderText = latest is null
            ? "Log how you feel to add a thought"
            : "What was behind it?";

        var text = latest?.Note ?? string.Empty;

        // Never while it is being typed in: the box is the live copy until it commits, and writing
        // the config's value over it mid-sentence would fight the caret.
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
            // Nothing in the window at all: no average, rather than a misleading zero.
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

    /// <summary>
    /// A level was tapped. The instant is read once here and stored with its offset, so the entry
    /// keeps the day it was actually logged on whatever the machine's zone does afterwards.
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

        // The log lives inside the state file that every widget drag rewrites, so it is bounded at
        // the moment it grows rather than at some sweep later.
        MoodRules.Prune(_config.Entries, MoodTrackerConfig.MaxEntries);

        _dirty = true;
        _saveTimer?.Start();
        Render();
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
    /// Write the thought onto today's most recent entry.
    /// <para>
    /// On commit, not per keystroke: the box's value is the live copy while it has focus, and a
    /// write per character would both fight the caret and rewrite the state file on every letter.
    /// </para>
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
        _dirty = true;
        _saveTimer?.Start();
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

        SizeChanged -= OnSizeChanged;
        _mapHost.SizeChanged -= OnMapResized;

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

        foreach (var cell in _dayCells)
        {
            cell.Detach();
        }

        _dayCells.Clear();
        _cells.Children.Clear();
        _ticker = null;
    }

    /// <summary>
    /// One of the five points of the scale: a swatch from the same ramp the heatmap uses, the level
    /// as a digit, and the level's name in words for the tooltip and the screen reader.
    /// </summary>
    private sealed class LevelButton
    {
        private readonly Action<int> _picked;
        private readonly Brush _ring;
        private readonly Brush _noRing;

        public LevelButton(
            int level,
            Brush accent,
            Brush emptyFill,
            Brush onAccent,
            Brush onCard,
            Brush ring,
            Brush noRing,
            double wash,
            Action<int> picked)
        {
            Level = level;
            _picked = picked;
            _ring = ring;
            _noRing = noRing;

            var fill = new Rectangle
            {
                Fill = accent,

                // The opacity is on the shape. The accent brush is shared with every other accent
                // surface in the app, and dimming it here would dim all of them.
                Opacity = wash,
            };

            var digit = Fluent.Text(FluentType.Caption);
            digit.Text = level.ToString(CultureInfo.InvariantCulture);
            digit.Foreground = wash >= 0.5 ? onAccent : onCard;
            digit.HorizontalAlignment = HorizontalAlignment.Center;
            digit.VerticalAlignment = VerticalAlignment.Center;

            var face = new Grid { Children = { fill, digit } };

            Host = new Button
            {
                Content = face,
                Background = emptyFill,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = Fluent.Space6,
                Height = Fluent.Space6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                BorderThickness = new Thickness(Fluent.Hairline),
                BorderBrush = noRing,
            };

            var name = $"{MoodRules.LevelName(level)}, {level} of {MoodRules.MaxLevel}";
            AutomationProperties.SetName(Host, name);
            ToolTipService.SetToolTip(Host, name);

            Host.Click += OnClick;
        }

        public int Level { get; }

        public Button Host { get; }

        /// <summary>
        /// Which level today already holds. Marked with a thicker accent outline rather than with a
        /// different fill, because the fill is the scale and re-using it would say something about
        /// the value instead of about the selection.
        /// </summary>
        public void SetSelected(bool selected)
        {
            Host.BorderBrush = selected ? _ring : _noRing;
            Host.BorderThickness = new Thickness(selected ? Fluent.Space1 / 2 : Fluent.Hairline);
            AutomationProperties.SetItemStatus(Host, selected ? "Logged today" : string.Empty);
        }

        private void OnClick(object sender, RoutedEventArgs args) => _picked(Level);

        public void Detach() => Host.Click -= OnClick;
    }

    /// <summary>
    /// One square of the heatmap: a day, and every channel it has to say its value in.
    /// <para>
    /// The wash is the fast one, and it is the only one that depends on being able to tell two
    /// colours apart. Under it sits a bar whose <b>height</b> is the level, which survives any
    /// colour vision and any theme; over it a digit when the square is big enough; and the whole day
    /// is spelled out in the tooltip and the accessible name at every size.
    /// </para>
    /// </summary>
    private sealed class DayCell
    {
        private readonly Rectangle _wash;
        private readonly Rectangle _bar;
        private readonly TextBlock? _digit;
        private readonly Brush _onAccent;
        private readonly Brush _onCard;
        private readonly Brush _ring;
        private readonly double _size;
        private readonly IDisposable? _hover;

        public DayCell(double size, bool digits, Brush accent, Brush emptyFill, Brush onAccent, Brush onCard, Brush ring)
        {
            _size = size;
            _onAccent = onAccent;
            _onCard = onCard;
            _ring = ring;

            _wash = new Rectangle { Fill = accent };

            _bar = new Rectangle
            {
                Fill = accent,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 0,
            };

            var face = new Grid();
            face.Children.Add(_wash);
            face.Children.Add(_bar);

            if (digits)
            {
                _digit = Fluent.Text(FluentType.Caption);
                _digit.HorizontalAlignment = HorizontalAlignment.Center;
                _digit.VerticalAlignment = VerticalAlignment.Center;
                face.Children.Add(_digit);
            }

            Host = new Border
            {
                Child = face,
                Width = size,
                Height = size,
                Background = emptyFill,
                CornerRadius = new CornerRadius(Fluent.ControlRadius / 2),
                BorderThickness = new Thickness(0),
            };

            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        /// <summary>A square outside the window: a gap, so the weekday columns still line up.</summary>
        public void ShowBlank()
        {
            Host.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(Host, null);
            AutomationProperties.SetName(Host, string.Empty);
        }

        public void Show(MoodDay day, bool isToday)
        {
            Host.Visibility = Visibility.Visible;

            var bucket = Math.Clamp(day.Bucket, 0, MoodRules.MaxLevel);

            _wash.Opacity = Wash[bucket];
            _bar.Height = _size * bucket / MoodRules.MaxLevel;
            _bar.Opacity = bucket == 0 ? 0 : 1;

            if (_digit is not null)
            {
                _digit.Text = bucket == 0 ? string.Empty : bucket.ToString(CultureInfo.InvariantCulture);
                _digit.Foreground = bucket >= 3 ? _onAccent : _onCard;
            }

            // Today is outlined rather than tinted: tinting it would be a sixth value on a
            // five-value scale.
            Host.BorderBrush = isToday ? _ring : null;
            Host.BorderThickness = new Thickness(isToday ? Fluent.Hairline : 0);

            var described = MoodRules.Describe(day);
            var label = isToday ? $"Today. {described}" : described;

            ToolTipService.SetToolTip(Host, label);
            AutomationProperties.SetName(Host, label);
        }

        public void Detach()
        {
            _hover?.Dispose();
            Host.Child = null;
        }
    }
}
