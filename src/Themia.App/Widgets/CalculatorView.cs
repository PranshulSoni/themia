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
    private readonly Grid _header;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly ScrollViewer _historyScroll;
    private readonly StackPanel _empty;
    private readonly FontIcon _emptyIcon;
    private readonly TextBlock _preview;
    private readonly TextBox _input = new();

    /// <summary>The surface this widget is drawn on: where it asks for the keyboard.</summary>
    private IWidgetSurface? _surface;
    private readonly Border _entryArea;
    private readonly CalculatorHistory _history = new();

    /// <summary>One per row. A handler left hooked re-lights a row that is no longer on screen.</summary>
    private readonly List<IDisposable> _hovers = [];

    private readonly Brush _previewBrush;
    private readonly Brush _errorBrush;

    private CalculatorConfig _config = new();
    private bool _detached;

    public CalculatorView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _previewBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        // The stock "something is wrong" fill, so the error tracks the theme rather than being a
        // red this file decided on.
        _errorBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

        var definition = WidgetCatalog.Definition(WidgetKind.Calculator);

        _title.Text = definition.Label;

        // The standard pane header, at a fixed ramp step: a header that grew with the tile is the
        // web habit this port came in with.
        _header = Fluent.Header(definition.Glyph, _title);
        _header.Margin = new Thickness(Fluent.Space4, Fluent.Space4, Fluent.Space4, Fluent.Space2);

        // Rows reach both edges of the card, so the list scrolls with no inset of its own.
        _rows.Margin = WidgetLayout.ListInset;

        _historyScroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // [S-03]/[S-04] The running answer is support for the line being typed, not a headline of
        // its own: Body in the secondary colour, the same as any Windows hint line.
        _preview = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        _preview.HorizontalAlignment = HorizontalAlignment.Right;
        _preview.Visibility = Visibility.Collapsed; // [S-04] nothing typed, nothing to show

        // A stock Windows 11 text box, styled by the theme rather than by this file.
        _input.PlaceholderText = "2 + 3 * 4";
        _input.IsSpellCheckEnabled = false;
        _input.IsTextPredictionEnabled = false;
        _input.TextAlignment = TextAlignment.Right;
        _input.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _input.TextChanged += OnInputChanged;
        _input.KeyDown += OnInputKeyDown;
        _input.GotFocus += OnInputGotFocus;
        _input.LostFocus += OnInputLostFocus;

        // The line being typed is separated from the ones already worked out. This is the widget's
        // one rule, and it earns it the way a dialog's command area does: it divides a scrolling
        // region from a fixed one. The history rows themselves have no dividers at all.
        _entryArea = new Border
        {
            BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Spacing = Fluent.Space1, Children = { _preview, _input } },
        };

        // A tall tile with nothing worked out yet was a large blank area above the input, which
        // reads as something failing to load. Windows Calculator says so in words instead.
        _empty = Fluent.EmptyState(Glyphs.Calculator, new TextBlock { Text = "No history yet." }, out _emptyIcon);
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);

        Grid.SetRow(_header, 0);
        Grid.SetRow(_historyScroll, 1);
        Grid.SetRow(_entryArea, 2);

        Children.Add(_header);
        Children.Add(_historyScroll);
        Children.Add(_empty);
        Children.Add(_entryArea);

        SizeChanged += OnSizeChanged;
        ApplyLayout();
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
                _preview.Foreground = _previewBrush;
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
        ClearRows();

        // At most eight rows ([S-09]), so redrawing the lot is cheaper than tracking which changed.
        foreach (var entry in _history.Entries)
        {
            _rows.Children.Add(BuildRow(entry));
        }

        // ApplyLayout owns what is shown at this size, so there is one place that decides how the
        // widget looks at 180x120.
        ApplyLayout();
    }

    /// <summary>
    /// One history row, in the standard Windows 11 list shape: a fixed 40px height, the content
    /// inset carried as the row's own padding so the row — and the fill it takes under the pointer —
    /// reaches both edges of the card, and no divider.
    /// </summary>
    /// <summary>Drop the rows and the pointer handlers that belong to them. Safe to call twice.</summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _rows.Children.Clear();
    }

    private FrameworkElement BuildRow(Calculation entry)
    {
        var expression = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        expression.Text = entry.Expression;

        var result = Fluent.Text(FluentType.BodyStrong);
        result.Text = "= " + entry.Result;
        result.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

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

        var row = Fluent.Row(layout);
        row.Tag = entry.Result;

        _hovers.Add(Fluent.Hover(row));

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

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout();

    /// <summary>
    /// Decide what a tile this size shows. Nothing here is a size: every piece of type is a fixed
    /// ramp step and the rows are the standard 40px, so a tile that grows shows <i>more rows</i>
    /// rather than larger ones — which is what Explorer and Settings do, and what the fractions this
    /// widget used to compute did not.
    /// </summary>
    private void ApplyLayout()
    {
        var height = ActualHeight;

        // [S-131] Below roughly 90 DIPs the title takes the room the sum needs.
        _header.Visibility = height < 90 ? Visibility.Collapsed : Visibility.Visible;

        // [S-131] The original hides the history under 120px of height, and it is right to: in a
        // 180x120 tile the thing being typed is the whole point, and two clipped rows above it are
        // noise. A measured height of 0 is a tile that has not been laid out yet, not a short one.
        var showHistory = _history.Count > 0 && (height >= 120 || height <= 0);

        _historyScroll.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;

        // The empty state takes the room the history would have had, on a tile tall enough to
        // have shown it. On a short tile neither appears and the sum has the whole widget.
        var roomForHistory = height >= 120 || height <= 0;
        _empty.Visibility = !showHistory && roomForHistory ? Visibility.Visible : Visibility.Collapsed;
        _emptyIcon.Visibility = height >= 180 ? Visibility.Visible : Visibility.Collapsed;

        // With nothing above it the rule would be a line across the top of the widget.
        _entryArea.BorderThickness = new Thickness(0, showHistory ? 1 : 0, 0, 0);
        _entryArea.Padding = new Thickness(
            Fluent.Space4,
            showHistory ? Fluent.Space3 : 0,
            Fluent.Space4,
            Fluent.Space4);
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
        ClearRows();
    }

}
