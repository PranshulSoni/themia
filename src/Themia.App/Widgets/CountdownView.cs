using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Time left until the dates the user cares about. [C-01]
/// <para>
/// The rows are built when the timer list changes and only re-lettered on a tick, rather than rebuilt
/// each time: a list that is thrown away once a second drops the row the pointer is over, and with a
/// seconds format that happens every second. [S-143]
/// </para>
/// <para>
/// The list is a Windows 11 list. Rows are a <b>fixed</b> 40px — 32 on a small tile — and the type is
/// the ramp's list-row steps, so a tile that grows shows <i>more rows</i> rather than bigger ones,
/// exactly as Settings and Explorer do. The only thing that scales with the tile is which of the two
/// densities is in use, and whether the row has room for its second line. There is no rule between
/// rows: Windows separates them with space and hover, and a ruled list is the clearest tell of a web
/// table.
/// </para>
/// </summary>
internal sealed partial class CountdownView : Grid, IWidgetView
{
    /// <summary>Under this a row goes compact: 32px, and the target date sheds. [C-06]</summary>
    private const double CompactWidth = 220;

    private const double CompactHeight = 170;

    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = Fluent.Text(FluentType.Body, "TextFillColorTertiaryBrush");
    private readonly List<Row> _rows = [];

    private Ticker? _ticker;
    private CountdownConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private bool _compact;

    public CountdownView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _scroll = new ScrollViewer
        {
            // Nothing either side, so rows reach both edges and carry the inset as their own padding;
            // a little under the last row so it is not flush against the card.
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 0);

        // [C-02] Says what to do about it, rather than just that there is nothing.
        _empty.Text = "No timers.\nAdd one in this widget's settings.";
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.Visibility = Visibility.Collapsed;

        Grid.SetRow(_empty, 0);

        Children.Add(_scroll);
        Children.Add(_empty);

        ApplyDensity();
        SizeChanged += (_, _) => ApplyDensity();
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as CountdownConfig ?? new CountdownConfig();

        BuildRows();
        Resubscribe();
        Refresh();
    }

    /// <summary>
    /// Only when the cadence actually changed. Dropping and remaking the subscription on every
    /// config touch makes dragging a settings slider stutter, because each frame tears down the
    /// timer subscription for every widget on the desktop and builds it again. [C-20]
    /// </summary>
    private void Resubscribe()
    {
        // Nothing to re-letter means nothing to wake for, and an empty countdown is a widget the
        // user has only just dropped. [S-142]
        if (_config.Timers.Count == 0)
        {
            _subscription?.Dispose();
            _subscription = null;
            _subscribedInterval = TimeSpan.Zero;
            return;
        }

        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Refresh);
    }

    private void BuildRows()
    {
        foreach (var existing in _rows)
        {
            existing.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();

        var empty = _config.Timers.Count == 0;
        _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        foreach (var timer in _config.Timers)
        {
            var row = new Row(timer);
            row.SetDensity(_compact);
            _rows.Add(row);
            _list.Children.Add(row.Host);
        }
    }

    /// <summary>Re-letter the existing rows. Called on every tick, so it allocates nothing but strings.</summary>
    private void Refresh()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;

        foreach (var row in _rows)
        {
            row.Update(now, zone);
        }
    }

    /// <summary>
    /// Pick the row density for this tile.
    /// <para>
    /// This is the whole of the widget's responsiveness, and deliberately so: the type is the ramp's
    /// list-row steps at every size, because a Windows list does not grow its text with the window —
    /// it shows more rows. Changing density in place also keeps the row under the pointer, which
    /// rebuilding the list would not.
    /// </para>
    /// </summary>
    private void ApplyDensity()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var measured = width > 0 && height > 0;

        _compact = measured && (width < CompactWidth || height < CompactHeight);

        foreach (var row in _rows)
        {
            row.SetDensity(_compact);
        }
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;

        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _list.Children.Clear();
    }

    /// <summary>One timer's row: label and target on the left, the duration on the right. [C-03]</summary>
    private sealed class Row
    {
        private readonly CountdownTimer _timer;
        private readonly TextBlock _label = Fluent.Text(FluentType.Body);
        private readonly TextBlock _date = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _remaining = Fluent.Text(FluentType.BodyStrong);

        private readonly Brush _primary = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private readonly Brush _secondary = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        // [C-40] The theme's own "something has gone past" colour, so it tracks light/dark and the
        // high-contrast themes rather than being a red this file made up.
        private readonly Brush _critical = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

        private readonly IDisposable _hover;

        public Row(CountdownTimer timer)
        {
            _timer = timer;

            _label.Text = string.IsNullOrWhiteSpace(timer.Label) ? CountdownFormatter.UntitledLabel : timer.Label;
            _label.MaxLines = 1;

            _date.MaxLines = 1;

            _remaining.VerticalAlignment = VerticalAlignment.Center;
            _remaining.Margin = new Thickness(Fluent.Space3, 0, 0, 0);

            // [C-04] Tabular figures, so a ticking duration does not shuffle sideways as the digits
            // change under it.
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_remaining, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_date, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);

            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_label);
            text.Children.Add(_date);

            Grid.SetColumn(text, 0);
            Grid.SetColumn(_remaining, 1);

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children = { text, _remaining },
            };

            // A Windows 11 list row: a fixed height, the inset as its own padding so the highlight
            // reaches both edges of the widget, and nothing ruled underneath it.
            Host = Fluent.Row(grid);

            // Held, and dropped in Detach: a handler left hooked keeps the row alive and re-lights
            // it after the list has been rebuilt or the widget torn down.
            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        public void Update(DateTimeOffset now, TimeZoneInfo zone)
        {
            var reading = CountdownFormatter.Read(_timer.Target, now, _timer.Format);

            _remaining.Text = reading.Text;
            _date.Text = CountdownFormatter.TargetLabel(_timer.Target, now, zone);

            // [C-40] [S-140] A target that has gone by is still worth showing — it is how long ago it
            // went — but it is no longer the thing to look at, so the label steps back to the
            // secondary text colour and the duration carries the fact in the theme's critical one.
            _remaining.Foreground = reading.Elapsed ? _critical : _primary;
            _label.Foreground = reading.Elapsed ? _secondary : _primary;

            // [C-05] The whole instant, for a row whose subtitle is necessarily abbreviated.
            ToolTipService.SetToolTip(Host, CountdownFormatter.FullTargetLabel(_timer.Target, zone));
        }

        /// <summary>
        /// [C-06] Standard or compact, the two heights Windows lists come in. The second line goes
        /// with the compact row rather than being squeezed into it: 32px holds one line of body type
        /// and no more, and a clipped date is worse than none.
        /// </summary>
        public void SetDensity(bool compact)
        {
            Fluent.SetDensity(Host, compact);
            _date.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Safe to call twice: the hover handle is idempotent and the child is already gone.</summary>
        public void Detach()
        {
            _hover.Dispose();
            Host.Child = null;
        }
    }
}
