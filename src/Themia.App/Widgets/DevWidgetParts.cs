using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;

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
            Content = Str.WidgetsA_Office_OpenAccounts.Text(),
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
/// <param name="Detail">[S-743] The row's detail (author, comments, subject) at its right edge.</param>
/// <param name="Labels">[S-743] The row's label chips.</param>
internal readonly record struct DevRowLayout(bool TwoLine, bool Time, bool Lead, bool Meta, bool Detail = false, bool Labels = false);

/// <summary>One list row's text, as the developer lists draw it.</summary>
/// <param name="Lead">The short coloured lead: <c>#312</c>, a reason word, a commit's hash.</param>
/// <param name="Meta">What goes after the lead on the second line: the repository, a branch.</param>
/// <param name="Dim">Read, or a draft: drawn in the secondary colour.</param>
/// <param name="Url">Opened on a double-click, through the launcher's allow list.</param>
/// <param name="Detail">[S-743] Secondary facts for a wide row: "rosa · 4 comments".</param>
/// <param name="Labels">[S-743] Label chips for a wide row; null for none.</param>
/// <param name="Unread">[S-748] Null for a list without read state; else whether to draw the unread dot (its slot is kept either way, so titles align).</param>
internal sealed record DevRowText(
    string Title,
    string Lead,
    Brush LeadBrush,
    string Meta,
    string Time,
    bool Dim,
    string? Url,
    string Detail = "",
    IReadOnlyList<IssueLabel>? Labels = null,
    bool? Unread = null);

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

    /// <summary>[S-743] From this width a two-line row puts its detail at the right of its second line.</summary>
    public const double DetailTwoLineWidth = 400;

    /// <summary>[S-743] From this width a two-line row adds its label chips.</summary>
    public const double LabelsTwoLineWidth = 480;

    /// <summary>[S-743] A one-line row has the title to feed as well: detail and chips need more.</summary>
    public const double DetailOneLineWidth = 640;

    public const double LabelsOneLineWidth = 720;

    /// <summary>[S-743] Chips a row draws at most; the tooltip names the rest.</summary>
    public const int ChipsShown = 2;

    /// <summary>A one-line row's height: the compact row.</summary>
    public const double OneLineHeight = Fluent.RowHeightCompact;

    /// <summary>A two-line row's height: body and caption lines plus the row's inner margin.</summary>
    public const double TwoLineHeight = (Fluent.Space1 * 2) + 20 + 16;

    /// <summary>A section heading's height: a caption line plus its margins.</summary>
    public const double SectionHeight = 16 + Fluent.Space2 + Fluent.Space1;

    /// <summary>
    /// What a list's rows show at a size: two lines where the body is tall enough, the age, the
    /// coloured lead and — on one line — the meta only where they fit; the detail and the label
    /// chips where the width has room for them beside the rest. An unmeasured box gets all.
    /// </summary>
    /// <param name="twoLineFrom">The body height two-line rows need; lists that would rather show more about each item than more items pass less.</param>
    public static DevRowLayout LayoutFor(double width, double height, bool twoLineAllowed = true, double twoLineFrom = Fluent.CompactBelow)
    {
        var w = width > 0 ? width : double.PositiveInfinity;
        var h = height > 0 ? height : double.PositiveInfinity;
        var twoLine = twoLineAllowed && h >= twoLineFrom;
        return new DevRowLayout(
            twoLine,
            w >= TimeNeedsWidth,
            w >= LeadNeedsWidth,
            w >= MetaInlineWidth,
            w >= (twoLine ? DetailTwoLineWidth : DetailOneLineWidth),
            w >= (twoLine ? LabelsTwoLineWidth : LabelsOneLineWidth));
    }

    /// <summary>
    /// A row: two lines (title and time over the coloured lead, its meta, chips and detail) or,
    /// compact, one line (lead, title, meta, chips, detail where they fit, time). Edge to edge with
    /// the text inset; double-click opens it, right-click opens <paramref name="menu"/> when given.
    /// </summary>
    public static Border Build(DevRowText text, DevRowLayout layout, List<IDisposable> hovers, Func<List<ItemMenuEntry>>? menu = null)
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

        var marker = text.Unread is { } unread && layout.Lead ? UnreadDot(unread) : null;
        var chips = layout.Labels && text.Labels is { Count: > 0 } labels ? Chips(labels) : null;
        var detail = layout.Detail && text.Detail.Length > 0 ? Caption(text.Detail, "TextFillColorTertiaryBrush") : null;

        FrameworkElement content;

        if (twoLine)
        {
            var meta = Caption(text.Meta, "TextFillColorTertiaryBrush");

            var top = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };
            Place(top, marker, 0);
            Place(top, title, 1);
            Place(top, showTime ? time : null, 2);

            var bottom = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            // The lead lines up under the title, past the unread dot's slot.
            if (marker is not null)
            {
                bottom.Margin = new Thickness(marker.Width + marker.Margin.Right, 0, 0, 0);
            }

            lead.Margin = new Thickness(0, 0, text.Meta.Length > 0 ? Fluent.Space2 : 0, 0);
            Place(bottom, lead, 0);
            Place(bottom, meta, 2);
            Place(bottom, chips, 3);
            Place(bottom, detail, 4);

            if (chips is not null)
            {
                chips.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
            }

            if (detail is not null)
            {
                detail.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
                detail.TextTrimming = TextTrimming.None;
            }

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
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            Place(grid, marker, 0);

            if (layout.Lead && text.Lead.Length > 0)
            {
                lead.Margin = new Thickness(0, 0, Fluent.Space2, 0);
                Place(grid, lead, 1);
            }

            Place(grid, title, 2);

            if (layout.Meta && text.Meta.Length > 0)
            {
                var meta = Caption(text.Meta, "TextFillColorTertiaryBrush");
                meta.MaxWidth = 160;
                meta.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
                Place(grid, meta, 3);
            }

            if (chips is not null)
            {
                chips.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
                Place(grid, chips, 4);
            }

            if (detail is not null)
            {
                detail.MaxWidth = 180;
                detail.Margin = new Thickness(Fluent.Space3, 0, 0, 0);
                Place(grid, detail, 5);
            }

            Place(grid, showTime ? time : null, 6);
            content = grid;
        }

        var row = Fluent.Row(content, compact: !twoLine);
        hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, Tooltip(text));

        if (text.Url is { } url)
        {
            row.DoubleTapped += (_, args) =>
            {
                Open(url);
                args.Handled = true;
            };
        }

        if (menu is not null)
        {
            row.RightTapped += (_, args) => ItemMenu.Show(row, args, menu());
        }

        return row;
    }

    /// <summary>The whole row in words: what a trimmed row leaves out is in its tooltip.</summary>
    private static string Tooltip(DevRowText text)
    {
        var lines = new List<string>(4) { text.Title };

        if (text.Meta.Length > 0)
        {
            lines.Add($"{text.Lead} {text.Meta}".Trim());
        }

        if (text.Detail.Length > 0)
        {
            lines.Add(text.Detail);
        }

        if (text.Labels is { Count: > 0 } labels)
        {
            lines.Add(string.Join(", ", labels.Select(l => l.Name)));
        }

        return string.Join('\n', lines);
    }

    private static TextBlock Caption(string text, string colorKey)
    {
        var block = Fluent.Text(FluentType.Caption, colorKey);
        block.Text = text;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    /// <summary>[S-748] The unread dot, so read and unread differ by more than colour; a read row keeps the slot empty.</summary>
    private static FontIcon UnreadDot(bool unread)
    {
        var dot = Fluent.Icon(Glyphs.Dot, "AccentTextFillColorPrimaryBrush", 8);
        dot.Width = 8;
        dot.Margin = new Thickness(0, 0, Fluent.Space2, 0);
        dot.VerticalAlignment = VerticalAlignment.Center;
        dot.Opacity = unread ? 1 : 0;
        AutomationProperties.SetName(dot, unread ? Str.WidgetsA_Github_Unread.Text() : string.Empty);
        return dot;
    }

    /// <summary>
    /// [S-744] Label chips: a dot in the label's own colour (GitHub's data, validated to six hex
    /// digits) beside its name on the theme's subtle fill, so the text keeps the theme's contrast
    /// whatever colour the label is. At most <see cref="ChipsShown"/>; the tooltip names the rest.
    /// </summary>
    private static StackPanel Chips(IReadOnlyList<IssueLabel> labels)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1, VerticalAlignment = VerticalAlignment.Center };

        foreach (var label in labels.Take(ChipsShown))
        {
            var dot = Fluent.Icon(Glyphs.Dot, "TextFillColorTertiaryBrush", 8);
            if (HexColor.TryParse(label.Color, out var r, out var g, out var b))
            {
                dot.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }

            var name = Caption(label.Name, "TextFillColorSecondaryBrush");
            name.MaxWidth = 96;

            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                Padding = new Thickness(Fluent.Space1, 0, Fluent.Space2, 0),
                Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1, Children = { dot, name } },
            });
        }

        if (labels.Count > ChipsShown)
        {
            panel.Children.Add(Caption($"+{labels.Count - ChipsShown}", "TextFillColorTertiaryBrush"));
        }

        return panel;
    }

    private static void Place(Grid grid, FrameworkElement? element, int column)
    {
        if (element is null)
        {
            return;
        }

        Grid.SetColumn(element, column);
        grid.Children.Add(element);
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

    /// <summary>
    /// [S-749] The menu every linked row shares: open it, copy its address, and open the repository
    /// it lives in (when that is a different page). The caller adds its own items after these.
    /// </summary>
    public static List<ItemMenuEntry> LinkMenu(string url, string? repoUrl)
    {
        var menu = new List<ItemMenuEntry>
        {
            new(Glyphs.OpenExternal, Str.Github_OpenInBrowser.Text(), () => Open(url)),
            new(Glyphs.Copy, Str.WidgetsA_Github_CopyLink.Text(), () => CopyLink(url)),
        };

        if (repoUrl is not null && !string.Equals(repoUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            menu.Add(new(Glyphs.Code, Str.WidgetsA_Github_OpenRepository.Text(), () => Open(repoUrl)));
        }

        return menu;
    }

    /// <summary>[S-749] Put a page's address on the clipboard, kept after Themia exits.</summary>
    public static void CopyLink(string url)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(url);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; Windows fails the call rather than wait.
            Log.Error(Scope, "could not copy a GitHub link to the clipboard", ex);
        }
    }
}

/// <summary>
/// [S-742] One or two columns of sectioned lists side by side, each a <see cref="WholeRows"/> host,
/// so a wide tile puts its lists next to each other instead of leaving the right half empty.
/// </summary>
internal sealed partial class DevColumns : Grid
{
    private readonly StackPanel[] _lists = [new() { Spacing = 0 }, new() { Spacing = 0 }];
    private readonly WholeRows[] _hosts;

    public DevColumns()
    {
        ColumnSpacing = 0;
        _hosts = [.. _lists.Select(list => new WholeRows(
            new ScrollViewer
            {
                Padding = Fluent.ListPadding,
                Content = list,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            },
            list))];

        for (var i = 0; i < _hosts.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetColumn(_hosts[i], i);
            Children.Add(_hosts[i]);
        }
    }

    public int Count { get; private set; } = 1;

    /// <summary>The list of column <paramref name="index"/>.</summary>
    public StackPanel this[int index] => _lists[index];

    /// <summary>Show <paramref name="count"/> columns (1 or 2), emptied.</summary>
    public void Reset(int count)
    {
        Count = Math.Clamp(count, 1, _lists.Length);

        for (var i = 0; i < _lists.Length; i++)
        {
            _lists[i].Children.Clear();
            _hosts[i].Visibility = i < Count ? Visibility.Visible : Visibility.Collapsed;
            ColumnDefinitions[i].Width = i < Count ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }
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
