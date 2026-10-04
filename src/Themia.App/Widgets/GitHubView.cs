using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-610] One repository at a glance: its stars, forks and open issues, its language, last push
/// and default branch, the newest workflow run's verdict in the header, and short lists of recent
/// commits, pull requests, issues and runs.
/// <para>
/// It sheds from the bottom up. A short tile drops the lists and keeps the numbers; a wide, short
/// one moves the lists beside the numbers instead; the smallest stacks the three numbers, which
/// are what a repository tile is for.
/// </para>
/// </summary>
internal sealed partial class GitHubView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    /// <summary>The body height under which the lists go and only the numbers stay.</summary>
    private const double ListsNeedHeight = 140;

    /// <summary>A tile at least this wide but short puts the lists beside the numbers.</summary>
    private const double SideBySideWidth = 400;

    private const double SideBySideBelowHeight = 220;

    /// <summary>The numbers' column when the lists sit beside them.</summary>
    private const double SummaryColumnWidth = 170;

    /// <summary>Under this width the three numbers stack rather than sit on one line.</summary>
    private const double StatsInlineWidth = 200;

    /// <summary>The meta line (language, last push, branch) needs this much body height.</summary>
    private const double MetaNeedsHeight = 92;

    /// <summary>Under this width the header's verdict is its coloured dot alone; the title needs the room.</summary>
    private const double CiWordNeedsWidth = 300;

    private readonly DevChrome _chrome = new(Glyphs.Code);
    private readonly StackPanel _summary = new() { Spacing = Fluent.Space1 };
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly WholeRows _lists;
    private readonly List<IDisposable> _hovers = [];

    private readonly FontIcon _ciDot = Fluent.Icon(Glyphs.Dot, "TextFillColorTertiaryBrush", 8);
    private readonly TextBlock _ciText = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly StackPanel _ci;
    private readonly StackPanel _trailing;

    private IGitHubSource? _source;
    private IDevFeed<RepoActivity>? _feed;
    private string? _feedKey;
    private IDisposable? _clock;
    private IWidgetSurface? _surface;
    private GitHubConfig _config = new();
    private string _layoutShape = string.Empty;
    private bool _detached;

    public GitHubView()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _summary.Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space2);

        _scroll = new ScrollViewer
        {
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        _lists = new WholeRows(_scroll, _list);

        _ciDot.VerticalAlignment = VerticalAlignment.Center;
        _ciText.VerticalAlignment = VerticalAlignment.Center;
        _ciText.TextTrimming = TextTrimming.None;
        _ci = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space1,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Children = { _ciDot, _ciText },
        };

        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _chrome.HeaderTrailing, _ci },
        };

        SetColumnSpan(_chrome.StatePanel, 2);
        SetRowSpan(_chrome.StatePanel, 2);

        Children.Add(_summary);
        Children.Add(_lists);
        Children.Add(_chrome.StatePanel);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>[S-610] The newest run's verdict, and the warning when a refresh failed.</summary>
    public FrameworkElement? HeaderTrailing => _trailing;

    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle(null);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        _source = AppServices.GitHub;

        if (_source is not null)
        {
            _source.AccountsChanged += OnAccountsChanged;
        }

        ApplyConfig(config);
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), Render);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as GitHubConfig ?? new GitHubConfig();
        Reopen();
        Render();
    }

    private void Reopen()
    {
        var repo = GitHubRules.ParseRepo(_config.Repo);

        if (_source is null || repo is null)
        {
            CloseFeed();
            return;
        }

        var query = new RepoQuery(
            [.. _config.AccountIds],
            repo,
            _config.EffectiveCommitCount,
            _config.EffectivePullCount,
            _config.PullFilter,
            _config.PullOrder,
            _config.EffectiveIssueCount,
            _config.EffectiveRunCount,
            _config.RefreshInterval);

        if (_feed is not null && query.Key == _feedKey)
        {
            return;
        }

        CloseFeed();
        _feedKey = query.Key;
        _feed = _source.OpenRepo(query);
        _feed.Updated += Render;
    }

    private void CloseFeed()
    {
        if (_feed is null)
        {
            return;
        }

        _feed.Updated -= Render;
        _feed.Dispose();
        _feed = null;
        _feedKey = null;
    }

    private void OnAccountsChanged() => Render();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _chrome.Fit(args.NewSize.Height);
        _ciText.Visibility = args.NewSize.Width >= CiWordNeedsWidth ? Visibility.Visible : Visibility.Collapsed;

        if (Shape(args.NewSize.Width, args.NewSize.Height) != _layoutShape)
        {
            Render();
        }
    }

    /// <summary>The layout decisions a size makes, so a resize redraws only when one of them changes.</summary>
    private static string Shape(double width, double height)
        => $"{SideBySide(width, height)}{Lists(width, height)}{width >= StatsInlineWidth || SideBySide(width, height)}{height >= MetaNeedsHeight}{width >= DevRows.TimeNeedsWidth}";

    private static bool SideBySide(double width, double height) => width >= SideBySideWidth && height < SideBySideBelowHeight && height > 0;

    private static bool Lists(double width, double height) => height <= 0 || height >= ListsNeedHeight || SideBySide(width, height);

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var now = DateTimeOffset.Now;
        var snapshot = _feed?.Snapshot ?? DevFeedSnapshot<RepoActivity>.Empty;
        var activity = snapshot.Value;
        var screen = DevScreens.Decide("GitHub", _source?.Accounts ?? [], _config.AccountIds, single: true, snapshot.Outcomes, activity is not null, string.Empty, now);

        // [S-614] A repository to show comes after an account to show it with.
        if (GitHubRules.ParseRepo(_config.Repo) is null
            && screen.Kind is not (DevScreenKind.NoAccounts or DevScreenKind.AccountsGone or DevScreenKind.SignInRequired))
        {
            screen = new DevScreen(
                DevScreenKind.NeedsSetup,
                string.IsNullOrWhiteSpace(_config.Repo)
                    ? "Choose a repository: right-click, then Configure."
                    : $"\"{_config.Repo.Trim()}\" is not a repository. Use owner/name.",
                false,
                null);
        }

        _layoutShape = Shape(ActualWidth, ActualHeight);

        if (!_chrome.Show(screen, ActualHeight) || activity is null)
        {
            _summary.Visibility = Visibility.Collapsed;
            _lists.Visibility = Visibility.Collapsed;
            _ci.Visibility = Visibility.Collapsed;
            PublishTitle(null);
            return;
        }

        PublishTitle(activity.Repo.FullName);
        ShowRun(activity.Runs.Count > 0 ? activity.Runs[0] : null);

        var width = ActualWidth;
        var height = ActualHeight;
        var side = SideBySide(width, height);
        var lists = Lists(width, height);

        Place(side);
        BuildSummary(activity, now, inline: side || width <= 0 || width >= StatsInlineWidth, meta: height <= 0 || height >= MetaNeedsHeight, side);

        _summary.Visibility = Visibility.Visible;
        _lists.Visibility = lists ? Visibility.Visible : Visibility.Collapsed;

        if (lists)
        {
            BuildLists(activity, now, DevRows.LayoutFor(side ? width - SummaryColumnWidth : width, 0, twoLineAllowed: false));
        }
    }

    /// <summary>Numbers over the lists, or beside them on a wide, short tile.</summary>
    private void Place(bool side)
    {
        SetRow(_summary, 0);
        SetColumn(_summary, 0);
        SetColumnSpan(_summary, side ? 1 : 2);
        SetRowSpan(_summary, side ? 2 : 1);
        _summary.Width = side ? SummaryColumnWidth : double.NaN;

        SetRow(_lists, side ? 0 : 1);
        SetColumn(_lists, side ? 1 : 0);
        SetColumnSpan(_lists, side ? 1 : 2);
        SetRowSpan(_lists, side ? 2 : 1);
    }

    private void ShowRun(RunItem? run)
    {
        if (run is null)
        {
            _ci.Visibility = Visibility.Collapsed;
            return;
        }

        _ciDot.Foreground = DevBrushes.Run(run.State);
        _ciText.Text = GitHubRules.RunLabel(run.State);
        _ciText.Visibility = ActualWidth <= 0 || ActualWidth >= CiWordNeedsWidth ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_ci, $"{run.Name} on {run.Branch}: {GitHubRules.RunLabel(run.State)}");
        _ci.Visibility = Visibility.Visible;
    }

    private void BuildSummary(RepoActivity activity, DateTimeOffset now, bool inline, bool meta, bool side)
    {
        _summary.Children.Clear();

        var repo = activity.Repo;
        var stats = new[]
        {
            (Glyphs.Star, GitHubRules.Compact(repo.Stars), "stars"),
            (Glyphs.PullRequest, GitHubRules.Compact(repo.Forks), "forks"),
            (Glyphs.Issue, GitHubRules.Compact(repo.OpenIssues), "open issues and pull requests"),
        };

        var line = new StackPanel { Orientation = inline && !side ? Orientation.Horizontal : Orientation.Vertical, Spacing = inline && !side ? Fluent.Space4 : 2 };

        foreach (var (glyph, value, what) in stats)
        {
            var icon = Fluent.Icon(glyph, "TextFillColorSecondaryBrush", 14);
            var text = Fluent.Text(FluentType.BodyStrong);
            text.Text = value;
            text.TextTrimming = TextTrimming.None;

            var stat = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2, Children = { icon, text } };
            ToolTipService.SetToolTip(stat, $"{value} {what}");
            line.Children.Add(stat);
        }

        _summary.Children.Add(line);

        if (activity.Release is { } release && (inline || side))
        {
            var tag = Fluent.Text(FluentType.Caption, "AccentTextFillColorPrimaryBrush");
            tag.Text = release.Tag;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space1,
                Children = { Fluent.Icon(Glyphs.Tag, "AccentTextFillColorPrimaryBrush", 12), tag },
            };
            ToolTipService.SetToolTip(row, $"Latest release {release.Tag}. Double-click to open.");
            row.DoubleTapped += (_, args) =>
            {
                DevRows.Open(release.HtmlUrl);
                args.Handled = true;
            };
            _summary.Children.Add(row);
        }

        if (meta)
        {
            var parts = new List<string>(3);
            if (repo.Language is { } language)
            {
                parts.Add(language);
            }

            if (repo.PushedAt is { } pushed)
            {
                parts.Add($"pushed {GitHubRules.Ago(pushed, now)}");
            }

            parts.Add(repo.DefaultBranch);

            var text = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            text.Text = string.Join(" · ", parts);
            text.TextWrapping = side ? TextWrapping.Wrap : TextWrapping.NoWrap;
            text.MaxLines = side ? 2 : 1;
            _summary.Children.Add(text);
        }
    }

    private void BuildLists(RepoActivity activity, DateTimeOffset now, DevRowLayout layout)
    {
        if (activity.Commits.Count > 0)
        {
            _list.Children.Add(DevRows.Section("Commits"));
            foreach (var commit in activity.Commits)
            {
                Add(new DevRowText(commit.Message, commit.Sha, DevBrushes.Secondary, string.Empty, GitHubRules.Ago(commit.At, now), false, commit.HtmlUrl), layout);
            }
        }

        if (activity.Pulls.Count > 0)
        {
            _list.Children.Add(DevRows.Section("Pull requests"));
            foreach (var pull in activity.Pulls)
            {
                Add(new DevRowText(pull.Title, $"#{pull.Number}", DevBrushes.Pull(pull.State, pull.Draft), string.Empty, GitHubRules.Ago(pull.CreatedAt, now), pull.Draft, pull.HtmlUrl), layout);
            }
        }

        if (activity.Issues.Count > 0)
        {
            _list.Children.Add(DevRows.Section("Issues"));
            foreach (var issue in activity.Issues)
            {
                Add(new DevRowText(issue.Title, $"#{issue.Number}", DevBrushes.Success, string.Empty, GitHubRules.Ago(issue.CreatedAt, now), false, issue.HtmlUrl), layout);
            }
        }

        var runs = activity.Runs.Take(_config.EffectiveRunCount).ToList();
        if (runs.Count > 0)
        {
            _list.Children.Add(DevRows.Section("Workflow runs"));
            foreach (var run in runs)
            {
                var title = run.Branch.Length > 0 ? $"{run.Name} · {run.Branch}" : run.Name;
                Add(new DevRowText(title, "●", DevBrushes.Run(run.State), string.Empty, GitHubRules.Ago(run.At, now), false, run.HtmlUrl), layout);
            }
        }
    }

    private void Add(DevRowText text, DevRowLayout layout) => _list.Children.Add(DevRows.Build(text, layout, _hovers));

    /// <summary>The repository's name in the card's header; the user's own label still wins (the card decides).</summary>
    private void PublishTitle(string? fullName) => _surface?.SetHeaderTitle(fullName);

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => [new(Glyphs.Refresh, "Refresh", () => _feed?.Refresh())];

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        SizeChanged -= OnSizeChanged;
        _chrome.Detach();
        ClearRows();
        _summary.Children.Clear();

        _clock?.Dispose();
        _clock = null;

        CloseFeed();

        if (_source is not null)
        {
            _source.AccountsChanged -= OnAccountsChanged;
            _source = null;
        }

        _surface = null;
    }
}
