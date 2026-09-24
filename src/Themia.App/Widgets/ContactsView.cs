using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Hearth;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// People, read from the hearth hub.
/// <para>
/// There is no account, token or provider anywhere in this widget, and no connection of its own
/// either: hearth owns sign-in, sync and consent, and the app's one connection is reached through
/// <see cref="AppServices.Data"/>. This lists the address books hearth granted and refreshes when
/// the hub says something changed.
/// </para>
/// </summary>
internal sealed partial class ContactsView : Grid, IWidgetView
{
    private const string Scope = "contacts-widget";

    private readonly TextBlock _title = new();
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _emptyText = new();
    private readonly StackPanel _empty;

    // What a resize has to re-fit: the rows themselves, and the avatar, which is sized from the row
    // height rather than from the tile. Everything else in a row is a step of the ramp or of the
    // 4px grid, so a resize cannot change it.
    private readonly List<Border> _avatars = [];
    private readonly List<Border> _rows = [];

    // One per row, so the hover handlers come off with the row they light. A leaked one keeps the
    // row alive and re-lights it after teardown.
    private readonly List<IDisposable> _hovers = [];

    private HearthDataService? _data;
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private ContactsConfig _config = new();
    private IReadOnlyList<ContactRecord> _records = [];
    private bool _refreshing;
    private bool _requeryWanted;
    private bool _loaded;
    private bool _detached;

    public ContactsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "People";
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        // The count is Fluent's InfoBadge shape, which the design system owns.
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

        var header = Fluent.Header(Glyphs.Contact, _title, trailing);
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
        _empty = Fluent.EmptyState(Glyphs.Contact, _emptyText, out _emptyIcon);
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
        _ticker = ticker;
        _data = AppServices.Data;

        if (_data is not null)
        {
            _data.Changed += OnHubChanged;
        }

        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as ContactsConfig ?? new ContactsConfig();

        // Only the fields that change what the hub is asked for warrant a round trip. Without this
        // check, dragging an unrelated settings slider fired a query per frame.
        var needsQuery = _config.CollectionId != next.CollectionId
            || _config.EffectiveMaxCount != next.EffectiveMaxCount;

        // These two change only how the records already in hand are drawn, so they re-render.
        var redraws = _config.ShowDetail != next.ShowDetail || _config.ShowAvatar != next.ShowAvatar;

        _config = next;
        Resubscribe();

        if (needsQuery || !_loaded)
        {
            _ = RefreshAsync();
        }
        else if (redraws)
        {
            Render(_records);
        }
    }

    private void OnHubChanged() => _ = RefreshAsync();

    /// <summary>
    /// Re-subscribe only when the cadence actually changed. Dropping and remaking the subscription
    /// on every config touch made dragging a settings slider stutter, because each frame tore down
    /// the timer subscription for every widget on the desktop and built it again.
    /// </summary>
    private void Resubscribe()
    {
        if (_detached)
        {
            return;
        }

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
    /// The hub pushes changes, so this is a backstop with one job worth having: a hub that was not
    /// running when the desktop started is noticed within a tick of being started, rather than
    /// leaving the widget saying so until something else happens to poke it.
    /// </summary>
    private void OnTick() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_detached)
        {
            return;
        }

        // The host builds the connection before any surface exists, so this is only reachable in a
        // teardown race - but a widget that finds no service must still say so rather than sit
        // blank with no explanation.
        if (_data is null)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        // A second request while one is in flight is not dropped: the settings window can change
        // the collection mid-query, and swallowing that would leave the list showing the old one.
        if (_refreshing)
        {
            _requeryWanted = true;
            return;
        }

        _refreshing = true;
        _busy.IsActive = true;
        _busy.Visibility = Visibility.Visible;

        if (!_loaded)
        {
            ShowMessage("Loading…");
        }

        try
        {
            var contacts = await _data.ContactsAsync(
                _config.CollectionId,
                null,
                _config.EffectiveMaxCount).ConfigureAwait(true);

            if (_detached)
            {
                return;
            }

            _loaded = true;
            _records = contacts;
            Render(contacts);
        }
        catch (Exception ex)
        {
            // Never let a hub problem take the surface down; say so in the widget instead.
            Log.Error(Scope, "could not refresh the contact list", ex);
            ShowMessage("Could not read contacts. See themia.log.");
        }
        finally
        {
            _refreshing = false;
            _busy.IsActive = false;
            _busy.Visibility = Visibility.Collapsed;

            if (_requeryWanted && !_detached)
            {
                _requeryWanted = false;
                _ = RefreshAsync();
            }
        }
    }

    private void Render(IReadOnlyList<ContactRecord> contacts)
    {
        ClearRows();

        if (contacts.Count == 0)
        {
            ShowMessage(EmptyMessage());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var compact = Compact;

        foreach (var contact in contacts)
        {
            var row = BuildRow(contact, compact);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _badge.Text = contacts.Count.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = Visibility.Visible;

        // The avatars have no size until this runs, and a first render need not be followed by a
        // resize, so it is called here rather than left to SizeChanged alone.
        ApplyDensity();
    }

    private Border BuildRow(ContactRecord contact, bool compact)
    {
        var name = Fluent.Text(FluentType.BodyStrong);
        name.Text = contact.Who;
        name.VerticalAlignment = VerticalAlignment.Center;

        // No spacing between the lines of a row: each one already carries the line height Fluent
        // pairs with its size, and a gap on top of that is the web app's look.
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);

        var detail = _config.ShowDetail ? DetailLine(contact) : string.Empty;
        if (detail.Length != 0)
        {
            var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            line.Text = detail;
            text.Children.Add(line);
        }

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        if (_config.ShowAvatar)
        {
            var monogram = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            monogram.Text = Monogram(contact.Who);
            monogram.HorizontalAlignment = HorizontalAlignment.Center;
            monogram.VerticalAlignment = VerticalAlignment.Center;

            // A circle, sized in ApplyDensity from the row height - never from the tile, which is
            // what made it a different size on every widget on the desktop.
            var avatar = new Border
            {
                Child = monogram,
                Background = FluentLayout.Themed("ControlAltFillColorSecondaryBrush", Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Fluent.Space3, 0),
            };

            _avatars.Add(avatar);

            Grid.SetColumn(avatar, 0);
            body.Children.Add(avatar);
        }

        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var row = Fluent.Row(body, compact);
        _hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, Tooltip(contact));
        return row;
    }

    /// <summary>
    /// The row's second line: the address, then the number, then the job or employer - or nothing
    /// at all, so a contact with none of the three gets one line rather than an empty second one.
    /// <para>
    /// The address leads because a contact is usually reached at one of the two, and leading with
    /// the job title would answer a question the row did not ask.
    /// </para>
    /// </summary>
    private static string DetailLine(ContactRecord contact)
    {
        if (!string.IsNullOrWhiteSpace(contact.PrimaryEmail))
        {
            return contact.PrimaryEmail;
        }

        return !string.IsNullOrWhiteSpace(contact.PrimaryPhone) ? contact.PrimaryPhone : contact.Detail;
    }

    /// <summary>Everything the row had to trim, on hover.</summary>
    private static string Tooltip(ContactRecord contact)
    {
        var lines = new List<string> { contact.Who };

        foreach (var line in new[] { contact.PrimaryEmail, contact.PrimaryPhone, contact.Detail })
        {
            if (!string.IsNullOrWhiteSpace(line) && !lines.Contains(line, StringComparer.Ordinal))
            {
                lines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Monogram(string who) => Formatting.Monogram(who);

    /// <summary>
    /// Re-fit what is already drawn to the tile: the row height, the avatar that is sized from it,
    /// and whether the empty state has room for its icon. No query, and no font size derived from
    /// the tile — every size in a row is a step of the ramp or of the 4px grid.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = Compact;
        var height = compact ? Fluent.RowHeightCompact : Fluent.RowHeight;

        // The circle leaves one step of the 4px grid above and below it inside the row: 32 on the
        // standard row, 24 on the compact one, which is the pair of sizes Fluent's own person
        // picture uses. Sized from the row, never from the tile.
        var avatarSize = height - Fluent.Space2;

        foreach (var row in _rows)
        {
            Fluent.SetDensity(row, compact);
        }

        foreach (var avatar in _avatars)
        {
            avatar.Width = avatarSize;
            avatar.Height = avatarSize;
            avatar.CornerRadius = new CornerRadius(avatarSize / 2);
        }

        _emptyIcon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Say what is actually true, because every one of these needs a different action.</summary>
    private string EmptyMessage()
    {
        var granted = _data?.ContactCollections.Count ?? 0;

        return _data?.Status switch
        {
            null => "Themia is still starting.",
            HearthStatus.NotRunning => "hearth is not running.\nStart it to see your contacts here.",
            HearthStatus.Denied => "hearth has not granted Themia access to any address book.",
            HearthStatus.Failed => "Could not read from hearth. See themia.log.",
            HearthStatus.Connecting or HearthStatus.Idle => "Connecting to hearth…",
            _ when granted == 0 => "hearth has granted no address books.",
            _ => "No contacts.",
        };
    }

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
        _avatars.Clear();
    }

    public void Detach()
    {
        _detached = true;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        if (_data is not null)
        {
            _data.Changed -= OnHubChanged;
            _data = null;
        }

        _ticker = null;
        _records = [];
        ClearRows();
    }
}
