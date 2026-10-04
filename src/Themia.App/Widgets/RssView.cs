using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Web;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// Headlines from one feed, as a Windows 11 list.
/// <para>
/// Everything on this widget was written by somebody else and fetched over the network: a title, a
/// summary, a link. All of it is <b>text</b> here and nothing else — set as
/// <see cref="TextBlock.Text"/>, never as markup, never as a path, never as a command. The parser
/// strips tags before any of it arrives and refuses a link that is not http or https, so the worst
/// a hostile feed can do to this widget is put rude words in it.
/// </para>
/// <para>
/// Double-click to open, not single. A widget sits under the pointer's path across the desktop, and
/// one stray click sending a browser to a stranger's URL is how a glanceable surface becomes a
/// hazard.
/// </para>
/// </summary>
internal sealed partial class RssView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware
{
    private const string Scope = "rss";

    /// <summary>Under this width the source line is dropped; the headline is what the widget is for.</summary>
    private const double SourceNeedsWidth = 220;

    private readonly TextBlock _freshness = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage = new();

    private readonly List<IDisposable> _hovers = [];

    private readonly Brush _freshBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _staleBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);

    private readonly RefreshSchedule _schedule = new(Scope, "feed");

    private Ticker? _ticker;
    private RssConfig _config = new();

    /// <summary>The card this view draws on, which owns the header it names.</summary>
    private IWidgetSurface? _surface;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;
    private int _generation;

    private Fetched<FeedDocument>? _feed;
    private bool _showSource = true;
    private bool _detached;

    public RssView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        _emptyState = Fluent.EmptyState(Glyphs.Rss, _emptyMessage, out _);
        _emptyState.Visibility = Visibility.Collapsed;

        Children.Add(_scroll);
        Children.Add(_emptyState);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The age of the feed, in the card's header. Built in the constructor because the card reads
    /// this once, when it builds that header.
    /// </summary>
    public FrameworkElement? HeaderTrailing => _freshness;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as RssConfig ?? new RssConfig();
        var previous = _config;
        _config = next;
        PublishTitle();

        // Only the address and the cap change what comes back. Toggling the preview or the source
        // line redraws what is already in hand — it must not fetch the feed again.
        var needsFetch = previous.FeedUrl != next.FeedUrl
            || previous.EffectiveMaxItems != next.EffectiveMaxItems;

        if (needsFetch)
        {
            _feed = null;
            _schedule.Reset();
        }

        if (Resubscribe())
        {
            return;
        }

        if (needsFetch)
        {
            Reload();
        }
        else
        {
            Render();
        }
    }

    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, OnTick);
        return _subscription is not null;
    }

    private void OnTick()
    {
        if (_detached)
        {
            return;
        }

        if (_schedule.MayFetch(DateTimeOffset.Now))
        {
            Reload();
            return;
        }

        // Backed off, but "4m ago" has to become "5m ago" whether or not the last fetch worked.
        Render();
    }

    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        var resolved = _config.ResolvedUrl;

        if (!resolved.IsValid)
        {
            // An empty box is "not set up"; anything else is refused, and the reason is kept so the
            // widget can say which rule the address broke rather than "that did not work".
            var status = resolved.Problem == WebUrlProblem.Empty ? FetchStatus.NotConfigured : FetchStatus.Refused;
            _schedule.Record(status, _config.TickInterval, WebInput.Explain(resolved.Problem), DateTimeOffset.Now);
            Render();
            return;
        }

        var url = resolved.Url;
        var cap = _config.EffectiveMaxItems;

        _feed ??= FeedService.Peek(url, cap);

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        Render();
        _ = LoadAsync(url, cap, generation, cts);
    }

    private async Task LoadAsync(string url, int cap, int generation, CancellationTokenSource cts)
    {
        try
        {
            var result = await FeedService
                .LoadAsync(url, cap, _config.TickInterval, cts.Token)
                .ConfigureAwait(true);

            // A late reply for a feed the widget has since left. Dropped rather than drawn.
            if (_detached || generation != _generation)
            {
                return;
            }

            _schedule.Record(result.Status, _config.TickInterval, result.Detail, DateTimeOffset.Now);

            if (result.Value is not null)
            {
                _feed = result;
            }
            else if (_feed is { } held)
            {
                // The articles stay on screen; the header says how old they are and the tooltip
                // says what went wrong. Hiding a failed refresh outright is what let the original
                // sit two days stale with nothing on screen to show for it.
                _feed = held.Stale(result.Status, result.Detail);
            }

            Render();
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer request, which will draw in this one's place.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
            }

            cts.Dispose();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var source = e.NewSize.Width >= SourceNeedsWidth;

        if (source == _showSource)
        {
            return;
        }

        _showSource = source;
        Render();
    }

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var now = DateTimeOffset.Now;
        var held = _feed;
        var document = held?.Value;

        if (document is null || document.Entries.Count == 0)
        {
            var status = _schedule.Status;

            _emptyMessage.Text = status switch
            {
                FetchStatus.NotConfigured => "Add a feed address in settings",
                FetchStatus.Refused => WebInput.Explain(_config.ResolvedUrl.Problem),
                FetchStatus.Empty => "This feed has no articles",
                _ => FetchState.Message(status),
            };

            _emptyState.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            _freshness.Text = string.Empty;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        foreach (var entry in document.Entries)
        {
            _list.Children.Add(BuildRow(entry, now));
        }

        UpdateFreshness(held!, now);
    }

    private void UpdateFreshness(Fetched<FeedDocument> held, DateTimeOffset now)
    {
        var failed = FetchState.IsFailure(held.Status);
        var stale = failed || FetchState.IsStale(held.At, now, _config.TickInterval);

        _freshness.Text = FetchState.Freshness(held.At, now);
        _freshness.Foreground = stale ? _staleBrush : _freshBrush;

        ToolTipService.SetToolTip(
            _freshness,
            failed
                ? $"Last refresh failed: {FetchState.Message(held.Status)}. Showing articles from {FetchState.Freshness(held.At, now).ToLowerInvariant()}."
                : $"Refreshed {FetchState.Freshness(held.At, now).ToLowerInvariant()}");
    }

    /// <summary>
    /// One article. A two- or three-line row rather than a fixed Fluent list row, because a
    /// headline is the one thing here that legitimately wraps: trimming it to one line is what
    /// turns a feed reader into a list of sentence fragments.
    /// </summary>
    private FrameworkElement BuildRow(FeedEntry entry, DateTimeOffset now)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, Fluent.Space2, 0, Fluent.Space2) };

        if (_showSource && _config.ShowSource && Host(entry.Link) is { Length: > 0 } host)
        {
            var source = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            source.Text = host;
            stack.Children.Add(source);
        }

        var headline = Fluent.Text(FluentType.Body);
        headline.Text = entry.Title.Length > 0 ? entry.Title : "(untitled)";
        headline.TextWrapping = TextWrapping.Wrap;
        headline.MaxLines = 2;
        stack.Children.Add(headline);

        if (_config.ShowPreview && entry.Summary.Length > 0)
        {
            var preview = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            preview.Text = entry.Summary;
            preview.TextWrapping = TextWrapping.Wrap;
            preview.MaxLines = 2;
            stack.Children.Add(preview);
        }

        if (_config.ShowDate && entry.Published is { } published)
        {
            var when = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            when.Text = FetchState.Freshness(published, now);
            stack.Children.Add(when);
        }

        // Fluent.Row for the padding and the hover fill, with the minimum height dropped: the row
        // is as tall as the headline needs and no taller.
        var row = Fluent.Row(stack);
        row.MinHeight = 0;
        _hovers.Add(Fluent.Hover(row));

        if (entry.Link.Length > 0)
        {
            row.DoubleTapped += (_, _) => Open(entry.Link);
            ToolTipService.SetToolTip(row, entry.Link);
        }

        return row;
    }

    /// <summary>
    /// The site an item came from, as a bare host name.
    /// <para>
    /// Parsed rather than pattern-matched, and only from a link the parser already accepted, so
    /// what ends up on screen is <see cref="Uri.Host"/> — a value with no path, no query and no
    /// room for anything a feed author wrote by hand.
    /// </para>
    /// </summary>
    private static string Host(string link)
    {
        if (link.Length == 0 || !Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// Hand a link to the shell.
    /// <para>
    /// The address was checked when it was parsed — http or https, a real host, no credentials —
    /// and it is checked once more here, because this is the call that actually launches something
    /// and the gate belongs beside it rather than three files away.
    /// </para>
    /// </summary>
    private static void Open(string link)
    {
        var checkedUrl = WebInput.Url(link);

        if (!checkedUrl.IsValid)
        {
            Log.Warn(Scope, $"refused to open a feed link: {WebInput.Explain(checkedUrl.Problem)}");
            return;
        }

        try
        {
            // using: ShellExecute hands back a process handle even when nothing waits on it, and
            // leaking one per click on a surface that lives for weeks adds up.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = checkedUrl.Url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No browser associated, or the shell refused. The desktop stays up and the log says
            // why nothing opened.
            Log.Warn(Scope, "could not open a feed link", ex);
        }
    }

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        ClearRows();

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        _inFlight?.Cancel();
        _inFlight = null;

        _ticker = null;
    }
    /// <summary>
    /// Tell the card what this widget is about. The catalogue can only say "Weather" or
    /// "Folder"; the widget knows which place, which folder, which ticker — and on a desktop
    /// holding three of them, that is the only thing telling them apart.
    /// </summary>
    private void PublishTitle()
    {
        var title = _config.Title;
        _surface?.SetHeaderTitle(string.IsNullOrWhiteSpace(title) ? null : title);
    }

}
