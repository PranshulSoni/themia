using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Accounts;
using Themia.App.Widgets;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-539] The Accounts page of the settings window: who Themia is signed in to, and the way to add,
/// rename and sign out of an account.
/// <para>
/// One flow at a time, drawn in place under the Add account row rather than in a second window: a
/// device code, a browser wait, a paste box or a short form. Everything that can go wrong is said on
/// the page, in the line under the flow, never left to a log — a sign-in that fails silently is one
/// the user tries five times. Leaving the page cancels a flow in progress.
/// </para>
/// <para>
/// The list follows <see cref="AccountService.AccountsChanged"/>, so an account added here, renamed,
/// marked "sign in again" by a failed refresh, or imported, shows without the page being rebuilt.
/// </para>
/// </summary>
internal sealed class AccountsPage
{
    private const string Scope = "config.accounts";

    private static readonly string OpenGlyph = char.ConvertFromUtf32(0xE8A7);
    private static readonly string KeyGlyph = char.ConvertFromUtf32(0xE8D7);
    private static readonly string ImportGlyph = char.ConvertFromUtf32(0xE8B5);

    private readonly AccountService _service;
    private readonly StackPanel _root = new() { Spacing = Fluent.Space1 };
    private readonly StackPanel _list = new() { Spacing = Fluent.Space1 };
    private readonly StackPanel _flow = new() { Spacing = Fluent.Space1 };
    private readonly StackPanel _import = new() { Spacing = Fluent.Space1 };

    private CancellationTokenSource? _flowCancel;
    private bool _subscribed;

    private AccountsPage(AccountService service) => _service = service;

    /// <summary>Build the page. Without the service (the gallery, a failed start) it says so.</summary>
    public static FrameworkElement Build()
    {
        if (AppServices.Accounts is not { } service)
        {
            var empty = new StackPanel { Spacing = Fluent.Space1 };
            empty.Children.Add(Title());
            empty.Children.Add(FluentLayout.Note("Accounts are not available right now. See themia.log."));
            return empty;
        }

        return new AccountsPage(service).Compose();
    }

    private FrameworkElement Compose()
    {
        _root.Children.Add(Title());

        var subtitle = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        subtitle.Text = "Sign in once here. Mail, calendar, contacts, tasks, OneDrive, GitHub and Claude widgets read from these accounts.";
        subtitle.TextWrapping = TextWrapping.Wrap;
        subtitle.Margin = new Thickness(0, 0, 0, Fluent.Space2);
        _root.Children.Add(subtitle);

        if (_service.LoadProblem is { } problem)
        {
            _root.Children.Add(Message(problem, critical: true));
        }

        _root.Children.Add(FluentLayout.Header("Signed in"));
        _root.Children.Add(_list);

        _root.Children.Add(FluentLayout.Header("Add an account"));
        _root.Children.Add(FluentLayout.Row(Glyphs.Add, "Add account", "Choose the service to sign in to", AddMenu()));
        _root.Children.Add(_flow);

        _root.Children.Add(_import);

        RebuildList();
        _ = RefreshImportAsync();

#if DEBUG
        // Lets the debug settings render (--settings-png Accounts) draw a flow as well as the list.
        // Google is left out on purpose: it opens the browser, which a render must never do.
        switch (Environment.GetEnvironmentVariable("THEMIA_DEBUG_ACCOUNTS_FLOW"))
        {
            case "microsoft": StartDevice(AccountProvider.Microsoft); break;
            case "imap": StartImap(); break;
            case "caldav": StartDav(AccountProvider.CalDav); break;
            case "github-token": StartGitHubToken(); break;
            case "claude": StartClaude(); break;
        }
#endif

        // Subscribed while on screen only: the window replaces the page on navigation, and a page
        // that kept listening would keep itself (and its flow) alive behind the next one.
        _root.Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                _service.AccountsChanged += OnAccountsChanged;
                _subscribed = true;
            }
        };

        _root.Unloaded += (_, _) =>
        {
            if (_subscribed)
            {
                _service.AccountsChanged -= OnAccountsChanged;
                _subscribed = false;
            }

            CancelFlow();
        };

        return _root;
    }

    private static TextBlock Title()
    {
        var title = Fluent.Text(FluentType.Title);
        title.Text = "Accounts";
        return title;
    }

    private void OnAccountsChanged()
    {
        RebuildList();
        _ = RefreshImportAsync();
    }

    // -----------------------------------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------------------------------

    private void RebuildList()
    {
        _list.Children.Clear();

        var accounts = _service.Accounts;
        if (accounts.Count == 0)
        {
            _list.Children.Add(FluentLayout.Note("No accounts yet. Add one below, or bring yours over from classic Themia."));
            return;
        }

        foreach (var account in accounts)
        {
            _list.Children.Add(AccountCard(account));
        }
    }

    private FrameworkElement AccountCard(AccountInfo account)
    {
        var icon = new FontIcon
        {
            Glyph = ProviderGlyph(account.Provider),
            FontSize = Fluent.IconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space4, 0),
        };

        var name = Fluent.Text(FluentType.BodyStrong);
        name.Text = account.Label;

        var detail = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        detail.Text = account.Label == account.Address
            ? account.Provider.Label()
            : $"{account.Address}  ·  {account.Provider.Label()}";

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };

        if (account.Problem is { } problem)
        {
            var warning = Fluent.Text(FluentType.Caption, "SystemFillColorCriticalBrush");
            warning.Text = problem;
            warning.TextWrapping = TextWrapping.Wrap;
            warning.TextTrimming = TextTrimming.None;
            text.Children.Add(warning);
        }

        var rename = FluentLayout.IconCommand(Glyphs.Rename, "Rename");
        rename.Flyout = RenameFlyout(account);

        var signOut = new Button { Content = "Sign out", VerticalAlignment = VerticalAlignment.Center };
        signOut.Click += (_, _) => _ = SignOutAsync(account, signOut);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Margin = new Thickness(Fluent.Space3, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { rename, signOut },
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(commands, 2);

        return FluentLayout.Card(new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { icon, text, commands },
        });
    }

    private Flyout RenameFlyout(AccountInfo account)
    {
        var box = new TextBox
        {
            Header = "Nickname",
            Text = account.Nickname ?? string.Empty,
            PlaceholderText = account.DisplayName.Length > 0 ? account.DisplayName : account.Address,
            Width = 260,
        };

        var verdict = Message(string.Empty, critical: true);
        var save = new Button { Content = "Save", Style = AccentStyle() };
        var flyout = new Flyout
        {
            Content = new StackPanel { Spacing = Fluent.Space2, Children = { box, save, verdict } },
        };

        async Task SaveAsync()
        {
            save.IsEnabled = false;
            try
            {
                await _service.RenameAsync(account.Id, box.Text).ConfigureAwait(true);
                flyout.Hide();
            }
            catch (AccountException ex)
            {
                Say(verdict, ex.Message, critical: true);
            }
            finally
            {
                save.IsEnabled = true;
            }
        }

        save.Click += (_, _) => _ = SaveAsync();
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                _ = SaveAsync();
            }
        };

        return flyout;
    }

    private async Task SignOutAsync(AccountInfo account, Button source)
    {
        var confirmed = await FluentLayout.ConfirmAsync(
            source,
            $"Sign out of {account.Label}?",
            $"Themia forgets this account and its sign-in on this PC. Widgets that read from it go empty until you add it again. Nothing changes at {account.Provider.Label()}.",
            "Sign out").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _service.SignOutAsync(account.Id).ConfigureAwait(true);
        }
        catch (AccountException ex)
        {
            // The card is still there; say why under the Add row, where messages go.
            ShowFlow(null, Message(ex.Message, critical: true));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Adding
    // -----------------------------------------------------------------------------------------

    private DropDownButton AddMenu()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };

        void Add(string text, AccountProvider provider, Action start)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = ProviderGlyph(provider) } };
            item.Click += (_, _) => start();
            menu.Items.Add(item);
        }

        Add("Microsoft", AccountProvider.Microsoft, () => StartDevice(AccountProvider.Microsoft));
        Add("Google", AccountProvider.Google, StartGoogle);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Mail server (IMAP)", AccountProvider.Imap, StartImap);
        Add("Calendar server (CalDAV)", AccountProvider.CalDav, () => StartDav(AccountProvider.CalDav));
        Add("Contacts server (CardDAV)", AccountProvider.CardDav, () => StartDav(AccountProvider.CardDav));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("GitHub", AccountProvider.GitHub, () => StartDevice(AccountProvider.GitHub));
        Add("GitHub with a token (Enterprise)", AccountProvider.GitHub, StartGitHubToken);
        Add("Claude", AccountProvider.Claude, StartClaude);

        return new DropDownButton { Content = "Add account", Flyout = menu };
    }

    /// <summary>Replace whatever flow is showing, cancelling it, and return the new flow's token.</summary>
    private CancellationToken ShowFlow(string? heading, params FrameworkElement[] content)
    {
        CancelFlow();
        _flowCancel = new CancellationTokenSource();

        _flow.Children.Clear();

        if (heading is not null)
        {
            var title = Fluent.Text(FluentType.Subtitle);
            title.Text = heading;
            title.Margin = new Thickness(0, Fluent.Space3, 0, Fluent.Space1);
            _flow.Children.Add(title);
        }

        foreach (var element in content)
        {
            _flow.Children.Add(element);
        }

        return _flowCancel.Token;
    }

    private void CancelFlow()
    {
        if (_flowCancel is null)
        {
            return;
        }

        _flowCancel.Cancel();
        _flowCancel.Dispose();
        _flowCancel = null;
    }

    private void CloseFlow(string? message = null)
    {
        CancelFlow();
        _flow.Children.Clear();

        if (message is not null)
        {
            _flow.Children.Add(Message(message, critical: false));
        }
    }

    // ---- device code: Microsoft, GitHub ----------------------------------------------------

    private void StartDevice(AccountProvider provider)
    {
        var code = Fluent.Text(FluentType.TitleLarge);
        code.Text = "········";
        code.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        code.IsTextSelectionEnabled = true;

        var where = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        where.TextWrapping = TextWrapping.Wrap;
        where.Text = $"Asking {provider.Label()} for a sign-in code…";

        var copy = new Button { Content = Labelled(Glyphs.Copy, "Copy code"), IsEnabled = false };
        var open = new Button { Content = Labelled(OpenGlyph, "Open sign-in page"), IsEnabled = false, Style = AccentStyle() };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => CloseFlow();

        var (status, ring, line) = StatusLine("Starting…");

        var body = new StackPanel
        {
            Spacing = Fluent.Space3,
            Children =
            {
                where,
                code,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2, Children = { open, copy, cancel } },
                status,
            },
        };

        var token = ShowFlow($"Sign in to {provider.Label()}", FluentLayout.Card(body));
        _ = RunDeviceAsync(provider, code, where, copy, open, ring, line, token);
    }

    private async Task RunDeviceAsync(
        AccountProvider provider,
        TextBlock code,
        TextBlock where,
        Button copy,
        Button open,
        ProgressRing ring,
        TextBlock line,
        CancellationToken cancellation)
    {
        try
        {
            var signIn = await _service.BeginDeviceSignInAsync(provider, cancellation).ConfigureAwait(true);
            var session = signIn.Session;

            code.Text = session.UserCode;
            where.Text = $"Open {session.VerificationUri.Host}{session.VerificationUri.AbsolutePath.TrimEnd('/')}, enter this code, and approve Themia.";

            copy.IsEnabled = true;
            open.IsEnabled = true;
            copy.Click += (_, _) => Copy(session.UserCode, line);
            open.Click += (_, _) => Open(session.VerificationUri, line);

            Say(line, $"Waiting for you to approve. The code works until {session.ExpiresAt.ToLocalTime():t}.", critical: false);

            var account = await _service.CompleteDeviceSignInAsync(signIn, cancellation).ConfigureAwait(true);
            CloseFlow($"Signed in to {provider.Label()} as {account.Address}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // ignore: the user cancelled or left the page; nothing to say.
        }
        catch (AccountException ex)
        {
            Fail(ring, line, ex.Message, copy, open);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"{provider} sign-in failed unexpectedly", ex);
            Fail(ring, line, $"The sign-in failed: {ex.Message}", copy, open);
        }
    }

    // ---- Google ----------------------------------------------------------------------------

    private void StartGoogle()
    {
        if (AccountService.GoogleUnavailableReason is { } reason)
        {
            ShowFlow("Sign in to Google", Message(reason, critical: true));
            return;
        }

        var explain = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        explain.TextWrapping = TextWrapping.Wrap;
        explain.Text = "Your browser opens Google's sign-in page. Choose the account and allow Themia; this page carries on by itself when the browser comes back.";

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => CloseFlow();

        var (status, ring, line) = StatusLine("Opening the browser…");

        var token = ShowFlow("Sign in to Google", FluentLayout.Card(new StackPanel
        {
            Spacing = Fluent.Space3,
            Children = { explain, status, cancel },
        }));

        _ = RunGoogleAsync(ring, line, token);
    }

    private async Task RunGoogleAsync(ProgressRing ring, TextBlock line, CancellationToken cancellation)
    {
        try
        {
            var account = await _service.SignInWithGoogleAsync(url =>
            {
                var opened = ShellLauncher.Open(url.AbsoluteUri) == LaunchOutcome.Opened;
                if (opened)
                {
                    Say(line, "Waiting for the browser. Finish signing in there.", critical: false);
                }

                return opened;
            }, cancellation).ConfigureAwait(true);

            CloseFlow($"Signed in to Google as {account.Address}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // ignore: the user cancelled or left the page.
        }
        catch (AccountException ex)
        {
            Fail(ring, line, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "Google sign-in failed unexpectedly", ex);
            Fail(ring, line, $"The sign-in failed: {ex.Message}");
        }
    }

    // ---- Claude ----------------------------------------------------------------------------

    private void StartClaude()
    {
        var signIn = AccountService.BeginClaudeSignIn();

        var steps = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        steps.TextWrapping = TextWrapping.Wrap;
        steps.Text = "Open the Claude sign-in page, sign in and approve Themia, then copy the code the page shows and paste it here. Themia reads your plan's usage and nothing else.";

        var open = new Button { Content = Labelled(OpenGlyph, "Open Claude sign-in"), Style = AccentStyle() };

        var code = new TextBox { PlaceholderText = "Paste the code here", Width = 360 };
        var nickname = new TextBox { PlaceholderText = "Optional", Width = 360 };
        var connect = new Button { Content = "Connect", Style = AccentStyle() };
        var cancel = new Button { Content = "Cancel" };

        var (status, ring, line) = StatusLine(string.Empty);
        status.Visibility = Visibility.Collapsed;

        open.Click += (_, _) => Open(signIn.AuthorizeUrl, line, status);
        cancel.Click += (_, _) => CloseFlow();

        var token = ShowFlow(
            "Connect Claude",
            FluentLayout.Card(new StackPanel { Spacing = Fluent.Space3, Children = { steps, open } }),
            FluentLayout.Row(KeyGlyph, "Code", "The code#state value the page shows", code),
            FluentLayout.Row(Glyphs.Rename, "Nickname", null, nickname),
            Buttons(connect, cancel),
            status);

        connect.Click += (_, _) => _ = RunFormAsync(connect, status, ring, line,
            () => _service.CompleteClaudeSignInAsync(signIn, code.Text, nickname.Text, token),
            account => $"Connected Claude{(account.Nickname is null ? string.Empty : $" as {account.Nickname}")}.");
    }

    // ---- forms: IMAP, CalDAV, CardDAV, GitHub token ----------------------------------------

    private void StartImap()
    {
        var address = new TextBox { PlaceholderText = "you@example.com", Width = 280 };
        var host = new TextBox { PlaceholderText = "imap.example.com", Width = 280 };
        var port = new TextBox { Text = "993", Width = 280 };
        var user = new TextBox { PlaceholderText = "Usually your address", Width = 280 };
        var password = new PasswordBox { Width = 280 };
        var nickname = new TextBox { PlaceholderText = "Optional", Width = 280 };
        var hint = FluentLayout.Note(string.Empty);
        hint.Visibility = Visibility.Collapsed;

        // The preset fills only fields the user has not typed into: a shortcut, never a cage.
        var hostTyped = false;
        var userTyped = false;
        var filling = false;
        host.TextChanged += (_, _) => hostTyped |= !filling;
        user.TextChanged += (_, _) => userTyped |= !filling;

        address.TextChanged += (_, _) =>
        {
            filling = true;
            try
            {
                if (!userTyped)
                {
                    user.Text = address.Text.Trim();
                }

                var preset = ImapPresets.ForAddress(address.Text);
                if (!hostTyped && preset is not null)
                {
                    host.Text = preset.Host;
                    port.Text = preset.Port.ToString(CultureInfo.InvariantCulture);
                }

                hint.Text = preset is null ? string.Empty : $"{preset.Name}: {preset.Note ?? "server settings filled in."}";
                hint.Visibility = hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            finally
            {
                filling = false;
            }
        };

        var connect = new Button { Content = "Connect", Style = AccentStyle() };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => CloseFlow();

        var (status, ring, line) = StatusLine(string.Empty);
        status.Visibility = Visibility.Collapsed;

        var token = ShowFlow(
            "Add a mail server",
            FluentLayout.Row(Glyphs.Mail, "Email address", null, address),
            hint,
            FluentLayout.Row(Glyphs.Network, "Server", "IMAP over TLS", host),
            FluentLayout.Row(Glyphs.Network, "Port", "993 for almost every provider", port),
            FluentLayout.Row(Glyphs.Contact, "User name", null, user),
            FluentLayout.Row(KeyGlyph, "Password", "Some providers want an app password here", password),
            FluentLayout.Row(Glyphs.Rename, "Nickname", null, nickname),
            Buttons(connect, cancel),
            status);

        connect.Click += (_, _) => _ = RunFormAsync(connect, status, ring, line, () =>
        {
            if (!int.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                throw new AccountException(AccountError.Refused, "The port must be a number (usually 993).");
            }

            return _service.AddImapAsync(address.Text, host.Text, number, user.Text, password.Password, nickname.Text, token);
        }, account => $"Connected {account.Address}.");
    }

    private void StartDav(AccountProvider provider)
    {
        var kind = provider == AccountProvider.CalDav ? DavKind.Calendar : DavKind.Contacts;

        var url = new TextBox { PlaceholderText = "https://cloud.example.com", Width = 280 };
        var user = new TextBox { PlaceholderText = "Often your email address", Width = 280 };
        var password = new PasswordBox { Width = 280 };
        var nickname = new TextBox { PlaceholderText = "Optional", Width = 280 };

        var urlTyped = false;
        var filling = false;
        url.TextChanged += (_, _) => urlTyped |= !filling;
        user.TextChanged += (_, _) =>
        {
            if (urlTyped || DavPresets.ForAddress(user.Text, kind) is not { } preset)
            {
                return;
            }

            filling = true;
            url.Text = preset;
            filling = false;
        };

        var connect = new Button { Content = "Connect", Style = AccentStyle() };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => CloseFlow();

        var (status, ring, line) = StatusLine(string.Empty);
        status.Visibility = Visibility.Collapsed;

        var what = kind == DavKind.Calendar ? "calendar" : "address book";

        var token = ShowFlow(
            provider == AccountProvider.CalDav ? "Add a calendar server" : "Add a contacts server",
            FluentLayout.Row(Glyphs.Contact, "User name", null, user),
            FluentLayout.Row(KeyGlyph, "Password", "Some providers want an app password here", password),
            FluentLayout.Row(Glyphs.Link, "Server", $"The server, or your {what}'s full address. Themia finds the {what}.", url),
            FluentLayout.Row(Glyphs.Rename, "Nickname", null, nickname),
            Buttons(connect, cancel),
            status);

        connect.Click += (_, _) => _ = RunFormAsync(connect, status, ring, line,
            () => _service.AddDavAsync(provider, url.Text, user.Text, password.Password, nickname.Text, token),
            account => $"Connected {account.Address}'s {what}.");
    }

    private void StartGitHubToken()
    {
        var tokenBox = new PasswordBox { Width = 280 };
        var api = new TextBox { PlaceholderText = "Empty for github.com", Width = 280 };
        var nickname = new TextBox { PlaceholderText = "Optional", Width = 280 };

        var create = new HyperlinkButton { Content = "Create a token on github.com" };

        var connect = new Button { Content = "Connect", Style = AccentStyle() };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => CloseFlow();

        var (status, ring, line) = StatusLine(string.Empty);
        status.Visibility = Visibility.Collapsed;
        create.Click += (_, _) => Open(OAuthClients.GitHub.NewTokenPage, line, status);

        var token = ShowFlow(
            "Add GitHub with a token",
            FluentLayout.Note("For GitHub Enterprise Server, or if you prefer a token to signing in. It needs the repo, read:user and notifications scopes."),
            FluentLayout.Row(KeyGlyph, "Token", null, tokenBox),
            FluentLayout.Row(Glyphs.Link, "API address", "For Enterprise: https://your-server/api/v3", api),
            FluentLayout.Row(Glyphs.Rename, "Nickname", null, nickname),
            Buttons(connect, cancel, create),
            status);

        connect.Click += (_, _) => _ = RunFormAsync(connect, status, ring, line,
            () => _service.AddGitHubTokenAsync(tokenBox.Password, api.Text, nickname.Text, token),
            account => $"Connected GitHub as {account.DisplayName}.");
    }

    /// <summary>
    /// The one way a form's Connect button runs: busy while the check is out, the verdict on the
    /// line underneath, the flow closed with a confirmation when it worked.
    /// </summary>
    private async Task RunFormAsync(
        Button connect,
        FrameworkElement status,
        ProgressRing ring,
        TextBlock line,
        Func<Task<AccountInfo>> add,
        Func<AccountInfo, string> done)
    {
        connect.IsEnabled = false;
        status.Visibility = Visibility.Visible;
        ring.IsActive = true;
        ring.Visibility = Visibility.Visible;
        Say(line, "Checking…", critical: false);

        try
        {
            var account = await add().ConfigureAwait(true);
            CloseFlow(done(account));
        }
        catch (OperationCanceledException)
        {
            // ignore: the page was left or the flow replaced; there is no one to tell.
        }
        catch (AccountException ex)
        {
            Fail(ring, line, ex.Message);
            connect.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "adding an account failed unexpectedly", ex);
            Fail(ring, line, $"That did not work: {ex.Message}");
            connect.IsEnabled = true;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Import from classic Themia
    // -----------------------------------------------------------------------------------------

    private async Task RefreshImportAsync()
    {
        ClassicPreview? preview;
        try
        {
            preview = await _service.PreviewClassicAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not look for classic Themia accounts", ex);
            preview = null;
        }

        _import.Children.Clear();

        if (preview is null || (preview.New.Count == 0 && preview.Skipped.Count == 0))
        {
            return;
        }

        _import.Children.Add(FluentLayout.Header("Classic Themia"));

        var verdict = Message(string.Empty, critical: false);

        if (preview.New.Count == 0)
        {
            _import.Children.Add(FluentLayout.Row(ImportGlyph, "Accounts from classic Themia",
                preview.AlreadyHere > 0 ? "Everything that can come over is already here." : "Nothing in the previous app's file can come over.",
                new TextBlock { Text = string.Empty }));
        }
        else
        {
            var names = string.Join(", ", preview.New.Take(4).Select(a => $"{a.Account.Address} ({a.Account.Provider.Label()})"));
            var more = preview.New.Count > 4 ? $" and {preview.New.Count - 4} more" : string.Empty;

            var button = new Button
            {
                Content = $"Import {preview.New.Count} account{(preview.New.Count == 1 ? string.Empty : "s")}",
                Style = AccentStyle(),
            };

            button.Click += (_, _) => _ = ImportAsync(button, verdict);

            _import.Children.Add(FluentLayout.Row(ImportGlyph, "Bring accounts over from classic Themia",
                $"{names}{more}. Sign-ins come along; the previous app's file is only read, never changed.", button));
        }

        if (preview.Skipped.Count > 0)
        {
            _import.Children.Add(FluentLayout.Note($"Not brought over: {string.Join("; ", preview.Skipped)}."));
        }

        _import.Children.Add(verdict);
    }

    private async Task ImportAsync(Button button, TextBlock verdict)
    {
        button.IsEnabled = false;
        try
        {
            var outcome = await _service.ImportClassicAsync().ConfigureAwait(true);
            var notes = outcome.Notes.Count > 0 ? $" {string.Join("; ", outcome.Notes)}." : string.Empty;
            Say(verdict, $"Imported {outcome.Imported} account{(outcome.Imported == 1 ? string.Empty : "s")}.{notes}", critical: false);
        }
        catch (AccountException ex)
        {
            Say(verdict, ex.Message, critical: true);
            button.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "importing classic accounts failed unexpectedly", ex);
            Say(verdict, $"The import failed: {ex.Message}", critical: true);
            button.IsEnabled = true;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Small pieces
    // -----------------------------------------------------------------------------------------

    private static string ProviderGlyph(AccountProvider provider) => provider switch
    {
        AccountProvider.Microsoft => char.ConvertFromUtf32(0xE753),
        AccountProvider.Google => char.ConvertFromUtf32(0xE774),
        AccountProvider.Imap => Glyphs.Mail,
        AccountProvider.CalDav => Glyphs.Calendar,
        AccountProvider.CardDav => Glyphs.Contact,
        AccountProvider.GitHub => char.ConvertFromUtf32(0xE943),
        AccountProvider.Claude => char.ConvertFromUtf32(0xE99A),
        _ => Glyphs.Info,
    };

    private static StackPanel Labelled(string glyph, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Fluent.Space2,
        Children = { new FontIcon { Glyph = glyph, FontSize = Fluent.IconSize }, new TextBlock { Text = text } },
    };

    private static StackPanel Buttons(params FrameworkElement[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
        };

        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>A busy ring and the line under a flow that says what is happening or what went wrong.</summary>
    private static (StackPanel Panel, ProgressRing Ring, TextBlock Line) StatusLine(string text)
    {
        var ring = new ProgressRing { IsActive = true, Width = Fluent.IconSize, Height = Fluent.IconSize };
        var line = Message(text, critical: false);
        line.Margin = new Thickness(0);
        line.VerticalAlignment = VerticalAlignment.Center;

        return (new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Margin = new Thickness(0, Fluent.Space1, 0, 0),
            Children = { ring, line },
        }, ring, line);
    }

    private static TextBlock Message(string text, bool critical)
    {
        var line = Fluent.Text(FluentType.Body, critical ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
        line.TextWrapping = TextWrapping.Wrap;
        line.TextTrimming = TextTrimming.None;
        line.Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1);
        Say(line, text, critical);
        return line;
    }

    private static void Say(TextBlock line, string text, bool critical)
    {
        line.Text = text;
        line.Foreground = FluentLayout.Themed(critical ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush", critical ? Colors.Red : Colors.Gray);
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Fail(ProgressRing ring, TextBlock line, string message, params Button[] disable)
    {
        ring.IsActive = false;
        ring.Visibility = Visibility.Collapsed;
        Say(line, message, critical: true);

        foreach (var button in disable)
        {
            button.IsEnabled = false;
        }
    }

    private static void Copy(string text, TextBlock line)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Say(line, "Code copied. Paste it on the sign-in page.", critical: false);
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; say so rather than claim it copied.
            Log.Warn(Scope, "could not copy the sign-in code", ex);
            Say(line, "The clipboard is busy. Type the code instead.", critical: true);
        }
    }

    private static void Open(Uri url, TextBlock line, FrameworkElement? status = null)
    {
        if (ShellLauncher.Open(url.AbsoluteUri) != LaunchOutcome.Opened)
        {
            if (status is not null)
            {
                status.Visibility = Visibility.Visible;
            }

            Say(line, $"The browser could not be opened. Go to {url.Host} yourself.", critical: true);
        }
    }

    private static Style? AccentStyle()
        => Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;
}
