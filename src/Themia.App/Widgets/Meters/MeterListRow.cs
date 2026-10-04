using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// [S-1127] One row of a meter's breakdown list: the name, its figure right-aligned in tabular
/// digits, and — on a panel with room for it — a hairline bar of its share underneath. A compact
/// Fluent row, edge to edge with the text inset; kept and rewritten in place on every sample.
/// <para>
/// [S-1166] Hover only on a row that does something when clicked (a drive opens in Explorer): a
/// highlight that promises an action and then gives none is worse than no highlight.
/// </para>
/// </summary>
internal sealed class MeterListRow
{
    private static readonly Brush TrackBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);
    private static readonly Brush HoverBrush = Fluent.RowHover();

    private readonly TextBlock _name = Fluent.Text(FluentType.Body);
    private readonly TextBlock _value = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly Grid _bar = new() { Height = Fluent.Hairline * 2, Margin = new Thickness(0, Fluent.Space1 / 2, 0, 0) };
    private readonly Rectangle _fill = new();
    private readonly Rectangle _rest = new();
    private readonly Brush _idle;
    private Action? _open;

    public MeterListRow()
    {
        _name.MaxLines = 1;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;

        // A column of numbers not aligned on their last digit cannot be compared at a glance, and
        // proportional digits shuffle as the value changes.
        _value.TextAlignment = TextAlignment.Right;
        Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);

        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _rest.Fill = TrackBrush;
        Grid.SetColumn(_rest, 1);
        _bar.Children.Add(_fill);
        _bar.Children.Add(_rest);

        Grid.SetColumn(_value, 1);
        Grid.SetRow(_bar, 1);
        Grid.SetColumnSpan(_bar, 2);

        var grid = new Grid
        {
            ColumnSpacing = Fluent.Space3,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            Children = { _name, _value, _bar },
        };

        Host = Fluent.Row(grid, compact: true);
        _idle = Host.Background;

        Host.PointerEntered += (_, _) => Host.Background = _open is null ? _idle : HoverBrush;
        Host.PointerExited += (_, _) => Host.Background = _idle;
        Host.Tapped += (_, e) =>
        {
            if (_open is not { } open)
            {
                return;
            }

            e.Handled = true;

            try
            {
                open();
            }
            catch (Exception ex)
            {
                // A row's click runs the kind's code; nothing it throws may reach the surface.
                Log.Error("meter", "a meter row's click failed", ex);
            }
        };
    }

    public Border Host { get; }

    /// <summary>Rewrite the row; <paramref name="bars"/> shows the share bar in <paramref name="brush"/>.</summary>
    public void Show(MeterRow row, bool bars, Brush brush)
    {
        _name.Text = row.Name;
        _value.Text = row.Value;

        if ((_open is null) != (row.Open is null))
        {
            Host.Background = _idle;
        }

        _open = row.Open;

        var bar = bars && row.Fraction is { } f && double.IsFinite(f);
        _bar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;

        if (bar)
        {
            var share = Math.Clamp(row.Fraction!.Value, 0, 1);
            _bar.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
            _bar.ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);
            _fill.Fill = brush;
        }

        ToolTipService.SetToolTip(Host, row.Tooltip);
    }
}
