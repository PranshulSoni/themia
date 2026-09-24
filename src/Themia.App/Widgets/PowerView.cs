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
/// Where the machine's power comes from, which plan it is running, and whether battery saver is on.
/// <para>
/// A readout, not a battery meter. The battery gauge already exists as a meter widget; what this
/// one carries is the state around it — mains or not, which plan, saver on or off — and those are
/// facts rather than samples, so there is no history to draw and nothing to draw it with.
/// </para>
/// <para>
/// <b>A desktop is a complete reading, not a failed one.</b> A machine with no battery still has a
/// power source and a power plan, so it shows them; the battery is what goes, and it goes as a
/// named line rather than as a dimmed number next to an empty bar. Only a machine Windows will not
/// answer about at all gets the empty state.
/// </para>
/// </summary>
internal sealed partial class PowerView : Grid, IWidgetView
{
    /// <summary>
    /// The rows the tile can show, in the order it shows them — which is also the order it drops
    /// them in. The plan comes first because it is the fact the widget is named for; the source is
    /// second because on a laptop the headline already carries the charge; battery saver is last
    /// because it is the one a glance least often needs.
    /// </summary>
    private enum Fact
    {
        Plan = 0,
        Source = 1,
        Saver = 2,
    }

    // Segoe Fluent Icons code points this view needs beyond the shared table.
    private static readonly string PlanGlyph = From(0xE945);   // Lightning
    private static readonly string SaverGlyph = From(0xEBC0);  // BatterySaver

    private readonly TextBlock _title = new();
    private readonly TextBlock _readout = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _reading = new();
    private readonly Grid _header;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly StackPanel _empty;

    private readonly FactRow _source;
    private readonly FactRow _plan;
    private readonly FactRow _saver;
    private readonly List<IDisposable> _hovers = [];

    private readonly Brush _readingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _dimBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);

    private Ticker? _ticker;
    private PowerConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    private PowerSnapshot _snapshot = PowerSnapshot.Unreadable;
    private TypeStep _heroStep = FluentType.Subtitle;
    private TypeStep _captionStep = FluentType.Caption;
    private bool _detached;

    public PowerView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var definition = WidgetCatalog.Definition(WidgetKind.Power);
        _title.Text = definition.Label;

        _header = Fluent.Header(definition.Glyph, _title);
        _header.Margin = Fluent.HeaderMargin;
        SetRow(_header, 0);

        _readout.Text = "--";
        _readout.Foreground = _readingBrush;
        _readout.VerticalAlignment = VerticalAlignment.Bottom;

        _caption.VerticalAlignment = VerticalAlignment.Bottom;
        _caption.Margin = new Thickness(0, 0, 0, Fluent.Space1);

        // A Grid, not a horizontal StackPanel: a StackPanel measures with unbounded width, so the
        // caption never learns its room and never trims — it simply runs off the card.
        _reading.ColumnSpacing = Fluent.Space2;
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _reading.Margin = WidgetLayout.SideInset;
        _reading.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_readout, 0);
        SetColumn(_caption, 1);
        _reading.Children.Add(_readout);
        _reading.Children.Add(_caption);
        SetRow(_reading, 1);

        _source = BuildRow(definition.Glyph, "Source");
        _plan = BuildRow(PlanGlyph, "Plan");
        _saver = BuildRow(SaverGlyph, "Battery saver");

        _list.Children.Add(_plan.Root);
        _list.Children.Add(_source.Root);
        _list.Children.Add(_saver.Root);
        _list.Margin = WidgetLayout.ListInset;
        SetRow(_list, 2);

        var message = new TextBlock { Text = PowerRules.UnreadableMessage };
        _empty = Fluent.EmptyState(definition.Glyph, message, out _);
        _empty.Visibility = Visibility.Collapsed;
        SetRow(_empty, 1);

        Children.Add(_header);
        Children.Add(_reading);
        Children.Add(_list);
        Children.Add(_empty);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// One fact, as a Windows 11 list row: the label on the left, the value on the right, the
    /// standard inset carried as the row's own padding so the hover fill reaches both card edges.
    /// No divider — Windows 11 separates rows with space and hover, not with a rule.
    /// </summary>
    private FactRow BuildRow(string glyph, string label)
    {
        var icon = Fluent.Icon(glyph);
        icon.Margin = new Thickness(0, 0, Fluent.Space3, 0);

        var name = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        name.Text = label;

        var value = Fluent.Text(FluentType.Body);
        value.TextAlignment = TextAlignment.Right;
        value.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        SetColumn(icon, 0);
        SetColumn(name, 1);
        SetColumn(value, 2);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(value);

        var row = Fluent.Row(grid);
        _hovers.Add(Fluent.Hover(row));

        return new FactRow(row, name, value);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as PowerConfig ?? new PowerConfig();

        // Subscribing fires a tick of its own, which reads with the new config already in place.
        if (!Resubscribe())
        {
            Present(_snapshot);
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed — dropping and
    /// remaking it on every config touch made dragging a settings slider stutter, because each
    /// frame rebuilt the shared timer for every widget on the desktop.
    /// </summary>
    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

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

        Present(PowerStatus.Read());
    }

    private void Present(PowerSnapshot snapshot)
    {
        _snapshot = snapshot;

        if (!snapshot.Readable)
        {
            _empty.Visibility = Visibility.Visible;
            _reading.Visibility = Visibility.Collapsed;
            _list.Visibility = Visibility.Collapsed;
            return;
        }

        _empty.Visibility = Visibility.Collapsed;
        _reading.Visibility = Visibility.Visible;
        _list.Visibility = Visibility.Visible;

        // With a battery, the charge is the headline and the source becomes a row. Without one,
        // the source IS the headline — there is nothing else on this machine big enough to be it,
        // and a dash over an empty bar would read as a battery meter that had failed.
        if (snapshot.HasBattery)
        {
            _readout.Text = PowerRules.BatteryReadout(snapshot);
            _readout.Foreground = snapshot.Percent is null ? _dimBrush : _readingBrush;
        }
        else
        {
            _readout.Text = PowerRules.SourceLabel(snapshot);
            _readout.Foreground = _readingBrush;
        }

        _caption.Text = PowerRules.BatteryCaption(snapshot, _config.ShowTimeRemaining);

        _source.Value.Text = PowerRules.SourceLabel(snapshot);
        _plan.Value.Text = PowerRules.PlanLabel(snapshot);
        _saver.Value.Text = PowerRules.SaverLabel(snapshot);

        // A plan Windows would not name is a row that says so, dimmed, rather than an empty one.
        _plan.Value.Foreground = snapshot.PlanName is null ? _dimBrush : _readingBrush;

        ApplyLayout();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout();

    /// <summary>
    /// Decide what fits, then size the type to it.
    /// <para>
    /// Rows are a fixed Fluent height and the tile shows as many of them as it has room for, in
    /// priority order. A row scaled to a fraction of the tile is a web idea; next to Settings it
    /// reads as the wrong application.
    /// </para>
    /// </summary>
    private void ApplyLayout()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        var showHeader = height <= 0 || height >= Fluent.HeaderHidesBelow;
        _header.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;

        var compact = height > 0 && height < Fluent.CompactBelow;
        var rowHeight = compact ? Fluent.RowHeightCompact : Fluent.RowHeight;

        Fluent.SetDensity(_source.Root, compact);
        Fluent.SetDensity(_plan.Root, compact);
        Fluent.SetDensity(_saver.Root, compact);

        // Which facts are worth a row at all. The source is only a row when it is not already the
        // headline, and saver is meaningless on a machine with no battery to save.
        var wanted = new List<Fact>(3);

        if (_config.ShowPlan)
        {
            wanted.Add(Fact.Plan);
        }

        if (_snapshot.HasBattery)
        {
            wanted.Add(Fact.Source);
        }

        if (_config.ShowBatterySaver && _snapshot.HasBattery)
        {
            wanted.Add(Fact.Saver);
        }

        // What is left under the header once the readout has its floor. The rows give way to the
        // number rather than the other way round: the number is what the tile is read for.
        var chrome = (showHeader ? FluentType.BodyStrong.LineHeight + Fluent.Space4 + Fluent.Space2 : Fluent.Space4)
            + FluentType.Subtitle.LineHeight
            + Fluent.Space4;

        var room = height - chrome;
        var fit = height <= 0 ? wanted.Count : (int)Math.Floor(Math.Max(0, room) / rowHeight);
        var shown = Math.Min(wanted.Count, fit);

        // A row is on screen when it is wanted at all and its place in the order is inside what
        // fits. Dropping from the bottom of the list rather than hiding whichever happens to
        // overflow keeps the order stable as the tile is resized.
        bool Fits(Fact fact)
        {
            var index = wanted.IndexOf(fact);
            return index >= 0 && index < shown;
        }

        _source.Root.Visibility = Show(Fits(Fact.Source));
        _plan.Root.Visibility = Show(Fits(Fact.Plan));
        _saver.Root.Visibility = Show(Fits(Fact.Saver));

        // Below this the label and the value cannot both be read, so the label goes: the value is
        // the fact, and the icon already says which fact it is.
        var labels = width <= 0 || width >= 220;
        _source.Label.Visibility = Show(labels);
        _plan.Label.Visibility = Show(labels);
        _saver.Label.Visibility = Show(labels);

        ApplyTypeScale(shown * rowHeight);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Step the headline to the tile: Subtitle, then Title, then larger. A step from the ramp, not
    /// a computed size — text that is not on one of Windows' own sizes looks foreign beside
    /// everything else on the desktop.
    /// </summary>
    private void ApplyTypeScale(double rowsHeight)
    {
        var width = ActualWidth - (Fluent.Space4 * 2);

        var chrome = (_header.Visibility == Visibility.Visible
                ? FluentType.BodyStrong.LineHeight + Fluent.Space4 + Fluent.Space2
                : Fluent.Space4)
            + rowsHeight
            + Fluent.Space4;

        var readingWidth = _caption.Visibility == Visibility.Visible ? width / 2 : width;

        // The worst case this readout can hold, not the string currently in it: fitting to the live
        // text would make "9%" and "Plugged in" pick different steps and the headline would jump
        // size when the charger came out.
        var characters = _snapshot.HasBattery ? 4 : 10;

        var hero = FluentType.Hero(
            readingWidth,
            Math.Min(ActualHeight - chrome, FluentType.Display.LineHeight),
            characters);

        if (hero.FontSize < FluentType.Subtitle.FontSize)
        {
            hero = FluentType.Subtitle;
        }

        var caption = hero.FontSize >= FluentType.Title.FontSize ? FluentType.Body : FluentType.Caption;

        if (hero != _heroStep)
        {
            _heroStep = hero;
            _readout.Apply(hero);
        }

        if (caption != _captionStep)
        {
            _captionStep = caption;
            _caption.Apply(caption);
        }
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;

        // A hover handler left hooked keeps the row it lights alive and re-lights it after the
        // widget has gone.
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;
        _ticker = null;
    }

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);

    /// <summary>A built row, with the two text blocks the presenter writes into.</summary>
    private sealed record FactRow(Border Root, TextBlock Label, TextBlock Value);
}
