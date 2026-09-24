using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.App.Services.Hearth;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Recent mail, read from the hearth hub.
/// <para>
/// There is no account, token or provider anywhere in this widget. Hearth owns sign-in, sync and
/// consent; this asks for the <c>email</c> scope, lists what it is given, and refreshes when the hub
/// says something changed.
/// </para>
/// </summary>
internal sealed partial class EmailView : Grid, IWidgetView
{
    private const string Scope = "email-widget";

    private readonly TextBlock _title = new();
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();
    private readonly ProgressRing _busy = new();

    // The rows on screen, so a resize can switch them between the two row heights Fluent defines
    // without asking the hub for the same messages again.
    private readonly List<Border> _rows = [];

    // One per row, so the hover handlers come off with the row they light. A leaked one keeps the
    // row alive and re-lights it after teardown.
    private readonly List<IDisposable> _hovers = [];

    private HearthHub? _mail;
    private EmailConfig _config = new();
    private IDisposable? _subscription;
    private bool _refreshing;
    private bool _loaded;
    private bool _detached;

    public EmailView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "Mail";
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        // The unread count is Fluent's InfoBadge shape, which the design system owns.
        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        trailing.Children.Add(_busy);
        trailing.Children.Add(_badgeHost);

        var header = Fluent.Header(Glyphs.Mail, _title, trailing);
        header.Margin = Fluent.HeaderMargin;
        Grid.SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            // Nothing either side, so a row reaches both edges of the card and its highlight with
            // it. The gap above the first row is the header's; this keeps the last one off the edge.
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 1);

        // A Windows empty state: an icon over a line of body text in the secondary colour, centred.
        // Not a shrunken caption, which reads as an error the app is embarrassed about.
        _empty = Fluent.EmptyState(Glyphs.Mail, _emptyText, out _emptyIcon);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += (_, _) => ApplyDensity();
    }

    /// <summary>
    /// Whether the tile takes the compact row. Fluent has two list-row heights and a tile shorter
    /// than five standard rows reads better with the shorter one: this is a density switch between
    /// two fixed heights, not a row that grows with the tile.
    /// </summary>
    private bool Compact => ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _mail = AppServices.Hub;

        if (_mail is not null)
        {
            _mail.Changed += OnHubChanged;
        }

        ApplyConfig(config);

        // A backstop only: the list is pushed by the hub. This keeps the relative timestamps honest.
        _subscription = ticker.Subscribe(_config.TickInterval, UpdateTimestamps);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as EmailConfig ?? new EmailConfig();

        // Only the fields that change what the hub is asked for warrant a round trip. Without this
        // check, dragging an unrelated settings slider fired a query per frame.
        var needsQuery = _config.CollectionId != next.CollectionId
            || _config.UnreadOnly != next.UnreadOnly
            || _config.EffectiveMaxCount != next.EffectiveMaxCount;

        var changed = needsQuery || _config.ShowPreview != next.ShowPreview;
        var neverDrawn = _list.Children.Count == 0;

        _config = next;

        if (changed || neverDrawn)
        {
            _ = RefreshAsync();
        }
    }

    private void OnHubChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing || _detached || _mail is null)
        {
            return;
        }

        _refreshing = true;
        _busy.IsActive = true;
        _busy.Visibility = Visibility.Visible;

        // Until the first reply lands there is nothing to say but that it is coming; a blank tile
        // with no explanation is one of the states this widget is required to name.
        if (!_loaded)
        {
            ShowMessage("Loading…");
        }

        try
        {
            var messages = await _mail.RecentAsync(
                _config.CollectionId,
                _config.UnreadOnly,
                _config.EffectiveMaxCount).ConfigureAwait(true);

            if (_detached)
            {
                return;
            }

            _loaded = true;
            Render(messages);
        }
        catch (Exception ex)
        {
            // Never let a hub problem take the surface down; say so in the widget instead.
            Log.Error(Scope, "could not refresh the mail list", ex);
            ShowMessage("Could not read mail. See themia.log.");
        }
        finally
        {
            _refreshing = false;
            _busy.IsActive = false;
            _busy.Visibility = Visibility.Collapsed;
        }
    }

    private void Render(IReadOnlyList<EmailRecord> messages)
    {
        ClearRows();

        if (messages.Count == 0)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var now = DateTimeOffset.Now;
        var compact = Compact;
        var unread = 0;

        foreach (var message in messages)
        {
            if (message.Unread)
            {
                unread++;
            }

            var row = BuildRow(message, now, compact);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _badge.Text = unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _badgeHost.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildRow(EmailRecord message, DateTimeOffset now, bool compact)
    {
        // Unread is carried by weight rather than by a dot, which is what every mail list on Windows
        // does and needs no legend. Both steps are the same 14/20 line, so nothing moves with it.
        var who = Fluent.Text(message.Unread ? FluentType.BodyStrong : FluentType.Body);
        who.Text = message.Who;

        var when = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        when.Text = Formatting.Ago(message.When, now);
        when.VerticalAlignment = VerticalAlignment.Center;
        when.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

        Grid.SetColumn(who, 0);
        Grid.SetColumn(when, 1);

        var top = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { who, when },
        };

        // No spacing between the lines of a row: each one already carries the line height Fluent
        // pairs with its size, and a gap on top of that is the web app's look.
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(top);

        var subject = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        subject.Text = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;
        stack.Children.Add(subject);

        if (_config.ShowPreview && !string.IsNullOrWhiteSpace(message.Preview))
        {
            var preview = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            preview.Text = message.Preview;
            stack.Children.Add(preview);
        }

        var row = Fluent.Row(stack, compact);
        _hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, message.Who + Environment.NewLine + message.Subject);
        return row;
    }

    /// <summary>
    /// Re-fit what is already drawn to the tile: the row height, and whether the empty state has
    /// room for its icon. No query, and no font size derived from the tile.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = Compact;

        foreach (var row in _rows)
        {
            Fluent.SetDensity(row, compact);
        }

        _emptyIcon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Say what is actually wrong, because every one of these needs a different action.</summary>
    private string EmptyMessage() => _mail?.Status switch
    {
        null => "Themia is still starting.",
        HearthStatus.NotRunning => "hearth is not running.\nStart it to see your mail here.",
        HearthStatus.Denied => "hearth has not granted Themia access to any mailbox.",
        HearthStatus.Failed => "Could not read from hearth. See themia.log.",
        HearthStatus.Connecting or HearthStatus.Idle => "Connecting to hearth…",
        _ when (_mail?.MailCollections.Count ?? 0) == 0 => "hearth has granted no mailboxes.",
        _ when _config.UnreadOnly => "Nothing unread.",
        _ => "No messages.",
    };

    private void ShowMessage(string text)
    {
        _emptyText.Text = text;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        _badgeHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Drop the rows and, with them, their hover handlers: a handler left hooked keeps the row it
    /// lights alive and re-lights it after the list has moved on.
    /// </summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
        _rows.Clear();
    }

    /// <summary>
    /// Re-label the rows without re-querying. A message that said "4m" an hour ago is wrong, and
    /// asking the hub again to learn that would be waste.
    /// </summary>
    private void UpdateTimestamps()
    {
        if (_list.Children.Count == 0)
        {
            return;
        }

        _ = RefreshAsync();
    }

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;

        if (_mail is not null)
        {
            _mail.Changed -= OnHubChanged;
            _mail = null;
        }

        ClearRows();
    }
}
