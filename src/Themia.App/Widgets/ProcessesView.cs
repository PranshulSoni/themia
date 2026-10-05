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
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// The busiest processes, ranked — a Task Manager list sized to whatever tile it is dropped into.
/// <para>
/// [S-1151] Its disclosure ladder: as many whole rows as the tile holds (up to the configured
/// ceiling) with the ranked figure; the other figure, then threads, then the pid as the width allows
/// ([S-1152]); on a tile Expanded in height, column headers that sort the list when clicked and a
/// footer with the totals; on one Expanded both ways, a hairline share bar under each row ([S-1153]).
/// </para>
/// <para>
/// The rows are built once and rewritten in place on every sample. Rebuilding the list each tick
/// was the obvious first version and it flickered: every row was a new visual, so the whole list
/// blinked twice a second even when nothing about it had changed.
/// </para>
/// </summary>
internal sealed partial class ProcessesView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSizeAware, IWidgetSurfaceAware, IWidgetKeys
{
    private const string Scope = "processes-widget";

    /// <summary>
    /// The height of a row: Fluent's compact list row, the same one Task Manager and Explorer's
    /// details view use. Fixed, not a fraction of the tile: the tile shows as many rows of the right
    /// height as it has room for, and they are the top of the ranking, which is the part worth seeing.
    /// </summary>
    private const double RowHeight = Fluent.RowHeightCompact;

    /// <summary>Below this, the numeric columns stop holding open their full width and the ranking label goes.</summary>
    private const double NarrowBelow = 160;

    private static readonly TimeSpan ProcessAge = TimeSpan.FromSeconds(2);

    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

    private readonly Grid _columns = new() { ColumnSpacing = ProcessTable.Spacing };
    private readonly TextBlock[] _columnLabels = new TextBlock[5];
    private readonly StackPanel _stack = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _footer = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _empty = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly List<Row> _rows = [];

    /// <summary>[S-1566] The pid of each row on screen, top first: what the keyboard cursor moves over.</summary>
    private readonly List<string> _shownKeys = [];

    private readonly Brush _secondaryBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
    private readonly Brush _tertiaryBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);

    private Ticker? _ticker;
    private IWidgetSurface? _surface;
    private ProcessesConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private ProcessSnapshot _latest = ProcessSnapshot.Empty;
    private bool _failed;
    private bool _detached;

    private WidgetArea _area;
    private int _rowCount;
    private ProcessColumn _shown = ProcessColumn.Cpu;
    private bool _bars;

    /// <summary>[S-1566] The process under the keyboard cursor (its pid), or null; it follows the process through re-rankings.</summary>
    private string? _cursor;

    /// <summary>The row that carries the ring, or -1; rows are reused, so it is re-marked only when it moves.</summary>
    private int _marked = -1;

    public ProcessesView()
    {
        // Which column the ranking is on. Without it the list is ambiguous the moment two processes
        // have similar numbers in both columns. It rides on the card's header, which reads it once
        // at construction — so it is built here, not on first use.
        _caption.HorizontalAlignment = HorizontalAlignment.Right;

        BuildColumns();

        _footer.Margin = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space2);
        _footer.MaxLines = 1;
        Typography.SetNumeralAlignment(_footer, FontNumeralAlignment.Tabular);

        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextTrimming = TextTrimming.None;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Padding = WidgetLayout.SideInset;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.Text = Str.WidgetsC_Processes_ReadFailed.Text();
        _empty.Visibility = Visibility.Collapsed;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_columns, 0);
        Grid.SetRow(_stack, 1);
        Grid.SetRow(_footer, 2);
        Grid.SetRowSpan(_empty, 3);

        Children.Add(_columns);
        Children.Add(_stack);
        Children.Add(_footer);
        Children.Add(_empty);
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _caption;

    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        Strings.Changed -= OnLanguage;
        Strings.Changed += OnLanguage;
        ApplyConfig(config);
    }

    /// <summary>[S-1110] The caption, the column headings and the message again; the rows redraw.</summary>
    private void OnLanguage(Locale locale)
    {
        _empty.Text = Str.WidgetsC_Processes_ReadFailed.Text();
        _caption.Text = SortCaption(_config.Sort);
        ApplyColumnLabels();
        Render();
    }

    private static string SortCaption(ProcessSort sort)
        => (sort == ProcessSort.Cpu ? Str.WidgetsC_Processes_ByCpu : Str.WidgetsC_Processes_ByMemory).Text();

    private void ApplyColumnLabels()
    {
        Str[] labels = [Str.WidgetsC_Processes_Col_Name, Str.WidgetsC_Processes_Col_Cpu, Str.WidgetsC_Processes_Col_Memory, Str.WidgetsC_Processes_Col_Threads, Str.WidgetsC_Processes_Col_Pid];
        for (var i = 0; i < labels.Length && i < _columnLabels.Length; i++)
        {
            if (_columnLabels[i] is { } label)
            {
                label.Text = labels[i].Text();
            }
        }
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ProcessesConfig ?? new ProcessesConfig();
        _caption.Text = SortCaption(_config.Sort);

        // Subscribing fires a tick of its own; otherwise the shape of what is on screen changed
        // (rows, columns, ranking) and is redrawn now from a fresh sample.
        Lay();

        if (!Resubscribe())
        {
            Sample();
        }
    }

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
        Render();
    }

    /// <summary>[S-1154] Change the ranking, persist it, and redraw from the sample in hand.</summary>
    private void SetSort(ProcessSort sort)
    {
        if (_config.Sort == sort)
        {
            return;
        }

        _config.Sort = sort;
        _caption.Text = SortCaption(sort);
        _surface?.RequestSave();
        Lay();
        Render();
    }

    /// <summary>True when the subscription was rebuilt, which also fires an immediate tick.</summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        // Only when the cadence actually changed: remaking the subscription on every config touch
        // made dragging a settings slider stutter.
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
            _latest = ProcessList.Recent(ProcessAge);
            _failed = _latest.Rows.Count == 0;
        }
        catch (Exception ex)
        {
            // A widget that cannot read the process table says so, rather than taking the whole
            // surface down from inside a tick.
            Log.Error(Scope, "could not sample the process list", ex);
            _latest = ProcessSnapshot.Empty;
            _failed = true;
        }

        Render();
    }

    /// <summary>
    /// [S-1151] [S-1152] [S-1153] Climb the ladder for the current area: the header and footer on a
    /// tall tile, whole rows of what is left, then the columns the width holds.
    /// </summary>
    private void Lay()
    {
        var measured = _area.IsMeasured;
        var tall = measured && _area.HeightClass >= SizeClass.Expanded;
        var room = new Disclosure(measured ? _area.Width : double.MaxValue, measured ? _area.Height : _config.EffectiveMaxCount * RowHeight);

        var header = tall && room.Take(FluentType.Caption.LineHeight + Fluent.Space1);
        var footer = tall && _config.ShowTotals && room.Take(FluentType.Caption.LineHeight + Fluent.Space1 + Fluent.Space2);

        _rowCount = Math.Max(1, room.TakeRows(RowHeight, _config.EffectiveMaxCount));

        var inner = measured ? _area.Width - (Fluent.Space4 * 2) : double.MaxValue;
        _shown = ProcessTable.Columns(inner, new ProcessColumnOptions(_config.Sort, _config.ShowMemory, _config.ShowThreads, _config.ShowPid));
        _bars = measured && _area.AtLeast(SizeClass.Expanded, SizeClass.Expanded);

        _columns.Visibility = header ? Visibility.Visible : Visibility.Collapsed;
        _footer.Visibility = footer ? Visibility.Visible : Visibility.Collapsed;

        // On a tile too narrow for the title and the ranking label both, the label goes: the single
        // column of numbers left on such a tile is the ranking, so it says itself.
        _caption.Visibility = measured && _area.Width < NarrowBelow ? Visibility.Collapsed : Visibility.Visible;

        if (header)
        {
            ShowColumnLabels();
        }

        Resize(_rowCount);
    }

    /// <summary>Puts the latest sample on screen, reusing the row visuals that are already there.</summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _empty.Visibility = _failed ? Visibility.Visible : Visibility.Collapsed;
        _stack.Visibility = _failed ? Visibility.Collapsed : Visibility.Visible;

        if (_failed)
        {
            return;
        }

        var top = _latest.Top(_rowCount, _config.Sort);
        _shownKeys.Clear();
        var narrow = _area.IsMeasured && _area.Width - (Fluent.Space4 * 2) < NarrowBelow;
        var physical = _bars && _config.Sort == ProcessSort.Memory && MemoryCounters.Read() is { } memory ? (double)memory.Total : 0;

        for (var i = 0; i < _rows.Count; i++)
        {
            var shown = i < top.Count;
            _rows[i].Host.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

            if (shown)
            {
                var usage = top[i];
                double? share = !_bars ? null
                    : _config.Sort == ProcessSort.Cpu ? (usage.CpuKnown ? usage.CpuFraction : null)
                    : physical > 0 ? usage.WorkingSetBytes / physical : null;

                _rows[i].Show(usage, _shown, narrow, share);
                _shownKeys.Add(usage.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        MarkCursor();

        if (_footer.Visibility == Visibility.Visible)
        {
            var totals = _latest.Totals;
            _footer.Text = CpuRules.Totals(totals.Processes, totals.Threads);
        }
    }

    /// <summary>Grows or shrinks the list to <paramref name="count"/> rows; extra rows are collapsed, not destroyed.</summary>
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

    /// <summary>[S-1153] The column header row: same columns and widths as the rows under it.</summary>
    private void BuildColumns()
    {
        // The rows carry the keyboard ring inside their edge, so the headings step in by as much.
        _columns.Margin = new Thickness(Fluent.Space4 + RowFocus.Ring, 0, Fluent.Space4 + RowFocus.Ring, Fluent.Space1);
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] labels = [Str.WidgetsC_Processes_Col_Name.Text(), Str.WidgetsC_Processes_Col_Cpu.Text(), Str.WidgetsC_Processes_Col_Memory.Text(), Str.WidgetsC_Processes_Col_Threads.Text(), Str.WidgetsC_Processes_Col_Pid.Text()];

        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = labels[i];
            label.MaxLines = 1;

            if (i > 0)
            {
                label.TextAlignment = TextAlignment.Right;
                label.MinWidth = ProcessTable.WidthOf(Row.ColumnAt(i));
            }

            Grid.SetColumn(label, i);
            _columnLabels[i] = label;
            _columns.Children.Add(label);
        }

        // Task Manager's gesture: clicking a figure's heading ranks the list on it.
        _columnLabels[1].Tapped += (_, e) => { e.Handled = true; SetSort(ProcessSort.Cpu); };
        _columnLabels[2].Tapped += (_, e) => { e.Handled = true; SetSort(ProcessSort.Memory); };
    }

    private void ShowColumnLabels()
    {
        for (var i = 1; i < _columnLabels.Length; i++)
        {
            var column = Row.ColumnAt(i);
            _columnLabels[i].Visibility = _shown.HasFlag(column) ? Visibility.Visible : Visibility.Collapsed;

            // The ranked column's heading is the one in a stronger grey: the sort, said without a chip.
            var ranked = (column == ProcessColumn.Cpu && _config.Sort == ProcessSort.Cpu)
                || (column == ProcessColumn.Memory && _config.Sort == ProcessSort.Memory);
            _columnLabels[i].Foreground = ranked ? _secondaryBrush : _tertiaryBrush;
            _columnLabels[i].FontWeight = ranked ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>
    /// [S-1566] A key from the shell's keyboard lease: Up/Down/Home/End move a cursor over the
    /// processes shown, which stays on its process as the ranking reshuffles (<see cref="KeyedCursor"/>);
    /// Escape — also sent whenever the lease ends — drops it.
    /// </summary>
    public bool HandleKey(WidgetKey key)
    {
        if (_detached)
        {
            return false;
        }

        var result = KeyedCursor.Move(_shownKeys, _cursor, key);
        _cursor = result.Key;
        MarkCursor();
        return result.Used;
    }

    private void MarkCursor()
    {
        var at = KeyedCursor.IndexOf(_shownKeys, _cursor) ?? -1;

        if (at == _marked)
        {
            return;
        }

        if (_marked >= 0 && _marked < _rows.Count)
        {
            RowFocus.Mark(_rows[_marked].Host, false);
        }

        if (at >= 0 && at < _rows.Count)
        {
            RowFocus.Mark(_rows[at].Host, true);
        }

        _marked = at;
    }

    public void Detach()
    {
        _detached = true;
        Strings.Changed -= OnLanguage;
        _subscription?.Dispose();
        _subscription = null;

        // The rows go with the subscription that fed them, hover handlers and all: a handler left
        // hooked keeps its row alive and re-lights it after teardown.
        foreach (var row in _rows)
        {
            row.Detach();
        }

        _rows.Clear();
        _stack.Children.Clear();
    }

    /// <summary>One row's visuals, kept so a sample can rewrite them instead of replacing them.</summary>
    private sealed class Row
    {
        // A value that could not be read is a colour, not an opacity: the same disabled brush every
        // other unavailable thing on the desktop uses, so it follows the theme.
        private static readonly Brush KnownBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        private static readonly Brush UnknownBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);
        private static readonly Brush AccentBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        private static readonly Brush TrackBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);

        private readonly TextBlock _name = Fluent.Text(FluentType.Body);
        private readonly TextBlock[] _figures = new TextBlock[4];
        private readonly Grid _bar = new() { Height = Fluent.Hairline * 2, VerticalAlignment = VerticalAlignment.Bottom };
        private readonly Rectangle _fill = new();
        private readonly IDisposable _hover;
        private readonly Grid _grid;

        public Row()
        {
            _name.MaxLines = 1;

            var grid = _grid = new Grid
            {
                ColumnSpacing = ProcessTable.Spacing,
                ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
            };

            Grid.SetColumn(_name, 0);
            grid.Children.Add(_name);

            for (var i = 0; i < _figures.Length; i++)
            {
                // Right-aligned tabular figures: a column of numbers that are not aligned on their last
                // digit cannot be compared at a glance, and proportional digits shuffle every sample.
                var figure = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
                figure.TextAlignment = TextAlignment.Right;
                Typography.SetNumeralAlignment(figure, FontNumeralAlignment.Tabular);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(figure, i + 1);
                grid.Children.Add(figure);
                _figures[i] = figure;
            }

            // [S-1153] The share bar: a hairline along the bottom of the row, under the text.
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _fill.Fill = AccentBrush;
            var rest = new Rectangle { Fill = TrackBrush };
            Grid.SetColumn(rest, 1);
            _bar.Children.Add(_fill);
            _bar.Children.Add(rest);
            Grid.SetColumnSpan(_bar, 5);
            grid.Children.Add(_bar);

            // A Fluent compact row: fixed height, the standard inset as its own padding so the row —
            // and the highlight it draws — reaches both edges of the widget, no divider underneath.
            Host = Fluent.Row(grid, compact: true);
            RowFocus.Prepare(Host);
            _hover = Fluent.Hover(Host);
        }

        public Border Host { get; }

        /// <summary>The figure column at header index <paramref name="index"/> (1-based; 0 is the name).</summary>
        public static ProcessColumn ColumnAt(int index) => index switch
        {
            1 => ProcessColumn.Cpu,
            2 => ProcessColumn.Memory,
            3 => ProcessColumn.Threads,
            4 => ProcessColumn.Pid,
            _ => ProcessColumn.None,
        };

        /// <summary>Unhook the hover. Safe to call twice: the handle itself is idempotent.</summary>
        public void Detach() => _hover.Dispose();

        /// <summary>Rewrite this row in place.</summary>
        public void Show(ProcessUsage usage, ProcessColumn columns, bool narrow, double? share)
        {
            _name.Text = ProcessLabels.For(usage);

            // On a narrow tile the gap gives way too, so the name keeps a few more letters.
            _grid.ColumnSpacing = narrow ? Fluent.Space1 : ProcessTable.Spacing;

            for (var i = 0; i < _figures.Length; i++)
            {
                var column = ColumnAt(i + 1);
                var figure = _figures[i];
                var visible = columns.HasFlag(column);
                figure.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

                if (!visible)
                {
                    continue;
                }

                // On a narrow tile a column holding open its widest value takes the name's room for a
                // digit it may never show; the value's own width is enough there.
                figure.MinWidth = narrow ? 0 : ProcessTable.WidthOf(column);
                figure.Text = column switch
                {
                    ProcessColumn.Cpu => usage.CpuText,
                    ProcessColumn.Memory => usage.MemoryText,
                    ProcessColumn.Threads => CpuRules.Count(usage.Threads),
                    _ => usage.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };

                // A row whose CPU could not be read is greyed as well as marked, so "--" reads as
                // "not available" rather than as a figure to compare.
                figure.Foreground = column == ProcessColumn.Cpu && !usage.CpuKnown ? UnknownBrush : KnownBrush;
            }

            var bar = share is { } s && double.IsFinite(s);
            _bar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;

            if (bar)
            {
                var clamped = Math.Clamp(share!.Value, 0, 1);
                _bar.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Star);
                _bar.ColumnDefinitions[1].Width = new GridLength(1 - clamped, GridUnitType.Star);
            }

            ToolTipService.SetToolTip(Host, Str.WidgetsC_Processes_RowTip.Format(("name", usage.Name), ("pid", usage.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
    }
}
