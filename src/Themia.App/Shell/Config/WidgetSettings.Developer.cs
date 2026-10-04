using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Developer;
using Themia.Core.Model;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-610]..[S-636] The developer widgets' settings: which GitHub or Claude accounts they read, what
/// they list, and how often they ask. The classic widgets' Source, Display and Refresh tabs, each on
/// one page.
/// <para>
/// Accounts themselves are signed in on the Accounts page, not here — no token box, as in the
/// classic app since [S-13] there — so there is exactly one place an account comes into being.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void GitHub(List<FrameworkElement> rows, GitHubConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        rows.Add(FluentLayout.Header("Repository"));

        var address = new TextBox { Text = config.Repo, Width = 260, MaxLength = 300, PlaceholderText = "owner/name" };
        var verdict = FluentLayout.Verdict();

        void Commit()
        {
            var typed = address.Text.Trim();
            if (typed == config.Repo)
            {
                return;
            }

            // [S-611] Refused here, where the user can see why, rather than stored and refused again
            // on every tick. Written as typed: a pasted web address keeps working as one.
            if (typed.Length > 0 && GitHubRules.ParseRepo(typed) is null)
            {
                FluentLayout.Refuse(verdict, "Use owner/name, or the repository's web address.");
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            config.Repo = typed;
            save();
        }

        address.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };
        address.LostFocus += (_, _) => Commit();

        rows.Add(FluentLayout.FieldRow(Glyphs.Code, "Repository",
            "owner/name or its web address. Requests go to the account's own GitHub, Enterprise included", address, verdict));

        SingleAccountRow(rows, AppServices.GitHub?.Accounts ?? [], "GitHub", config.AccountIds.FirstOrDefault() ?? string.Empty,
            id => { config.AccountIds = id.Length == 0 ? [] : [id]; save(); }, navigate);

        rows.Add(FluentLayout.Header("Lists"));

        rows.Add(CountRow(Glyphs.Code, "Commits", "The newest commits on the default branch; 0 hides the list",
            config.EffectiveCommitCount, 0, GitHubConfig.MaxListCount, value => { config.CommitCount = value; save(); }));

        var pullRows = new List<FrameworkElement>();

        void ShowPullRows()
        {
            foreach (var row in pullRows)
            {
                row.Visibility = config.EffectivePullCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        rows.Add(CountRow(Glyphs.PullRequest, "Pull requests", "0 hides the list",
            config.EffectivePullCount, 0, GitHubConfig.MaxListCount, value => { config.PullCount = value; save(); ShowPullRows(); }));

        // [S-613] Only meaningful while there is a pull request list to filter.
        pullRows.Add(FluentLayout.Row(Glyphs.View, "Show", "Closed includes merged",
            FluentLayout.Choice<PullFilter>(
                [(PullFilter.Open, "Open"), (PullFilter.Closed, "Closed"), (PullFilter.All, "All")],
                config.PullFilter,
                value => { config.PullFilter = value; save(); })));

        pullRows.Add(FluentLayout.Row(Glyphs.Sort, "Order", "By when each was opened",
            FluentLayout.Choice<PullOrder>(
                [(PullOrder.Newest, "Newest first"), (PullOrder.Oldest, "Oldest first")],
                config.PullOrder,
                value => { config.PullOrder = value; save(); })));

        rows.AddRange(pullRows);
        ShowPullRows();

        rows.Add(CountRow(Glyphs.Issue, "Issues", "Open issues, newest first; 0 hides the list",
            config.EffectiveIssueCount, 0, GitHubConfig.MaxListCount, value => { config.IssueCount = value; save(); }));

        rows.Add(CountRow(Glyphs.Refresh, "Workflow runs", "The newest run is always the header's CI status; 0 hides the list",
            config.EffectiveRunCount, 0, GitHubConfig.MaxListCount, value => { config.RunCount = value; save(); }));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    public static void GitHubIssues(List<FrameworkElement> rows, GitHubIssuesConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        GitHubAccountRows(rows, config, save, navigate);

        rows.Add(FluentLayout.Header("Issues"));

        rows.Add(CountRow(Glyphs.Count, "Issues", "The most recently updated, across every account",
            config.EffectiveCount, GitHubIssuesConfig.MinCount, GitHubIssuesConfig.MaxCount, value => { config.Count = value; save(); }));

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Include mentions", "Also open issues that mention you",
            FluentLayout.Toggle(config.IncludeMentioned, value => { config.IncludeMentioned = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    public static void GitHubReviews(List<FrameworkElement> rows, GitHubReviewsConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        GitHubAccountRows(rows, config, save, navigate);

        rows.Add(FluentLayout.Header("Pull requests"));

        rows.Add(CountRow(Glyphs.Count, "Pull requests", "The most recently updated, across every account",
            config.EffectiveCount, GitHubReviewsConfig.MinCount, GitHubReviewsConfig.MaxCount, value => { config.Count = value; save(); }));

        rows.Add(FluentLayout.Row(Glyphs.Edit, "Include drafts", "A draft is not asking for a review yet",
            FluentLayout.Toggle(config.IncludeDrafts, value => { config.IncludeDrafts = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    public static void GitHubNotifications(List<FrameworkElement> rows, GitHubNotificationsConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        GitHubAccountRows(rows, config, save, navigate);

        rows.Add(FluentLayout.Header("Notifications"));

        rows.Add(CountRow(Glyphs.Count, "Notifications", "The newest, across every account",
            config.EffectiveCount, GitHubNotificationsConfig.MinCount, GitHubNotificationsConfig.MaxCount, value => { config.Count = value; save(); }));

        rows.Add(FluentLayout.Row(Glyphs.MailRead, "Show read", "Also list notifications you have already read",
            FluentLayout.Toggle(config.IncludeRead, value => { config.IncludeRead = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Participating only", "Only threads you take part in or were mentioned in",
            FluentLayout.Toggle(config.ParticipatingOnly, value => { config.ParticipatingOnly = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Group, "Group by repository", "A heading over each repository's notifications",
            FluentLayout.Toggle(config.GroupByRepository, value => { config.GroupByRepository = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
        rows.Add(FluentLayout.Note("GitHub may ask for a slower pace than this; the widget follows it."));
    }

    public static void ClaudeUsage(List<FrameworkElement> rows, ClaudeUsageConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        rows.Add(FluentLayout.Header("Account"));

        SingleAccountRow(rows, AppServices.ClaudeUsage?.Accounts ?? [], "Claude", config.AccountId,
            id => { config.AccountId = id; save(); }, navigate);

        rows.Add(FluentLayout.Header("Limits"));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, "Weekly Opus limit", "Its own bar; 0% while the plan sets no separate limit",
            FluentLayout.Toggle(config.ShowOpus, value => { config.ShowOpus = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, "Weekly Sonnet limit", "Its own bar; 0% while the plan sets no separate limit",
            FluentLayout.Toggle(config.ShowSonnet, value => { config.ShowSonnet = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
        rows.Add(FluentLayout.Note("Read from the endpoint the Claude CLI uses for its own usage. It is undocumented and may change."));
    }

    private static Border CountRow(string glyph, string title, string description, int value, int min, int max, Action<int> changed)
        => FluentLayout.Row(glyph, title, description,
            FluentLayout.Slider(value, min, max, 1, string.Empty, v => changed((int)Math.Round(v))));

    /// <summary>
    /// [S-623] "Every GitHub account", or a choice of them. Every is the default and means accounts
    /// added later join too; turning it off starts from the accounts there are now, so nothing
    /// disappears from the widget until an account is then turned off.
    /// </summary>
    private static void GitHubAccountRows(List<FrameworkElement> rows, GitHubAccountsConfig config, Action save, Action<ConfigTarget> navigate)
    {
        var accounts = AppServices.GitHub?.Accounts ?? [];

        rows.Add(FluentLayout.Header("Accounts"));

        var manage = new Button { Content = accounts.Count == 0 ? "Add an account" : "Manage accounts" };
        manage.Click += (_, _) => navigate(new ConfigTarget.App(AppPage.Accounts));

        if (accounts.Count == 0)
        {
            rows.Add(FluentLayout.Row(Glyphs.Code, "No GitHub account yet",
                "Sign in to GitHub (or GitHub Enterprise) on the Accounts page", manage));
            return;
        }

        var toggles = new List<ToggleSwitch>();
        var chosenRows = new List<FrameworkElement>();

        // Set while this page moves a switch itself, so the switch's own handler does not take that
        // for the user choosing one account.
        var quiet = false;
        var every = config.AccountIds.Count == 0;

        void ShowChoice()
        {
            foreach (var row in chosenRows)
            {
                row.Visibility = config.AccountIds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        rows.Add(FluentLayout.Row(Glyphs.Code, "Every GitHub account", "Accounts you add later are included too",
            FluentLayout.Toggle(every, on =>
            {
                config.AccountIds = on ? [] : [.. accounts.Select(a => a.Id)];

                quiet = true;
                foreach (var toggle in toggles)
                {
                    toggle.IsOn = true;
                }

                quiet = false;

                save();
                ShowChoice();
            })));

        foreach (var account in accounts)
        {
            var index = toggles.Count;
            var toggle = FluentLayout.Toggle(every || config.AccountIds.Contains(account.Id), on =>
            {
                if (quiet)
                {
                    return;
                }

                var ids = config.AccountIds.ToList();
                ids.Remove(account.Id);
                if (on)
                {
                    ids.Add(account.Id);
                }

                // At least one stays: an empty list means "every account", the opposite of the user
                // turning the last one off.
                if (ids.Count == 0)
                {
                    quiet = true;
                    toggles[index].IsOn = true;
                    quiet = false;
                    return;
                }

                config.AccountIds = [.. accounts.Select(a => a.Id).Where(ids.Contains)];
                save();
            });

            toggles.Add(toggle);

            var row = FluentLayout.Row(Glyphs.Contact, account.Label, account.Problem ?? "GitHub", toggle);
            chosenRows.Add(row);
            rows.Add(row);
        }

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Accounts", "Sign in, sign out and rename on the Accounts page", manage));
        ShowChoice();
    }

    /// <summary>One account of a kind, or "the first", with the way to the Accounts page beside it.</summary>
    private static void SingleAccountRow(
        List<FrameworkElement> rows,
        IReadOnlyList<DevAccount> accounts,
        string service,
        string current,
        Action<string> changed,
        Action<ConfigTarget> navigate)
    {
        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem
        {
            Content = accounts.Count > 0 ? $"First {service} account ({accounts[0].Label})" : $"First {service} account to sign in",
            Tag = string.Empty,
        });

        var selected = 0;
        foreach (var account in accounts)
        {
            if (account.Id == current)
            {
                selected = combo.Items.Count;
            }

            combo.Items.Add(new ComboBoxItem
            {
                Content = account.Problem is null ? account.Label : $"{account.Label} — sign in again",
                Tag = account.Id,
            });
        }

        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id } && id != current)
            {
                current = id;
                changed(id);
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Account",
            accounts.Count == 0 ? $"No {service} account is signed in" : $"Which {service} account to read with",
            combo));

        if (accounts.Count == 0 || accounts.Any(a => a.Problem is not null))
        {
            rows.Add(FluentLayout.ClickRow(Glyphs.Contact, "Accounts",
                accounts.Count == 0 ? $"Sign in to {service}" : "An account needs you to sign in again",
                () => navigate(new ConfigTarget.App(AppPage.Accounts))));
        }
    }
}
