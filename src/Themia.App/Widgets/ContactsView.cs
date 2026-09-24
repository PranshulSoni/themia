using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    /// <summary>
    /// A shared transparent brush for the rows' rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so a row would never
    /// raise the pointer events its hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    private readonly FontIcon _icon = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly ProgressRing _busy = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _emptyText = new();
    private readonly StackPanel _empty = new();

    // What a resize has to re-size, kept by role. A widget is dragged to any size, so the rows are
    // measured from the tile rather than fixed - and holding the pieces means a resize re-sizes
    // what is already on screen instead of asking the hub for the same records again.
    private readonly List<TextBlock> _primaryText = [];
    private readonly List<TextBlock> _secondaryText = [];
    private readonly List<Border> _avatars = [];
    private readonly List<TextBlock> _monograms = [];
    private readonly List<Border> _rows = [];

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

        var muted = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);

        _title.Text = "People";
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = muted;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _icon.Glyph = Glyphs.Contact;
        _icon.Foreground = muted;
        _icon.Margin = new Thickness(0, 0, 6, 0);

        _badge.FontWeight = FontWeights.Bold;
        _badge.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush", Colors.White);

        _badgeHost = new Border
        {
            Child = _badge,
            Background = ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _busy.Width = 14;
        _busy.Height = 14;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        right.Children.Add(_busy);
        right.Children.Add(_badgeHost);

        var header = new Grid
        {
            Margin = new Thickness(WidgetLayout.ContentInset, WidgetLayout.ContentInset, WidgetLayout.ContentInset, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(right, 2);
        header.Children.Add(_icon);
        header.Children.Add(_title);
        header.Children.Add(right);
        Grid.SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            Padding = WidgetLayout.ListInset,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 1);

        _emptyText.TextWrapping = TextWrapping.Wrap;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.TextAlignment = TextAlignment.Center;
        _emptyText.Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray);

        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.Children.Add(_emptyText);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += (_, _) => ApplyTypeScale();
    }

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

        foreach (var contact in contacts)
        {
            var row = BuildRow(contact);
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _badge.Text = contacts.Count.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = Visibility.Visible;

        ApplyTypeScale();
    }

    private Border BuildRow(ContactRecord contact)
    {
        var name = new TextBlock
        {
            Text = contact.Who,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush", Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _primaryText.Add(name);

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);

        var detail = _config.ShowDetail ? DetailLine(contact) : string.Empty;
        if (detail.Length != 0)
        {
            var line = new TextBlock
            {
                Text = detail,
                Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };

            _secondaryText.Add(line);
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
            var monogram = new TextBlock
            {
                Text = Monogram(contact.Who),
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var avatar = new Border
            {
                Child = monogram,
                Background = ThemeBrush("ControlAltFillColorSecondaryBrush", Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };

            _monograms.Add(monogram);
            _avatars.Add(avatar);

            Grid.SetColumn(avatar, 0);
            body.Children.Add(avatar);
        }

        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var row = new Border
        {
            Child = body,
            Background = Transparent,
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        // Hover, the way every list on Windows does it. Done by hand rather than with a ListView
        // because the row is a custom layout, but the brush is the stock one so it tracks the theme
        // and the accent. PointerExited alone is not enough: the pointer can leave without it when
        // the list scrolls or the widget is torn down mid-hover, stranding a row lit.
        row.PointerEntered += (_, _) => row.Background = ThemeBrush("SubtleFillColorSecondaryBrush", Colors.Gray);
        row.PointerExited += (_, _) => row.Background = Transparent;
        row.PointerCaptureLost += (_, _) => row.Background = Transparent;
        row.PointerCanceled += (_, _) => row.Background = Transparent;

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
    /// Size the rows to the tile. A widget is dragged to any size, so a fixed row would leave a
    /// large tile mostly empty and clip a small one.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var primary = TypeScale.Fit(width, height, 0.055, 0.085, 11, 20);
        var secondary = TypeScale.Fit(width, height, 0.046, 0.072, 10, 17);
        var heading = TypeScale.Fit(width, height, 0.050, 0.080, 10, 18);
        var pad = Math.Clamp(primary * 0.55, 4, 12);

        _title.FontSize = heading;
        _icon.FontSize = heading;
        _badge.FontSize = Math.Max(9, heading - 2);
        _emptyText.FontSize = secondary;

        foreach (var text in _primaryText)
        {
            text.FontSize = primary;
        }

        foreach (var text in _secondaryText)
        {
            text.FontSize = secondary;
        }

        var avatarSize = Math.Round(primary * 2.2);
        foreach (var avatar in _avatars)
        {
            avatar.Width = avatarSize;
            avatar.Height = avatarSize;
            avatar.CornerRadius = new CornerRadius(avatarSize / 2);
        }

        foreach (var monogram in _monograms)
        {
            monogram.FontSize = Math.Max(9, primary * 0.8);
        }

        foreach (var row in _rows)
        {
            // The sides are the standard inset rather than the tile-scaled one, so a row
            // reaches both edges of the widget and its text still lines up with the header.
            row.Padding = new Thickness(WidgetLayout.ContentInset, pad * 0.7, WidgetLayout.ContentInset, pad * 0.7);

            // Negative side margins so the hover highlight reaches the tile's edge rather than
            // stopping short of it in a box of its own.
        }
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

    private void ClearRows()
    {
        _list.Children.Clear();
        _rows.Clear();
        _primaryText.Clear();
        _secondaryText.Clear();
        _avatars.Clear();
        _monograms.Clear();
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

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
