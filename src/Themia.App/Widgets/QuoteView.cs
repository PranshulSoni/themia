using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A line worth rereading, on a rotation.
/// <para>
/// Offline by construction: the quotes are the user's own list, seeded with a bundled set, and
/// which one shows is a pure function of the clock, the seed and how many times the widget has been
/// clicked — see <see cref="QuoteRotation"/>. There is no quote service and nothing to be signed in
/// to, which is why this widget is as good on a train as on a desk.
/// </para>
/// <para>
/// Clicking shows the next one. That step is deliberately <b>not</b> persisted: it is a nudge, not a
/// setting, and writing to the state file every time someone clicks a quote would be a file write
/// per click for something that is supposed to come back to today's quote in the morning.
/// </para>
/// </summary>
internal sealed partial class QuoteView : Grid, IWidgetView, IWidgetSurfaceAware
{
    /// <summary>
    /// Under this width the header keeps its icon and drops its words. Four commands and a title in
    /// 180px leaves the title two characters and an ellipsis, which says less than nothing.
    /// </summary>
    private const double TitleHidesBelow = 240;

    private readonly TextBlock _title = new();

    /// <summary>The title the header shows when it has room for words. Kept because the header drops it on a narrow tile.</summary>
    private string _label = WidgetCatalog.Definition(WidgetKind.Quote).Label;

    private readonly Grid _header;
    private readonly Button _next;

    private readonly TextBlock _text = Fluent.Text(FluentType.Body);
    private readonly TextBlock _author = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly Grid _body;

    private readonly StackPanel _empty;

    private QuoteConfig _config = new();
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    /// <summary>
    /// How many times the user has asked for the next one. Ephemeral on purpose: a reload comes back
    /// to the quote the rotation says it should be showing.
    /// </summary>
    private long _step;

    private bool _detached;

    public QuoteView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = _label;

        _next = CommandButton(Glyphs.Reset, "Show another quote");
        _next.Click += OnNextClicked;

        _header = Fluent.Header(WidgetCatalog.Definition(WidgetKind.Quote).Glyph, _title, _next);
        _header.Margin = Fluent.HeaderMargin;

        _body = BuildBody();

        _empty = Fluent.EmptyState(
            WidgetCatalog.Definition(WidgetKind.Quote).Glyph,
            new TextBlock { Text = "No quotes yet. Add some in settings." },
            out _);

        _empty.Visibility = Visibility.Collapsed;

        SetRow(_header, 0);
        SetRow(_body, 1);
        SetRow(_empty, 1);

        Children.Add(_header);
        Children.Add(_body);
        Children.Add(_empty);

        // The whole card advances the quote, not only the button: the widget is one sentence and a
        // name, and a target that size should not need aiming at.
        //
        // Tapped rather than PointerPressed, and deliberately not marked handled. In edit mode the
        // card captures the pointer on press to drag the widget, so the gesture never completes here
        // and no quote changes while a widget is being moved. See ScratchpadView for the whole story.
        Tapped += OnTapped;
        SizeChanged += OnSizeChanged;
    }

    private Grid BuildBody()
    {
        _text.TextWrapping = TextWrapping.Wrap;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;

        _author.TextWrapping = TextWrapping.NoWrap;
        _author.Margin = new Thickness(0, Fluent.Space2, 0, 0);

        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, WidgetLayout.ContentInset),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        SetRow(_text, 0);
        SetRow(_author, 1);
        grid.Children.Add(_text);
        grid.Children.Add(_author);

        return grid;
    }

    /// <summary>
    /// A header command: a glyph with the stock subtle fill behind it and nothing at rest.
    /// <para>
    /// The brushes go in through the template's own resource keys rather than onto
    /// <c>Background</c>, because the stock button's visual states assign the pointer-over and
    /// pressed fills straight onto the border element and would outrank a local value. Same reason
    /// as the note editor: see <c>NotesView.BuildEditor</c>.
    /// </para>
    /// </summary>
    private static Button CommandButton(string glyph, string tooltip)
    {
        var clear = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        var hover = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);
        var pressed = FluentLayout.Themed("SubtleFillColorTertiaryBrush", Colors.DimGray);

        var button = new Button
        {
            Content = Fluent.Icon(glyph),
            Padding = new Thickness(Fluent.Space1),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        button.Resources["ButtonBackground"] = clear;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = pressed;
        button.Resources["ButtonBackgroundDisabled"] = clear;
        button.Resources["ButtonBorderBrush"] = clear;
        button.Resources["ButtonBorderBrushPointerOver"] = clear;
        button.Resources["ButtonBorderBrushPressed"] = clear;
        button.Resources["ButtonBorderBrushDisabled"] = clear;

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        // [S-02] The widget's own title when the user set one, so a desktop of quotes is readable.
        _label = string.IsNullOrWhiteSpace(surface.Label)
            ? WidgetCatalog.Definition(WidgetKind.Quote).Label
            : surface.Label;

        _title.Text = _label;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as QuoteConfig ?? new QuoteConfig();

        _author.Visibility = _config.ShowAuthor ? Visibility.Visible : Visibility.Collapsed;

        Subscribe();
        Render();
    }

    /// <summary>
    /// Wake only as often as the cadence needs, and not at all for a quote that only changes when it
    /// is clicked. A widget that is a sentence has no business waking the process once a second.
    /// </summary>
    private void Subscribe()
    {
        var wanted = _config.TickInterval;

        if (_detached || _ticker is null || wanted == _subscribedInterval)
        {
            return;
        }

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = wanted;

        if (wanted > TimeSpan.Zero)
        {
            _subscription = _ticker.Subscribe(wanted, Render);
        }
    }

    private void OnNextClicked(object sender, RoutedEventArgs args) => ShowNext();

    /// <summary>
    /// Deliberately not marked handled: the card's own double-click still has to reach it, which is
    /// how the desktop is put into edit mode. The cost is that a double-click advances twice, which
    /// is a quote, and the benefit is that the gesture works the same on every widget.
    /// </summary>
    private void OnTapped(object sender, TappedRoutedEventArgs args) => ShowNext();

    private void ShowNext()
    {
        _step++;
        Render();
    }

    /// <summary>
    /// Pick the quote for right now and set it. Cheap enough to run on every tick and every resize,
    /// because choosing is arithmetic and the two text blocks are already built.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        var quotes = _config.EffectiveQuotes;
        var period = QuoteRotation.Period(DateTimeOffset.Now, _config.Cadence) + _step;
        var index = QuoteRotation.Select(quotes.Count, period, _config.Seed, _config.Shuffle);

        if (index < 0)
        {
            // An empty list is a real state — the user cleared it — not a failure to load.
            _body.Visibility = Visibility.Collapsed;
            _empty.Visibility = Visibility.Visible;
            _next.IsEnabled = false;
            return;
        }

        var quote = quotes[index];

        _body.Visibility = Visibility.Visible;
        _empty.Visibility = Visibility.Collapsed;

        // One quote has nowhere to go, and a button that visibly does nothing is worse than one
        // that says so.
        _next.IsEnabled = quotes.Count > 1;

        _text.Text = QuoteLayout.Quoted(quote.Text);
        _author.Text = QuoteLayout.Attribution(quote.Author);
        _author.Visibility = _config.ShowAuthor && _author.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        Resize();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Resize();

    /// <summary>
    /// Step the type to the tile. [S-210] The quote picks the largest ramp step a paragraph of it
    /// still fits at, so a small tile shows a readable Body and a large one shows a Title — and
    /// every size on screen is one Windows itself uses.
    /// </summary>
    private void Resize()
    {
        var hasHeader = ActualHeight >= Fluent.HeaderHidesBelow;
        _header.Visibility = hasHeader ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = ActualWidth < TitleHidesBelow ? string.Empty : _label;

        var width = ActualWidth - (WidgetLayout.ContentInset * 2);

        // The header is a fixed ramp step plus its own margin; what is left is the quote's.
        var headerHeight = hasHeader ? Fluent.RowHeight : 0;
        var authorHeight = _author.Visibility == Visibility.Visible ? FluentType.Body.LineHeight + Fluent.Space2 : 0;
        var height = ActualHeight - headerHeight - authorHeight - WidgetLayout.ContentInset;

        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        var step = QuoteLayout.Fit(width, height, _text.Text.Length, FluentType.Caption, FluentType.Title);
        _text.Apply(step);

        // The attribution sits one step down, never above Body: it is a name, not the quote.
        _author.Apply(step.FontSize > FluentType.Body.FontSize ? FluentType.Body : FluentType.Caption);
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _subscription?.Dispose();
        _subscription = null;
        _ticker = null;

        _next.Click -= OnNextClicked;
        Tapped -= OnTapped;
        SizeChanged -= OnSizeChanged;
    }
}
