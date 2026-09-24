using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.ApplicationModel.DataTransfer;

namespace Themia.App.Widgets;

/// <summary>
/// A palette you can copy colours out of.
/// <para>
/// A list of Windows rows rather than a mosaic of tiles: each colour is a chip, a name and the exact
/// text a click will put on the clipboard, in the notation the widget is set to. Showing the value
/// is the point — a palette you cannot read the numbers off is a picture of a palette.
/// </para>
/// <para>
/// The <b>colours it shows are content</b> and are therefore literal: they are the user's palette,
/// and a theme brush would be the wrong answer by definition. Everything the widget is made of —
/// the text, the chip's hairline, the fill under the pointer — comes from named brushes, so the
/// chrome follows light, dark and high contrast while the colours stay exactly what the user typed.
/// </para>
/// </summary>
internal sealed partial class ColorSwatchView : Grid, IWidgetView, IWidgetSurfaceAware
{
    private const string Scope = "color-swatch";

    /// <summary>
    /// A column narrower than this cannot hold a chip, a name and a value, so the widget uses fewer
    /// of them. Wider tiles get columns rather than taller rows: [S-213] a row is a fixed height.
    /// </summary>
    private const double MinColumnWidth = 210;

    /// <summary>Four columns of colours is already a lot to scan. Past that it is a swatch book.</summary>
    private const int MaxColumns = 4;

    /// <summary>Under this width there is no room for the value beside the name; the chip and the name stay.</summary>
    private const double ValueHidesBelow = 150;

    private readonly TextBlock _title = new();
    private string _label = WidgetCatalog.Definition(WidgetKind.ColorSwatch).Label;

    private readonly Grid _header;
    private readonly Grid _columns = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty;

    private readonly List<SwatchRow> _rows = [];

    private ColorSwatchConfig _config = new();

    /// <summary>What the rows were last built for, so a resize that changes neither rebuilds nothing.</summary>
    private int _builtColumns = -1;
    private bool _builtCompact;

    private bool _detached;

    public ColorSwatchView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = _label;
        _header = Fluent.Header(WidgetCatalog.Definition(WidgetKind.ColorSwatch).Glyph, _title);
        _header.Margin = Fluent.HeaderMargin;

        _scroller = new ScrollViewer
        {
            Content = _columns,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = WidgetLayout.ListInset,
        };

        _empty = Fluent.EmptyState(
            WidgetCatalog.Definition(WidgetKind.ColorSwatch).Glyph,
            new TextBlock { Text = "No colours here. Add some in settings." },
            out _);

        _empty.Visibility = Visibility.Collapsed;

        SetRow(_header, 0);
        SetRow(_scroller, 1);
        SetRow(_empty, 1);

        Children.Add(_header);
        Children.Add(_scroller);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _label = string.IsNullOrWhiteSpace(surface.Label)
            ? WidgetCatalog.Definition(WidgetKind.ColorSwatch).Label
            : surface.Label;

        _title.Text = _label;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        // Nothing to tick: a palette changes when it is edited, not when time passes.
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ColorSwatchConfig ?? new ColorSwatchConfig();

        // The palette itself changed, so the rows have to be rebuilt whatever the size is doing.
        _builtColumns = -1;
        Rebuild();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Rebuild();

    /// <summary>
    /// Lay the palette out for the size the widget currently is, and only when that has actually
    /// changed something — a resize drag raises this on every frame, and tearing down and rebuilding
    /// forty rows per frame is how a widget layer starts costing a core.
    /// </summary>
    private void Rebuild()
    {
        if (_detached)
        {
            return;
        }

        _header.Visibility = ActualHeight >= Fluent.HeaderHidesBelow ? Visibility.Visible : Visibility.Collapsed;

        var swatches = _config.EffectiveSwatches;

        if (swatches.Count == 0)
        {
            Clear();
            _scroller.Visibility = Visibility.Collapsed;
            _empty.Visibility = Visibility.Visible;
            _builtColumns = -1;
            return;
        }

        _scroller.Visibility = Visibility.Visible;
        _empty.Visibility = Visibility.Collapsed;

        var columns = ColumnCount(ActualWidth);
        var compact = ActualHeight < Fluent.CompactBelow;

        if (columns == _builtColumns && compact == _builtCompact && _rows.Count == swatches.Count)
        {
            // Same shape, same palette: the rows already on screen are the right ones.
            return;
        }

        _builtColumns = columns;
        _builtCompact = compact;

        Clear();

        for (var column = 0; column < columns; column++)
        {
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var perColumn = (int)Math.Ceiling(swatches.Count / (double)columns);
        var stacks = new StackPanel[columns];

        for (var column = 0; column < columns; column++)
        {
            stacks[column] = new StackPanel();
            SetColumn(stacks[column], column);
            _columns.Children.Add(stacks[column]);
        }

        var showValue = _config.ShowValues && (ActualWidth / columns) >= ValueHidesBelow;

        for (var i = 0; i < swatches.Count; i++)
        {
            var row = new SwatchRow(swatches[i], _config.CopyAs, compact, _config.ShowNames, showValue);
            _rows.Add(row);
            stacks[Math.Min(i / perColumn, columns - 1)].Children.Add(row.Root);
        }
    }

    /// <summary>Columns, not taller rows: a row is a fixed Fluent height whatever the tile does. [S-213]</summary>
    private static int ColumnCount(double width)
        => !double.IsFinite(width) || width <= 0
            ? 1
            : Math.Clamp((int)(width / MinColumnWidth), 1, MaxColumns);

    private void Clear()
    {
        foreach (var row in _rows)
        {
            row.Dispose();
        }

        _rows.Clear();
        _columns.Children.Clear();
        _columns.ColumnDefinitions.Clear();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        SizeChanged -= OnSizeChanged;
        Clear();
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// One colour: the chip, its name, and the text a click copies.
    /// <para>
    /// A class rather than a method because a row owns three handlers and a hover subscription, and
    /// every one of them has to come off again — a view that rebuilds its rows on resize would
    /// otherwise leak a set per drag.
    /// </para>
    /// </summary>
    private sealed class SwatchRow : IDisposable
    {
        private readonly Border _root;
        private readonly TextBlock? _value;
        private readonly IDisposable _hover;
        private readonly string _copied;

        private bool _disposed;

        public SwatchRow(Swatch swatch, ColorNotation notation, bool compact, bool showName, bool showValue)
        {
            _copied = ColorFormats.Format(swatch.Color, notation);

            var content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            // The chip is sized from the ROW, not from the tile: a widget dragged twice as wide
            // shows more colours, not bigger ones. [S-213]
            var size = (compact ? Fluent.RowHeightCompact : Fluent.RowHeight) - (Fluent.Space2 * 2);

            var chip = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Fluent.Space3, 0),

                // The one literal colour in the widget, and the only one that should be: this IS
                // the content.
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255,
                    swatch.Color.R,
                    swatch.Color.G,
                    swatch.Color.B)),

                // A swatch the same colour as the card would otherwise have no edge at all.
                BorderBrush = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray),
                BorderThickness = new Thickness(Fluent.Hairline),
            };

            Grid.SetColumn(chip, 0);
            content.Children.Add(chip);

            if (showName)
            {
                var name = Fluent.Text(FluentType.Body);
                name.Text = swatch.Name;
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 1);
                content.Children.Add(name);
            }

            if (showValue)
            {
                _value = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                _value.Text = _copied;
                _value.VerticalAlignment = VerticalAlignment.Center;
                _value.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
                Grid.SetColumn(_value, 2);
                content.Children.Add(_value);
            }

            _root = Fluent.Row(content, compact);
            _hover = Fluent.Hover(_root);

            ToolTipService.SetToolTip(_root, $"Copy {_copied}");

            // Tapped, not PointerPressed: in edit mode the card captures the pointer on press to
            // drag the widget, the gesture never completes here, and nothing is copied while a
            // widget is being moved. See ScratchpadView for the whole story.
            _root.Tapped += OnTapped;
            _root.PointerExited += OnLeft;
            _root.PointerCaptureLost += OnLeft;
        }

        public FrameworkElement Root => _root;

        /// <summary>
        /// Copy, and say so in place of the value until the pointer leaves.
        /// <para>
        /// Event-driven rather than a timer: "Copied" that clears itself after a second and a half
        /// needs a wall clock, a dispatcher and something to dispose, and the pointer leaving the row
        /// is both a better signal and free.
        /// </para>
        /// </summary>
        private void OnTapped(object sender, TappedRoutedEventArgs args)
        {
            // Not marked handled: the card's double-click, which is how the desktop enters edit
            // mode, still has to reach it. Copying the same colour twice costs nothing.
            try
            {
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetText(_copied);
                Clipboard.SetContent(package);
            }
            catch (Exception ex)
            {
                // Another process can hold the clipboard open; Windows fails the call rather than
                // waiting. Say so instead of leaving the row claiming it copied something.
                Log.Error(Scope, $"could not copy {_copied} to the clipboard", ex);
                return;
            }

            try
            {
                // Without this the colour is gone from the clipboard the moment Themia exits, which
                // for a widget that is running all day is exactly when it would be missed.
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                Log.Warn(Scope, "the clipboard was copied but could not be flushed; it will not outlive the app", ex);
            }

            if (_value is not null)
            {
                _value.Text = "Copied";
            }
        }

        private void OnLeft(object sender, PointerRoutedEventArgs args)
        {
            if (_value is not null)
            {
                _value.Text = _copied;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _root.Tapped -= OnTapped;
            _root.PointerExited -= OnLeft;
            _root.PointerCaptureLost -= OnLeft;
            _hover.Dispose();
        }
    }
}
