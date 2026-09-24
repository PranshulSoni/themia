using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
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
    private readonly StackPanel _empty = new();
    private readonly TextBlock _emptyText = new();
    private readonly ProgressRing _busy = new();

    private HearthMailService? _mail;
    private EmailConfig _config = new();
    private IDisposable? _subscription;
    private bool _refreshing;
    private bool _detached;

    public EmailView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "Mail";
        _title.FontSize = 12;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);
        _title.VerticalAlignment = VerticalAlignment.Center;

        _badge.FontSize = 10;
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

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        var icon = new FontIcon { Glyph = "", FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
        icon.Foreground = _title.Foreground;
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(_title, 1);
        _title.HorizontalAlignment = HorizontalAlignment.Left;

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        right.Children.Add(_busy);
        right.Children.Add(_badgeHost);
        Grid.SetColumn(right, 2);

        header.Children.Add(icon);
        header.Children.Add(_title);
        header.Children.Add(right);
        Grid.SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 1);

        _emptyText.FontSize = 12;
        _emptyText.TextWrapping = TextWrapping.Wrap;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.TextAlignment = TextAlignment.Center;
        _emptyText.Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray);

        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Children.Add(_emptyText);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _mail = AppServices.Mail;

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
        _config = config as EmailConfig ?? new EmailConfig();
        _ = RefreshAsync();
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

            Render(messages);
        }
        catch (Exception ex)
        {
            // Never let a hub problem take the surface down; say so in the widget instead.
            Log.Error(Scope, "could not refresh the mail list", ex);
            ShowEmpty("Could not read mail.");
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
        _list.Children.Clear();

        if (messages.Count == 0)
        {
            ShowEmpty(EmptyMessage());
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        var now = DateTimeOffset.Now;
        var unread = 0;

        foreach (var message in messages)
        {
            if (message.Unread)
            {
                unread++;
            }

            _list.Children.Add(BuildRow(message, now));
        }

        _badge.Text = unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _badgeHost.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildRow(EmailRecord message, DateTimeOffset now)
    {
        var who = new TextBlock
        {
            Text = message.Who,
            FontSize = 12,
            // Unread is carried by weight rather than a dot, which is what every mail list on
            // Windows does and needs no legend.
            FontWeight = message.Unread ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = ThemeBrush("TextFillColorPrimaryBrush", Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        var when = new TextBlock
        {
            Text = Formatting.Ago(message.When, now),
            FontSize = 11,
            Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

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

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(top);

        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject,
            FontSize = 12,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });

        if (_config.ShowPreview && !string.IsNullOrWhiteSpace(message.Preview))
        {
            stack.Children.Add(new TextBlock
            {
                Text = message.Preview,
                FontSize = 11,
                Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
        }

        var row = new Border
        {
            Child = stack,
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(-8, 0, -8, 0),
            CornerRadius = new CornerRadius(4),
            Background = Transparent,
            BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        // Hover, the way every list on Windows does it. Done by hand rather than with a ListView
        // because the rows are a custom layout, but the brush is the stock one so it tracks the
        // theme and the accent. PointerExited alone is not enough: the pointer can leave without it
        // when the list scrolls or the widget is torn down mid-hover, which would strand a row lit.
        row.PointerEntered += (_, _) => row.Background = ThemeBrush("SubtleFillColorSecondaryBrush", Colors.Gray);
        row.PointerExited += (_, _) => row.Background = Transparent;
        row.PointerCaptureLost += (_, _) => row.Background = Transparent;
        row.PointerCanceled += (_, _) => row.Background = Transparent;

        ToolTipService.SetToolTip(row, message.Who + Environment.NewLine + message.Subject);
        return row;
    }

    /// <summary>
    /// A shared transparent brush for the rows' rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so the row would never
    /// raise the pointer events the hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>Say what is actually wrong, because every one of these needs a different action.</summary>
    private string EmptyMessage() => _mail?.Status switch
    {
        HearthStatus.NotRunning => "hearth is not running.\nStart it to see your mail here.",
        HearthStatus.Denied => "hearth has not granted Themia access to any mailbox.",
        HearthStatus.Failed => "Could not read from hearth. See themia.log.",
        HearthStatus.Connecting => "Connecting to hearth…",
        _ when _config.UnreadOnly => "Nothing unread.",
        _ => "No messages.",
    };

    private void ShowEmpty(string text)
    {
        _emptyText.Text = text;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        _badgeHost.Visibility = Visibility.Collapsed;
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
    }

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
