using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Tasks;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Widgets;

/// <summary>The rows, and everything the user does to one.</summary>
internal sealed partial class TasksView
{
    /// <summary>Under this width the due date goes under the title whatever the layout: a column would starve it.</summary>
    private const double NarrowBelow = 240;

    private readonly Border _addRow;

    // One per row, so the handlers come off with the row they belong to. A leaked one keeps the row
    // alive and re-lights it after the list has moved on.
    private readonly List<IDisposable> _hovers = [];
    private readonly List<(Border Row, Border Marker)> _rows = [];

    /// <summary>The rows as drawn, in order: what "move up" and a new task's place are measured against.</summary>
    private IReadOnlyList<TaskItem> _displayed = [];

    /// <summary>The row height and due-date placement the rows on screen were drawn for.</summary>
    private (bool Compact, bool Narrow)? _drawnDensity;

    private (bool Compact, bool Narrow) Density() => (Compact, ActualWidth > 0 && ActualWidth < NarrowBelow);

    /// <summary>The open editor, if any: renaming a task (its id) or adding one (null id).</summary>
    private (TextBox Box, string? TaskId)? _editor;

    private void BuildRows(IReadOnlyList<TaskItem> rows, DateOnly today)
    {
        // A rebuild while the user types would throw their words away; the editor's own commit or
        // cancel redraws when it is done.
        if (_editor is { TaskId: { } editing })
        {
            if (rows.Any(r => r.Id == editing))
            {
                return;
            }

            // The task being renamed has gone (deleted elsewhere): its editor goes with it.
            CancelEdit();
        }

        ClearRows();

        var (compact, narrow) = Density();
        _drawnDensity = (compact, narrow);

        foreach (var task in rows)
        {
            var row = BuildRow(task, today, compact, narrow);
            _list.Children.Add(row);
        }
    }

    /// <summary>
    /// One task: the state marker, the title wrapping to the configured number of lines ([S-601]),
    /// and the due date under it or beside it ([S-602]). A fixed Fluent height at minimum, taller
    /// only when the title wraps.
    /// </summary>
    private Border BuildRow(TaskItem task, DateOnly today, bool compact, bool narrow)
    {
        var markerSize = compact ? Fluent.IconSize : Fluent.Space5;
        var marker = BuildMarker(task, markerSize);

        // Weight marks an important task the way an unread message is marked: no extra glyph to
        // learn, and both steps are the same 14/20 line, so nothing moves.
        var title = Fluent.Text(
            task.Important && !task.Completed ? FluentType.BodyStrong : FluentType.Body,
            task.Completed ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");

        title.Text = task.DisplayTitle;
        title.TextWrapping = TextWrapping.WrapWholeWords;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxLines = compact ? Math.Min(2, _config.EffectiveTitleLines) : _config.EffectiveTitleLines;

        // [S-186] Done is struck through, not merely dimmed: the one mark that says "done" without
        // a legend and without relying on colour.
        title.TextDecorations = task.Completed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(title);

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
        Grid.SetColumn(content, 1);
        body.Children.Add(marker);
        body.Children.Add(content);

        var due = TaskDue.Resolve(task.DueRaw, today, TimeZoneInfo.Local);

        if (_config.ShowDueDate && due is { } resolved)
        {
            // [S-186] Overdue says so in words as well as in the critical colour, and only for open
            // work: a finished task is not late.
            var overdue = resolved.IsPast && !task.Completed;
            var when = Fluent.Text(FluentType.Caption, overdue ? "SystemFillColorCriticalBrush" : "TextFillColorTertiaryBrush");
            when.Text = resolved.IsPast && task.Completed ? TaskDue.ShortDate(resolved.Day) : TaskDue.Text(resolved);

            if (_config.ViewMode == TaskViewMode.Compact && !narrow)
            {
                when.VerticalAlignment = VerticalAlignment.Center;
                when.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
                Grid.SetColumn(when, 2);
                body.Children.Add(when);
            }
            else
            {
                content.Children.Add(when);
            }
        }

        var row = Fluent.Row(body, compact);

        // The title may wrap; a little air above and below keeps two wrapped rows apart without a rule.
        row.Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space1);
        row.Tag = task;
        _hovers.Add(Fluent.Hover(row));
        _rows.Add((row, marker));

        row.DoubleTapped += OnRowDoubleTapped;
        row.RightTapped += OnRowRightTapped;

        ToolTipService.SetToolTip(row, Tooltip(task, due));
        return row;
    }

    /// <summary>
    /// [S-597] The state marker, which is also the one control that completes a task: an empty ring
    /// for open work, a filled tick for finished. Drawn rather than taken from the icon font, because
    /// there is no hollow circle in it, and sized from the row height, never from the tile.
    /// </summary>
    private Border BuildMarker(TaskItem task, double size)
    {
        var marker = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space3, 0),
            BorderBrush = FluentLayout.Themed("ControlStrongStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(task.Completed ? 0 : 1),
            Background = task.Completed
                ? FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue)
                : Fluent.RowRest(),
            Tag = task,
        };

        if (task.Completed)
        {
            marker.Child = new FontIcon
            {
                Glyph = Glyphs.Done,
                FontSize = Math.Round(size * 0.6),
                Foreground = FluentLayout.Themed("TextOnAccentFillColorPrimaryBrush", Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        ToolTipService.SetToolTip(marker, task.Completed ? "Mark as not done" : "Mark as done");
        marker.Tapped += OnMarkerTapped;
        marker.DoubleTapped += OnMarkerDoubleTapped;
        return marker;
    }

    private static string Tooltip(TaskItem task, Due? due)
    {
        var lines = new List<string> { task.DisplayTitle, task.Completed ? "Completed" : "Open" };

        if (due is { } resolved)
        {
            lines.Add($"Due {TaskDue.LongText(resolved)}{(resolved.IsPast && !task.Completed ? " (overdue)" : string.Empty)}");
        }

        if (task.Important)
        {
            lines.Add("High priority");
        }

        lines.Add("Double-click to rename; right-click for more");
        return string.Join(Environment.NewLine, lines);
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        foreach (var (row, marker) in _rows)
        {
            row.DoubleTapped -= OnRowDoubleTapped;
            row.RightTapped -= OnRowRightTapped;
            marker.Tapped -= OnMarkerTapped;
            marker.DoubleTapped -= OnMarkerDoubleTapped;
        }

        _hovers.Clear();
        _rows.Clear();
        _list.Children.Clear();
    }

    // ---- what a click does ---------------------------------------------------------------------------

    private void OnMarkerTapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;

        if (sender is FrameworkElement { Tag: TaskItem task })
        {
            SetCompleted(task, !task.Completed);
        }
    }

    /// <summary>Two quick ticks are two ticks, not a rename: the row must not see them as a double-click.</summary>
    private static void OnMarkerDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) => args.Handled = true;

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is Border { Tag: TaskItem task } row)
        {
            args.Handled = true;
            BeginRename(row, task);
        }
    }

    /// <summary>[S-597]/[S-598]/[S-600] A task's own menu, ahead of the card's.</summary>
    private void OnRowRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not Border { Tag: TaskItem task } row)
        {
            return;
        }

        var menu = new MenuFlyout();
        var index = IndexOf(task.Id);

        menu.Items.Add(MenuItem(task.Completed ? Glyphs.Undo : Glyphs.Done, task.Completed ? "Mark as not done" : "Mark as done",
            () => SetCompleted(task, !task.Completed)));
        menu.Items.Add(MenuItem(Glyphs.Rename, "Rename", () => BeginRename(row, task)));
        menu.Items.Add(new MenuFlyoutSeparator());

        var up = MenuItem(Glyphs.MoveUp, "Move up", () => Move(task, -1));
        up.IsEnabled = index > 0;
        menu.Items.Add(up);

        var down = MenuItem(Glyphs.MoveDown, "Move down", () => Move(task, +1));
        down.IsEnabled = index >= 0 && index < _displayed.Count - 1;
        menu.Items.Add(down);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Glyphs.Delete, "Delete", () => Delete(task)));

        menu.ShowAt(row, args.GetPosition(row));
        args.Handled = true;
    }

    private static MenuFlyoutItem MenuItem(string glyph, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < _displayed.Count; i++)
        {
            if (_displayed[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- changes ---------------------------------------------------------------------------------------

    private void SetCompleted(TaskItem task, bool completed)
    {
        if (_service is null)
        {
            return;
        }

        if (IsLocal)
        {
            if (_localListId is { } listId)
            {
                _service.Local.Update(listId, tasks => LocalTaskLists.SetDone(tasks, task.Id, completed));
            }

            return;
        }

        if (_feed is { } feed)
        {
            _ = _service.SetCompletedAsync(feed, task, completed);
        }
    }

    private void Rename(TaskItem task, string title)
    {
        if (_service is null)
        {
            return;
        }

        if (IsLocal)
        {
            if (_localListId is { } listId)
            {
                _service.Local.Update(listId, tasks => LocalTaskLists.RenameTask(tasks, task.Id, title));
            }

            return;
        }

        if (_feed is { } feed)
        {
            _ = _service.RenameAsync(feed, task, title);
        }
    }

    private void Delete(TaskItem task)
    {
        if (_service is null)
        {
            return;
        }

        if (IsLocal)
        {
            if (_localListId is { } listId)
            {
                _service.Local.Update(listId, tasks => LocalTaskLists.RemoveTask(tasks, task.Id));
            }

            return;
        }

        if (_feed is { } feed)
        {
            _ = _service.DeleteAsync(feed, task);
        }
    }

    /// <summary>
    /// [S-598] Move a task one place among the rows shown. A list on this PC stores its order in the
    /// list; an account list's order lives in this widget's config, because no provider keeps one.
    /// </summary>
    private void Move(TaskItem task, int step)
    {
        var shown = _displayed.Select(t => t.Id).ToList();
        var moved = TaskRules.Nudge(shown, task.Id, step);

        if (ReferenceEquals(moved, shown) || _service is null)
        {
            return;
        }

        if (IsLocal)
        {
            if (_localListId is { } listId)
            {
                _service.Local.Update(listId, tasks => LocalTaskLists.Reorder(tasks, moved));
            }

            return;
        }

        _config.TaskOrder = [.. moved];
        _surface?.RequestSave();
        Render();
    }

    private async Task AddAsync(string title)
    {
        if (_service is null || title.Trim().Length == 0)
        {
            return;
        }

        if (IsLocal)
        {
            if (_localListId is { } listId)
            {
                _service.Local.Update(listId, tasks => LocalTaskLists.AddTask(tasks, title, $"local-{Guid.NewGuid():n}"));
            }

            return;
        }

        if (_feed is not { } feed)
        {
            return;
        }

        var created = await _service.CreateAsync(feed, title).ConfigureAwait(true);

        if (created is null || _detached || !ReferenceEquals(feed, _feed))
        {
            return;
        }

        // [S-599] Pin the arrangement on screen with the new task last. Without this the next
        // refresh re-imposes the provider's own sort — Graph lists most recently changed first —
        // which is how a task just added kept jumping to the top. Rebuilt from the rows shown, so
        // ids of deleted tasks prune themselves rather than accumulate.
        var order = _displayed.Select(t => t.Id).Where(id => id != created.Id).ToList();
        order.Add(created.Id);
        _config.TaskOrder = order;
        _surface?.RequestSave();
        Render();
    }

    // ---- the editors -----------------------------------------------------------------------------------

    /// <summary>
    /// [S-600] Rename in place: the title becomes a text box holding the whole title. Enter or
    /// leaving it commits, Escape cancels; a blank or unchanged title commits nothing.
    /// </summary>
    private void BeginRename(Border row, TaskItem task)
    {
        if (_feed?.Edits.IsUnconfirmedCreate(task.Id) == true || row.Child is not Grid body)
        {
            return;
        }

        CancelEdit();

        var box = NewEditor(task.Title);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);

        // The title's column takes the box; the rest of the row stays as it was.
        foreach (var child in body.Children)
        {
            if (Grid.GetColumn((FrameworkElement)child) >= 1)
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        body.Children.Add(box);
        _editor = (box, task.Id);
        StartTyping(box);
    }

    /// <summary>[S-599] The add row at the foot of the list: a plus and a line of text, then a box.</summary>
    private Border BuildAddRow()
    {
        var plus = Fluent.Icon(Glyphs.Add, "TextFillColorSecondaryBrush");
        plus.Margin = new Thickness(Fluent.Space1 / 2, 0, Fluent.Space3 + (Fluent.Space1 / 2), 0);

        var label = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        label.Text = "Add a task";
        label.VerticalAlignment = VerticalAlignment.Center;

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        Grid.SetColumn(label, 1);
        body.Children.Add(plus);
        body.Children.Add(label);

        var row = Fluent.Row(body);
        row.Visibility = Visibility.Collapsed;
        row.Tapped += OnAddTapped;
        _addHover = Fluent.Hover(row);
        return row;
    }

    private IDisposable? _addHover;

    private void OnAddTapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;
        OpenAddEditor();
    }

    private void OpenAddEditor()
    {
        if (_editor is { TaskId: null } || _addRow.Child is not Grid body)
        {
            return;
        }

        CancelEdit();

        var box = NewEditor(string.Empty);
        box.PlaceholderText = "Task name";
        Grid.SetColumn(box, 1);
        body.Children[1].Visibility = Visibility.Collapsed;
        body.Children.Add(box);
        _editor = (box, null);
        StartTyping(box);
    }

    /// <summary>
    /// The add row shows only when there is a list to add to and room for it: on a small tile every
    /// row is worth more as a task.
    /// </summary>
    private void FitAddRow()
    {
        var room = ActualHeight <= 0 || ActualHeight >= AddRowNeedsHeight;
        var writable = IsLocal ? _service?.Local.Writable == true && _localListId is not null : _feed is { Loaded: true };
        var show = room && _screen == Screen.Ready && writable;

        _addRow.Visibility = show || _editor is { TaskId: null } ? Visibility.Visible : Visibility.Collapsed;
        Fluent.SetDensity(_addRow, Compact);
    }

    private TextBox NewEditor(string text)
    {
        var box = new TextBox
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        box.KeyDown += OnEditorKeyDown;
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
        _surface?.RequestTextInput(true);
        box.Focus(FocusState.Programmatic);
        box.SelectionStart = box.Text.Length;
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

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
                CancelEdit();
                Render();
                break;
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs args) => CommitEdit(keepAdding: false);

    /// <summary>
    /// Commit what the editor holds. Enter in the add row leaves it open for the next task, so a list
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

        if (editor.TaskId is { } taskId)
        {
            if (_displayed.FirstOrDefault(t => t.Id == taskId) is { } task)
            {
                Rename(task, text);
            }

            Render();
            return;
        }

        _ = AddAsync(text);
        Render();

        if (keepAdding && text.Trim().Length > 0 && !_detached)
        {
            OpenAddEditor();
        }
    }

    private void CancelEdit() => CloseEditor();

    /// <summary>Take the editor down without committing it, and give the keyboard back.</summary>
    private void CloseEditor()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _editor = null;
        editor.Box.KeyDown -= OnEditorKeyDown;
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

        _surface?.RequestTextInput(false);
    }

    private void DetachAddRow()
    {
        _addRow.Tapped -= OnAddTapped;
        _addHover?.Dispose();
        _addHover = null;
    }
}
