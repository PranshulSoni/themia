using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Services.Mail;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Mail;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-540] Recent mail from the accounts signed in to Themia: Microsoft (Graph), Google (Gmail) and
/// IMAP, merged into one list.
/// <para>
/// The view holds no account, token or socket. It opens a feed on the app's one
/// <see cref="IMailSource"/> for its accounts, folder and count, draws what the feed holds, and
/// redraws when the feed or the accounts change. Fetching, the cadence and the back-off are the
/// service's ([S-546]); which screen to show is Core's (<see cref="MailScreens"/>); how much detail
/// fits the tile is Core's too (<see cref="MailLayout"/>).
/// </para>
/// <para>
/// Double-click opens a message, right-click offers what its account allows ([S-544]). Unread is
/// carried by weight and an accent bar at the row's leading edge, the way Windows mail lists mark
/// it, so it needs no legend.
/// </para>
/// </summary>
internal sealed partial class EmailView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    private const string Scope = "email-widget";

    /// <summary>The leading strip of a row that carries the unread bar, inside the row's own padding.</summary>
    private const double BarLane = 12;

    /// <summary>Below this width the header has no room for a warning beside the title and the badge.</summary>
    private const double WarningNeedsWidth = 160;

    /// <summary>Below this content height the accounts link under an empty state has no room.</summary>
    private const double LinkNeedsHeight = 100;

    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly FontIcon _warning;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _trailing;

    private readonly Grid _columnHeader = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;

    private readonly Grid _emptyHost = new();
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly HyperlinkButton _accountsLink;

    private readonly List<IDisposable> _hovers = [];
    private readonly List<(TextBlock Text, DateTimeOffset? When, bool Full)> _times = [];

    private IMailSource? _source;
    private IMailFeed? _feed;
    private string? _feedKey;
    private IWidgetSurface? _surface;
    private IDisposable? _clock;
    private EmailConfig _config = new();
    private string? _actionProblem;
    private bool _warningOpensAccounts;
    private bool _refreshing;
    private bool _detached;

    // The size the rows were last built for, so a resize redraws only when a threshold is crossed.
    private MailRowLayout _rowLayout;
    private string _columnsShape = string.Empty;
    private bool _compact;

    public EmailView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _warning = Fluent.Icon(Glyphs.Warning, "SystemFillColorCautionBrush", 14);
        _warning.Visibility = Visibility.Collapsed;
        _warning.Tapped += OnWarningTapped;

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        // Read once by the card when it builds the header, so it exists from construction.
        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _warning, _busy, _badgeHost },
        };

        _columnHeader.Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space1);
        _columnHeader.Visibility = Visibility.Collapsed;
        Grid.SetRow(_columnHeader, 0);

        _scroll = new ScrollViewer
        {
            // Nothing either side, so a row and its hover reach both card edges.
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Grid.SetRow(_scroll, 1);

        _empty = Fluent.EmptyState(Glyphs.Mail, _emptyText, out _);

        _accountsLink = new HyperlinkButton
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1),
            Visibility = Visibility.Collapsed,
        };
        _accountsLink.Click += OnAccountsClicked;

        // The sentence and the link, centred together. The sentence's panel sheds its icon and
        // lines against a height cap set on resize, because an Auto row measures without one.
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_accountsLink, 2);
        _emptyHost.Children.Add(_empty);
        _emptyHost.Children.Add(_accountsLink);
        _emptyHost.Visibility = Visibility.Collapsed;
        Grid.SetRow(_emptyHost, 0);
        Grid.SetRowSpan(_emptyHost, 2);

        Children.Add(_columnHeader);
        Children.Add(_scroll);
        Children.Add(_emptyHost);

        SizeChanged += OnSizeChanged;
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _trailing;

    private bool Compact => ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;

    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle();
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        _source = AppServices.Mail;

        if (_source is not null)
        {
            _source.AccountsChanged += OnAccountsChanged;
        }

        ApplyConfig(config);

        // Only the relative times ("4m") move between fetches; the feed itself is pushed.
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), UpdateTimes);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as EmailConfig ?? new EmailConfig();
        PublishTitle();
        Reopen();
        Render();
    }

    /// <summary>Open the feed this config asks for, letting go of the old one only if it changed.</summary>
    private void Reopen()
    {
        if (_source is null)
        {
            return;
        }

        var query = new MailQuery(_config.EffectiveAccountIds, _config.Folder, _config.EffectiveMaxCount);
        if (_feed is not null && query.Key == _feedKey)
        {
            return;
        }

        CloseFeed();
        _feedKey = query.Key;
        _feed = _source.Open(query);
        _feed.Updated += OnFeedUpdated;
    }

    private void CloseFeed()
    {
        if (_feed is null)
        {
            return;
        }

        _feed.Updated -= OnFeedUpdated;
        _feed.Dispose();
        _feed = null;
        _feedKey = null;
    }

    private void OnFeedUpdated()
    {
        if (_refreshing && _feed is { Snapshot.Busy: false })
        {
            _refreshing = false;
        }

        _actionProblem = null;
        Render();
    }

    private void OnAccountsChanged() => Render();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = Compact;
        var layout = MailLayout.Rows(_config, args.NewSize.Width, args.NewSize.Height);
        var columns = ColumnsShape(args.NewSize.Width);

        FitEmptyState(args.NewSize.Height);

        if (compact != _compact || layout != _rowLayout || columns != _columnsShape
            || (args.PreviousSize.Width < WarningNeedsWidth) != (args.NewSize.Width < WarningNeedsWidth))
        {
            Render();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------------------------

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var accounts = _source?.Accounts ?? [];
        var snapshot = _feed?.Snapshot ?? MailFeedSnapshot.Empty;
        var messages = _config.SortOrder == MailSortOrder.OldestFirst ? Enumerable.Reverse(snapshot.Messages).ToList() : snapshot.Messages;
        var screen = MailScreens.Decide(accounts, _config.EffectiveAccountIds, snapshot.Outcomes, messages.Count, _config.Folder);

        _busy.IsActive = _refreshing;
        _busy.Visibility = _refreshing ? Visibility.Visible : Visibility.Collapsed;

        var warning = _actionProblem ?? screen.Warning;
        _warningOpensAccounts = _actionProblem is null && screen.OffersAccounts;
        // On a tile this narrow the glyph would squeeze the folder name to a letter; the warning waits.
        _warning.Visibility = warning is null || ActualWidth < WarningNeedsWidth ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_warning, warning);

        if (screen.Kind != MailScreenKind.Messages)
        {
            ShowScreen(screen);
            return;
        }

        _emptyHost.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var unread = MailList.Unread(messages);
        _badge.Text = unread.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;

        var labels = accounts.ToDictionary(a => a.Id, a => a.Label, StringComparer.Ordinal);
        var now = DateTimeOffset.Now;

        _compact = Compact;
        _rowLayout = MailLayout.Rows(_config, ActualWidth, ActualHeight);
        _columnsShape = ColumnsShape(ActualWidth);

        if (_config.ViewMode == MailViewMode.Row)
        {
            var columns = MailLayout.Columns(_config.EffectiveColumns, InnerWidth, Fluent.Space2);
            BuildColumnHeader(columns);

            foreach (var message in messages)
            {
                AddRow(BuildTableRow(message, columns, labels, now), message);
            }
        }
        else
        {
            _columnHeader.Visibility = Visibility.Collapsed;

            foreach (var message in messages)
            {
                AddRow(BuildListRow(message, labels, now), message);
            }
        }
    }

    private void ShowScreen(MailScreen screen)
    {
        _scroll.Visibility = Visibility.Collapsed;
        _columnHeader.Visibility = Visibility.Collapsed;
        _badgeHost.Visibility = Visibility.Collapsed;
        _emptyHost.Visibility = Visibility.Visible;
        _emptyText.Text = screen.Text;
        _accountsLink.Tag = screen.OffersAccounts;
        _accountsLink.Content = screen.Kind switch
        {
            MailScreenKind.NoAccounts => "Add a mail account",
            MailScreenKind.SignInRequired => "Sign in again",
            _ => "Choose an account",
        };
        FitEmptyState(ActualHeight);
    }

    /// <summary>The link shows when the screen offers it and there is room; the sentence gets the rest.</summary>
    private void FitEmptyState(double height)
    {
        var offers = _accountsLink.Tag is true;
        var link = offers && (height <= 0 || height >= LinkNeedsHeight);
        _accountsLink.Visibility = link ? Visibility.Visible : Visibility.Collapsed;
        _empty.MaxHeight = height > 0 ? Math.Max(0, height - (link ? 36 : 0)) : double.PositiveInfinity;
    }

    /// <summary>The row's width inside its padding: what the text has to share.</summary>
    private double InnerWidth => Math.Max(0, ActualWidth - (2 * Fluent.Space4));

    private string ColumnsShape(double width)
        => _config.ViewMode != MailViewMode.Row
            ? string.Empty
            : string.Join(",", MailLayout.Columns(_config.EffectiveColumns, Math.Max(0, width - (2 * Fluent.Space4)), Fluent.Space2).Select(c => c.Column));

    /// <summary>
    /// A list or compact row: sender and time; then the subject with the account beside it; then,
    /// in the list view, the preview.
    /// </summary>
    private Grid BuildListRow(MailMessage message, Dictionary<string, string> labels, DateTimeOffset now)
    {
        var layout = _rowLayout;
        var unread = !message.IsRead;

        var lines = new StackPanel { Spacing = 0 };

        var sender = Fluent.Text(unread ? FluentType.BodyStrong : FluentType.Body);
        sender.Text = message.Sender;

        if (layout.Time)
        {
            var when = Fluent.Text(FluentType.Caption, unread ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
            when.Text = MailTime.Short(message.Received, now, CultureInfo.CurrentCulture);
            when.VerticalAlignment = VerticalAlignment.Center;
            when.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            _times.Add((when, message.Received, false));
            lines.Children.Add(TwoColumns(sender, when));
        }
        else
        {
            lines.Children.Add(sender);
        }

        var account = layout.Account && labels.TryGetValue(message.AccountId, out var label) ? label : null;

        if (layout.Subject || account is not null)
        {
            var subject = Fluent.Text(FluentType.Caption, unread ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            subject.Text = layout.Subject ? message.SubjectLine : string.Empty;

            if (account is not null)
            {
                var tag = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                tag.Text = account;
                tag.MaxWidth = 120;
                tag.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
                lines.Children.Add(TwoColumns(subject, tag));
            }
            else
            {
                lines.Children.Add(subject);
            }
        }

        if (layout.Preview && !string.IsNullOrWhiteSpace(message.Preview))
        {
            var preview = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            preview.Text = message.Preview;
            preview.TextWrapping = TextWrapping.Wrap;
            preview.MaxLines = layout.PreviewLines;
            lines.Children.Add(preview);
        }

        return WithBar(lines, unread);
    }

    /// <summary>A row-view row: one line, the chosen columns that fit.</summary>
    private Grid BuildTableRow(MailMessage message, IReadOnlyList<MailColumnFit> columns, Dictionary<string, string> labels, DateTimeOffset now)
    {
        var unread = !message.IsRead;
        var grid = ColumnGrid(columns);

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i].Column;
            var cell = column switch
            {
                MailColumn.From => Fluent.Text(unread ? FluentType.BodyStrong : FluentType.Body),
                MailColumn.Subject => Fluent.Text(FluentType.Body, unread ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
                MailColumn.Date => Fluent.Text(FluentType.Caption, unread ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
                _ => Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush"),
            };

            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.Text = column switch
            {
                MailColumn.From => message.Sender,
                MailColumn.Subject => message.SubjectLine,
                MailColumn.Date => MailTime.Full(message.Received, now, CultureInfo.CurrentCulture),
                MailColumn.Preview => message.Preview,
                _ => labels.TryGetValue(message.AccountId, out var label) ? label : string.Empty,
            };

            if (column == MailColumn.Date)
            {
                cell.TextAlignment = TextAlignment.Right;
                _times.Add((cell, message.Received, true));
            }

            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return WithBar(grid, unread);
    }

    /// <summary>[S-542] The row view's column titles, laid out on the same grid as its rows.</summary>
    private void BuildColumnHeader(IReadOnlyList<MailColumnFit> columns)
    {
        _columnHeader.Children.Clear();
        _columnHeader.ColumnDefinitions.Clear();

        var template = ColumnGrid(columns);
        foreach (var definition in template.ColumnDefinitions.ToList())
        {
            template.ColumnDefinitions.Remove(definition);
            _columnHeader.ColumnDefinitions.Add(definition);
        }

        _columnHeader.ColumnSpacing = template.ColumnSpacing;

        for (var i = 0; i < columns.Count; i++)
        {
            var title = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            title.Text = columns[i].Column.Label();
            if (columns[i].Column == MailColumn.Date)
            {
                title.TextAlignment = TextAlignment.Right;
            }

            Grid.SetColumn(title, i);
            _columnHeader.Children.Add(title);
        }

        // A header over two rows of data is a third of the tile; drop it when there is no room.
        _columnHeader.Visibility = ActualHeight >= 140 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid ColumnGrid(IReadOnlyList<MailColumnFit> columns)
    {
        var grid = new Grid { ColumnSpacing = Fluent.Space2 };

        foreach (var column in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column.Star > 0 ? new GridLength(column.Star, GridUnitType.Star) : new GridLength(column.Width),
            });
        }

        return grid;
    }

    private static Grid TwoColumns(FrameworkElement left, FrameworkElement right)
    {
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { left, right },
        };
    }

    /// <summary>
    /// The content with the unread lane in front of it. The lane sits inside the row's leading
    /// padding (a negative margin pulls it there), so read and unread rows keep their text on the
    /// same line and the bar costs no width.
    /// </summary>
    private static Grid WithBar(FrameworkElement content, bool unread)
    {
        var grid = new Grid
        {
            Margin = new Thickness(-BarLane, 0, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(BarLane) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        if (unread)
        {
            var bar = new Rectangle
            {
                Width = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(Fluent.Space1, Fluent.Space1, 0, Fluent.Space1),
            };

            grid.Children.Add(bar);
        }

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private void AddRow(Grid content, MailMessage message)
    {
        var row = Fluent.Row(content, _compact);

        if (_config.ViewMode == MailViewMode.Row)
        {
            row.MinHeight = Fluent.RowHeightCompact;
        }
        else
        {
            // As tall as its lines, with the breathing room a row of text needs; no taller.
            row.MinHeight = 0;
            content.Margin = new Thickness(-BarLane, _compact ? Fluent.Space1 : 6, 0, _compact ? Fluent.Space1 : 6);
        }

        _hovers.Add(Fluent.Hover(row));

        row.DoubleTapped += (_, args) =>
        {
            args.Handled = true;
            OpenMessage(message);
        };

        row.RightTapped += (_, args) =>
        {
            args.Handled = true;
            ShowMessageMenu(row, message, args);
        };

        ToolTipService.SetToolTip(row, Tooltip(message));
        _list.Children.Add(row);
    }

    private static string Tooltip(MailMessage message)
    {
        var from = message.FromAddress.Length > 0 && message.FromName.Length > 0
            ? $"{message.FromName} <{message.FromAddress}>"
            : message.Sender;

        var when = message.Received is { } at ? Environment.NewLine + at.ToLocalTime().ToString("f", CultureInfo.CurrentCulture) : string.Empty;
        return from + Environment.NewLine + message.SubjectLine + when;
    }

    /// <summary>Re-label the times without rebuilding a row: "4m" an hour later is wrong, refetching to learn that is waste.</summary>
    private void UpdateTimes()
    {
        var now = DateTimeOffset.Now;

        foreach (var (text, when, full) in _times)
        {
            text.Text = full
                ? MailTime.Full(when, now, CultureInfo.CurrentCulture)
                : MailTime.Short(when, now, CultureInfo.CurrentCulture);
        }
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _times.Clear();
        _list.Children.Clear();
    }

    // -----------------------------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------------------------

    private void OpenMessage(MailMessage message)
    {
        if (_source?.Open(message) == false)
        {
            ShowProblem(message.Provider == Themia.Core.Accounts.AccountProvider.Imap
                ? "Mail server (IMAP) messages have no web page to open. Use Reply, or your mail app."
                : "Could not open that message.");
        }
    }

    /// <summary>[S-544] A message's own commands, offered only where its account allows them.</summary>
    private void ShowMessageMenu(FrameworkElement anchor, MailMessage message, RightTappedRoutedEventArgs args)
    {
        var menu = new MenuFlyout();
        var account = _source?.Accounts.FirstOrDefault(a => a.Id == message.AccountId);
        var canChange = account is { ReadOnly: false };

        menu.Items.Add(Item(Glyphs.Open, "Open", () => OpenMessage(message)));
        menu.Items.Add(Item(Glyphs.MailReply, "Reply", () =>
        {
            if (_source?.Reply(message) == false)
            {
                ShowProblem("There is no address to reply to.");
            }
        }));

        if (canChange)
        {
            menu.Items.Add(message.IsRead
                ? Item(Glyphs.Unread, "Mark as unread", () => Change(message, MailChange.MarkUnread))
                : Item(Glyphs.MailRead, "Mark as read", () => Change(message, MailChange.MarkRead)));

            if (message.Folder != MailFolder.Junk)
            {
                menu.Items.Add(Item(Glyphs.Archive, "Archive", () => Change(message, MailChange.Archive)));
                menu.Items.Add(Item(Glyphs.Junk, "Move to Junk", () => Change(message, MailChange.Junk)));
            }
        }

        menu.Items.Add(Item(Glyphs.Copy, "Copy subject", () =>
        {
            if (!ClipboardWatcher.SetText(message.SubjectLine))
            {
                ShowProblem("Could not copy to the clipboard.");
            }
        }));

        if (canChange)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(Glyphs.Delete,
                message.Folder == MailFolder.Deleted ? "Delete permanently" : "Delete",
                () => Change(message, MailChange.Delete)));
        }

        menu.ShowAt(anchor, args.GetPosition(anchor));
    }

    private static MenuFlyoutItem Item(string glyph, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private async void Change(MailMessage message, MailChange change)
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            var problem = await _source.ChangeAsync(message, change).ConfigureAwait(true);
            if (problem is not null)
            {
                ShowProblem(problem);
            }
        }
        catch (Exception ex)
        {
            // async void from a menu click: nothing above this may see an exception.
            Log.Error(Scope, $"{change} failed", ex);
            ShowProblem("That did not work. See themia.log.");
        }
    }

    /// <summary>A failed command, on the header's warning until the next update replaces it.</summary>
    private void ShowProblem(string problem)
    {
        if (_detached)
        {
            return;
        }

        _actionProblem = problem;
        _warningOpensAccounts = false;
        _warning.Visibility = ActualWidth < WarningNeedsWidth ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_warning, problem);
    }

    /// <summary>A warning about an account that needs signing in leads to where that is done.</summary>
    private void OnWarningTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_warningOpensAccounts)
        {
            args.Handled = true;
            OnAccountsClicked(sender, args);
        }
    }

    private void OnAccountsClicked(object sender, RoutedEventArgs args)
    {
        if (AppServices.ShowAccounts is { } show)
        {
            show();
        }
        else
        {
            Log.Info(Scope, "no settings window here to show the Accounts page in");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            // [S-542] The classic menu: refresh, the view, the folder, the order. The current choice
            // carries a check, the others their own icon.
            var actions = new List<WidgetAction>
            {
                new(Glyphs.Refresh, "Refresh", Refresh),
            };

            foreach (var (mode, label, glyph) in new[]
            {
                (MailViewMode.List, "List view", Glyphs.Preview),
                (MailViewMode.Compact, "Compact view", Glyphs.Count),
                (MailViewMode.Row, "Row view", Glyphs.Grid),
            })
            {
                actions.Add(new(_config.ViewMode == mode ? Glyphs.Done : glyph, label, () => Edit(c => c.ViewMode = mode)));
            }

            foreach (var folder in MailFolders.Listable)
            {
                actions.Add(new(_config.Folder == folder ? Glyphs.Done : FolderGlyph(folder), folder.Label(), () => Edit(c => c.Folder = folder)));
            }

            actions.Add(_config.SortOrder == MailSortOrder.NewestFirst
                ? new(Glyphs.Sort, "Show oldest first", () => Edit(c => c.SortOrder = MailSortOrder.OldestFirst))
                : new(Glyphs.Sort, "Show newest first", () => Edit(c => c.SortOrder = MailSortOrder.NewestFirst)));

            return actions;
        }
    }

    internal static string FolderGlyph(MailFolder folder) => folder switch
    {
        MailFolder.Sent => Glyphs.Send,
        MailFolder.Drafts => Glyphs.Edit,
        MailFolder.Deleted => Glyphs.Delete,
        MailFolder.Junk => Glyphs.Junk,
        _ => Glyphs.Mail,
    };

    private void Refresh()
    {
        if (_feed is null)
        {
            return;
        }

        _refreshing = true;
        _feed.Refresh();
        Render();
    }

    /// <summary>Change this widget's own config from its menu, save it, and redraw.</summary>
    private void Edit(Action<EmailConfig> change)
    {
        change(_config);
        _surface?.RequestSave();
        PublishTitle();
        Reopen();
        Render();
    }

    /// <summary>[S-542] The card's title names the folder; the user's own label still wins (the card decides).</summary>
    private void PublishTitle() => _surface?.SetHeaderTitle(_config.Folder.Label());

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        SizeChanged -= OnSizeChanged;
        _warning.Tapped -= OnWarningTapped;
        _accountsLink.Click -= OnAccountsClicked;

        _clock?.Dispose();
        _clock = null;

        CloseFeed();

        if (_source is not null)
        {
            _source.AccountsChanged -= OnAccountsChanged;
            _source = null;
        }

        ClearRows();
    }
}
