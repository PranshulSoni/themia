using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-630] How much of a Claude subscription's limits is used: the five-hour session and the week,
/// and the per-model weeks when asked for, each a labelled bar.
/// <para>
/// The widget never scrolls ([S-635]) and uses the whole tile ([S-790]..[S-795]): the rows are drawn
/// at the loosest density that fits the box, and the height they leave over goes to thicker bars, a
/// larger percentage and air between rows. A wide strip adds a reset column; a panel adds the
/// reset's clock time, a pace tick and "ahead of pace"; a Huge panel splits into a session gauge
/// and the weekly bars; an XS tile with two bars shows the session as one large reading. The
/// tooltip always carries the whole of a row.
/// </para>
/// </summary>
internal sealed partial class ClaudeUsageView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions, IWidgetSizeAware
{
    /// <summary>The inset of the content: the card's text inset either side, a little above, more below.</summary>
    private static readonly Thickness Inset = new(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space3);

    /// <summary>[S-794] The gauge's stroke.</summary>
    private const double GaugeStroke = 10;

    private readonly DevChrome _chrome = new(Glyphs.Gauge);
    private readonly Grid _body = new() { Padding = Inset };
    private readonly TextBlock _footer;

    private IClaudeUsageSource? _source;
    private IDevFeed<ClaudeUsageReport>? _feed;
    private string? _feedKey;
    private IDisposable? _clock;
    private ClaudeUsageConfig _config = new();
    private WidgetArea _area;
    private bool _detached;

    public ClaudeUsageView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _footer = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        _footer.Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space3);
        _footer.Visibility = Visibility.Collapsed;
        SetRow(_footer, 1);

        SetRowSpan(_chrome.StatePanel, 2);
        Children.Add(_body);
        Children.Add(_footer);
        Children.Add(_chrome.StatePanel);
    }

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _chrome.HeaderTrailing;

    public void Bind(IWidgetSurface surface)
    {
        // ignore: the catalogue's name is the right title; the card's own label still wins.
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        Strings.Changed += OnLanguage;
        _source = AppServices.ClaudeUsage;

        if (_source is not null)
        {
            _source.AccountsChanged += OnAccountsChanged;
        }

        ApplyConfig(config);

        // The "resets in" lines, the pace and "updated" count on between fetches.
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), Render);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ClaudeUsageConfig ?? new ClaudeUsageConfig();
        Reopen();
        Render();
    }

    /// <inheritdoc />
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        _chrome.Fit(area.Height);
        Render();
    }

    private void Reopen()
    {
        if (_source is null)
        {
            return;
        }

        var key = $"{_config.AccountId}|{_config.RefreshInterval.TotalMinutes}";
        if (_feed is not null && key == _feedKey)
        {
            return;
        }

        CloseFeed();
        _feedKey = key;
        _feed = _source.Open(_config.AccountId, _config.RefreshInterval);
        _feed.Updated += Render;
    }

    private void CloseFeed()
    {
        if (_feed is null)
        {
            return;
        }

        _feed.Updated -= Render;
        _feed.Dispose();
        _feed = null;
        _feedKey = null;
    }

    private void OnAccountsChanged() => Render();

    private IReadOnlyList<UsageBar> Bars()
        => _feed?.Snapshot.Value is { } usage ? ClaudeUsageRules.BuildBars(usage, _config.ShowOpus, _config.ShowSonnet, Strings.Current) : [];

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _body.Children.Clear();
        _body.ColumnDefinitions.Clear();

        var now = DateTimeOffset.Now;
        var snapshot = _feed?.Snapshot ?? DevFeedSnapshot<ClaudeUsageReport>.Empty;
        IReadOnlyList<string> chosen = string.IsNullOrWhiteSpace(_config.AccountId) ? [] : [_config.AccountId];
        var accounts = _source?.Accounts ?? [];
        var screen = DevScreens.Decide("Claude", accounts, chosen, single: true, snapshot.Outcomes, snapshot.Value is not null, string.Empty, now, Strings.Current);
        var height = _area.IsMeasured ? _area.Height : double.NaN;

        if (!_chrome.Show(screen, double.IsFinite(height) ? height : 0))
        {
            _body.Visibility = Visibility.Collapsed;
            _footer.Visibility = Visibility.Collapsed;
            return;
        }

        _body.Visibility = Visibility.Visible;

        var bars = Bars();
        var gauge = ClaudeUsageRules.GaugePlace(_area, bars.Count);
        var twoPanels = gauge == UsageGaugePlace.Beside;

        // [S-795] When the numbers were read, from Regular height up; the account too on a panel.
        var footer = ClaudeUsageRules.Footer(_area) && snapshot.At is { } at;
        if (footer)
        {
            var who = twoPanels ? accounts.FirstOrDefault(a => chosen.Count == 0 || a.Id == chosen[0])?.Label : null;
            var updated = now - snapshot.At!.Value < TimeSpan.FromMinutes(1)
                ? Str.WidgetsA_Claude_UpdatedJustNow.Text()
                : Str.Github_Updated.Format(("time", LocaleFormat.Relative(Strings.Current, snapshot.At!.Value, now, RelativeStyle.Short)));
            _footer.Text = who is { Length: > 0 } ? $"{updated} · {who}" : updated;
        }

        _footer.Visibility = footer ? Visibility.Visible : Visibility.Collapsed;

        // The rows' box: the slot less the inset and the footer under it.
        var width = _area.IsMeasured ? Math.Max(0, _area.Width - Inset.Left - Inset.Right) : double.NaN;
        var box = double.IsFinite(height)
            ? Math.Max(0, height - Inset.Top - Inset.Bottom - (footer ? FluentType.Caption.LineHeight + Fluent.Space3 : 0))
            : double.NaN;

        if (ClaudeUsageRules.SessionHero(_area, bars.Count))
        {
            Hero(bars, width, box);
        }
        else if (gauge != UsageGaugePlace.None)
        {
            Panels(bars, width, box, now, gauge);
        }
        else
        {
            _body.Children.Add(Rows(bars, width, box, now, extras: _area.WidthClass >= SizeClass.Expanded));
        }
    }

    /// <summary>
    /// [S-635]/[S-790]/[S-791] The bars at the loosest density that fits, the leftover height spent on
    /// them. <paramref name="extras"/> adds the reset's clock time, the pace and the pace tick.
    /// </summary>
    private static StackPanel Rows(IReadOnlyList<UsageBar> bars, double width, double height, DateTimeOffset now, bool extras)
    {
        var density = ClaudeUsageRules.Density(height, bars.Count);
        var resetColumn = ClaudeUsageRules.ResetColumn(width, density);
        var size = ClaudeUsageRules.LabelSize(bars, width, density, resetColumn);
        var spread = ClaudeUsageRules.Spread(height, bars.Count, density);

        var rows = new StackPanel
        {
            Spacing = spread.Gap,

            // One-line rows hold far less than the box on a wide, short tile, and stacked ones may
            // leave slack past the widest gap: centred, not hanging off the top.
            VerticalAlignment = density == UsageDensity.Tight || spread.Slack > 0 ? VerticalAlignment.Center : VerticalAlignment.Stretch,
        };

        // Micro: the rows share whatever height is left, so any height still fits.
        var microHeight = density == UsageDensity.Micro && double.IsFinite(height) && bars.Count > 0 ? height / bars.Count : double.NaN;

        foreach (var bar in bars)
        {
            FrameworkElement row = density is UsageDensity.Full or UsageDensity.Compact
                ? Stacked(bar, size, density == UsageDensity.Full, now, spread, extras)
                : Inline(bar, size, resetColumn, now);

            if (double.IsFinite(microHeight))
            {
                row.Height = microHeight;
            }

            ToolTipService.SetToolTip(row, Tooltip(bar, now));
            rows.Children.Add(row);
        }

        return rows;
    }

    private static string Tooltip(UsageBar bar, DateTimeOffset now)
    {
        var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);
        var parts = new List<string> { Str.WidgetsA_Claude_BarValue.Format(("label", bar.Label), ("value", Percent(percent))) };

        if (ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, Strings.Current) is { Length: > 0 } reset)
        {
            parts.Add($"{reset} ({ClaudeUsageRules.ResetClock(bar.Window.ResetsAt!.Value, now, TimeZoneInfo.Local, Strings.Current)})");
        }

        if (ClaudeUsageRules.Elapsed(ClaudeUsageRules.WindowLength(bar), bar.Window.ResetsAt, now) is { } elapsed)
        {
            parts.Add($"{ClaudeUsageRules.PaceText(ClaudeUsageRules.Pace(percent, elapsed), Strings.Current)} ({WindowGone(elapsed)})");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Label and percentage over the bar, and — loosest — the reset line under it.</summary>
    private static Grid Stacked(UsageBar bar, UsageLabelSize size, bool withReset, DateTimeOffset now, UsageSpread spread, bool extras)
    {
        var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);
        var elapsed = ClaudeUsageRules.Elapsed(ClaudeUsageRules.WindowLength(bar), bar.Window.ResetsAt, now);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = Fluent.Text(FluentType.Body);
        label.Text = ClaudeUsageRules.Label(bar, size);
        label.VerticalAlignment = VerticalAlignment.Bottom;

        var value = Fluent.Text(spread.Value, "TextFillColorSecondaryBrush");
        value.Text = Percent(percent);
        value.TextTrimming = TextTrimming.None;
        value.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
        value.VerticalAlignment = VerticalAlignment.Bottom;

        var track = Track(percent, spread.Bar, extras ? elapsed : null);
        track.Margin = new Thickness(0, Fluent.Space1, 0, withReset ? Fluent.Space1 : 0);

        SetColumn(value, 1);
        SetRow(track, 1);
        SetColumnSpan(track, 2);
        grid.Children.Add(label);
        grid.Children.Add(value);
        grid.Children.Add(track);

        if (withReset)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // [S-632] The line keeps its slot when a window has no reset, so every row is the same
            // height and the bars line up. [S-793] On a panel it also says when, and the pace.
            var reset = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            reset.Text = ResetLine(bar, size, now, percent, elapsed, extras) is { Length: > 0 } text ? text : " ";
            SetRow(reset, 2);
            SetColumnSpan(reset, 2);
            grid.Children.Add(reset);
        }

        return grid;
    }

    private static string ResetLine(UsageBar bar, UsageLabelSize size, DateTimeOffset now, double percent, double? elapsed, bool extras)
    {
        var reset = ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, Strings.Current, brief: size != UsageLabelSize.Full && !extras);
        if (!extras || reset.Length == 0)
        {
            return reset;
        }

        var line = $"{reset} · {ClaudeUsageRules.ResetClock(bar.Window.ResetsAt!.Value, now, TimeZoneInfo.Local, Strings.Current)}";
        return elapsed is { } e ? $"{line} · {ClaudeUsageRules.PaceText(ClaudeUsageRules.Pace(percent, e), Strings.Current)}" : line;
    }

    /// <summary>
    /// The reset column is as wide as the longest brief reset the language can write ("6d 23h",
    /// "6 Tg. 23 h"), so the columns line up row to row and German is not cut off at the left.
    /// </summary>
    private static int ResetColumnChars(DateTimeOffset now)
        => Math.Max(
            ClaudeUsageRules.ResetLabel(now + new TimeSpan(6, 23, 0, 30), now, Strings.Current, brief: true).Length,
            ClaudeUsageRules.ResetLabel(now + new TimeSpan(0, 23, 59, 30), now, Strings.Current, brief: true).Length);

    /// <summary>Label, bar and percentage on one line; on a wide strip the reset after them ([S-791]).</summary>
    private static Grid Inline(UsageBar bar, UsageLabelSize size, bool resetColumn, DateTimeOffset now)
    {
        var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);

        var grid = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        var label = Fluent.Text(FluentType.Caption);
        label.Text = ClaudeUsageRules.Label(bar, size);
        label.VerticalAlignment = VerticalAlignment.Center;

        var track = Track(percent, ClaudeUsageRules.BarMin, elapsed: null);
        track.VerticalAlignment = VerticalAlignment.Center;

        var value = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        value.Text = Percent(percent);
        value.TextTrimming = TextTrimming.None;
        value.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(track, 1);
        SetColumn(value, 2);
        grid.Children.Add(label);
        grid.Children.Add(track);
        grid.Children.Add(value);

        if (resetColumn)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FluentType.EstimateWidth(FluentType.Caption, ResetColumnChars(now))) });

            var reset = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            reset.Text = ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, Strings.Current, brief: true);
            reset.TextTrimming = TextTrimming.None;
            reset.VerticalAlignment = VerticalAlignment.Center;
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            SetColumn(reset, 3);
            grid.Children.Add(reset);
        }

        return grid;
    }

    /// <summary>
    /// [S-631] The bar: a track, and a fill in the severity's colour as wide as the percentage.
    /// [S-793] With <paramref name="elapsed"/>, a tick where usage would be if spent evenly.
    /// </summary>
    private static Grid Track(double percent, double thickness, double? elapsed)
    {
        var track = new Grid
        {
            Height = thickness,
            CornerRadius = new CornerRadius(thickness / 2),
            Background = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) },
            },
        };

        if (percent > 0)
        {
            track.Children.Add(new Border { CornerRadius = new CornerRadius(thickness / 2), Background = SeverityBrush(percent) });
        }

        if (elapsed is not { } share)
        {
            return track;
        }

        var host = new Grid { Height = thickness };
        host.Children.Add(track);

        var marks = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) },
            },
        };

        var tick = new Border
        {
            Width = Fluent.Hairline * 2,
            Background = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-Fluent.Hairline, 0, 0, 0),
        };
        ToolTipService.SetToolTip(tick, Str.WidgetsA_Claude_PaceTick.Text());
        SetColumn(tick, 1);
        marks.Children.Add(tick);
        host.Children.Add(marks);
        return host;
    }

    private static Brush SeverityBrush(double percent) => ClaudeUsageRules.Severity(percent) switch
    {
        UsageSeverity.Critical => DevBrushes.Critical,
        UsageSeverity.Warn => DevBrushes.Caution,
        _ => FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
    };

    private static Brush SeverityText(double percent) => ClaudeUsageRules.Severity(percent) switch
    {
        UsageSeverity.Critical => DevBrushes.Critical,
        UsageSeverity.Warn => DevBrushes.Caution,
        _ => FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White),
    };

    private static string Percent(double percent) => LocaleFormat.Percent(Strings.Current, Math.Round(percent));

    /// <summary>"40% of the window gone".</summary>
    private static string WindowGone(double elapsed)
        => Str.WidgetsA_Claude_WindowGone.Format(("percent", Percent(elapsed * 100)));

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => [new(Glyphs.Refresh, Str.Common_Refresh.Text(), () => _feed?.Refresh())];

    /// <summary>[S-1109] Everything on the tile is worded from the snapshot in hand; nothing is refetched.</summary>
    private void OnLanguage(Locale locale) => Render();

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        Strings.Changed -= OnLanguage;

        _detached = true;
        _chrome.Detach();
        _body.Children.Clear();

        _clock?.Dispose();
        _clock = null;

        CloseFeed();

        if (_source is not null)
        {
            _source.AccountsChanged -= OnAccountsChanged;
            _source = null;
        }
    }
}
