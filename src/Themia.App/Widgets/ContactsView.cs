using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Office;
using Themia.App.Shell;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Office;
using Themia.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;

namespace Themia.App.Widgets;

/// <summary>
/// [S-580] People from the user's Microsoft, Google and CardDAV address books, through Themia's own
/// accounts, merged into one list: as a list, compact rows, a details table or avatar cards
/// ([S-581]), with a search box ([S-584]) and per-person actions on right-click ([S-587]).
/// <para>
/// The view asks the shared <see cref="IOfficeSource"/> ([S-588]) and keeps the merged list it was
/// given; searching, capping and switching layouts redraw from that list without asking again.
/// </para>
/// </summary>
internal sealed partial class ContactsView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    private const string Scope = "contacts-widget";

    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(15);

    /// <summary>Below this height the search box goes: the list is what the widget is for.</summary>
    private const double SearchHidesBelow = 150;

    /// <summary>A grid card's width on the 4px grid: a 40px avatar and a name under it.</summary>
    private const double CardWidth = 96;

    /// <summary>Below this width the avatar and the count give their room to the names.</summary>
    private const double NarrowBelow = 160;

    private static readonly string ListGlyph = char.ConvertFromUtf32(0xE779);
    private static readonly string CompactGlyph = char.ConvertFromUtf32(0xE14C);
    private static readonly string DetailsGlyph = char.ConvertFromUtf32(0xE80A);
    private static readonly string CardsGlyph = char.ConvertFromUtf32(0xE8A9);
    private static readonly string PhoneGlyph = char.ConvertFromUtf32(0xE717);
    private static readonly string MessageGlyph = char.ConvertFromUtf32(0xE8BD);

    private readonly TextBox _search = new();
    private readonly Grid _columnHeader = new();
    private readonly ScrollViewer _scroll;
    private readonly OfficeStatePanel _state = new(Glyphs.Contact);
    private readonly ProgressRing _busy = new();
    private readonly FontIcon _warning;
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly StackPanel _trailing;
    private readonly List<IDisposable> _hovers = [];
    private readonly Dictionary<string, Border> _indicators = new(StringComparer.Ordinal);

    private IWidgetSurface? _surface;
    private IOfficeSource? _source;
    private IDisposable? _poll;
    private ContactsConfig _config = new();
    private string _query = string.Empty;
    private string? _appliedDefaultSearch;
    private string? _selectedId;
    private IReadOnlyList<Contact> _contacts = [];
    private Screen _screen = Screen.Loading;
    private string _problemText = string.Empty;
    private bool _problemOffersAccounts;
    private string? _warningText;
    private bool _warningOpensAccounts;
    private string _requestKey = string.Empty;
    private bool _refreshing;
    private bool _again;
    private bool _againForced;
    private bool _detached;

    public ContactsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.IsActive = false;
        _busy.Visibility = Visibility.Collapsed;

        _warning = Fluent.Icon(Glyphs.Warning, "SystemFillColorCautionBrush");
        _warning.Visibility = Visibility.Collapsed;
        _warning.Tapped += OnWarningTapped;

        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _warning, _busy, _badgeHost },
        };

        // [S-584] The stock Windows search box, so it has the system's caret, clear button and menu.
        _search.PlaceholderText = "Search contacts";
        _search.IsSpellCheckEnabled = false;
        _search.IsTextPredictionEnabled = false;
        _search.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _search.Margin = new Thickness(Fluent.Space3, 0, Fluent.Space3, Fluent.Space2);
        _search.TextChanged += OnSearchChanged;
        _search.GotFocus += OnSearchFocus;
        _search.LostFocus += OnSearchBlur;
        _search.KeyDown += OnSearchKeyDown;
        SetRow(_search, 0);

        _columnHeader.Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space1);
        _columnHeader.Visibility = Visibility.Collapsed;
        SetRow(_columnHeader, 1);

        _scroll = new ScrollViewer
        {
            Padding = Fluent.ListPadding,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        SetRow(_scroll, 2);

        SetRow(_state, 0);
        SetRowSpan(_state, 3);

        Children.Add(_search);
        Children.Add(_columnHeader);
        Children.Add(_scroll);
        Children.Add(_state);

        // [S-580] Clicking the background clears the selection, as the classic widget did.
        Tapped += OnBackgroundTapped;
        SizeChanged += OnSizeChanged;
    }

    private enum Screen
    {
        Loading,
        NoAccounts,
        AccountsGone,
        Problem,
        Ready,
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _trailing;

    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        ApplyDefaultSearch(config as ContactsConfig ?? new ContactsConfig());
        _source = OfficeSources.Current;

        if (_source is not null)
        {
            _source.Changed += OnAccountsChanged;
        }

        _poll = ticker.Subscribe(Poll, () => _ = RefreshAsync(force: false));
    }

    public void ApplyConfig(WidgetConfig config)
    {
        ApplyDefaultSearch(config as ContactsConfig ?? new ContactsConfig());

        if (RequestKey() != _requestKey)
        {
            _ = RefreshAsync(force: false);
        }

        Render();
    }

    /// <summary>
    /// [S-584] The configured search is where the box starts, and where it goes back to when the
    /// setting changes; what the user types in between is theirs until then.
    /// </summary>
    private void ApplyDefaultSearch(ContactsConfig next)
    {
        _config = next;

        if (_appliedDefaultSearch != next.DefaultSearch)
        {
            _appliedDefaultSearch = next.DefaultSearch;
            _query = next.DefaultSearch;
            _search.Text = next.DefaultSearch;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            // [S-581] The classic menu: refresh, then the four layouts with the current one checked.
            var actions = new List<WidgetAction> { new(Glyphs.Refresh, "Refresh", () => _ = RefreshAsync(force: true)) };

            foreach (var (layout, label, glyph) in new[]
            {
                (ContactsLayout.List, "List", ListGlyph),
                (ContactsLayout.Compact, "Compact", CompactGlyph),
                (ContactsLayout.Details, "Details", DetailsGlyph),
                (ContactsLayout.Grid, "Grid", CardsGlyph),
            })
            {
                actions.Add(new(_config.EffectiveLayout == layout ? Glyphs.Done : glyph, label, () => SetLayout(layout)));
            }

            return actions;
        }
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _poll?.Dispose();
        _poll = null;

        if (_source is not null)
        {
            _source.Changed -= OnAccountsChanged;
            _source = null;
        }

        _surface?.RequestTextInput(false);
        _search.TextChanged -= OnSearchChanged;
        _search.GotFocus -= OnSearchFocus;
        _search.LostFocus -= OnSearchBlur;
        _search.KeyDown -= OnSearchKeyDown;
        _warning.Tapped -= OnWarningTapped;
        Tapped -= OnBackgroundTapped;
        SizeChanged -= OnSizeChanged;
        _state.Detach();
        ClearRows();
    }

    // -----------------------------------------------------------------------------------------
    // Data
    // -----------------------------------------------------------------------------------------

    private void OnAccountsChanged() => _ = RefreshAsync(force: false);

    private string RequestKey() => $"{string.Join(",", _config.AccountIds)}|{ContactRules.FetchSize(_config.EffectiveCount)}";

    private async Task RefreshAsync(bool force)
    {
        if (_detached)
        {
            return;
        }

        if (_refreshing)
        {
            _again = true;
            _againForced |= force;
            return;
        }

        var source = _source;
        var accounts = source?.Accounts(AccountFeatures.Contacts) ?? [];
        _requestKey = RequestKey();

        if (source is null || accounts.Count == 0)
        {
            _screen = Screen.NoAccounts;
            Render();
            return;
        }

        var selected = CalendarRules.SelectAccounts(_config.AccountIds, [.. accounts.Select(a => a.Id)]);
        if (selected.Count == 0)
        {
            _screen = Screen.AccountsGone;
            Render();
            return;
        }

        var labels = accounts.ToDictionary(a => a.Id, a => a.Label, StringComparer.Ordinal);
        var size = ContactRules.FetchSize(_config.EffectiveCount);

        _refreshing = true;
        SetBusy(true);

        try
        {
            var tasks = selected.Select(a => source.ContactsAsync(a, size, force, CancellationToken.None)).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(true);

            if (_detached)
            {
                return;
            }

            var results = tasks.Select(t => t.Result).ToList();
            var problems = results.Select((r, i) => (Account: selected[i], r.Problem)).Where(p => p.Problem != SyncProblem.None).ToList();
            var anyData = results.Any(r => r.Value is not null);

            _contacts = ContactRules.Merge(results.Select(r => r.Value ?? []));

            if (!anyData && problems.Count > 0)
            {
                var worst = problems.OrderByDescending(p => SyncProblems.NeedsUser(p.Problem)).First();
                _screen = Screen.Problem;
                _problemText = SyncProblems.Message(worst.Problem, "contacts");
                _problemOffersAccounts = SyncProblems.NeedsUser(worst.Problem);
                _warningText = null;
            }
            else
            {
                _screen = Screen.Ready;
                _warningText = problems.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, problems.Select(p =>
                        $"{(labels.TryGetValue(p.Account, out var label) ? label : p.Account)}: {SyncProblems.Message(p.Problem, "contacts")}"));
                _warningOpensAccounts = problems.Any(p => SyncProblems.NeedsUser(p.Problem));
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not refresh the contact list", ex);
            _screen = Screen.Problem;
            _problemText = "Could not read contacts. See themia.log.";
            _problemOffersAccounts = false;
        }
        finally
        {
            _refreshing = false;
            SetBusy(false);
        }

        Render();

        if (_again && !_detached)
        {
            var forced = _againForced;
            _again = false;
            _againForced = false;
            await RefreshAsync(forced).ConfigureAwait(true);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy.IsActive = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetLayout(ContactsLayout layout)
    {
        _config.Layout = layout;
        _surface?.RequestSave();
        Render();
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    /// <summary>[S-585] The list follows the box as it is typed in.</summary>
    private void OnSearchChanged(object sender, TextChangedEventArgs args)
    {
        if (_query == _search.Text)
        {
            return;
        }

        _query = _search.Text;
        Render();
    }

    // The surface takes no keystrokes unless asked: the box asks while it has focus ([S-584]).
    private void OnSearchFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnSearchBlur(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(false);

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Escape)
        {
            args.Handled = true;
            _search.Text = string.Empty;
        }
    }

    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs args)
    {
        if (!args.Handled && _selectedId is not null)
        {
            Select(null);
        }
    }

    private void OnWarningTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_warningOpensAccounts)
        {
            args.Handled = true;
            OfficeStatePanel.OpenAccounts();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // The search box and the layout fallback both follow the size, so the list is re-laid.
        if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.5 || Math.Abs(args.NewSize.Height - args.PreviousSize.Height) > 0.5)
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

        var ready = _screen == Screen.Ready;
        _warning.Visibility = ready && _warningText is not null ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_warning, _warningText);
        _badgeHost.Visibility = Visibility.Collapsed;

        var showSearch = ready && ActualHeight >= SearchHidesBelow;
        _search.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;

        switch (_screen)
        {
            case Screen.Loading:
                _state.Show("Loading…", offerAccounts: false);
                return;

            case Screen.NoAccounts:
                _state.Show("No contacts account yet", offerAccounts: true);
                return;

            case Screen.AccountsGone:
                _state.Show(SyncProblems.Message(SyncProblem.NoAccount, "contacts"), offerAccounts: true);
                return;

            case Screen.Problem:
                _state.Show(_problemText, _problemOffersAccounts);
                return;
        }

        // A search typed into a box that has since been hidden still filters; the box shows the
        // query again as soon as there is room for it.
        var visible = ContactRules.Visible(_contacts, _query, _config.EffectiveCount);

        if (visible.Count == 0)
        {
            _state.Show(_contacts.Count == 0 ? "No contacts" : "No contacts found", offerAccounts: false);

            // The box stays, so a search that matched nothing can be changed.
            if (showSearch)
            {
                _state.Margin = new Thickness(0, 40, 0, 0);
            }

            return;
        }

        _state.Margin = new Thickness(0);
        _state.Hide();

        _badge.Text = visible.Count.ToString(CultureInfo.InvariantCulture);
        // The count gives way to the title on a tile too narrow for both.
        _badgeHost.Visibility = ActualWidth is > 0 and < NarrowBelow ? Visibility.Collapsed : Visibility.Visible;

        var width = ActualWidth;
        var compactRows = ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;
        var layout = ContactRules.Effective(_config.EffectiveLayout, width, _config.EffectiveColumns.Count);

        switch (layout)
        {
            case ContactsLayout.Details:
                DrawDetails(visible);
                break;

            case ContactsLayout.Grid:
                DrawCards(visible, width);
                break;

            default:
                DrawList(visible, layout == ContactsLayout.Compact || compactRows, layout == ContactsLayout.Compact);
                break;
        }
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _indicators.Clear();
        _scroll.Content = null;
        _columnHeader.Children.Clear();
        _columnHeader.ColumnDefinitions.Clear();
        _columnHeader.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// [S-581] List: avatar, name, then "title · company" and the address or number. Compact: a
    /// small avatar, the name and one secondary field.
    /// </summary>
    private void DrawList(IReadOnlyList<Contact> contacts, bool compactRows, bool compactLayout)
    {
        var list = new StackPanel { Spacing = 0 };
        var avatarSize = compactRows ? Fluent.RowHeightCompact - Fluent.Space2 : Fluent.RowHeight - Fluent.Space2;

        foreach (var contact in contacts)
        {
            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            var name = Fluent.Text(FluentType.BodyStrong);
            name.Text = contact.Name;
            text.Children.Add(name);

            var detail = compactLayout
                ? ContactRules.CompactDetail(contact)
                : FirstNonEmpty(ContactRules.Role(contact), contact.Email, contact.Phone);

            if (detail.Length > 0)
            {
                var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                line.Text = detail;
                text.Children.Add(line);
            }

            // The list layout gives the address or number its own line when the role took the first.
            if (!compactLayout && !compactRows && ContactRules.Role(contact).Length > 0 && FirstNonEmpty(contact.Email, contact.Phone) is { Length: > 0 } reach)
            {
                var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                line.Text = reach;
                text.Children.Add(line);
            }

            // A narrow tile gives the avatar's width to the name.
            var avatar = ActualWidth is > 0 and < NarrowBelow ? null : Avatar(contact, compactLayout ? Math.Min(avatarSize, 24) : avatarSize);
            var body = RowBody(contact, avatar, text);
            var row = Fluent.Row(body, compactRows);
            row.Padding = new Thickness(Fluent.Space4 - Fluent.Space2, Fluent.Space1, Fluent.Space4, Fluent.Space1);
            Wire(row, contact);
            list.Children.Add(row);
        }

        _scroll.Content = list;
    }

    /// <summary>[S-582] Details: a table of the chosen columns, in canonical order, under a header row.</summary>
    private void DrawDetails(IReadOnlyList<Contact> contacts)
    {
        var columns = _config.EffectiveColumns;

        _columnHeader.Visibility = Visibility.Visible;
        AddColumns(_columnHeader, columns);
        for (var c = 0; c < columns.Count; c++)
        {
            var title = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            title.Text = ContactRules.ColumnTitle(columns[c]);
            title.Margin = new Thickness(0, 0, Fluent.Space2, 0);
            SetColumn(title, c);
            _columnHeader.Children.Add(title);
        }

        var list = new StackPanel { Spacing = 0 };

        foreach (var contact in contacts)
        {
            var cells = new Grid();
            AddColumns(cells, columns);

            for (var c = 0; c < columns.Count; c++)
            {
                var cell = Fluent.Text(c == 0 ? FluentType.BodyStrong : FluentType.Body, c == 0 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
                cell.Text = ContactRules.Cell(contact, columns[c]);
                cell.Margin = new Thickness(0, 0, Fluent.Space2, 0);
                SetColumn(cell, c);
                cells.Children.Add(cell);
            }

            var body = RowBody(contact, null, cells);
            var row = Fluent.Row(body, compact: true);
            row.Padding = new Thickness(Fluent.Space4 - Fluent.Space2, 0, Fluent.Space4, 0);
            Wire(row, contact);
            list.Children.Add(row);
        }

        _scroll.Content = list;
    }

    /// <summary>The classic widths: the phone a fixed column, the email a little wider than the rest.</summary>
    private static void AddColumns(Grid grid, IReadOnlyList<ContactColumn> columns)
    {
        foreach (var column in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column switch
                {
                    ContactColumn.Phone => new GridLength(124),
                    ContactColumn.Email => new GridLength(1.2, GridUnitType.Star),
                    _ => new GridLength(1, GridUnitType.Star),
                },
            });
        }
    }

    /// <summary>[S-581] Grid: avatar cards with the name and the company (or job title).</summary>
    private void DrawCards(IReadOnlyList<Contact> contacts, double width)
    {
        var perRow = Math.Max(2, (int)Math.Floor((width - Fluent.Space2) / CardWidth));
        var grid = new Grid { Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, 0) };

        for (var c = 0; c < perRow; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < contacts.Count; i++)
        {
            if (i % perRow == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var contact = contacts[i];
            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };

            var avatar = Avatar(contact, 40);
            avatar.HorizontalAlignment = HorizontalAlignment.Center;
            avatar.Margin = new Thickness(0);
            stack.Children.Add(avatar);

            var name = Fluent.Text(FluentType.Caption);
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            name.Text = contact.Name;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(name);

            if (ContactRules.CardDetail(contact) is { Length: > 0 } detail)
            {
                var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                line.Text = detail;
                line.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(line);
            }

            var card = new Border
            {
                Child = stack,
                Padding = new Thickness(Fluent.Space1, Fluent.Space2, Fluent.Space1, Fluent.Space2),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                Background = Fluent.RowRest(),
            };

            Wire(card, contact);
            SetRow(card, i / perRow);
            SetColumn(card, i % perRow);
            grid.Children.Add(card);
        }

        _scroll.Content = grid;
    }

    /// <summary>A row's content: the selection mark, the avatar when there is one, and the text.</summary>
    private Grid RowBody(Contact contact, FrameworkElement? avatar, FrameworkElement text)
    {
        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(Fluent.Space2) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        // [S-580] Selection is the accent pill Windows lists use, so it never fights the hover fill.
        var indicator = new Border
        {
            Width = 3,
            Height = Fluent.IconSize,
            CornerRadius = new CornerRadius(1.5),
            Background = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = contact.Id == _selectedId ? Visibility.Visible : Visibility.Collapsed,
        };
        _indicators[contact.Id] = indicator;
        body.Children.Add(indicator);

        if (avatar is not null)
        {
            SetColumn(avatar, 1);
            body.Children.Add(avatar);
        }

        SetColumn(text, 2);
        body.Children.Add(text);
        return body;
    }

    /// <summary>[S-586] Initials on a colour hashed from the name, so a person keeps their colour.</summary>
    private static Border Avatar(Contact contact, double size)
    {
        var initials = Fluent.Text(size >= 40 ? FluentType.BodyStrong : FluentType.Caption);

        // The fill is always dark (34% lightness, the classic hsl), so the initials are always white,
        // in either theme; a theme brush would put black on it in the dark theme.
        initials.Foreground = new SolidColorBrush(Colors.White);
        initials.Text = Formatting.Monogram(contact.Name);
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        initials.TextTrimming = TextTrimming.None;

        return new Border
        {
            Child = initials,
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(FromHsl(ContactRules.AvatarHue(contact.Name), 0.38, 0.34)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space3, 0),
        };
    }

    /// <summary>Hover, select on click, and the per-person menu on right-click.</summary>
    private void Wire(Border row, Contact contact)
    {
        _hovers.Add(Fluent.Hover(row));
        ToolTipService.SetToolTip(row, Tooltip(contact));

        row.Tapped += (_, args) =>
        {
            // Handled, so the background tap that clears the selection does not also see it.
            args.Handled = true;
            Select(_selectedId == contact.Id ? null : contact.Id);
        };

        row.RightTapped += (_, args) =>
        {
            args.Handled = true;
            Select(contact.Id);
            BuildMenu(contact).ShowAt(row, args.GetPosition(row));
        };
    }

    private void Select(string? id)
    {
        if (_selectedId is { } old && _indicators.TryGetValue(old, out var previous))
        {
            previous.Visibility = Visibility.Collapsed;
        }

        _selectedId = id;

        if (id is not null && _indicators.TryGetValue(id, out var current))
        {
            current.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// [S-587] The classic contact menu: the name and role as a header, then write and copy the
    /// address, call, message and copy the number, copy the name, and refresh.
    /// </summary>
    private MenuFlyout BuildMenu(Contact contact)
    {
        var menu = new MenuFlyout();

        var header = new MenuFlyoutItem { Text = string.IsNullOrEmpty(ContactRules.Role(contact)) ? contact.Name : $"{contact.Name} · {ContactRules.Role(contact)}", IsEnabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new MenuFlyoutSeparator());

        if (ContactLinks.Build(ContactLinkKind.Email, contact.Email) is not null)
        {
            menu.Items.Add(Item("Send email", Glyphs.Mail, () => ShellLauncher.OpenContactLink(ContactLinkKind.Email, contact.Email)));
            menu.Items.Add(Item("Copy email", Glyphs.Copy, () => Copy(contact.Email!)));
        }

        if (ContactLinks.Build(ContactLinkKind.Call, contact.Phone) is not null)
        {
            menu.Items.Add(Item("Call", PhoneGlyph, () => ShellLauncher.OpenContactLink(ContactLinkKind.Call, contact.Phone)));
            menu.Items.Add(Item("Message", MessageGlyph, () => ShellLauncher.OpenContactLink(ContactLinkKind.Message, contact.Phone)));
        }

        if (!string.IsNullOrWhiteSpace(contact.Phone))
        {
            menu.Items.Add(Item("Copy phone", Glyphs.Copy, () => Copy(contact.Phone!)));
        }

        menu.Items.Add(Item("Copy name", Glyphs.Copy, () => Copy(contact.Name)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Refresh", Glyphs.Refresh, () => _ = RefreshAsync(force: true)));
        return menu;

        static MenuFlyoutItem Item(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            return item;
        }
    }

    private static void Copy(string text)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; Windows fails the call rather than wait.
            Log.Error(Scope, "could not copy to the clipboard", ex);
        }
    }

    private static string Tooltip(Contact contact)
    {
        var lines = new List<string> { contact.Name };
        foreach (var line in new[] { ContactRules.Role(contact), contact.Email, contact.Phone })
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    /// <summary>The classic <c>hsl(h, 38%, 34%)</c>, as a colour.</summary>
    private static Windows.UI.Color FromHsl(int hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = c * (1 - Math.Abs((hue / 60.0 % 2) - 1));
        var m = lightness - (c / 2);

        var (r, g, b) = (hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Windows.UI.Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
