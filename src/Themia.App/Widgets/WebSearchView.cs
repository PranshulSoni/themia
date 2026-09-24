using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A box: type a query, press Enter, the default browser opens the results.
/// <para>
/// The whole widget is one stock Windows 11 text field and a line under it saying where the query
/// will go. Nothing is styled by this file — the box is the system's, so it picks up the theme, the
/// caret, the selection colours and the context menu that every other box in Windows has.
/// </para>
/// <para>
/// <b>Keyboard.</b> The desktop surface carries <c>WS_EX_NOACTIVATE</c> and never becomes the
/// foreground window, so a text box on it gets a caret and no keystrokes. The view asks for the
/// keyboard through <see cref="IWidgetSurface.RequestTextInput"/> while the box has focus and gives
/// it back when it does not. Without that this widget looks like it works and silently swallows
/// everything typed into it.
/// </para>
/// <para>
/// The URL is composed by <see cref="SearchEngines"/>, which percent-encodes the query and refuses
/// a template that is not an http/https address with exactly one placeholder outside its host. It
/// is then opened by <see cref="ShellLauncher"/>, like everything else this app opens.
/// </para>
/// </summary>
internal sealed partial class WebSearchView : Grid, IWidgetView, IWidgetSurfaceAware
{
    /// <summary>Below this height the hint line goes: the box is what the widget is for.</summary>
    private const double HintHidesBelow = 112;

    private readonly TextBox _input = new();
    private readonly TextBlock _hint;
    private readonly Border _body;

    private readonly Brush _hintBrush;
    private readonly Brush _errorBrush;

    private IWidgetSurface? _surface;
    private WebSearchConfig _config = new();
    private bool _detached;

    public WebSearchView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _hintBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        // The stock "something is wrong" fill, so a bad template reads the way every other error in
        // Windows does rather than as a red this file picked.
        _errorBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

        _input.PlaceholderText = "Search the web";
        _input.IsSpellCheckEnabled = false;
        _input.IsTextPredictionEnabled = false;
        _input.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += OnInputChanged;
        _input.GotFocus += OnInputGotFocus;
        _input.LostFocus += OnInputLostFocus;

        _hint = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.TextTrimming = TextTrimming.None;

        _body = new Border
        {
            Padding = WidgetLayout.FooterInset,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = Fluent.Space1,
                Children = { _input, _hint },
            },
        };

        Children.Add(_body);

        SizeChanged += OnSizeChanged;
        UpdateHint();
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        // No ticker: this widget changes when it is typed in.
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as WebSearchConfig ?? new WebSearchConfig();
        ApplyLayout();
        UpdateHint();
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    private void OnInputChanged(object sender, TextChangedEventArgs args) => UpdateHint();

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case Windows.System.VirtualKey.Enter:
                args.Handled = true;
                Submit();
                break;

            case Windows.System.VirtualKey.Escape:
                args.Handled = true;
                _input.Text = string.Empty;
                break;
        }
    }

    /// <summary>
    /// Build the URL and hand it to the shell. A blank box does nothing at all — not an error, just
    /// nothing to search for — and a refused template says so instead of opening the engine bare.
    /// </summary>
    private void Submit()
    {
        if (_detached)
        {
            return;
        }

        var built = SearchEngines.Build(_config.EffectiveEngine, _config.CustomTemplate, _input.Text);

        if (!built.IsValid)
        {
            ShowHint(SearchEngines.ProblemMessage(built.Problem), built.Problem != SearchProblem.NoQuery);
            return;
        }

        if (ShellLauncher.Open(built.Url) == LaunchOutcome.Opened && _config.ClearAfterSearch)
        {
            _input.Text = string.Empty;
        }

        UpdateHint();
    }

    /// <summary>
    /// The line under the box: where the query is going, or what is wrong with the template. Worth
    /// showing both — a box whose engine cannot be seen is a box you have to guess at.
    /// </summary>
    private void UpdateHint()
    {
        var engine = _config.EffectiveEngine;

        if (engine == SearchEngine.Custom)
        {
            var problem = SearchEngines.CheckTemplate(_config.CustomTemplate);

            if (problem != SearchProblem.None)
            {
                ShowHint(SearchEngines.ProblemMessage(problem), isError: true);
                return;
            }
        }

        ShowHint($"Enter to search with {SearchEngines.Label(engine)}", isError: false);
    }

    private void ShowHint(string text, bool isError)
    {
        _hint.Text = text;
        _hint.Foreground = isError ? _errorBrush : _hintBrush;
    }

    private void OnInputGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnInputLostFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(false);

    // -----------------------------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout();

    /// <summary>
    /// What a tile this size shows. Nothing scales: the box is a stock control at its own height
    /// and the type is a fixed ramp step, so a tile that grows gives the box more room rather than
    /// making it bigger.
    /// </summary>
    private void ApplyLayout()
    {
        var height = ActualHeight;
        var measured = height > 0;

        // On a 180x120 tile the box and its hint together are taller than the room there is, so the
        // hint goes first: it is support for the box rather than content of its own.
        _hint.Visibility = measured && height < HintHidesBelow ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        SizeChanged -= OnSizeChanged;
        _input.KeyDown -= OnInputKeyDown;
        _input.TextChanged -= OnInputChanged;
        _input.GotFocus -= OnInputGotFocus;
        _input.LostFocus -= OnInputLostFocus;

        // Give the keyboard back on the way out: a widget removed while its box had focus would
        // otherwise leave the surface activatable for the rest of the session.
        _surface?.RequestTextInput(false);
        _surface = null;
    }
}
