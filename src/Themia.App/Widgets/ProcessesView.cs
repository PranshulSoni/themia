using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
    /// The height of a row: Fluent's compact list row, the same one Task Manager and Explorer's
    /// details view use.
    /// <para>
    /// A fixed height, not a fraction of the tile. The row count is a setting, but the tile's height
    /// is not — twenty rows in a 140px widget is seven pixels each, which is a texture rather than a
    /// list, and rows that grow with the window are a web idea that reads as the wrong application
    /// next to Explorer. The tile shows as many rows of the right height as it has room for, and
    /// they are the top of the ranking, which is the part worth seeing.
    /// </para>
    /// </summary>
    private const double RowHeight = Fluent.RowHeightCompact;

    private readonly TextBlock _title = new();
    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

    // The Grid is what the rows are measured against: it is stretched by its star row, so its height
    // is the space the list actually has. The StackPanel inside it holds the rows at their own fixed
    // height, top down.
    private readonly Grid _list = new();
    private readonly StackPanel _stack = new();
    private readonly TextBlock _empty = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
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

        _title.Text = definition.Label;

        // Which column the ranking is on. Without it the list is ambiguous the moment two processes
        // have similar numbers in both columns.
        _caption.HorizontalAlignment = HorizontalAlignment.Right;

        var header = Fluent.Header(definition.Glyph, _title, _caption);
        header.Margin = Fluent.HeaderMargin;

        _stack.VerticalAlignment = VerticalAlignment.Top;
        _list.Children.Add(_stack);

        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextTrimming = TextTrimming.None;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.TextAlignment = TextAlignment.Center;
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
        // SizeChanged runs before its children have been arranged, so the height read there is the
        // previous layout's. This one carries the height the rows actually got.
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

    /// <summary>How many whole rows this tile has room to draw.</summary>
    private int Capacity()
    {
        var height = _list.ActualHeight;

        // Before the first layout pass there is no height to divide. The configured count is the
        // honest guess, and the first resize corrects it.
        if (!double.IsFinite(height) || height <= 0)
        {
            return _config.EffectiveMaxCount;
        }

        return Math.Max(1, (int)(height / RowHeight));
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
    }

    /// <summary>
    /// Grows or shrinks the list to <paramref name="count"/> rows.
    /// <para>
    /// Rows beyond the count are collapsed rather than removed: the count moves when the user drags
    /// the row slider, and destroying and rebuilding visuals through that drag is what made the list
    /// flicker.
    /// </para>
    /// </summary>
    private void Resize(int count)
    {
        while (_rows.Count < count)
        {
            var row = new Row();
            _rows.Add(row);
            _stack.Children.Add(row.Host);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Host.Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // A torn-down widget has no rows left to re-fit, and rebuilding them here would hand new
        // hover handlers to a view nothing will ever detach again.
        if (_detached)
        {
            return;
        }

        // A shorter tile holds fewer rows, so a resize can change what the list should contain and
        // not just how big it is. Redrawn from the sample already in hand; the next tick refills it
        // if the tile grew.
        if (_latest.Count > Capacity())
        {
            Render();
        }
    }

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;

        // The rows go with the subscription that fed them, hover handlers and all: a handler left
        // hooked keeps its row alive and re-lights it after teardown. Attach rebuilds them on the
        // first sample, which is what a re-attached widget draws anyway.
        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _stack.Children.Clear();
    }

    /// <summary>
    /// One row's visuals, kept so a sample can rewrite them instead of replacing them.
    /// </summary>
    private sealed class Row
    {
        /// <summary>The width the numeric columns hold open, so the name column does not jog.</summary>
        private const double CpuWidth = Fluent.Space6 * 2;

        private const double MemoryWidth = Fluent.Space4 * 4;

        // A value that could not be read is a colour, not an opacity: the same disabled brush every
        // other unavailable thing on the desktop uses, so it follows the theme. Shared by every row,
        // because a stock brush looked up twenty times is the same brush twenty times.
        private static readonly Brush KnownBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

        private static readonly Brush UnknownBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly TextBlock _cpu = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly TextBlock _memory = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        private readonly IDisposable _hover;

        public Row()
        {
            _name.MaxLines = 1;

            // Right-aligned, because a column of numbers that are not aligned on their last digit
            // cannot be compared at a glance, which is the only reason to rank them. Tabular
            // figures as well: proportional digits change width as the value changes, so a column
            // sampled twice a second visibly shuffles.
            _cpu.TextAlignment = TextAlignment.Right;
            _cpu.MinWidth = CpuWidth;
            Typography.SetNumeralAlignment(_cpu, FontNumeralAlignment.Tabular);

            _memory.TextAlignment = TextAlignment.Right;
            _memory.MinWidth = MemoryWidth;
            Typography.SetNumeralAlignment(_memory, FontNumeralAlignment.Tabular);

            Grid.SetColumn(_name, 0);
            Grid.SetColumn(_cpu, 1);
            Grid.SetColumn(_memory, 2);

            var grid = new Grid
            {
                ColumnSpacing = Fluent.Space3,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children = { _name, _cpu, _memory },
            };

            // A Fluent compact row: a fixed height, the standard inset as its own padding so the
            // row — and the highlight it draws — reaches both edges of the widget, and no divider
            // underneath. Windows 11 lists separate rows with space and hover, not with a rule.
            Host = Fluent.Row(grid, compact: true);

            // Hover, the way every list on Windows does it. Held, because a handler left hooked
            // keeps the row alive and re-lights it after the widget has been torn down.
            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        /// <summary>Unhook the hover. Safe to call twice: the handle itself is idempotent.</summary>
        public void Detach() => _hover.Dispose();

        /// <summary>Rewrite this row in place.</summary>
        public void Show(ProcessUsage usage, bool showMemory)
        {
            _name.Text = usage.Name;
            _cpu.Text = usage.CpuText;

            // A row whose CPU could not be read is greyed as well as marked, so "--" reads as
            // "not available" rather than as a column the eye should be comparing.
            _cpu.Foreground = usage.CpuKnown ? KnownBrush : UnknownBrush;

            _memory.Visibility = showMemory ? Visibility.Visible : Visibility.Collapsed;

            if (showMemory)
            {
                _memory.Text = usage.MemoryText;
            }

            ToolTipService.SetToolTip(Host, $"{usage.Name} (pid {usage.Pid})");
        }
    }
}
