using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-630] How much of a Claude subscription's limits is used: the five-hour session and the week,
/// and the per-model weeks when asked for, each a labelled bar.
/// <para>
/// The widget never scrolls ([S-635]): the rows are drawn at the loosest density that fits the
/// box — label, bar and reset line; then without the reset line; then label, bar and percentage on
/// one line — and the labels shorten with the width ("Weekly (all models)", "Weekly", "7d"). The
/// tooltip always carries the whole of it.
/// </para>
/// </summary>
internal sealed partial class ClaudeUsageView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    private const double BarHeight = 6;

    private readonly DevChrome _chrome = new(Glyphs.Gauge);
    private readonly StackPanel _rows = new() { Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space3) };

    private IClaudeUsageSource? _source;
    private IDevFeed<ClaudeUsageReport>? _feed;
    private string? _feedKey;
    private IDisposable? _clock;
    private ClaudeUsageConfig _config = new();
    private string _shape = string.Empty;
    private bool _detached;

    public ClaudeUsageView()
    {
        Children.Add(_rows);
        Children.Add(_chrome.StatePanel);
        SizeChanged += OnSizeChanged;
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
        _source = AppServices.ClaudeUsage;

        if (_source is not null)
        {
            _source.AccountsChanged += OnAccountsChanged;
        }

        ApplyConfig(config);

        // The "resets in" lines count down between fetches.
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), Render);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as ClaudeUsageConfig ?? new ClaudeUsageConfig();
        Reopen();
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _chrome.Fit(args.NewSize.Height);

        if (Shape(args.NewSize.Width, args.NewSize.Height) != _shape)
        {
            Render();
        }
    }

    private IReadOnlyList<UsageBar> Bars()
        => _feed?.Snapshot.Value is { } usage ? ClaudeUsageRules.BuildBars(usage, _config.ShowOpus, _config.ShowSonnet) : [];

    /// <summary>The rows' box: the view less the list's own padding.</summary>
    private (double Width, double Height) Box(double width, double height)
    {
        var padding = _rows.Padding;
        return (
            width > 0 ? Math.Max(0, width - padding.Left - padding.Right) : double.NaN,
            height > 0 ? Math.Max(0, height - padding.Top - padding.Bottom) : double.NaN);
    }

    private string Shape(double width, double height)
    {
        var bars = Bars();
        var (w, h) = Box(width, height);
        var density = ClaudeUsageRules.Density(h, bars.Count);
        return $"{density}{ClaudeUsageRules.LabelSize(bars, w, density)}";
    }

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        _rows.Children.Clear();

        var now = DateTimeOffset.Now;
        var snapshot = _feed?.Snapshot ?? DevFeedSnapshot<ClaudeUsageReport>.Empty;
        IReadOnlyList<string> chosen = string.IsNullOrWhiteSpace(_config.AccountId) ? [] : [_config.AccountId];
        var screen = DevScreens.Decide("Claude", _source?.Accounts ?? [], chosen, single: true, snapshot.Outcomes, snapshot.Value is not null, string.Empty, now);

        _shape = Shape(ActualWidth, ActualHeight);

        if (!_chrome.Show(screen, ActualHeight))
        {
            _rows.Visibility = Visibility.Collapsed;
            return;
        }

        _rows.Visibility = Visibility.Visible;

        var bars = Bars();
        var (width, height) = Box(ActualWidth, ActualHeight);
        var density = ClaudeUsageRules.Density(height, bars.Count);
        var size = ClaudeUsageRules.LabelSize(bars, width, density);
        _rows.Spacing = ClaudeUsageRules.RowGap(density);

        // One-line rows hold far less than the box on a wide, short tile: centred, not hanging
        // off the top with the space all below.
        _rows.VerticalAlignment = density == UsageDensity.Tight ? VerticalAlignment.Center : VerticalAlignment.Stretch;

        // Micro: the rows share whatever height is left, so any height still fits.
        var microHeight = density == UsageDensity.Micro && double.IsFinite(height) && bars.Count > 0 ? height / bars.Count : double.NaN;

        foreach (var bar in bars)
        {
            var row = density is UsageDensity.Full or UsageDensity.Compact
                ? Stacked(bar, size, density == UsageDensity.Full, now)
                : Inline(bar, size);

            if (double.IsFinite(microHeight))
            {
                row.Height = microHeight;
            }

            var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);
            var reset = ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now);
            ToolTipService.SetToolTip(row, reset.Length > 0 ? $"{bar.Label}: {Percent(percent)}, {reset}" : $"{bar.Label}: {Percent(percent)}");
            _rows.Children.Add(row);
        }
    }

    /// <summary>Label and percentage over the bar, and — loosest — the reset line under it.</summary>
    private static Grid Stacked(UsageBar bar, UsageLabelSize size, bool withReset, DateTimeOffset now)
    {
        var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);

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

        var value = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        value.Text = Percent(percent);
        value.TextTrimming = TextTrimming.None;
        value.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

        var track = Track(percent);
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
            // height and the bars line up.
            var reset = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            reset.Text = ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, brief: size != UsageLabelSize.Full) is { Length: > 0 } text ? text : " ";
            SetRow(reset, 2);
            SetColumnSpan(reset, 2);
            grid.Children.Add(reset);
        }

        return grid;
    }

    /// <summary>Label, bar and percentage on one line.</summary>
    private static Grid Inline(UsageBar bar, UsageLabelSize size)
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

        var track = Track(percent);
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
        return grid;
    }

    /// <summary>[S-631] The bar: a track, and a fill in the severity's colour as wide as the percentage.</summary>
    private static Grid Track(double percent)
    {
        var fill = new Border
        {
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = ClaudeUsageRules.Severity(percent) switch
            {
                UsageSeverity.Critical => DevBrushes.Critical,
                UsageSeverity.Warn => DevBrushes.Caution,
                _ => FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue),
            },
        };

        var track = new Grid
        {
            Height = BarHeight,
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) },
            },
        };

        if (percent > 0)
        {
            track.Children.Add(fill);
        }

        return track;
    }

    private static string Percent(double percent) => Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => [new(Glyphs.Refresh, "Refresh", () => _feed?.Refresh())];

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        SizeChanged -= OnSizeChanged;
        _chrome.Detach();
        _rows.Children.Clear();

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
