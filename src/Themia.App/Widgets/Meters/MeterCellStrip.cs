using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1150] A strip of small vertical bars, one per part — Task Manager's "logical processors" view
/// shrunk to a rung of the meter's ladder: a caption, then the bars in as many balanced rows as the
/// width needs, each filled to its part's load in the colour that load deserves, with its figure
/// under it where every bar is wide enough for one. Built once per bar count, rewritten in place.
/// </summary>
internal sealed class MeterCellStrip
{
    /// <summary>The gap between bars, across and down.</summary>
    private const double Gap = MeterCellForms.Gap;

    /// <summary>"100": the figure under a bar, without its percent sign.</summary>
    public const int LabelCharacters = 3;

    private static readonly Brush TrackBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);

    private readonly TextBlock _title = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _grid = new() { ColumnSpacing = Gap, RowSpacing = Gap };
    private readonly List<Cell> _cells = [];
    private CellStripForm _form;

    public MeterCellStrip()
    {
        _title.MaxLines = 1;
        _title.Margin = new Thickness(0, 0, 0, MeterCellForms.TitleGap);

        Host = new StackPanel
        {
            Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0),
            Children = { _title, _grid },
        };
    }

    public StackPanel Host { get; }

    /// <summary>Lay the bars out in <paramref name="form"/>; only rebuilds the grid when the form changed.</summary>
    public void Arrange(CellStripForm form, int count)
    {
        _title.Visibility = form.Title ? Visibility.Visible : Visibility.Collapsed;

        if (form == _form && count == _cells.Count)
        {
            return;
        }

        _form = form;
        var shape = form.Shape;
        var barHeight = form.BarHeight;
        var labels = form.Labels;

        while (_cells.Count < count)
        {
            var cell = new Cell(_cells.Count);
            _cells.Add(cell);
            _grid.Children.Add(cell.Host);
        }

        while (_cells.Count > count)
        {
            _grid.Children.Remove(_cells[^1].Host);
            _cells.RemoveAt(_cells.Count - 1);
        }

        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();

        for (var c = 0; c < shape.Columns; c++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < shape.Rows; r++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            Grid.SetColumn(cell.Host, shape.Columns == 0 ? 0 : i % shape.Columns);
            Grid.SetRow(cell.Host, shape.Columns == 0 ? 0 : i / shape.Columns);
            cell.Size(barHeight, labels);
        }
    }

    /// <summary>Rewrite the bars; parts the reading does not cover (no sample yet) draw empty.</summary>
    public void Show(MeterCells? cells, Func<MeterPressure, Brush> brushOf)
    {
        _title.Text = cells?.Title ?? string.Empty;
        var values = cells?.Cells ?? [];

        for (var i = 0; i < _cells.Count; i++)
        {
            var value = i < values.Count ? values[i] : new MeterCell(0, MeterPressure.Normal);
            _cells[i].Show(value, brushOf(value.Pressure));
        }
    }

    /// <summary>One bar: the track, the fill rising from the bottom, and its figure underneath.</summary>
    private sealed class Cell
    {
        private readonly Grid _track = new() { CornerRadius = new CornerRadius(1) };
        private readonly Border _fill = new() { VerticalAlignment = VerticalAlignment.Bottom };
        private readonly TextBlock _label = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private double _height;

        public Cell(int index)
        {
            _track.Background = TrackBrush;
            _track.Children.Add(_fill);

            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.TextTrimming = TextTrimming.None;
            Typography.SetNumeralAlignment(_label, FontNumeralAlignment.Tabular);

            Host = new StackPanel { Children = { _track, _label } };
            ToolTipService.SetToolTip(Host, Str.WidgetsB_Cpu_Processor.Format(("index", index)));
        }

        public StackPanel Host { get; }

        public void Size(double barHeight, bool labels)
        {
            _height = barHeight;
            _track.Height = barHeight;
            _label.Visibility = labels ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(MeterCell cell, Brush brush)
        {
            var fraction = double.IsFinite(cell.Fraction) ? Math.Clamp(cell.Fraction, 0, 1) : 0;
            _fill.Height = Math.Round(_height * fraction);
            _fill.Background = brush;

            if (_label.Visibility == Visibility.Visible)
            {
                _label.Text = Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
