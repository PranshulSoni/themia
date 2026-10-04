using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-628] The parts every developer widget shares: the state shown in place of content (with the
/// way to the Accounts page), and the header's trailing warning and count.
/// <para>
/// A plain object the view owns rather than a base class: a view is a sealed WinUI element, and the
/// four of them differ in everything but these two pieces.
/// </para>
/// </summary>
internal sealed class DevChrome
{
    private const string Scope = "developer";

    /// <summary>Below this height the accounts link under a state has no room; the sentence gets it all.</summary>
    private const double LinkNeedsHeight = 100;

    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly FontIcon _warning;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly HyperlinkButton _accountsLink;
    private bool _offersAccounts;
    private bool _warningOpensAccounts;

    public DevChrome(string glyph)
    {
        _badgeHost = Fluent.Badge(_badge);
        _badgeHost.Visibility = Visibility.Collapsed;

        _warning = Fluent.Icon(Glyphs.Warning, "SystemFillColorCautionBrush", 14);
        _warning.Visibility = Visibility.Collapsed;
        _warning.Tapped += OnWarningTapped;

        HeaderTrailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _warning, _badgeHost },
        };

        _empty = Fluent.EmptyState(glyph, _emptyText, out _);

        _accountsLink = new HyperlinkButton
        {
            Content = "Open Accounts settings",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1),
            Visibility = Visibility.Collapsed,
        };
        _accountsLink.Click += OnAccountsClicked;

        // The sentence and the link, centred together. The sentence's panel sheds its icon and
        // lines against a height cap set on resize, because an Auto row measures without one.
        var host = new Grid { Visibility = Visibility.Collapsed };
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_accountsLink, 2);
        host.Children.Add(_empty);
        host.Children.Add(_accountsLink);
        StatePanel = host;
    }

    /// <summary>The warning and the count, for the card's header. Built once: the card reads it once.</summary>
    public FrameworkElement HeaderTrailing { get; }

    /// <summary>The state shown in place of content. The view puts it over its whole body.</summary>
    public Grid StatePanel { get; }

    /// <summary>
    /// Show a screen. Returns true when the view should draw its content, false when the state
    /// panel has taken the body.
    /// </summary>
    public bool Show(DevScreen screen, double height, string? actionProblem = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var warning = actionProblem ?? screen.Warning;
        _warningOpensAccounts = actionProblem is null && screen.OfferAccounts;
        _warning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_warning, warning);

        if (screen.Kind == DevScreenKind.Content)
        {
            StatePanel.Visibility = Visibility.Collapsed;
            return true;
        }

        SetCount(0);
        _emptyText.Text = screen.Text;
        _offersAccounts = screen.OfferAccounts;
        StatePanel.Visibility = Visibility.Visible;
        Fit(height);
        return false;
    }

    /// <summary>The header's count pill; hidden at zero.</summary>
    public void SetCount(int count)
    {
        _badge.Text = count.ToString(CultureInfo.InvariantCulture);
        _badgeHost.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The link shows when the screen offers it and there is room; the sentence gets the rest.</summary>
    public void Fit(double height)
    {
        var link = _offersAccounts && (height <= 0 || height >= LinkNeedsHeight);
        _accountsLink.Visibility = link ? Visibility.Visible : Visibility.Collapsed;
        _empty.MaxHeight = height > 0 ? Math.Max(0, height - (link ? 36 : 0)) : double.PositiveInfinity;
    }

    public void Detach()
    {
        _warning.Tapped -= OnWarningTapped;
        _accountsLink.Click -= OnAccountsClicked;
    }

    private void OnAccountsClicked(object sender, RoutedEventArgs args) => ShowAccounts();

    private void OnWarningTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        if (_warningOpensAccounts)
        {
            ShowAccounts();
            args.Handled = true;
        }
    }

    public static void ShowAccounts()
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
}

/// <summary>What a list row shows at the widget's current size.</summary>
/// <param name="TwoLine">Title and age over the lead and meta, rather than one line.</param>
/// <param name="Time">The age, at the end of the (first) line.</param>
/// <param name="Lead">The coloured lead on a one-line row.</param>
/// <param name="Meta">The meta on a one-line row.</param>
internal readonly record struct DevRowLayout(bool TwoLine, bool Time, bool Lead, bool Meta);

/// <summary>One list row's text, as the developer lists draw it.</summary>
/// <param name="Lead">The short coloured lead: <c>#312</c>, a reason word, a commit's hash.</param>
/// <param name="Meta">What goes after the lead on the second line: the repository, a branch.</param>
/// <param name="Dim">Read, or a draft: drawn in the secondary colour.</param>
/// <param name="Url">Opened on a double-click, through the launcher's allow list.</param>
internal sealed record DevRowText(string Title, string Lead, Brush LeadBrush, string Meta, string Time, bool Dim, string? Url);

/// <summary>[S-627] Building and opening developer list rows.</summary>
internal static class DevRows
{
    private const string Scope = "developer";

    /// <summary>Under this width a row drops its time; the title is what the row is for.</summary>
    public const double TimeNeedsWidth = 180;

    /// <summary>Under this width a one-line row drops its lead too, and is the title alone.</summary>
    public const double LeadNeedsWidth = 160;

    /// <summary>From this width a one-line row has room for its meta (the repository) as well.</summary>
    public const double MetaInlineWidth = 400;

    /// <summary>
    /// What a list's rows show at a size: two lines where the body is tall enough, the age, the
    /// coloured lead and — on one line — the meta only where they fit. An unmeasured box gets all.
    /// </summary>
    public static DevRowLayout LayoutFor(double width, double height, bool twoLineAllowed = true)
    {
        var w = width > 0 ? width : double.PositiveInfinity;
        var h = height > 0 ? height : double.PositiveInfinity;
        return new DevRowLayout(twoLineAllowed && h >= Fluent.CompactBelow, w >= TimeNeedsWidth, w >= LeadNeedsWidth, w >= MetaInlineWidth);
    }

    /// <summary>
    /// A row: two lines (title and time over the coloured lead and its meta) or, compact, one line
    /// (lead, title, meta where it fits, time). Edge to edge with the text inset; double-click opens it.
    /// </summary>
    public static Border Build(DevRowText text, DevRowLayout layout, List<IDisposable> hovers)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(hovers);

        var twoLine = layout.TwoLine;
        var showTime = layout.Time;

        var title = Fluent.Text(FluentType.Body, text.Dim ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        title.Text = text.Title;
        title.VerticalAlignment = VerticalAlignment.Center;

        var lead = Fluent.Text(FluentType.Caption);
        lead.Text = text.Lead;
        lead.Foreground = text.LeadBrush;
        lead.VerticalAlignment = VerticalAlignment.Center;
        lead.TextTrimming = TextTrimming.None;

        var time = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        time.Text = text.Time;
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
        time.TextTrimming = TextTrimming.None;

        FrameworkElement content;

        if (twoLine)
        {
            var meta = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            meta.Text = text.Meta;
            meta.VerticalAlignment = VerticalAlignment.Center;

            var top = Columns(title, showTime ? time : null);
            var bottom = Columns(null, null);
            bottom.ColumnDefinitions[0].Width = GridLength.Auto;
            bottom.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            lead.Margin = new Thickness(0, 0, text.Meta.Length > 0 ? Fluent.Space2 : 0, 0);
            Grid.SetColumn(lead, 0);
            Grid.SetColumn(meta, 1);
            bottom.Children.Add(lead);
            bottom.Children.Add(meta);

            content = new StackPanel
            {
                Spacing = 0,
                Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
                Children = { top, bottom },
            };
        }
        else
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            if (layout.Lead && text.Lead.Length > 0)
            {
                lead.Margin = new Thickness(0, 0, Fluent.Space2, 0);
                Grid.SetColumn(lead, 0);
                grid.Children.Add(lead);
            }

            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            if (layout.Meta && text.Meta.Length > 0)
            {
                var meta = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                meta.Text = text.Meta;
                meta.MaxWidth = 160;
                meta.VerticalAlignment = VerticalAlignment.Center;
                meta.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
                Grid.SetColumn(meta, 2);
                grid.Children.Add(meta);
            }

            if (showTime)
            {
                Grid.SetColumn(time, 3);
                grid.Children.Add(time);
            }

            content = grid;
        }

        var row = Fluent.Row(content, compact: !twoLine);
        hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, text.Meta.Length > 0 ? $"{text.Title}\n{text.Lead} {text.Meta}" : text.Title);

        if (text.Url is { } url)
        {
            row.DoubleTapped += (_, args) =>
            {
                Open(url);
                args.Handled = true;
            };
        }

        return row;
    }

    /// <summary>A small caption heading a section of a list, inset like the row text.</summary>
    public static TextBlock Section(string text, string? trailing = null)
    {
        var block = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        block.Text = trailing is null ? text : $"{text}  ·  {trailing}";
        block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        block.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space1);
        return block;
    }

    /// <summary>[S-627] Open a GitHub page. Only through the launcher, which allows http and https and nothing else.</summary>
    public static void Open(string url)
    {
        if (ShellLauncher.Open(url) != LaunchOutcome.Opened)
        {
            Log.Info(Scope, "a GitHub link did not open (see the launcher's line above)");
        }
    }

    private static Grid Columns(FrameworkElement? left, FrameworkElement? right)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        if (left is not null)
        {
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);
        }

        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }

        return grid;
    }
}

/// <summary>
/// A scrolling list that shows only whole rows. A list in a scroll viewer otherwise ends in a row
/// sliced through at the card's bottom edge; this host gives the viewer only the height of the
/// rows that fit, so the cut falls between rows and never right after a section heading. The rest
/// is still a scroll away. Decided in the layout pass itself, from the rows' measured sizes, so it
/// is right on the first frame and needs no resize events.
/// </summary>
internal sealed partial class WholeRows : Panel
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _list;

    public WholeRows(ScrollViewer scroll, StackPanel list)
    {
        _scroll = scroll;
        _list = list;
        Children.Add(scroll);

        // Rows rebuilt for a new density change the list but not this panel's own size, which
        // would leave the cap from the old rows in place. The two live and die together, so the
        // handler needs no unhooking.
        _list.SizeChanged += (_, _) => InvalidateArrange();
    }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        _scroll.Measure(availableSize);
        return new Windows.Foundation.Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : _scroll.DesiredSize.Width,
            double.IsFinite(availableSize.Height) ? availableSize.Height : _scroll.DesiredSize.Height);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        _scroll.Arrange(new Windows.Foundation.Rect(0, 0, finalSize.Width, Cap(finalSize.Height)));
        return finalSize;
    }

    private double Cap(double height)
    {
        var padding = _scroll.Padding.Top + _scroll.Padding.Bottom;
        var room = height - padding + 0.5;
        var used = 0.0;
        var lastWhole = 0.0;

        foreach (var child in _list.Children)
        {
            if (child is not FrameworkElement { Visibility: Visibility.Visible } element)
            {
                continue;
            }

            // DesiredSize carries the margins; the list measured every row with unbounded height.
            var rowHeight = element.DesiredSize.Height;
            if (used + rowHeight > room)
            {
                break;
            }

            used += rowHeight;
            if (element is not TextBlock)
            {
                lastWhole = used;
            }
        }

        return lastWhole > 0 ? Math.Min(height, lastWhole + padding) : height;
    }
}

/// <summary>The theme brushes the developer widgets colour state with. Tokens, never literals.</summary>
internal static class DevBrushes
{
    public static Brush Success => FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.SeaGreen);

    public static Brush Caution => FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);

    public static Brush Critical => FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

    public static Brush Accent => FluentLayout.Themed("AccentTextFillColorPrimaryBrush", Colors.DodgerBlue);

    public static Brush Secondary => FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

    public static Brush Tertiary => FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);

    /// <summary>
    /// [S-610] A pull request's number: open is success, merged is the accent (Fluent has no
    /// purple, and merged is the "done well" state), closed unmerged is critical, a draft tertiary.
    /// </summary>
    public static Brush Pull(PullState state, bool draft) => draft
        ? Tertiary
        : state switch
        {
            PullState.Open => Success,
            PullState.Merged => Accent,
            _ => Critical,
        };

    public static Brush Run(RunState state) => state switch
    {
        RunState.Success => Success,
        RunState.Failure => Critical,
        RunState.Running or RunState.Queued => Caution,
        _ => Tertiary,
    };

    /// <summary>[S-618] A notification reason's colour.</summary>
    public static Brush Reason(ReasonTone tone) => tone switch
    {
        ReasonTone.Critical => Critical,
        ReasonTone.Attention => Caution,
        ReasonTone.Accent => Accent,
        _ => Secondary,
    };
}
