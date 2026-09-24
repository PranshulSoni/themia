using Microsoft.UI;
using Microsoft.UI.Text;
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
/// seconds format that happens every second.
/// </para>
/// </summary>
internal sealed partial class CountdownView : Grid, IWidgetView
{
    private readonly TextBlock _title = new();
    private readonly FontIcon _icon = new();
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new();
    private readonly List<Row> _rows = [];

    private Ticker? _ticker;
    private CountdownConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    public CountdownView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _title.Text = "Countdowns";
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _icon.Glyph = Glyphs.Countdown;
        _icon.Foreground = _title.Foreground;
        _icon.Margin = new Thickness(0, 0, 6, 0);
        _icon.VerticalAlignment = VerticalAlignment.Center;

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(WidgetLayout.ContentInset, WidgetLayout.ContentInset, WidgetLayout.ContentInset, 6),
            Children = { _icon, _title },
        };

        Grid.SetRow(header, 0);

        _scroll = new ScrollViewer
        {
            Padding = WidgetLayout.ListInset,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Grid.SetRow(_scroll, 1);

        // [C-02] Says what to do about it, rather than just that there is nothing.
        _empty.Text = "No timers.\nAdd one in this widget's settings.";
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        _empty.Visibility = Visibility.Collapsed;

        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);

        SizeChanged += (_, _) => ApplyTypeScale();
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
        // user has only just dropped.
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
        _rows.Clear();
        _list.Children.Clear();

        var empty = _config.Timers.Count == 0;
        _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        foreach (var timer in _config.Timers)
        {
            var row = new Row(timer);
            _rows.Add(row);
            _list.Children.Add(row.Host);

            // A rule under the last row is a rule under nothing: it reads as an empty extra row.
            row.Host.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        if (_rows.Count > 0)
        {
            _rows[^1].Host.BorderThickness = new Thickness(0);
        }

        ApplyTypeScale();
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
    /// Size the rows to the tile. The duration is the reading, so it leads; the label and the date
    /// under it stay support, because a row whose three parts are the same size has no shape.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        _title.FontSize = TypeScale.Fit(width, height, 0.055, 0.090, 10, 18);
        _icon.FontSize = _title.FontSize;
        _empty.FontSize = TypeScale.Fit(width, height, 0.055, 0.090, 10, 18);

        var label = TypeScale.Fit(width, height, 0.055, 0.100, 11, 20);
        var date = TypeScale.Fit(width, height, 0.045, 0.080, 9, 15);
        var remaining = TypeScale.Fit(width, height, 0.075, 0.130, 12, 26);

        foreach (var row in _rows)
        {
            row.ApplyTypeScale(label, date, remaining);
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


    /// <summary>
    /// A shared transparent brush for a row's rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so the row would never
    /// raise the pointer events the hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Clear = new(Colors.Transparent);

    /// <summary>One timer's row: label and target on the left, the duration on the right. [C-03]</summary>
    private sealed class Row
    {
        private readonly CountdownTimer _timer;
        private readonly TextBlock _label = new();
        private readonly TextBlock _date = new();
        private readonly TextBlock _remaining = new();

        private readonly Brush _primary = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private readonly Brush _secondary = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        private readonly Brush _tertiary = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);

        // [C-40] The theme's own "something has gone past" colour, so it tracks light/dark and the
        // high-contrast themes rather than being a red this file made up.
        private readonly Brush _critical = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);

        public Row(CountdownTimer timer)
        {
            _timer = timer;

            _label.Text = string.IsNullOrWhiteSpace(timer.Label) ? CountdownFormatter.UntitledLabel : timer.Label;
            _label.TextTrimming = TextTrimming.CharacterEllipsis;
            _label.MaxLines = 1;
            _label.Foreground = _primary;

            _date.TextTrimming = TextTrimming.CharacterEllipsis;
            _date.MaxLines = 1;
            _date.Foreground = _tertiary;

            _remaining.VerticalAlignment = VerticalAlignment.Center;
            _remaining.Margin = new Thickness(10, 0, 0, 0);
            _remaining.FontWeight = FontWeights.SemiBold;
            _remaining.Foreground = _primary;

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

            Host = new Border
            {
                Child = grid,
                // The row is the full width of the widget, so the inset is its own padding and there
                // is no negative margin pulling it back out over a card that no longer pads it.
                // No corner radius either: a rounded highlight inside a flush list reads as a
                // button, and these are rows.
                Padding = new Thickness(WidgetLayout.ContentInset, 6, WidgetLayout.ContentInset, 6),
                Background = Clear,
                BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            };

            // PointerExited alone is not enough: the pointer can leave without it when the list
            // scrolls or the widget is torn down mid-hover, which would strand a row lit.
            Host.PointerEntered += OnPointerEntered;
            Host.PointerExited += OnPointerGone;
            Host.PointerCaptureLost += OnPointerGone;
            Host.PointerCanceled += OnPointerGone;
        }

        public Border Host { get; }

        public void Update(DateTimeOffset now, TimeZoneInfo zone)
        {
            var reading = CountdownFormatter.Read(_timer.Target, now, _timer.Format);

            _remaining.Text = reading.Text;
            _date.Text = CountdownFormatter.TargetLabel(_timer.Target, now, zone);

            // [C-40] A target that has gone by is still worth showing — it is how long ago it went —
            // but it is no longer the thing to look at, so the label steps back and the duration
            // carries the fact.
            _remaining.Foreground = reading.Elapsed ? _critical : _primary;
            _label.Foreground = reading.Elapsed ? _secondary : _primary;

            // [C-05] The whole instant, for a row whose subtitle is necessarily abbreviated.
            ToolTipService.SetToolTip(Host, CountdownFormatter.FullTargetLabel(_timer.Target, zone));
        }

        public void ApplyTypeScale(double label, double date, double remaining)
        {
            _label.FontSize = label;
            _date.FontSize = date;
            _remaining.FontSize = remaining;
        }

        public void Detach()
        {
            Host.PointerEntered -= OnPointerEntered;
            Host.PointerExited -= OnPointerGone;
            Host.PointerCaptureLost -= OnPointerGone;
            Host.PointerCanceled -= OnPointerGone;
            Host.Child = null;
        }

        private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
            => Host.Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);

        private void OnPointerGone(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
            => Host.Background = Clear;
    }
}
