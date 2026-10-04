using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Developer;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.Github_RepositoryLabel.Text()));

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
                FluentLayout.Refuse(verdict, Str.WidgetsA_Dev_Settings_RepoInvalid.Text());
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

        rows.Add(FluentLayout.FieldRow(Glyphs.Code, Str.Github_RepositoryLabel.Text(),
            Str.WidgetsA_Dev_Settings_Repo_Desc.Text(), address, verdict));

        SingleAccountRow(rows, AppServices.GitHub?.Accounts ?? [], "GitHub", config.AccountIds.FirstOrDefault() ?? string.Empty,
            id => { config.AccountIds = id.Length == 0 ? [] : [id]; save(); }, navigate, anonymous: true);

        rows.Add(FluentLayout.Header(Str.WidgetsA_Dev_Settings_Lists.Text()));

        // [S-740] Filling, a count is on or off: 0 still hides a list, any other number shows as many as fit.
        rows.Add(FillRow(config, save, Str.WidgetsA_Dev_Settings_FillLists_Desc.Text()));

        rows.Add(CountRow(Glyphs.Code, Str.Github_CommitsLabel.Text(), Str.WidgetsA_Dev_Settings_Commits_Desc.Text(),
            config.EffectiveCommitCount, 0, GitHubConfig.MaxListCount, value => { config.CommitCount = value; save(); }));

        var pullRows = new List<FrameworkElement>();

        void ShowPullRows()
        {
            foreach (var row in pullRows)
            {
                row.Visibility = config.EffectivePullCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        rows.Add(CountRow(Glyphs.PullRequest, Str.Github_PrsLabel.Text(), Str.WidgetsA_Dev_Settings_ZeroHides.Text(),
            config.EffectivePullCount, 0, GitHubConfig.MaxListCount, value => { config.PullCount = value; save(); ShowPullRows(); }));

        // [S-613] Only meaningful while there is a pull request list to filter.
        pullRows.Add(FluentLayout.Row(Glyphs.View, Str.WidgetsA_Bluetooth_Settings_Show.Text(), Str.WidgetsA_Dev_Settings_PullState_Desc.Text(),
            FluentLayout.Choice<PullFilter>(
                [(PullFilter.Open, Str.Github_PrStateOpen.Text()), (PullFilter.Closed, Str.Github_PrStateClosed.Text()), (PullFilter.All, Str.Github_PrStateAll.Text())],
                config.PullFilter,
                value => { config.PullFilter = value; save(); })));

        pullRows.Add(FluentLayout.Row(Glyphs.Sort, Str.WidgetsA_Dev_Settings_Order_Label.Text(), Str.WidgetsA_Dev_Settings_Order_Desc.Text(),
            FluentLayout.Choice<PullOrder>(
                [(PullOrder.Newest, Str.Github_PrSortNewest.Text()), (PullOrder.Oldest, Str.Github_PrSortOldest.Text())],
                config.PullOrder,
                value => { config.PullOrder = value; save(); })));

        rows.AddRange(pullRows);
        ShowPullRows();

        rows.Add(CountRow(Glyphs.Issue, Str.Github_IssuesLabel.Text(), Str.WidgetsA_Dev_Settings_Issues_Desc.Text(),
            config.EffectiveIssueCount, 0, GitHubConfig.MaxListCount, value => { config.IssueCount = value; save(); }));

        rows.Add(CountRow(Glyphs.Refresh, Str.WidgetsA_Github_WorkflowRuns.Text(), Str.WidgetsA_Dev_Settings_Runs_Desc.Text(),
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

        rows.Add(FluentLayout.Header(Str.Github_IssuesLabel.Text()));

        var count = CountRow(Glyphs.Count, Str.Github_IssuesLabel.Text(), Str.WidgetsA_Dev_Settings_RecentAcross.Text(),
            config.EffectiveCount, GitHubIssuesConfig.MinCount, GitHubIssuesConfig.MaxCount, value => { config.Count = value; save(); });
        rows.Add(FillRow(config, save, Str.WidgetsA_Dev_Settings_Fill_Desc.Text(), count));
        rows.Add(count);

        rows.Add(FluentLayout.Row(Glyphs.Contact, Str.WidgetsA_Dev_Settings_Mentions_Label.Text(), Str.WidgetsA_Dev_Settings_Mentions_Desc.Text(),
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

        rows.Add(FluentLayout.Header(Str.Github_PrsLabel.Text()));

        var count = CountRow(Glyphs.Count, Str.Github_PrsLabel.Text(), Str.WidgetsA_Dev_Settings_RecentAcross.Text(),
            config.EffectiveCount, GitHubReviewsConfig.MinCount, GitHubReviewsConfig.MaxCount, value => { config.Count = value; save(); });
        rows.Add(FillRow(config, save, Str.WidgetsA_Dev_Settings_Fill_Desc.Text(), count));
        rows.Add(count);

        rows.Add(FluentLayout.Row(Glyphs.Edit, Str.Github_IncludeDraftsLabel.Text(), Str.WidgetsA_Dev_Settings_Drafts_Desc.Text(),
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

        rows.Add(FluentLayout.Header(Str.Github_NotificationsLabel.Text()));

        var count = CountRow(Glyphs.Count, Str.Github_NotificationsLabel.Text(), Str.WidgetsA_Dev_Settings_NewestAcross.Text(),
            config.EffectiveCount, GitHubNotificationsConfig.MinCount, GitHubNotificationsConfig.MaxCount, value => { config.Count = value; save(); });
        rows.Add(FillRow(config, save, Str.WidgetsA_Dev_Settings_Fill_Desc.Text(), count));
        rows.Add(count);

        rows.Add(FluentLayout.Row(Glyphs.MailRead, Str.Github_ShowReadLabel.Text(), Str.WidgetsA_Dev_Settings_ShowRead_Desc.Text(),
            FluentLayout.Toggle(config.IncludeRead, value => { config.IncludeRead = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Contact, Str.Github_ParticipatingLabel.Text(), Str.WidgetsA_Dev_Settings_Participating_Desc.Text(),
            FluentLayout.Toggle(config.ParticipatingOnly, value => { config.ParticipatingOnly = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Group, Str.WidgetsA_Dev_Settings_GroupRepo_Label.Text(), Str.WidgetsA_Dev_Settings_GroupRepo_Desc.Text(),
            FluentLayout.Toggle(config.GroupByRepository, value => { config.GroupByRepository = value; save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
        rows.Add(FluentLayout.Note(Str.WidgetsA_Dev_Settings_SlowerNote.Text()));
    }

    public static void ClaudeUsage(List<FrameworkElement> rows, ClaudeUsageConfig config, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        rows.Add(FluentLayout.Header(Str.ClaudeUsage_AccountLabel.Text()));

        SingleAccountRow(rows, AppServices.ClaudeUsage?.Accounts ?? [], "Claude", config.AccountId,
            id => { config.AccountId = id; save(); }, navigate);

        rows.Add(FluentLayout.Header(Str.WidgetsA_Dev_Settings_Limits.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, Str.ClaudeUsage_OpusLabel.Text(), Str.WidgetsA_Dev_Settings_ModelLimit_Desc.Text(),
            FluentLayout.Toggle(config.ShowOpus, value => { config.ShowOpus = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, Str.ClaudeUsage_SonnetLabel.Text(), Str.WidgetsA_Dev_Settings_ModelLimit_Desc.Text(),
            FluentLayout.Toggle(config.ShowSonnet, value => { config.ShowSonnet = value; save(); })));

        // [S-796] Any five minutes from 5 to 60, as the classic stepper allowed; the preset box would
        // have snapped a stored 10 or 45 back to 15.
        rows.Add(FluentLayout.Header(Str.WidgetsA_Dev_Settings_Updating.Text()));
        rows.Add(FluentLayout.Row(Glyphs.Timer, Str.Common_Refresh.Text(), Str.WidgetsA_Dev_Settings_ClaudeRefresh_Desc.Text(),
            FluentLayout.Slider(config.RefreshMinutes, 5, 60, 5, Str.WidgetsA_Dev_Settings_MinutesUnit.Text(),
                value => { config.RefreshMinutes = ClaudeUsageRules.ClampRefreshMinutes((int)Math.Round(value)); save(); })));
        rows.Add(FluentLayout.Note(Str.WidgetsA_Dev_Settings_ClaudeEndpointNote.Text()));
    }

    /// <summary>
    /// [S-740] "Fill the tile": on, the list shows as many as the tile has room for; off, exactly the
    /// count. The count row, when given, only shows while it decides anything.
    /// </summary>
    private static Border FillRow(GitHubAccountsConfig config, Action save, string description, FrameworkElement? count = null)
    {
        void ShowCount()
        {
            if (count is not null)
            {
                count.Visibility = config.FillTile ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        ShowCount();
        return FluentLayout.Row(Glyphs.View, Str.WidgetsA_Dev_Settings_Fill_Label.Text(), description,
            FluentLayout.Toggle(config.FillTile, on => { config.FillTile = on; save(); ShowCount(); }));
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

        rows.Add(FluentLayout.Header(Str.WidgetUi_AccountsLabel.Text()));

        var manage = new Button { Content = (accounts.Count == 0 ? Str.WidgetsA_Office_AddAccount : Str.WidgetsA_Office_ManageAccounts).Text() };
        manage.Click += (_, _) => navigate(new ConfigTarget.App(AppPage.Accounts));

        if (accounts.Count == 0)
        {
            rows.Add(FluentLayout.Row(Glyphs.Code, Str.WidgetsA_Dev_Settings_NoGithub.Text(),
                Str.WidgetsA_Dev_Settings_NoGithub_Desc.Text(), manage));
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

        rows.Add(FluentLayout.Row(Glyphs.Code, Str.WidgetsA_Dev_Settings_EveryGithub.Text(), Str.WidgetsA_Office_EveryDesc.Text(),
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

        rows.Add(FluentLayout.Row(Glyphs.Contact, Str.WidgetUi_AccountsLabel.Text(), Str.WidgetsA_Office_ManageDesc.Text(), manage));
        ShowChoice();
    }

    /// <summary>One account of a kind, or "the first", with the way to the Accounts page beside it.</summary>
    private static void SingleAccountRow(
        List<FrameworkElement> rows,
        IReadOnlyList<DevAccount> accounts,
        string service,
        string current,
        Action<string> changed,
        Action<ConfigTarget> navigate,
        bool anonymous = false)
    {
        var combo = new ComboBox { MinWidth = 260 };
        combo.Items.Add(new ComboBoxItem
        {
            Content = accounts.Count > 0
                ? Str.WidgetsA_Dev_Settings_FirstAccountNamed.Format(("service", service), ("account", accounts[0].Label))
                : anonymous ? Str.WidgetsA_Dev_Settings_PublicOnly.Text() : Str.WidgetsA_Dev_Settings_FirstAccount.Format(("service", service)),
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
                Content = account.Problem is null ? account.Label : Str.WidgetsA_Dev_Settings_SignInAgainNamed.Format(("account", account.Label)),
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

        // [S-746] Optional where public data can be read without one.
        rows.Add(FluentLayout.Row(Glyphs.Contact, Str.ClaudeUsage_AccountLabel.Text(),
            anonymous
                ? Str.WidgetsA_Dev_Settings_OptionalAccount.Text()
                : accounts.Count == 0 ? Str.WidgetsA_Dev_Settings_NoneSignedIn.Format(("service", service)) : Str.WidgetsA_Dev_Settings_WhichAccount.Format(("service", service)),
            combo));

        if (accounts.Count == 0 || accounts.Any(a => a.Problem is not null))
        {
            rows.Add(FluentLayout.NavRow(Glyphs.Contact, Str.WidgetUi_AccountsLabel.Text(),
                accounts.Count == 0 ? Str.WidgetsA_Dev_Settings_SignInTo.Format(("service", service)) : Str.WidgetsA_Dev_Settings_AccountNeedsSignIn.Text(),
                () => navigate(new ConfigTarget.App(AppPage.Accounts))));
        }
    }
}
