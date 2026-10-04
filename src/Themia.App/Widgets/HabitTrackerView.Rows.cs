using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Shell;
using Themia.Core.Habits;
using Themia.Core.Widgets;
using Windows.System;
using Windows.UI.Core;

namespace Themia.App.Widgets;

/// <summary>
/// The rows of the Habits widget, and everything done to them in place: the box ([S-1421]), add,
/// rename, move and delete ([S-1425]), the item menu, and the keys ([S-1428]).
/// </summary>
internal sealed partial class HabitTrackerView
{
    private readonly List<HabitRow> _rows = [];
    private readonly Border _addRow;
    private IDisposable? _addHover;

    /// <summary>The open text box: renaming the habit with this id, or (null id) adding one.</summary>
    private (TextBox Box, string? HabitId)? _editor;

    private bool _rowFocused;
    private string? _focusId;
    private int _focusIndex;

    /// <summary>Whether a habit can be added or changed here: a list is chosen and the pool can be written.</summary>
    private bool Writable => _pool is { Writable: true } && _habitList is not null;

    /// <summary>
    /// Keep one row per habit, in list order. Rows are reused by id, so a tick, a rename or a period
    /// rollover re-letters what is there and keeps the row under the pointer; only a change to which
    /// habits there are, or their order, rebuilds.
    /// </summary>
    private void SyncRows(IReadOnlyList<Habit> habits)
    {
        var same = habits.Count == _rows.Count;

        for (var i = 0; same && i < habits.Count; i++)
        {
            same = habits[i].Id == _rows[i].Id;
        }

        if (same)
        {
            return;
        }

        // A rename open on a habit that has gone has nothing left to commit onto.
        if (_editor is { HabitId: { } editing } && habits.All(h => h.Id != editing))
        {
            CloseEditor();
        }

        var keep = _editor is { HabitId: { } open } ? open : null;
        var reuse = _rows.ToDictionary(r => r.Id, StringComparer.Ordinal);

        _rows.Clear();
        _list.Children.Clear();

        foreach (var habit in habits)
        {
            if (!reuse.Remove(habit.Id, out var row))
            {
                row = new HabitRow(this, habit.Id);
            }

            _rows.Add(row);
            _list.Children.Add(row.Host);
        }

        foreach (var gone in reuse.Values)
        {
            gone.Detach();
        }

        RestoreFocus(keep);
    }

    private HabitRow? RowFor(string id) => _rows.FirstOrDefault(r => r.Id == id);

    private Habit? HabitFor(string id) => Habits.FirstOrDefault(h => h.Id == id);

    // ---- changes: every one goes through the pool ---------------------------------------------------

    /// <summary>
    /// [S-1421] Tick or un-tick the period the box shows. The clock is read once, here, and the
    /// period it gives is both written and drawn — writing one period while drawing another left the
    /// original's box dead for a tick after every rollover.
    /// </summary>
    private void Toggle(string habitId)
    {
        if (!Writable || _habitList is not { } list)
        {
            return;
        }

        var key = Bucket(DateTimeOffset.Now).Key;
        _pool!.Update(list.Id, habits => HabitLists.ToggleHabit(habits, habitId, key));
    }

    /// <summary>[S-1425] Move a habit one place; the first can't go up, the last can't go down (a no-op writes nothing).</summary>
    private void Move(string habitId, int delta)
    {
        if (Writable && _habitList is { } list)
        {
            _focusId = habitId;
            _pool!.Update(list.Id, habits => HabitLists.MoveHabit(habits, habitId, delta));
        }
    }

    /// <summary>
    /// [S-1425] Delete a habit and its log, without a dialog: it sits behind a right-click or a
    /// focused row's Delete key, never a stray click. An editor open on it is cancelled first, so a
    /// pending rename cannot commit onto a habit that no longer exists.
    /// </summary>
    private void Delete(string habitId)
    {
        if (!Writable || _habitList is not { } list)
        {
            return;
        }

        if (_editor is { HabitId: { } editing } && editing == habitId)
        {
            CloseEditor();
        }

        var index = Habits.ToList().FindIndex(h => h.Id == habitId);
        _focusIndex = HabitLayout.AfterDelete(index, Habits.Count);
        _focusId = null;
        _pool!.Update(list.Id, habits => HabitLists.RemoveHabit(habits, habitId));
    }

    private void Rename(string habitId, string name)
    {
        if (Writable && _habitList is { } list)
        {
            _pool!.Update(list.Id, habits => HabitLists.RenameHabit(habits, habitId, name));
        }
    }

    private void Add(string name)
    {
        if (Writable && _habitList is { } list)
        {
            _pool!.Update(list.Id, habits => HabitLists.AddHabit(habits, name, HabitLists.NewId("habit")));
        }
    }

    /// <summary>[S-1425] A habit's own menu, ahead of the card's: tick, rename, move, delete.</summary>
    private IEnumerable<ItemMenuEntry> MenuFor(string habitId)
    {
        if (!Writable || HabitFor(habitId) is not { } habit)
        {
            return [];
        }

        var index = Habits.ToList().FindIndex(h => h.Id == habitId);
        var done = HabitRules.IsDone(HabitRules.Log(habit.Log), _current);
        var noun = HabitPeriods.CurrentNoun(_config.Cadence.Period);

        return
        [
            new(done ? Glyphs.Undo : Glyphs.Done, done ? $"Not done {noun}" : $"Done {noun}", () => Toggle(habitId)),
            new(Glyphs.Rename, "Rename", () => BeginRename(habitId)),
            ItemMenuEntry.Separator,
            new(Glyphs.MoveUp, "Move up", () => Move(habitId, -1), Disabled: index <= 0),
            new(Glyphs.MoveDown, "Move down", () => Move(habitId, +1), Disabled: index < 0 || index >= Habits.Count - 1),
            ItemMenuEntry.Separator,
            new(Glyphs.Delete, "Delete", () => Delete(habitId), Danger: true),
        ];
    }

    // ---- the keyboard ([S-1428]) --------------------------------------------------------------------

    /// <summary>
    /// Whether this widget wants the keyboard: an editor is open or a row is focused. The one place
    /// that asks the surface — when the shell grows its shared keyboard lease, only this changes; the
    /// row and editor handlers below already carry every key.
    /// </summary>
    private void UpdateKeyboard() => _surface?.RequestTextInput(!_detached && (_editor is not null || _rowFocused));

    private void FocusRow(HabitRow row)
    {
        _rowFocused = true;
        _focusId = row.Id;
        _focusIndex = Math.Max(0, _rows.IndexOf(row));
        UpdateKeyboard();
        row.Host.Focus(FocusState.Keyboard);
        row.Host.StartBringIntoView();
    }

    /// <summary>After a rebuild, the focus (or the editor) goes back to its habit, or to the row now in its place.</summary>
    private void RestoreFocus(string? editing)
    {
        if (editing is not null && RowFor(editing) is { } edited && _editor is { } editor)
        {
            edited.Adopt(editor.Box);
            return;
        }

        if (!_rowFocused || _editor is not null)
        {
            return;
        }

        if (_rows.Count == 0)
        {
            ReleaseRowFocus();
            return;
        }

        FocusRow((_focusId is null ? null : RowFor(_focusId)) ?? _rows[Math.Clamp(_focusIndex, 0, _rows.Count - 1)]);
    }

    private void ReleaseRowFocus()
    {
        _rowFocused = false;
        _focusId = null;
        UpdateKeyboard();
    }

    private void OnRowKey(HabitRow row, KeyRoutedEventArgs args)
    {
        if (_editor is not null || Key(args.Key) is not { } key)
        {
            return;
        }

        args.Handled = true;
        var decision = HabitLayout.Decide(key, _rows.IndexOf(row), _rows.Count);

        switch (decision.Command)
        {
            case HabitKeyCommand.Focus:
                FocusRow(_rows[decision.Index]);
                break;

            case HabitKeyCommand.Toggle:
                Toggle(row.Id);
                break;

            case HabitKeyCommand.Delete:
                Delete(row.Id);
                break;

            case HabitKeyCommand.Rename:
                BeginRename(row.Id);
                break;

            case HabitKeyCommand.Release:
                ReleaseRowFocus();
                break;
        }
    }

    private static HabitKey? Key(VirtualKey key) => key switch
    {
        VirtualKey.Up => HabitKey.Up,
        VirtualKey.Down => HabitKey.Down,
        VirtualKey.Home => HabitKey.Home,
        VirtualKey.End => HabitKey.End,
        VirtualKey.Tab => ShiftDown() ? HabitKey.ShiftTab : HabitKey.Tab,
        VirtualKey.Enter => HabitKey.Enter,
        VirtualKey.Delete => HabitKey.Delete,
        VirtualKey.F2 => HabitKey.Rename,
        VirtualKey.Escape => HabitKey.Escape,
        _ => null,
    };

    private static bool ShiftDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Leaving the list gives the keyboard back; moving between rows or into an editor is not leaving it.</summary>
    private void OnRowLostFocus()
    {
        if (!_rowFocused || XamlRoot is null)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (_rows.Any(r => ReferenceEquals(r.Host, focused)) || (_editor is { } editor && ReferenceEquals(focused, editor.Box)))
        {
            return;
        }

        ReleaseRowFocus();
    }

    // ---- the editors ([S-1425]) -----------------------------------------------------------------------

    /// <summary>
    /// Rename in place: the name becomes a text box holding it. Enter or leaving it commits, Escape
    /// cancels; a blank or unchanged name commits nothing.
    /// </summary>
    private void BeginRename(string habitId)
    {
        if (!Writable || RowFor(habitId) is not { } row || HabitFor(habitId) is not { } habit)
        {
            return;
        }

        CloseEditor();

        var box = NewEditor(habit.Name);
        row.Adopt(box);
        _editor = (box, habitId);
        StartTyping(box);
    }

    /// <summary>The add row at the foot of the list: a plus and a line of text, then a box.</summary>
    private Border BuildAddRow()
    {
        var plus = Fluent.Icon(Glyphs.Add, "TextFillColorSecondaryBrush");
        plus.Margin = new Thickness(Fluent.Space1 / 2, 0, Fluent.Space3 + (Fluent.Space1 / 2), 0);

        var label = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        label.Text = "Add a habit";
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        SetColumn(label, 1);
        body.Children.Add(plus);
        body.Children.Add(label);

        var row = Fluent.Row(body);
        row.Visibility = Visibility.Collapsed;
        row.Tapped += OnAddTapped;
        AutomationProperties.SetName(row, "Add a habit");
        _addHover = Fluent.Hover(row);
        return row;
    }

    private void OnAddTapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;
        OpenAddEditor();
    }

    private void OpenAddEditor()
    {
        if (!Writable || _editor is { HabitId: null } || _addRow.Child is not Grid body)
        {
            return;
        }

        CloseEditor();

        var box = NewEditor(string.Empty);
        box.PlaceholderText = "Habit name";
        SetColumn(box, 1);
        body.Children[1].Visibility = Visibility.Collapsed;
        body.Children.Add(box);
        _editor = (box, null);
        _addRow.Visibility = Visibility.Visible;
        StartTyping(box);
    }

    /// <summary>
    /// [S-1427] The add row shows where the plan has room for it; while it holds an open editor it
    /// stays whatever the plan says.
    /// </summary>
    private void FitAddRow(HabitLayoutPlan plan)
    {
        var show = plan.AddRow && Writable;
        _addRow.Visibility = show || _editor is { HabitId: null } ? Visibility.Visible : Visibility.Collapsed;
        _addRow.Padding = new Thickness(plan.Inset, 0, plan.Inset, 0);
        Fluent.SetDensity(_addRow, plan.CompactRows);
    }

    private TextBox NewEditor(string text)
    {
        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = false,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        box.KeyDown += OnEditorKeyDown;
        box.PreviewKeyDown += OnEditorPreviewKeyDown;
        box.LostFocus += OnEditorLostFocus;
        box.GotFocus += OnEditorGotFocus;
        return box;
    }

    /// <summary>
    /// Ask the surface for the keyboard first: outside edit mode it is unfocusable, so without this
    /// the caret would blink here while the letters went to whatever window had focus.
    /// </summary>
    private void StartTyping(TextBox box)
    {
        UpdateKeyboard();
        box.Focus(FocusState.Programmatic);
        box.SelectionStart = box.Text.Length;
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs args) => UpdateKeyboard();

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Enter:
                args.Handled = true;
                CommitEdit(keepAdding: true);
                break;

            case VirtualKey.Escape:
                args.Handled = true;
                CloseEditor();
                break;
        }
    }

    /// <summary>
    /// [S-1428] Tab, caught before focus navigation takes it: in a rename box it commits and opens the
    /// next habit's box, falling into the add row past the last; in the add box it commits and stays
    /// open for the next habit. Shift+Tab goes back, and from the add row commits and closes.
    /// </summary>
    private void OnEditorPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Tab || _editor is not { } editor)
        {
            return;
        }

        args.Handled = true;
        var backwards = ShiftDown();

        if (editor.HabitId is not { } editing)
        {
            CommitEdit(keepAdding: !backwards);
            return;
        }

        var index = _rows.FindIndex(r => r.Id == editing);
        var next = backwards ? index - 1 : index + 1;
        CommitEdit(keepAdding: false);

        if (_detached)
        {
            return;
        }

        if (next >= 0 && next < _rows.Count)
        {
            BeginRename(_rows[next].Id);
        }
        else if (!backwards)
        {
            OpenAddEditor();
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs args) => CommitEdit(keepAdding: false);

    /// <summary>
    /// Commit what the editor holds. Enter in the add row leaves it open for the next habit, so a list
    /// can be typed in one go; leaving the box commits and closes it.
    /// </summary>
    private void CommitEdit(bool keepAdding)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var text = editor.Box.Text;
        CloseEditor();

        if (editor.HabitId is { } habitId)
        {
            Rename(habitId, text);
            return;
        }

        Add(text);

        if (keepAdding && text.Trim().Length > 0 && !_detached)
        {
            OpenAddEditor();
        }
    }

    /// <summary>Take the editor down without committing it, and give the keyboard back.</summary>
    private void CloseEditor()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _editor = null;
        editor.Box.KeyDown -= OnEditorKeyDown;
        editor.Box.PreviewKeyDown -= OnEditorPreviewKeyDown;
        editor.Box.LostFocus -= OnEditorLostFocus;
        editor.Box.GotFocus -= OnEditorGotFocus;

        if (editor.Box.Parent is Panel parent)
        {
            parent.Children.Remove(editor.Box);

            foreach (var child in parent.Children)
            {
                child.Visibility = Visibility.Visible;
            }
        }

        if (editor.HabitId is { } habitId && RowFor(habitId) is { } row && _plan is { } plan)
        {
            // The row's optional parts come back as the ladder has them, not all at once.
            row.SetLayout(plan);
        }

        if (_plan is { } current)
        {
            FitAddRow(current);
        }

        UpdateKeyboard();
    }

    private void DetachAddRow()
    {
        _addRow.Tapped -= OnAddTapped;
        _addHover?.Dispose();
        _addHover = null;
    }

    /// <summary>
    /// One habit's row: a box, the name, the run, the history dots, and on a wide tile its rate and
    /// best run.
    /// <para>
    /// The box is the <b>only</b> pointer target that completes a habit ([S-1421]): a click on the
    /// name or the run never ticks anything, because a widget sits under the pointer's path across
    /// the whole desktop and has no undo. The box is drawn rather than a stock <c>CheckBox</c>, as
    /// Tasks draws its marker: the stock control's template, stripped of its 120px minimum to fit a
    /// row, clipped its own glyph to a dash in both states.
    /// </para>
    /// </summary>
    private sealed class HabitRow
    {
        private const double BoxSize = 20;

        private readonly HabitTrackerView _owner;
        private readonly Grid _grid;
        private readonly Border _box;
        private readonly FontIcon _tick;
        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly TextBlock _streak = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly StackPanel _history = new() { Orientation = Orientation.Horizontal, Spacing = HabitLayout.DotPitch - HabitLayout.DotSize };
        private readonly TextBlock _rate = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _best = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly List<Border> _dots = [];
        private readonly IDisposable _hover;
        private readonly IDisposable _menu;

        private readonly Brush _filled = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        // The empty dot must read as a slot on both themes; the alt fill is all but invisible on dark.
        private readonly Brush _blank = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);
        private readonly Brush _stroke = FluentLayout.Themed("ControlStrongStrokeColorDefaultBrush", Colors.Gray);

        private IReadOnlyList<HabitDot> _recent = [];
        private HabitPeriod _period = HabitPeriod.Daily;
        private int _streakCount;
        private bool _showStreak = true;
        private bool _done;

        public HabitRow(HabitTrackerView owner, string id)
        {
            _owner = owner;
            Id = id;

            _tick = new FontIcon
            {
                Glyph = Glyphs.Done,
                FontSize = 12,
                Foreground = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _box = new Border
            {
                Width = BoxSize,
                Height = BoxSize,
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Fluent.Space3, 0),
                Child = _tick,
            };

            _box.Tapped += OnBoxTapped;
            _box.DoubleTapped += OnBoxDoubleTapped;

            _name.VerticalAlignment = VerticalAlignment.Center;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.TextWrapping = TextWrapping.NoWrap;
            _name.DoubleTapped += OnNameDoubleTapped;

            foreach (var text in new[] { _streak, _rate, _best })
            {
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(HabitLayout.ColumnGap, 0, 0, 0);
                text.TextAlignment = TextAlignment.Right;
                Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            }

            _rate.MinWidth = HabitLayout.RateWidth;
            _best.MinWidth = HabitLayout.BestWidth;

            _history.VerticalAlignment = VerticalAlignment.Center;
            _history.Margin = new Thickness(HabitLayout.ColumnGap, 0, 0, 0);

            _grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var column = 0;
            foreach (var part in new FrameworkElement[] { _box, _name, _streak, _history, _rate, _best })
            {
                SetColumn(part, column++);
                _grid.Children.Add(part);
            }

            Host = Fluent.Row(_grid);

            // [S-1428] Focusable, so a clicked row can take the keys; the system focus rect shows which.
            Host.IsTabStop = true;
            Host.UseSystemFocusVisuals = true;
            Host.Tapped += OnRowTapped;
            Host.KeyDown += OnKeyDown;
            Host.PreviewKeyDown += OnPreviewKeyDown;
            Host.LostFocus += OnLostFocus;

            _hover = Fluent.Hover(Host);
            _menu = ItemMenu.Attach(Host, () => _owner.MenuFor(Id));
        }

        public string Id { get; }

        public Border Host { get; }

        public void Update(Habit habit, IReadOnlySet<string> log, HabitBucket current, HabitCadence cadence, int historyDots, bool showStreak)
        {
            var zone = TimeZoneInfo.Local;

            _name.Text = string.IsNullOrWhiteSpace(habit.Name) ? "Untitled habit" : habit.Name;
            ToolTipService.SetToolTip(_name, _name.Text);

            _done = HabitRules.IsDone(log, current);
            _box.Background = _done ? _filled : Fluent.RowRest();
            _box.BorderBrush = _stroke;
            _box.BorderThickness = new Thickness(_done ? 0 : 1);
            _tick.Visibility = _done ? Visibility.Visible : Visibility.Collapsed;

            var noun = HabitPeriods.CurrentNoun(cadence.Period);
            var state = _done ? $"done {noun}" : $"not done {noun}";
            AutomationProperties.SetName(_box, $"{_name.Text}, {state}");
            AutomationProperties.SetName(Host, $"{_name.Text}, {state}");
            ToolTipService.SetToolTip(_box, _done ? $"Mark as not done {noun}" : $"Mark as done {noun}");

            _period = cadence.Period;
            _showStreak = showStreak;
            _streakCount = HabitRules.Streak(log, current, cadence, zone);

            var runWords = _streakCount == 0 ? "No run yet" : $"{HabitPeriods.Units(_period, _streakCount)} in a row";
            AutomationProperties.SetName(_streak, runWords);
            ToolTipService.SetToolTip(_streak, runWords);

            _recent = HabitRules.Recent(log, current, cadence, zone, historyDots);
            DrawDots(cadence);

            var doneIn = HabitRules.DoneIn(log, current, cadence, zone, HabitRules.RateWindow);
            _rate.Text = string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(100.0 * doneIn / HabitRules.RateWindow)}%");
            var rateWords = $"Done {doneIn} of the last {HabitPeriods.Units(_period, HabitRules.RateWindow)}";
            ToolTipService.SetToolTip(_rate, rateWords);
            AutomationProperties.SetName(_rate, rateWords);

            var best = HabitRules.BestStreak(log, current, cadence, zone);
            _best.Text = string.Create(CultureInfo.InvariantCulture, $"best {best}");
            var bestWords = best == 0 ? "No best run yet" : $"Best run: {HabitPeriods.Units(_period, best)}";
            ToolTipService.SetToolTip(_best, bestWords);
            AutomationProperties.SetName(_best, bestWords);
        }

        /// <summary>
        /// [S-1426] All the configured dots exist; <see cref="SetLayout"/> shows the newest as many as
        /// fit. Each carries its period in the cadence's own words, and its state, because a filled dot
        /// and an empty one are otherwise the same shape.
        /// </summary>
        private void DrawDots(HabitCadence cadence)
        {
            if (_dots.Count != _recent.Count)
            {
                _dots.Clear();
                _history.Children.Clear();

                for (var i = 0; i < _recent.Count; i++)
                {
                    var dot = new Border
                    {
                        Width = HabitLayout.DotSize,
                        Height = HabitLayout.DotSize,
                        CornerRadius = new CornerRadius(HabitLayout.DotSize / 2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    _dots.Add(dot);
                    _history.Children.Add(dot);
                }
            }

            for (var i = 0; i < _recent.Count; i++)
            {
                var dot = _recent[i];
                _dots[i].Background = dot.Done ? _filled : _blank;

                var label = $"{HabitPeriods.Describe(dot.Bucket, cadence)}: {(dot.Done ? "done" : "not done")}";
                ToolTipService.SetToolTip(_dots[i], label);
                AutomationProperties.SetName(_dots[i], label);
            }
        }

        /// <summary>[S-1427] The parts this tile has room for. The oldest dots go first; the name never yields to them.</summary>
        public void SetLayout(HabitLayoutPlan plan)
        {
            Fluent.SetDensity(Host, plan.CompactRows);
            Host.Padding = new Thickness(plan.Inset, 0, plan.Inset, 0);

            if (_grid.Children.OfType<TextBox>().Any())
            {
                // Renaming: the box spans the trailing columns until it closes.
                return;
            }

            var count = _streakCount.ToString(CultureInfo.InvariantCulture);
            _streak.Text = _streakCount == 0 ? string.Empty : plan.StreakWords ? HabitPeriods.Units(_period, _streakCount) : count;
            _streak.Visibility = _showStreak && plan.Streak && _streakCount > 0 ? Visibility.Visible : Visibility.Collapsed;

            var shown = Math.Min(plan.Dots, _dots.Count);
            for (var i = 0; i < _dots.Count; i++)
            {
                _dots[i].Visibility = i >= _dots.Count - shown ? Visibility.Visible : Visibility.Collapsed;
            }

            _history.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rate.Visibility = plan.Rate ? Visibility.Visible : Visibility.Collapsed;
            _best.Visibility = plan.Best ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Put an editor where the name is; the trailing parts make room for it until it closes.</summary>
        public void Adopt(TextBox box)
        {
            if (box.Parent is Panel old && !ReferenceEquals(old, _grid))
            {
                old.Children.Remove(box);
            }

            SetColumn(box, 1);
            SetColumnSpan(box, 5);

            foreach (var child in _grid.Children)
            {
                if (GetColumn((FrameworkElement)child) >= 1)
                {
                    child.Visibility = Visibility.Collapsed;
                }
            }

            if (!_grid.Children.Contains(box))
            {
                _grid.Children.Add(box);
            }

            box.Visibility = Visibility.Visible;
        }

        private void OnBoxTapped(object sender, TappedRoutedEventArgs args)
        {
            args.Handled = true;
            _owner.Toggle(Id);
        }

        /// <summary>Two quick ticks are two ticks, not a rename.</summary>
        private static void OnBoxDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) => args.Handled = true;

        private void OnNameDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            args.Handled = true;
            _owner.BeginRename(Id);
        }

        /// <summary>[S-1428] A click on the row (not its box) gives the list the keyboard, focused here.</summary>
        private void OnRowTapped(object sender, TappedRoutedEventArgs args)
        {
            if (_owner._editor is null)
            {
                _owner.FocusRow(this);
            }
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != VirtualKey.Tab)
            {
                _owner.OnRowKey(this, args);
            }
        }

        /// <summary>Tab before focus navigation takes it: it moves between rows, wrapping.</summary>
        private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Tab && ReferenceEquals(args.OriginalSource, Host))
            {
                _owner.OnRowKey(this, args);
            }
        }

        private void OnLostFocus(object sender, RoutedEventArgs args) => _owner.OnRowLostFocus();

        /// <summary>Safe to call twice: every handle is idempotent.</summary>
        public void Detach()
        {
            _hover.Dispose();
            _menu.Dispose();
            _box.Tapped -= OnBoxTapped;
            _box.DoubleTapped -= OnBoxDoubleTapped;
            _name.DoubleTapped -= OnNameDoubleTapped;
            Host.Tapped -= OnRowTapped;
            Host.KeyDown -= OnKeyDown;
            Host.PreviewKeyDown -= OnPreviewKeyDown;
            Host.LostFocus -= OnLostFocus;
        }
    }
}
