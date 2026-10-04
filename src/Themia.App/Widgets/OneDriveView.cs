using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Services.Files;
using Themia.App.Shell;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-606] Files on a Microsoft account's OneDrive: the ones touched lately, or a folder walked into
/// from the top of the drive.
/// <para>
/// Read-only, and nothing is downloaded: a file opens as its OneDrive page in the browser, through
/// <see cref="ShellLauncher"/>'s allow list like every other launch in the app, and only on a
/// double-click — a widget sits in the pointer's path across the desktop, and one stray click
/// should not open a browser. Walking into a folder is a single click: it only changes the widget.
/// </para>
/// </summary>
internal sealed partial class OneDriveView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetContextActions
{
    private const string Scope = "onedrive-widget";

    /// <summary>Below this the link under an empty state is dropped; the sentence is worth more.</summary>
    private const double LinkNeedsHeight = 140;

    /// <summary>Under this width the size and age line is dropped: the name is what the row is for.</summary>
    private const double MetaNeedsWidth = 180;

    private static readonly TimeSpan CountDebounce = TimeSpan.FromMilliseconds(400);

    private readonly ProgressRing _busy = new();
    private readonly FontIcon _warning;
    private readonly StackPanel _trailing;
    private readonly Border _crumb;
    private readonly TextBlock _crumbText;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly Grid _emptyHost = new();
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly HyperlinkButton _emptyAction;
    private readonly List<IDisposable> _hovers = [];
    private readonly List<Border> _rows = [];
    private readonly IDisposable _crumbHover;
    private readonly DispatcherQueueTimer? _countTimer;

    private DriveService? _service;
    private IDisposable? _subscription;
    private OneDriveConfig _config = new();
    private (string? AccountId, DriveStart Start, int Count)? _bound;

    private IReadOnlyList<DriveFolder> _trail = [];
    private IReadOnlyList<DriveItem>? _items;
    private AccountException? _failure;
    private DriveAccount? _account;
    private string? _blocked;
    private int _generation;
    private bool _detached;

    public OneDriveView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _busy.Width = Fluent.IconSize;
        _busy.Height = Fluent.IconSize;
        _busy.Visibility = Visibility.Collapsed;

        _warning = Fluent.Icon(Glyphs.Warning, "SystemFillColorCautionBrush");
        _warning.Visibility = Visibility.Collapsed;

        _trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _warning, _busy },
        };

        // [S-609] The way back out of a folder: a row like any other, so it reaches both edges.
        _crumbText = Fluent.Text(FluentType.BodyStrong);
        _crumbText.TextTrimming = TextTrimming.CharacterEllipsis;
        _crumbText.VerticalAlignment = VerticalAlignment.Center;

        var back = Fluent.Icon(Glyphs.Back);
        back.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var crumbBody = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetColumn(_crumbText, 1);
        crumbBody.Children.Add(back);
        crumbBody.Children.Add(_crumbText);

        _crumb = Fluent.Row(crumbBody, compact: true);
        _crumb.Visibility = Visibility.Collapsed;
        _crumb.Tapped += OnCrumbTapped;
        _crumbHover = Fluent.Hover(_crumb);
        ToolTipService.SetToolTip(_crumb, "Back");
        Grid.SetRow(_crumb, 0);

        _scroll = new ScrollViewer
        {
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Grid.SetRow(_scroll, 1);

        _empty = Fluent.EmptyState(Glyphs.OneDrive, _emptyText, out _);
        _emptyAction = new HyperlinkButton
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1),
            Visibility = Visibility.Collapsed,
        };
        _emptyAction.Click += OnEmptyActionClicked;

        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emptyHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_emptyAction, 2);
        _emptyHost.Children.Add(_empty);
        _emptyHost.Children.Add(_emptyAction);
        _emptyHost.Visibility = Visibility.Collapsed;
        Grid.SetRow(_emptyHost, 1);

        Children.Add(_crumb);
        Children.Add(_scroll);
        Children.Add(_emptyHost);

        _countTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_countTimer is not null)
        {
            _countTimer.Interval = CountDebounce;
            _countTimer.IsRepeating = false;
            _countTimer.Tick += OnCountSettled;
        }

        SizeChanged += OnSizeChanged;
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _trailing;

    private bool Compact => ActualHeight > 0 && ActualHeight < Fluent.CompactBelow;

    /// <summary>The row height and whether the size line fits, as the rows on screen were drawn.</summary>
    private (bool Compact, bool Meta)? _drawnDensity;

    private (bool Compact, bool Meta) Density() => (Compact, ActualWidth <= 0 || ActualWidth >= MetaNeedsWidth);

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        _service = AppServices.Drive;

        if (_service is not null)
        {
            _service.AccountsChanged += OnAccountsChanged;
        }

        // [S-608] A drive changes slowly; the shared ticker asks again every five minutes.
        _subscription = ticker.Subscribe(OneDriveConfig.RefreshInterval, OnTick);
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as OneDriveConfig ?? new OneDriveConfig();

        // The settings page edits this very object in place, so the comparison is against what was
        // last loaded, never against "the previous config".
        var key = (Empty(_config.AccountId), _config.StartIn, _config.EffectiveFileCount);
        if (key == _bound)
        {
            return;
        }

        var onlyCount = _bound is { } bound && bound.AccountId == key.Item1 && bound.Start == key.StartIn;
        _bound = key;

        if (onlyCount && _countTimer is not null)
        {
            // A slider dragged across the range is one request when it settles, not one per step.
            _countTimer.Stop();
            _countTimer.Start();
            return;
        }

        // [S-609] A different account or start throws the folder trail away.
        _trail = [];
        _items = null;
        Load(TimeSpan.Zero);
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions
    {
        get
        {
            var actions = new List<WidgetAction> { new(Glyphs.Refresh, "Refresh", () => Load(TimeSpan.Zero)) };

            if (_trail.Count > 0)
            {
                actions.Add(new WidgetAction(Glyphs.Home, _config.StartIn == DriveStart.Root ? "Back to My files" : "Back to recent files", () =>
                {
                    _trail = [];
                    _items = null;
                    Load(TimeSpan.Zero);
                }));
            }

            return actions;
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private void OnCountSettled(DispatcherQueueTimer sender, object args) => Load(TimeSpan.Zero);

    private void OnTick() => Load(OneDriveConfig.RefreshInterval - TimeSpan.FromSeconds(30));

    private void OnAccountsChanged()
    {
        _items = null;
        Load(TimeSpan.Zero);
    }

    /// <summary>
    /// Show the current place: the innermost folder of the trail, else the configured start. A
    /// listing younger than <paramref name="maxAge"/> comes from the service's memory.
    /// </summary>
    private void Load(TimeSpan maxAge)
    {
        if (_detached)
        {
            return;
        }

        // [S-609] A reply for a place the widget has since left is dropped, never drawn (#110).
        var generation = ++_generation;
        _blocked = null;
        _account = null;

        if (_service is null)
        {
            _blocked = "Themia is still starting.";
            Render();
            return;
        }

        var accounts = _service.Accounts;
        var named = !string.IsNullOrEmpty(_config.AccountId);
        _account = named ? _service.Find(_config.AccountId) : accounts.FirstOrDefault();

        if (_account is null)
        {
            _blocked = named && accounts.Count > 0
                ? "The account this widget showed is no longer signed in."
                : "Sign in to Microsoft to see your OneDrive.";
            Render();
            return;
        }

        if (_account.Problem is not null)
        {
            _blocked = $"{_account.Label} needs you to sign in again.";
            Render();
            return;
        }

        _ = LoadAsync(_account.Id, DriveRules.Address(_trail, _config.StartIn, _config.EffectiveFileCount), maxAge, generation);
    }

    private async Task LoadAsync(string accountId, Uri address, TimeSpan maxAge, int generation)
    {
        SetBusy(true);
        Render();

        try
        {
            var items = await _service!.ListAsync(accountId, address, _config.EffectiveFileCount, maxAge).ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            _items = items;
            _failure = null;
        }
        catch (AccountException ex)
        {
            if (_detached || generation != _generation)
            {
                return;
            }

            Log.Warn(Scope, $"could not list {address.AbsolutePath}", ex);
            _failure = ex;
        }
        catch (Exception ex)
        {
            if (_detached || generation != _generation)
            {
                return;
            }

            Log.Error(Scope, $"listing {address.AbsolutePath} failed unexpectedly", ex);
            _failure = new AccountException(AccountError.BadResponse, "The files could not be read.", 0, ex);
        }
        finally
        {
            if (generation == _generation)
            {
                SetBusy(false);
            }
        }

        Render();
    }

    private void SetBusy(bool busy)
    {
        _busy.IsActive = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- drawing -----------------------------------------------------------------------------------

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();
        _drawnDensity = null;

        _crumb.Visibility = _trail.Count > 0 && _blocked is null ? Visibility.Visible : Visibility.Collapsed;
        _crumbText.Text = _trail.Count > 0 ? _trail[^1].Name : string.Empty;
        Fluent.SetDensity(_crumb, true);

        var stale = _items is not null && _failure is not null;
        _warning.Visibility = stale ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_warning, stale ? $"Could not refresh: {FetchState.Message(_failure!.ToFetchStatus())}." : null);

        if (_blocked is { } blocked)
        {
            ShowEmpty(blocked, _service is null ? null : EmptyAction.Accounts);
            return;
        }

        if (_items is null)
        {
            if (_failure is { } failure)
            {
                ShowEmpty(Explain(failure), failure.Error is AccountError.SignInRequired or AccountError.Forbidden
                    ? EmptyAction.Accounts
                    : EmptyAction.Retry);
            }
            else
            {
                ShowEmpty("Loading files…", null);
            }

            return;
        }

        if (_items.Count == 0)
        {
            ShowEmpty(_trail.Count > 0 ? "This folder is empty." : _config.StartIn == DriveStart.Root ? "Your OneDrive is empty." : "No recent files.", null);
            return;
        }

        _emptyHost.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var now = DateTimeOffset.Now;
        var (compact, meta) = Density();
        _drawnDensity = (compact, meta);

        foreach (var item in _items)
        {
            _list.Children.Add(BuildRow(item, now, compact, meta));
        }
    }

    private static string Explain(AccountException failure) => failure.Error switch
    {
        AccountError.SignInRequired => "This account needs you to sign in again.",
        AccountError.Forbidden => "This account does not let Themia read its files. Sign in again to grant it.",
        AccountError.NotFound => "That folder is not there any more.",
        _ => FetchState.Message(failure.ToFetchStatus()) is { Length: > 0 } message ? $"{message}." : "Could not read the files.",
    };

    /// <summary>
    /// [S-606] One item: its kind's icon, its name, and under it the size and how long ago it
    /// changed. Two lines on a standard row; the compact row keeps the name alone.
    /// </summary>
    private Border BuildRow(DriveItem item, DateTimeOffset now, bool compact, bool showMeta)
    {
        var icon = Fluent.Icon(Glyph(DriveRules.Kind(item)), item.IsFolder ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body);
        name.Text = item.Name;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);

        var metaText = DriveRules.Meta(item, now);
        if (showMeta && !compact && metaText.Length > 0)
        {
            var line = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            line.Text = metaText;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
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
        Grid.SetColumn(text, 1);
        body.Children.Add(icon);
        body.Children.Add(text);

        var row = Fluent.Row(body, compact);
        row.Tag = item;
        _hovers.Add(Fluent.Hover(row));
        _rows.Add(row);

        if (item.IsFolder)
        {
            row.Tapped += OnFolderTapped;
        }
        else
        {
            row.DoubleTapped += OnFileDoubleTapped;
        }

        ToolTipService.SetToolTip(row, $"{item.Name}{(metaText.Length > 0 ? $"{Environment.NewLine}{metaText}" : string.Empty)}{Environment.NewLine}"
            + (item.IsFolder ? "Click to open the folder" : "Double-click to open it in your browser"));
        return row;
    }

    private static string Glyph(DriveItemKind kind) => kind switch
    {
        DriveItemKind.Folder => Glyphs.Folder,
        DriveItemKind.Image => Glyphs.Gallery,
        DriveItemKind.Video => Glyphs.Video,
        DriveItemKind.Audio => Glyphs.Audio,
        DriveItemKind.Spreadsheet => Glyphs.Spreadsheet,
        DriveItemKind.Presentation => Glyphs.Presentation,
        DriveItemKind.Document => Glyphs.Document,
        _ => Glyphs.File,
    };

    private enum EmptyAction
    {
        Accounts,
        Retry,
    }

    private void ShowEmpty(string text, EmptyAction? action)
    {
        _scroll.Visibility = Visibility.Collapsed;
        _emptyHost.Visibility = Visibility.Visible;
        _emptyText.Text = text;
        _emptyAction.Tag = action;
        _emptyAction.Content = action switch
        {
            EmptyAction.Accounts => "Open Accounts settings",
            EmptyAction.Retry => "Try again",
            _ => null,
        };
        FitEmptyState();
    }

    private void FitEmptyState()
    {
        var height = ActualHeight;
        var link = _emptyAction.Tag is EmptyAction && (height <= 0 || height >= LinkNeedsHeight);
        _emptyAction.Visibility = link ? Visibility.Visible : Visibility.Collapsed;
        _empty.MaxHeight = height > 0 ? Math.Max(0, height - (link ? 36 : 0)) : double.PositiveInfinity;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        FitEmptyState();

        // Compared with what was drawn, not with the previous size: the first rows can be drawn
        // before the tile has any size at all.
        if (_drawnDensity is { } drawn && drawn != Density())
        {
            Render();
        }
    }

    // ---- clicks ------------------------------------------------------------------------------------------

    /// <summary>[S-609] Walk into a folder.</summary>
    private void OnFolderTapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is Border { Tag: DriveItem folder })
        {
            args.Handled = true;
            _trail = DriveRules.Enter(_trail, folder);
            _items = null;
            Load(TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>[S-609] One step back; past the first folder, back to the configured start.</summary>
    private void OnCrumbTapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;
        _trail = DriveRules.Back(_trail);
        _items = null;
        Load(TimeSpan.FromMinutes(1));
    }

    /// <summary>[S-606] A file opens as its OneDrive page, through the launcher's allow list.</summary>
    private void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is Border { Tag: DriveItem file })
        {
            args.Handled = true;
            var outcome = ShellLauncher.Open(file.WebUrl);
            if (outcome != LaunchOutcome.Opened)
            {
                Log.Warn(Scope, $"could not open {file.Name}: {outcome}");
            }
        }
    }

    private void OnEmptyActionClicked(object sender, RoutedEventArgs args)
    {
        if (_emptyAction.Tag is EmptyAction.Retry)
        {
            Load(TimeSpan.Zero);
        }
        else if (AppServices.ShowAccounts is { } show)
        {
            show();
        }
        else
        {
            Log.Info(Scope, "no settings window here to show the Accounts page in");
        }
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        foreach (var row in _rows)
        {
            row.Tapped -= OnFolderTapped;
            row.DoubleTapped -= OnFileDoubleTapped;
        }

        _hovers.Clear();
        _rows.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _generation++;

        SizeChanged -= OnSizeChanged;
        _crumb.Tapped -= OnCrumbTapped;
        _crumbHover.Dispose();
        _emptyAction.Click -= OnEmptyActionClicked;

        if (_countTimer is not null)
        {
            _countTimer.Stop();
            _countTimer.Tick -= OnCountSettled;
        }

        _subscription?.Dispose();
        _subscription = null;

        if (_service is not null)
        {
            _service.AccountsChanged -= OnAccountsChanged;
            _service = null;
        }

        ClearRows();
    }
}
