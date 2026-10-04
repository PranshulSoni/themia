using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Tasks;
using Themia.App.Widgets;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Tasks;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-592]/[S-604] The Tasks widget's settings: where the list comes from — an account's list, its
/// Today view, or a list kept on this PC — and how rows are drawn.
/// <para>
/// Accounts are signed in on the Accounts page, not here: this page lists the accounts that can hold
/// tasks and links there. Lists on this PC, by contrast, are made, renamed and deleted right here,
/// because this page is the only place they exist.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private const string TasksScope = "tasks-settings";

    public static void Tasks(List<FrameworkElement> rows, TasksConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        var service = AppServices.Tasks;

        rows.Add(FluentLayout.Header("Source"));

        // The rows under the source change with it, so they live in one host rebuilt in place.
        var sourceRows = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(sourceRows);

        var updating = new List<FrameworkElement>();

        void Rebuild()
        {
            sourceRows.Children.Clear();

            foreach (var row in TaskSourceRows(service, config, save, navigate, Rebuild))
            {
                sourceRows.Children.Add(row);
            }

            foreach (var row in updating)
            {
                row.Visibility = config.Source == TaskSource.Local ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        rows.Add(FluentLayout.Header("Display"));

        rows.Add(FluentLayout.Row(Glyphs.Done, "Show completed", "List finished tasks too, struck through",
            FluentLayout.Toggle(config.ShowCompleted, value => { config.ShowCompleted = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Show due date", "Overdue is said in words, not only in red",
            FluentLayout.Toggle(config.ShowDueDate, value => { config.ShowDueDate = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.View, "Layout", "List: the due date under the title. Compact: beside it",
            FluentLayout.Choice<TaskViewMode>(
                [(TaskViewMode.List, "List"), (TaskViewMode.Compact, "Compact")],
                config.ViewMode,
                value => { config.ViewMode = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Wrap, "Title lines", "How far a long title wraps before it is cut short",
            FluentLayout.Slider(config.EffectiveTitleLines, TaskRules.TitleLinesMin, TaskRules.TitleLinesMax, 1, string.Empty,
                value => { config.TitleLines = (int)Math.Round(value); save(); })));

        var header = FluentLayout.Header("Updating");
        var refresh = FluentLayout.Row(Glyphs.Timer, "Refresh", "How often the account is asked for changes",
            FluentLayout.Slider(config.RefreshInterval.TotalSeconds, 30, 600, 30, "s",
                value => { config.RefreshSeconds = (int)Math.Round(value); save(); }));
        updating.Add(header);
        updating.Add(refresh);
        rows.Add(header);
        rows.Add(refresh);

        Rebuild();
    }

    /// <summary>The source picker, then what that source offers: an account's lists, or this PC's.</summary>
    private static IEnumerable<FrameworkElement> TaskSourceRows(
        TaskService? service,
        TasksConfig config,
        Action save,
        Action<ConfigTarget> navigate,
        Action rebuild)
    {
        if (service is null)
        {
            yield return FluentLayout.Note("Tasks are not available until Themia has started.");
            yield break;
        }

        var accounts = service.Accounts;

        // "Automatic" first: the first account with tasks, now or later. Then each account, then this PC.
        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem { Content = accounts.Count > 0 ? $"First account ({accounts[0].Label})" : "First account to sign in", Tag = string.Empty });

        foreach (var account in accounts)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = account.Problem is null ? $"{account.Label} — {account.Provider.Label()}" : $"{account.Label} — sign in again",
                Tag = account.Id,
            });
        }

        combo.Items.Add(new ComboBoxItem { Content = "This PC", Tag = LocalTag });

        combo.SelectedIndex = config.Source == TaskSource.Local
            ? combo.Items.Count - 1
            : Math.Max(0, IndexOfTag(combo, config.AccountId));

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string tag })
            {
                return;
            }

            if (tag == LocalTag)
            {
                if (config.Source == TaskSource.Local)
                {
                    return;
                }

                // [S-604] The list it had if that still exists, else the first, else a new one.
                config.Source = TaskSource.Local;
                config.LocalListId = service.Local.Ensure(config.LocalListId, "My tasks");
            }
            else
            {
                if (config.Source == TaskSource.Account && config.AccountId == tag)
                {
                    return;
                }

                // A different account's lists, and a different list's order.
                config.Source = TaskSource.Account;
                config.AccountId = tag;
                config.ListId = string.Empty;
                config.ListName = string.Empty;
                config.TaskOrder = [];
            }

            save();
            rebuild();
        };

        yield return FluentLayout.Row(Glyphs.Contact, "Show tasks from",
            accounts.Count == 0 ? "No Microsoft or Google account is signed in" : "A Microsoft or Google account, or a list kept on this PC",
            combo);

        if (accounts.Count == 0 || accounts.Any(a => a.Problem is not null))
        {
            yield return FluentLayout.NavRow(Glyphs.Contact, "Accounts",
                accounts.Count == 0 ? "Sign in to Microsoft To Do or Google Tasks" : "An account needs you to sign in again",
                () => navigate(new ConfigTarget.App(AppPage.Accounts)));
        }

        if (config.Source == TaskSource.Local)
        {
            foreach (var row in LocalListRows(service.Local, config, save, rebuild))
            {
                yield return row;
            }

            yield break;
        }

        var resolved = string.IsNullOrEmpty(config.AccountId) ? accounts.FirstOrDefault() : service.Find(config.AccountId);
        if (resolved is not null)
        {
            yield return AccountListRow(service, resolved, config, save);
        }
    }

    private const string LocalTag = "\u0001local";

    /// <summary>
    /// [S-592]/[S-596] The account's lists: its default first (what an unchosen widget shows), then
    /// Today — every list's tasks due today or overdue — then the lists themselves. Read once and
    /// shared with the widget through the service's cache.
    /// </summary>
    private static Border AccountListRow(TaskService service, TaskAccount account, TasksConfig config, Action save)
    {
        var combo = new ComboBox { MinWidth = 260, IsEnabled = false, PlaceholderText = "Loading lists…" };
        var row = FluentLayout.Row(Glyphs.Task, "List", $"From {account.Label}", combo);

        _ = FillAsync();
        return row;

        async Task FillAsync()
        {
            IReadOnlyList<TaskListInfo> lists;
            try
            {
                lists = await service.ListsAsync(account.Id).ConfigureAwait(true);
            }
            catch (AccountException ex)
            {
                Log.Warn(TasksScope, $"could not read the lists of {account.Id}", ex);
                combo.PlaceholderText = ex.Error == AccountError.SignInRequired ? "Sign in again to see the lists" : "Could not read the lists";
                return;
            }

            var fallback = TaskRules.DefaultList(lists);
            combo.Items.Add(new ComboBoxItem { Content = fallback is null ? "The default list" : $"The default list ({fallback.Name})", Tag = string.Empty });
            combo.Items.Add(new ComboBoxItem { Content = "Today — due today and overdue, every list", Tag = TaskRules.TodayListId });

            foreach (var list in lists)
            {
                combo.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(list.Name) ? "(unnamed list)" : list.Name, Tag = list.Id });
            }

            // A list that has gone since it was picked is not in the box; it falls back to the
            // default entry rather than sitting blank on a setting that means something.
            combo.SelectedIndex = Math.Max(0, IndexOfTag(combo, config.ListId));
            combo.IsEnabled = true;

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem { Tag: string id } || id == config.ListId)
                {
                    return;
                }

                config.ListId = id;
                config.ListName = lists.FirstOrDefault(l => l.Id == id)?.Name ?? string.Empty;
                config.TaskOrder = [];
                save();
            };
        }
    }

    /// <summary>
    /// [S-604] The lists kept on this PC: pick one, rename it as you type, add one, delete one after
    /// asking. A pool every Tasks widget shares, so renaming here retitles each widget showing it.
    /// </summary>
    private static IEnumerable<FrameworkElement> LocalListRows(LocalTaskPool pool, TasksConfig config, Action save, Action rebuild)
    {
        if (pool.Problem is { } problem)
        {
            yield return FluentLayout.Note(problem);
        }

        foreach (var list in pool.Lists)
        {
            var pick = new RadioButton
            {
                GroupName = "tasks-local-lists",
                IsChecked = list.Id == config.LocalListId,
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var listId = list.Id;
            pick.Checked += (_, _) =>
            {
                if (config.LocalListId != listId)
                {
                    config.LocalListId = listId;
                    save();
                }
            };

            var name = new TextBox
            {
                Text = list.Name,
                PlaceholderText = "List name",
                MinWidth = 160,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = pool.Writable,
            };

            // Written through as it is typed; a blank name is kept so the box can be cleared.
            name.TextChanged += (_, _) => pool.Rename(listId, name.Text);

            var count = Fluent.Text(Themia.Core.Widgets.FluentType.Caption, "TextFillColorTertiaryBrush");
            count.Text = list.Tasks.Count == 1 ? "1 task" : $"{list.Tasks.Count} tasks";
            count.VerticalAlignment = VerticalAlignment.Center;

            var remove = FluentLayout.IconCommand(Glyphs.Delete, "Delete this list");
            remove.IsEnabled = pool.Writable;
            remove.Click += async (_, _) =>
            {
                try
                {
                    // [S-604] Always asked, empty or not: it takes every task in it, and the button
                    // sits one row from the name being typed in.
                    var detail = list.Tasks.Count == 0
                        ? $"\"{Display(list.Name)}\" will be deleted from this PC."
                        : $"\"{Display(list.Name)}\" and its {list.Tasks.Count} task{(list.Tasks.Count == 1 ? string.Empty : "s")} will be deleted from this PC. This cannot be undone.";

                    if (!await FluentLayout.ConfirmAsync(remove, "Delete this list?", detail, "Delete").ConfigureAwait(true))
                    {
                        return;
                    }

                    pool.Delete(listId);

                    // The widget showing it says "pick or start a list" rather than being handed
                    // another list's tasks.
                    if (config.LocalListId == listId)
                    {
                        config.LocalListId = string.Empty;
                        save();
                    }

                    rebuild();
                }
                catch (Exception ex)
                {
                    Log.Error(TasksScope, "could not delete the list", ex);
                }
            };

            var line = new Grid
            {
                ColumnSpacing = Fluent.Space2,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            Grid.SetColumn(name, 1);
            Grid.SetColumn(count, 2);
            Grid.SetColumn(remove, 3);
            line.Children.Add(pick);
            line.Children.Add(name);
            line.Children.Add(count);
            line.Children.Add(remove);

            yield return FluentLayout.Card(line);
        }

        if (pool.Lists.Count == 0)
        {
            yield return FluentLayout.Note("No lists on this PC yet.");
        }

        var add = new Button { Content = "Add a list", IsEnabled = pool.Writable };
        add.Click += (_, _) =>
        {
            config.LocalListId = pool.Create(LocalTaskLists.NewListName);
            save();
            rebuild();
        };

        yield return add;
    }

    private static string Display(string name) => string.IsNullOrWhiteSpace(name) ? "Untitled list" : name;

    private static int IndexOfTag(ComboBox combo, string? tag)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem { Tag: string value } && value == (tag ?? string.Empty))
            {
                return i;
            }
        }

        return -1;
    }
}
