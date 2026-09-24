using Microsoft.UI;
using Microsoft.UI.Text;
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
/// An arithmetic scratchpad: a history of what has been worked out, a line to type the next one on,
/// and the answer shown while it is still being typed.
/// <para>
/// The arithmetic itself is <see cref="ExpressionEvaluator"/> in Themia.Core, where it is tested;
/// this file is the surface only. [S-07]
/// </para>
/// </summary>
internal sealed partial class CalculatorView : Grid, IWidgetView, IWidgetSurfaceAware
{
    private readonly TextBlock _title = new();
    private readonly FontIcon _icon = new();
    private readonly Grid _header;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly ScrollViewer _historyScroll;
    private readonly TextBlock _preview = new();
    private readonly TextBox _input = new();

    /// <summary>The surface this widget is drawn on: where it asks for the keyboard.</summary>
    private IWidgetSurface? _surface;
    private readonly Border _entryArea;
    private readonly CalculatorHistory _history = new();

    private readonly Brush _resultBrush;
    private readonly Brush _errorBrush;

    private CalculatorConfig _config = new();
    private bool _detached;

    public CalculatorView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var muted = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        _resultBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);

        // The stock "something is wrong" fill, so the error tracks the theme rather than being a
        // red this file decided on.
        _errorBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

        var definition = WidgetCatalog.Definition(WidgetKind.Calculator);

        _icon.Glyph = definition.Glyph;
        _icon.Foreground = muted;
        _icon.Margin = new Thickness(0, 0, 6, 0);

        _title.Text = definition.Label;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = muted;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);

        _header = new Grid
        {
            Margin = new Thickness(WidgetLayout.ContentInset, WidgetLayout.ContentInset, WidgetLayout.ContentInset, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { _icon, _title },
        };

        _historyScroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _preview.HorizontalAlignment = HorizontalAlignment.Right;
        _preview.Foreground = _resultBrush;
        _preview.FontWeight = FontWeights.SemiBold;
        _preview.TextTrimming = TextTrimming.CharacterEllipsis;
        _preview.Margin = new Thickness(0, 2, 0, 2);
        _preview.Visibility = Visibility.Collapsed; // [S-04] nothing typed, nothing to show

        _input.PlaceholderText = "2 + 3 * 4";
        _input.IsSpellCheckEnabled = false;
        _input.IsTextPredictionEnabled = false;
        _input.TextAlignment = TextAlignment.Right;
        _input.TextChanged += OnInputChanged;
        _input.KeyDown += OnInputKeyDown;
        _input.GotFocus += OnInputGotFocus;
        _input.LostFocus += OnInputLostFocus;

        // The line being typed is separated from the ones already worked out, as in the original:
        // the rule is what stops the live preview reading as one more history row.
        _entryArea = new Border
        {
            BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Child = new StackPanel { Spacing = 4, Children = { _preview, _input } },
        };

        Grid.SetRow(_header, 0);
        Grid.SetRow(_historyScroll, 1);
        Grid.SetRow(_entryArea, 2);

        Children.Add(_header);
        Children.Add(_historyScroll);
        Children.Add(_entryArea);

        SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        // No ticker: this widget changes when it is typed in.
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as CalculatorConfig ?? new CalculatorConfig();

        // The decimal places changed under the rows that are already on screen, so they are redrawn
        // from the history rather than left showing the old precision. [S-05]
        UpdatePreview();
        RedrawHistory();
    }

    // -----------------------------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------------------------

    private void OnInputChanged(object sender, TextChangedEventArgs args) => UpdatePreview();

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        Commit();
    }

    /// <summary>[S-03]/[S-04] The answer, live, above the line being typed.</summary>
    private void UpdatePreview()
    {
        var result = ExpressionEvaluator.Evaluate(_input.Text);

        switch (result.Status)
        {
            case ExpressionStatus.Empty:
                _preview.Visibility = Visibility.Collapsed;
                _preview.Text = string.Empty;
                break;

            case ExpressionStatus.Error:
                _preview.Visibility = Visibility.Visible;
                _preview.Foreground = _errorBrush;
                _preview.Text = ExpressionResult.ErrorText;
                break;

            default:
                _preview.Visibility = Visibility.Visible;
                _preview.Foreground = _resultBrush;
                _preview.Text = "= " + result.Format(_config.EffectiveDecimalPlaces);
                break;
        }
    }

    /// <summary>[S-10] Enter files the calculation and clears the line, unless there is nothing to file.</summary>
    private void Commit()
    {
        var expression = _input.Text;
        var result = ExpressionEvaluator.Evaluate(expression);

        if (!result.IsNumber)
        {
            return;
        }

        if (!_history.Add(expression.Trim(), result.Format(_config.EffectiveDecimalPlaces)))
        {
            return;
        }

        _input.Text = string.Empty;
        RedrawHistory();
        UpdatePreview();
    }

    // -----------------------------------------------------------------------------------------
    // History
    // -----------------------------------------------------------------------------------------

    private void RedrawHistory()
    {
        _rows.Children.Clear();

        // At most eight rows ([S-09]), so redrawing the lot is cheaper than tracking which changed.
        foreach (var entry in _history.Entries)
        {
            _rows.Children.Add(BuildRow(entry));
        }

        // ApplyTypeScale owns both the sizes and what is shown at this size, so there is one place
        // that decides how the widget looks at 180x120.
        ApplyTypeScale();
    }

    private FrameworkElement BuildRow(Calculation entry)
    {
        var expression = new TextBlock
        {
            Text = entry.Expression,
            Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var result = new TextBlock
        {
            Text = "= " + entry.Result,
            Foreground = _resultBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(expression, 0);
        Grid.SetColumn(result, 1);

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { expression, result },
        };

        var row = new Border
        {
            Child = layout,
            Padding = new Thickness(WidgetLayout.ContentInset, 3, WidgetLayout.ContentInset, 3),
            Background = Transparent,
            Tag = entry.Result,
        };

        // Hover the way every list on Windows does it, with the stock brush so it follows the theme.
        // PointerExited alone strands a row lit when the list scrolls or is rebuilt under the mouse.
        row.PointerEntered += (_, _) => row.Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);
        row.PointerExited += (_, _) => row.Background = Transparent;
        row.PointerCaptureLost += (_, _) => row.Background = Transparent;
        row.PointerCanceled += (_, _) => row.Background = Transparent;

        // [S-11] A past answer is usually the next thing you want to work from.
        row.Tapped += OnRowTapped;

        ToolTipService.SetToolTip(row, $"{entry.Expression} = {entry.Result}");
        return row;
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_detached || sender is not Border { Tag: string result })
        {
            return;
        }

        _input.Text = result;
        _input.SelectionStart = result.Length;
        _input.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    // -----------------------------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyTypeScale();

    /// <summary>
    /// Size everything to the tile, so the widget reads at 180x120 and still fills 800x600 rather
    /// than leaving a field of empty card under three small lines.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        _title.FontSize = TypeScale.Fit(width, height, 0.075, 0.10, 10, 18);
        _icon.FontSize = _title.FontSize;

        var body = TypeScale.Fit(width, height, 0.065, 0.085, 10, 22);

        foreach (var child in _rows.Children)
        {
            if (child is Border { Child: Grid row })
            {
                foreach (var cell in row.Children)
                {
                    if (cell is TextBlock text)
                    {
                        text.FontSize = body;
                    }
                }
            }
        }

        _preview.FontSize = TypeScale.Fit(width, height, 0.095, 0.13, 12, 32);
        _input.FontSize = TypeScale.Fit(width, height, 0.080, 0.11, 11, 26);

        // Below roughly 90 DIPs the title takes the room the sum needs.
        _header.Visibility = height < 90 ? Visibility.Collapsed : Visibility.Visible;

        // The original hides the history under 120px of height, and it is right to: in a 180x120
        // tile the thing being typed is the whole point, and two clipped rows above it are noise.
        // A measured height of 0 is a tile that has not been laid out yet, not a short one.
        var showHistory = _history.Count > 0 && (height >= 120 || height <= 0);

        _historyScroll.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;

        // With nothing above it the rule would be a line across the top of the widget.
        _entryArea.BorderThickness = new Thickness(0, showHistory ? 1 : 0, 0, 0);
        _entryArea.Padding = new Thickness(
            WidgetLayout.ContentInset,
            showHistory ? 6 : 0,
            WidgetLayout.ContentInset,
            WidgetLayout.ContentInset);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface) => _surface = surface;

    /// <summary>
    /// Ask the surface for the keyboard. Outside edit mode it is unfocusable, so without this the
    /// caret blinks in the box while the digits go to whatever window actually has focus.
    /// </summary>
    private void OnInputGotFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(true);

    private void OnInputLostFocus(object sender, RoutedEventArgs args) => _surface?.RequestTextInput(false);

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        _input.TextChanged -= OnInputChanged;
        _input.KeyDown -= OnInputKeyDown;
        _input.GotFocus -= OnInputGotFocus;
        _input.LostFocus -= OnInputLostFocus;
        _surface?.RequestTextInput(false);
        SizeChanged -= OnSizeChanged;

        foreach (var child in _rows.Children)
        {
            if (child is Border row)
            {
                row.Tapped -= OnRowTapped;
            }
        }

        // [S-09] In-memory only: there is nothing to write out, and nothing is left behind.
        _history.Clear();
        _rows.Children.Clear();
    }

    /// <summary>
    /// Transparent rather than null for a row's rest state: a null background is not hit-testable,
    /// so the row would never raise the pointer events the hover and the click depend on.
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

}
