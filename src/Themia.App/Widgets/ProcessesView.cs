using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The busiest processes, ranked — a Task Manager list sized to whatever tile it is dropped into.
/// <para>
/// The rows are built once and rewritten in place on every sample. Rebuilding the list each tick
/// was the obvious first version and it flickered: every row was a new visual, so the whole list
/// blinked twice a second even when nothing about it had changed.
/// </para>
/// </summary>
internal sealed partial class ProcessesView : Grid, IWidgetView
{
    private const string Scope = "processes-widget";

    /// <summary>
    /// The shortest a row may be and still be read.
    /// <para>
    /// The row count is a setting, but the tile's height is not — twenty rows in a 140px widget is
    /// seven pixels each, which is a texture rather than a list. The tile wins: it shows as many of
    /// the requested rows as it can actually render, and they are the top of the ranking, which is
    /// the part worth seeing.
    /// </para>
    /// </summary>
    private const double MinimumRowHeight = 14;

    private readonly TextBlock _title = new();
    private readonly FontIcon _icon = new();
    private readonly TextBlock _caption = new();
    private readonly Grid _list = new();
    private readonly TextBlock _empty = new();
    private readonly List<Row> _rows = [];

    private Ticker? _ticker;
    private ProcessesConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private IReadOnlyList<ProcessUsage> _latest = [];
    private bool _detached;

    public ProcessesView()
    {
        var definition = WidgetCatalog.Definition(WidgetKind.Processes);
        var muted = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        _icon.Glyph = definition.Glyph;
        _icon.Foreground = muted;
        _icon.VerticalAlignment = VerticalAlignment.Center;

        _title.Text = definition.Label;
        _title.Foreground = muted;
        _title.FontWeight = FontWeights.SemiBold;
        _title.VerticalAlignment = VerticalAlignment.Center;

        // Which column the ranking is on. Without it the list is ambiguous the moment two processes
        // have similar numbers in both columns.
        _caption.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.HorizontalAlignment = HorizontalAlignment.Right;

        var header = new Grid
        {
            Margin = new Thickness(WidgetLayout.ContentInset, WidgetLayout.ContentInset, WidgetLayout.ContentInset, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_caption, 2);
        _title.Margin = new Thickness(6, 0, 0, 0);

        header.Children.Add(_icon);
        header.Children.Add(_title);
        header.Children.Add(_caption);

        _empty.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Visibility = Visibility.Collapsed;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(header, 0);
        Grid.SetRow(_list, 1);
        Grid.SetRow(_empty, 1);

        Children.Add(header);
        Children.Add(_list);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;

        // The list's own size, as well as the widget's: when the card is resized the parent's
        // SizeChanged runs before its children have been arranged, so the row height read there is
        // the previous layout's. This one carries the height the rows actually got.
        _list.SizeChanged += OnSizeChanged;
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ProcessesConfig ?? new ProcessesConfig();

        _caption.Text = _config.Sort == ProcessSort.Cpu ? "by CPU" : "by memory";

        // Subscribing fires a tick of its own, so a cadence change redraws on its own. Everything
        // else — the row count, the memory column, the ranking — changes the shape of what is
        // already on screen and has to be redrawn now, or the widget would sit wrong until the next
        // sample two seconds later.
        if (!Resubscribe())
        {
            Sample();
        }
    }

    /// <summary>True when the subscription was rebuilt, which also fires an immediate tick.</summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        // Only when the cadence actually changed. Dropping and remaking the subscription on every
        // config touch made dragging a settings slider stutter, because each frame tore down the
        // timer subscription for every widget on the desktop and built it again.
        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Sample);
        return _subscription is not null;
    }

    private void Sample()
    {
        if (_detached)
        {
            return;
        }

        try
        {
            _latest = ProcessList.Top(Math.Min(_config.EffectiveMaxCount, Capacity()), _config.Sort);
        }
        catch (Exception ex)
        {
            // A widget that cannot read the process table says so, rather than taking the whole
            // surface down from inside a tick.
            Log.Error(Scope, "could not sample the process list", ex);
            _latest = [];
        }

        Render();
    }

    /// <summary>How many rows this tile can render at a readable height.</summary>
    private int Capacity()
    {
        var height = _list.ActualHeight;

        // Before the first layout pass there is no height to divide. The configured count is the
        // honest guess, and the first resize corrects it.
        if (!double.IsFinite(height) || height <= 0)
        {
            return _config.EffectiveMaxCount;
        }

        return Math.Max(1, (int)(height / MinimumRowHeight));
    }

    /// <summary>
    /// Puts <see cref="_latest"/> on screen, reusing the row visuals that are already there.
    /// </summary>
    private void Render()
    {
        var count = _latest.Count;

        if (count == 0)
        {
            _empty.Text = "Could not read the process list.";
            _empty.Visibility = Visibility.Visible;
            _list.Visibility = Visibility.Collapsed;
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _list.Visibility = Visibility.Visible;

        Resize(count);

        for (var i = 0; i < count; i++)
        {
            _rows[i].Show(_latest[i], _config.ShowMemory);
        }

        ApplyTypeScale();
    }

    /// <summary>
    /// Grows or shrinks the list to <paramref name="count"/> rows.
    /// <para>
    /// Star-sized rows rather than fixed heights, so the list fills the tile exactly at any size
    /// without the view having to compute — or recompute on every resize — what each row is worth.
    /// Rows beyond the count are collapsed rather than removed: the count moves when the user drags
    /// the row slider, and destroying and rebuilding visuals through that drag is what made the
    /// list flicker.
    /// </para>
    /// </summary>
    private void Resize(int count)
    {
        while (_rows.Count < count)
        {
            var row = new Row();
            _rows.Add(row);
            _list.Children.Add(row.Host);
        }

        while (_list.RowDefinitions.Count < count)
        {
            _list.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        while (_list.RowDefinitions.Count > count)
        {
            _list.RowDefinitions.RemoveAt(_list.RowDefinitions.Count - 1);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var visible = i < count;
            _rows[i].Host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (visible)
            {
                Grid.SetRow(_rows[i].Host, i);
            }
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyTypeScale();

        // A shorter tile holds fewer readable rows, so a resize can change what the list should
        // contain and not just how big it is. Redrawn from the sample already in hand; the next
        // tick refills it if the tile grew.
        if (_latest.Count > Capacity())
        {
            Render();
        }
    }

    /// <summary>
    /// Size the text to the tile. The row height is what the numbers have to fit inside, so it —
    /// not the widget height — is what the row font scales against.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        _title.FontSize = TypeScale.Fit(width, height, 0.055, 0.10, 10, 20);
        _icon.FontSize = _title.FontSize;
        _caption.FontSize = TypeScale.Fit(width, height, 0.040, 0.075, 9, 15);

        var visible = Math.Max(1, _list.RowDefinitions.Count);
        var rowHeight = _list.ActualHeight > 0 ? _list.ActualHeight / visible : MinimumRowHeight;
        var font = TypeScale.Fit(width, rowHeight, 0.060, 0.52, 9, 26);

        foreach (var row in _rows)
        {
            row.Scale(font, rowHeight);
        }
    }

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;
    }


    /// <summary>
    /// A shared transparent brush for the rows' rest state.
    /// <para>
    /// Transparent rather than null: a null background is not hit-testable, so the row would never
    /// raise the pointer events the hover depends on.
    /// </para>
    /// </summary>
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>
    /// One row's visuals, kept so a sample can rewrite them instead of replacing them.
    /// </summary>
    private sealed class Row
    {
        private readonly TextBlock _name = new();
        private readonly TextBlock _cpu = new();
        private readonly TextBlock _memory = new();

        public Row()
        {
            _name.Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.MaxLines = 1;
            _name.VerticalAlignment = VerticalAlignment.Center;

            // Right-aligned, because a column of numbers that are not aligned on their last digit
            // cannot be compared at a glance, which is the only reason to rank them.
            _cpu.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
            _cpu.TextAlignment = TextAlignment.Right;
            _cpu.VerticalAlignment = VerticalAlignment.Center;

            _memory.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
            _memory.TextAlignment = TextAlignment.Right;
            _memory.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(_name, 0);
            Grid.SetColumn(_cpu, 1);
            Grid.SetColumn(_memory, 2);

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children = { _name, _cpu, _memory },
            };

            Host = new Border
            {
                Child = grid,
                Background = Transparent,
                Padding = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, 0),
            };

            // Hover, the way every list on Windows does it — the same treatment as the mail list,
            // and the same stock brush, so it tracks the theme and the accent. PointerExited alone
            // is not enough: the pointer can leave without it when the widget is torn down or moved
            // mid-hover, which would strand a row lit.
            Host.PointerEntered += (_, _) => Host.Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray);
            Host.PointerExited += (_, _) => Host.Background = Transparent;
            Host.PointerCaptureLost += (_, _) => Host.Background = Transparent;
            Host.PointerCanceled += (_, _) => Host.Background = Transparent;
        }

        public Border Host { get; }

        /// <summary>Rewrite this row in place.</summary>
        public void Show(ProcessUsage usage, bool showMemory)
        {
            _name.Text = usage.Name;
            _cpu.Text = usage.CpuText;

            // A row whose CPU could not be read is dimmed as well as marked, so "--" reads as
            // "not available" rather than as a column the eye should be comparing.
            _cpu.Opacity = usage.CpuKnown ? 1 : 0.5;

            _memory.Visibility = showMemory ? Visibility.Visible : Visibility.Collapsed;

            if (showMemory)
            {
                _memory.Text = usage.MemoryText;
            }

            ToolTipService.SetToolTip(Host, $"{usage.Name} (pid {usage.Pid})");
        }

        /// <summary>Resize the row's text to the tile.</summary>
        public void Scale(double font, double rowHeight)
        {
            _name.FontSize = font;
            _cpu.FontSize = font;
            _memory.FontSize = font;

            // Reserved width rather than measured, so the numbers stay in the same place as they
            // change width and the name column does not jog left and right every sample.
            _cpu.MinWidth = font * 3.4;
            _memory.MinWidth = font * 4.6;

            _cpu.Margin = new Thickness(font * 0.5, 0, 0, 0);
            _memory.Margin = new Thickness(font * 0.5, 0, 0, 0);

            Host.Padding = new Thickness(
                WidgetLayout.ContentInset,
                Math.Clamp(rowHeight * 0.08, 0, 6),
                WidgetLayout.ContentInset,
                Math.Clamp(rowHeight * 0.08, 0, 6));
        }
    }
}
