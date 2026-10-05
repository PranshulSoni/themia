using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// The getting-started guide: four pages of short prose about how this app works ([S-480..S-483],
/// [S-1532]), arranged by the room there is ([S-1531]).
/// <para>
/// The words live in <see cref="HelpGuide"/>; this only draws them. The arrangement comes from
/// <see cref="HelpLayout"/>: the page alone on the narrowest tile, a <see cref="SelectorBar"/> of
/// icon tabs above it from a Regular width, and from an Expanded width a labelled page list beside
/// it, as Windows Settings does — which also keeps lines a readable length. A large tile adds
/// "Edit widgets" and "Settings" under the page list, an extra-large one a card of the keys. The
/// open page is not saved: the guide is read, not configured.
/// </para>
/// </summary>
internal sealed partial class HelpView : Grid, IWidgetView, IWidgetSizeAware
{
    private static readonly IReadOnlyDictionary<HelpTab, string> TabGlyphs = new Dictionary<HelpTab, string>
    {
        [HelpTab.Basics] = Glyphs.Home,
        [HelpTab.Widgets] = Glyphs.Apps,
        [HelpTab.Views] = Glyphs.View,
        [HelpTab.Customize] = Glyphs.Palette,
    };

    private readonly SelectorBar _tabs = new();
    private readonly List<(HelpTab Tab, SelectorBarItem Item)> _items = [];
    private readonly Grid _pane = new();
    private readonly StackPanel _nav = new() { Spacing = Fluent.Space1 };
    private readonly List<(HelpTab Tab, Border Row, Border Pill)> _navRows = [];
    private readonly StackPanel _actions;
    private readonly Border _keys;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body = new() { Spacing = Fluent.Space2 };

    private HelpTab _tab = HelpTab.Basics;
    private WidgetArea _area;
    private HelpArrangement? _arrangement;
    private TypeStep _prose = FluentType.Body;
    private bool _detached;

    public HelpView()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        foreach (var page in HelpGuide.Pages)
        {
            var item = new SelectorBarItem
            {
                Tag = page.Tab,
                Icon = new FontIcon { Glyph = TabGlyphs[page.Tab], FontSize = Fluent.IconSize },
            };

            ToolTipService.SetToolTip(item, page.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, page.Label);
            _items.Add((page.Tab, item));
            _tabs.Items.Add(item);

            _nav.Children.Add(NavRow(page));
        }

        _tabs.SelectedItem = _items[0].Item;
        _tabs.SelectionChanged += OnTabChanged;
        _tabs.Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, Fluent.Space1);
        _tabs.HorizontalAlignment = HorizontalAlignment.Left;
        SetColumn(_tabs, 1);

        _actions = new StackPanel
        {
            Spacing = Fluent.Space2,
            Margin = new Thickness(Fluent.Space2, Fluent.Space2, Fluent.Space2, Fluent.Space4),
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { Stretch(AppActions.EditLayout()), Stretch(AppActions.Settings()) },
        };

        _pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _pane.Width = HelpLayout.PaneWidth;
        _pane.Padding = new Thickness(Fluent.Space1, 0, 0, 0);
        _pane.Children.Add(_nav);
        SetRow(_actions, 1);
        _pane.Children.Add(_actions);
        SetRowSpan(_pane, 2);

        _keys = KeysCard();
        SetColumn(_keys, 2);
        SetRowSpan(_keys, 2);

        _body.Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space4);

        _scroll = new ScrollViewer
        {
            Content = _body,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        SetColumn(_scroll, 1);
        SetRow(_scroll, 1);

        Children.Add(_tabs);
        Children.Add(_pane);
        Children.Add(_scroll);
        Children.Add(_keys);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        Lay();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        // ignore: the guide has no settings
    }

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
    }

    /// <summary>
    /// [S-1531] Pick the arrangement and the prose step for the area, then draw the open page when
    /// either changed (the page is built from text runs, so a different step is a different page).
    /// </summary>
    private void Lay()
    {
        if (_detached)
        {
            return;
        }

        var area = _area.IsMeasured ? _area : new WidgetArea(320, 196);
        var arrangement = HelpLayout.Arrangement(area);
        var twoPane = arrangement == HelpArrangement.TwoPane;

        _tabs.Visibility = arrangement == HelpArrangement.IconTabs ? Visibility.Visible : Visibility.Collapsed;
        _pane.Visibility = twoPane ? Visibility.Visible : Visibility.Collapsed;
        _actions.Visibility = HelpLayout.Actions(area) ? Visibility.Visible : Visibility.Collapsed;
        _keys.Visibility = HelpLayout.Keys(area) ? Visibility.Visible : Visibility.Collapsed;

        // The page takes the whole height when there is no tab strip above it.
        SetRow(_scroll, arrangement == HelpArrangement.IconTabs ? 1 : 0);
        SetRowSpan(_scroll, arrangement == HelpArrangement.IconTabs ? 1 : 2);

        // A short strip lists its pages in compact rows, so all four fit beside the page.
        var strip = area.HeightClass == SizeClass.Compact;
        var navRow = strip ? 28 : Fluent.RowHeightCompact + Fluent.Space1;
        _nav.Spacing = strip ? 0 : Fluent.Space1;
        foreach (var (_, row, _) in _navRows)
        {
            row.Height = navRow;
        }

        var prose = HelpLayout.BodyProse(area) ? FluentType.Body : FluentType.Caption;

        if (prose != _prose || arrangement != _arrangement || _body.Children.Count == 0)
        {
            _prose = prose;
            _arrangement = arrangement;
            Render();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Navigation
    // -----------------------------------------------------------------------------------------

    /// <summary>A page-list row: the accent pill on the open page, the icon, the name — Settings' own shape.</summary>
    private Border NavRow(HelpPage page)
    {
        var pill = new Border
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(1.5),
            Background = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Fluent.Space2, 0),
        };

        var icon = Fluent.Icon(TabGlyphs[page.Tab], "TextFillColorPrimaryBrush");
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);
        SetColumn(icon, 1);

        var label = Fluent.Text(FluentType.Body);
        label.Text = page.Label;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(label, 2);

        var row = new Border
        {
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
                Children = { pill, icon, label },
                VerticalAlignment = VerticalAlignment.Center,
            },
            Tag = page.Tab,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Padding = new Thickness(Fluent.Space1, 0, Fluent.Space2, 0),
        };

        row.Tapped += OnNavTapped;
        row.PointerEntered += OnNavEntered;
        row.PointerExited += OnNavExited;
        _navRows.Add((page.Tab, row, pill));
        return row;
    }

    private void OnNavTapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is Border { Tag: HelpTab tab })
        {
            args.Handled = true;
            Open(tab);
        }
    }

    private void OnNavEntered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is Border { Tag: HelpTab tab } row && tab != _tab)
        {
            row.Background = Fluent.RowHover();
        }
    }

    private void OnNavExited(object sender, PointerRoutedEventArgs args) => PaintNav();

    private void PaintNav()
    {
        foreach (var (tab, row, pill) in _navRows)
        {
            var open = tab == _tab;
            row.Background = open ? Fluent.RowHover() : Fluent.RowRest();
            pill.Opacity = open ? 1 : 0;
        }
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is { Tag: HelpTab tab })
        {
            Open(tab);
        }
    }

    /// <summary>Open a page from either control, and keep the other in step.</summary>
    private void Open(HelpTab tab)
    {
        if (_detached || tab == _tab)
        {
            return;
        }

        _tab = tab;

        var item = _items.Find(entry => entry.Tab == tab).Item;
        if (!ReferenceEquals(_tabs.SelectedItem, item))
        {
            _tabs.SelectedItem = item;
        }

        Render();
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    // -----------------------------------------------------------------------------------------
    // The page
    // -----------------------------------------------------------------------------------------

    /// <summary>Draw the open page: the opening paragraph, each section with its bullets, then the tip.</summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        PaintNav();

        var page = HelpGuide.Page(_tab);
        _body.Children.Clear();

        // Beside a page list the page names itself, as a Settings page does under its title.
        if (_arrangement == HelpArrangement.TwoPane)
        {
            var title = Fluent.Text(FluentType.Subtitle);
            title.Text = page.Label;
            _body.Children.Add(title);
        }

        _body.Children.Add(Prose(page.Intro, "TextFillColorPrimaryBrush"));

        foreach (var section in page.Sections)
        {
            var title = Fluent.Text(FluentType.Strong(_prose), "TextFillColorPrimaryBrush");
            title.Text = section.Title;
            title.Margin = new Thickness(0, Fluent.Space2, 0, 0);
            _body.Children.Add(title);

            foreach (var bullet in section.Bullets)
            {
                _body.Children.Add(Bullet(bullet));
            }
        }

        if (page.Tip is { } tip)
        {
            var line = Prose(tip, "TextFillColorTertiaryBrush");
            line.Margin = new Thickness(0, Fluent.Space2, 0, 0);
            _body.Children.Add(line);
        }
    }

    /// <summary>A bullet: a dot in its own column, so wrapped lines hang under the text rather than the dot.</summary>
    private Grid Bullet(string text)
    {
        var dot = Fluent.Text(_prose, "TextFillColorSecondaryBrush");
        dot.Text = "•";

        var prose = Prose(text, "TextFillColorSecondaryBrush");
        SetColumn(prose, 1);

        return new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { dot, prose },
        };
    }

    /// <summary>
    /// [S-482] A wrapped paragraph built from <see cref="RichText"/> runs: a term or a key in bold,
    /// something on screen in italic. Plain text runs, never parsed markup.
    /// </summary>
    private TextBlock Prose(string text, string colorKey)
    {
        var block = Fluent.Text(_prose, colorKey);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;

        foreach (var span in RichText.Parse(text))
        {
            // A key chord ("Strg+Alt+W") is one word: word joiners around each "+" keep the line
            // from breaking inside it, which the longer languages otherwise do.
            var run = new Run { Text = span.Kind == RichKind.Strong ? KeepChord(span.Text) : span.Text };

            switch (span.Kind)
            {
                case RichKind.Strong:
                    // Semibold in the primary colour, Fluent's own emphasis, rather than a bold the
                    // type ramp does not have.
                    block.Inlines.Add(new Span
                    {
                        Inlines = { run },
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White),
                    });
                    break;

                case RichKind.Emphasis:
                    block.Inlines.Add(new Span
                    {
                        Inlines = { run },
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                    });
                    break;

                default:
                    block.Inlines.Add(run);
                    break;
            }
        }

        return block;
    }

    private static string KeepChord(string text) => text.Contains('+') ? text.Replace("+", "\u2060+\u2060", StringComparison.Ordinal) : text;

    /// <summary>[S-1531] The keys card (XL): each key in the primary colour, what it does beside it.</summary>
    private static Border KeysCard()
    {
        var grid = new Grid
        {
            ColumnSpacing = Fluent.Space3,
            RowSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var heading = Fluent.Text(FluentType.BodyStrong);
        heading.Text = HelpGuide.KeysTitle(Strings.Current);
        heading.Margin = new Thickness(0, 0, 0, Fluent.Space1);
        SetColumnSpan(heading, 2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(heading);

        var keyList = HelpGuide.Keys;

        for (var i = 0; i < keyList.Count; i++)
        {
            var key = keyList[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keys = Fluent.Text(FluentType.Strong(FluentType.Caption));
            keys.Text = key.Keys;
            SetRow(keys, i + 1);

            var meaning = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            meaning.Text = key.Meaning;
            meaning.TextWrapping = TextWrapping.Wrap;
            meaning.TextTrimming = TextTrimming.None;
            SetRow(meaning, i + 1);
            SetColumn(meaning, 1);

            grid.Children.Add(keys);
            grid.Children.Add(meaning);
        }

        return new Border
        {
            Width = HelpLayout.KeysWidth - Fluent.Space4,
            Margin = new Thickness(0, 0, Fluent.Space4, Fluent.Space4),
            Padding = new Thickness(Fluent.Space3),
            CornerRadius = new CornerRadius(Fluent.OverlayRadius),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new ScrollViewer
            {
                Content = grid,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            },
        };
    }

    /// <summary>
    /// A command filling the navigation column. Its label sits in a star column so a long one
    /// ("Widgets bearbeiten") wraps onto a second line — and only past two lines ends in an
    /// ellipsis, with the whole label on hover — instead of being cut mid-letter by a horizontal
    /// stack that measures it as if it had all the room in the world.
    /// </summary>
    private static Button Stretch(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        if (button.Content is StackPanel { Children: [FrameworkElement icon, TextBlock label] } stack)
        {
            stack.Children.Clear();
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.WrapWholeWords;
            label.MaxLines = 2;
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);

            var grid = new Grid { ColumnSpacing = stack.Spacing };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(icon);
            grid.Children.Add(label);
            button.Content = grid;
            ToolTipService.SetToolTip(button, label.Text);
        }

        return button;
    }

    public void Detach()
    {
        _detached = true;
        _tabs.SelectionChanged -= OnTabChanged;

        foreach (var (_, row, _) in _navRows)
        {
            row.Tapped -= OnNavTapped;
            row.PointerEntered -= OnNavEntered;
            row.PointerExited -= OnNavExited;
        }
    }
}
