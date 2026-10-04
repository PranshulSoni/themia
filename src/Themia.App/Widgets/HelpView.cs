using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// The getting-started guide: four tabs of short prose about how this app works. [S-480..S-483]
/// <para>
/// The words live in <see cref="HelpGuide"/>; this only draws them. The tabs are a
/// <see cref="SelectorBar"/>, the control Windows 11 itself uses for a handful of peer pages, and
/// the prose scrolls under it. The open tab is not saved: the guide is read, not configured.
/// </para>
/// </summary>
internal sealed partial class HelpView : Grid, IWidgetView
{
    /// <summary>Below this width a tab carries its icon alone; the tooltip still names it.</summary>
    private const double LabelledTabsFrom = 400;

    /// <summary>
    /// Below this width even four icons do not fit, so the tabs go and the open page simply
    /// scrolls: a tab strip with its last tab cut off is a control that looks broken.
    /// </summary>
    private const double TabsFrom = 160;

    /// <summary>Below this width the prose drops a step on the ramp, so a line holds a phrase.</summary>
    private const double BodyTextFrom = 240;

    private static readonly IReadOnlyDictionary<HelpTab, string> TabGlyphs = new Dictionary<HelpTab, string>
    {
        [HelpTab.Basics] = Glyphs.Home,
        [HelpTab.Widgets] = Glyphs.Apps,
        [HelpTab.Views] = Glyphs.View,
        [HelpTab.Customize] = Glyphs.Palette,
    };

    private readonly SelectorBar _tabs = new();
    private readonly List<(HelpTab Tab, SelectorBarItem Item)> _items = [];
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body = new() { Spacing = Fluent.Space2 };

    private HelpTab _tab = HelpTab.Basics;
    private bool? _labelled;
    private TypeStep _prose = FluentType.Body;
    private bool _detached;

    public HelpView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        foreach (var page in HelpGuide.Pages)
        {
            var item = new SelectorBarItem { Tag = page.Tab };
            ToolTipService.SetToolTip(item, page.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, page.Label);
            _items.Add((page.Tab, item));
            _tabs.Items.Add(item);
        }

        _tabs.SelectedItem = _items[0].Item;
        _tabs.SelectionChanged += OnTabChanged;
        _tabs.Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, Fluent.Space1);
        _tabs.HorizontalAlignment = HorizontalAlignment.Left;

        _body.Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space4);

        _scroll = new ScrollViewer
        {
            Content = _body,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        SetRow(_tabs, 0);
        SetRow(_scroll, 1);
        Children.Add(_tabs);
        Children.Add(_scroll);

        SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        ApplyLayout(ActualWidth);
        Render();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        // ignore: the guide has no settings
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_detached || sender.SelectedItem is not { Tag: HelpTab tab } || tab == _tab)
        {
            return;
        }

        _tab = tab;
        Render();
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (ApplyLayout(args.NewSize.Width))
        {
            Render();
        }
    }

    /// <summary>
    /// Label the tabs and pick the prose step for the width there is. Returns true when the prose
    /// step changed, so the page has to be drawn again.
    /// </summary>
    private bool ApplyLayout(double width)
    {
        if (width <= 0)
        {
            return false;
        }

        _tabs.Visibility = width >= TabsFrom ? Visibility.Visible : Visibility.Collapsed;

        var labelled = width >= LabelledTabsFrom;
        if (labelled != _labelled)
        {
            _labelled = labelled;

            foreach (var (tab, item) in _items)
            {
                item.Text = labelled ? HelpGuide.Page(tab).Label : string.Empty;
                item.Icon = new FontIcon { Glyph = TabGlyphs[tab], FontSize = Fluent.IconSize };
            }
        }

        var prose = width >= BodyTextFrom ? FluentType.Body : FluentType.Caption;
        if (prose == _prose)
        {
            return false;
        }

        _prose = prose;
        return true;
    }

    /// <summary>Draw the open tab: the opening paragraph, each section with its bullets, then the tip.</summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        var page = HelpGuide.Page(_tab);
        _body.Children.Clear();

        var intro = Prose(page.Intro, "TextFillColorPrimaryBrush");
        _body.Children.Add(intro);

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
            var run = new Run { Text = span.Text };

            switch (span.Kind)
            {
                case RichKind.Strong:
                    // Semibold in the primary colour, Fluent's own emphasis, rather than a bold the
                    // type ramp does not have.
                    block.Inlines.Add(new Span
                    {
                        Inlines = { run },
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Microsoft.UI.Colors.White),
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

    public void Detach()
    {
        _detached = true;
        SizeChanged -= OnSizeChanged;
        _tabs.SelectionChanged -= OnTabChanged;
    }
}
