using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.App.Services.Web;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Storage.Streams;

namespace Themia.App.Widgets;

/// <summary>
/// One element of a web page, kept on the desktop and re-rendered on an interval.
/// <para>
/// The widget shows a <b>picture</b> of the element, drawn by an off-screen browser that ran the
/// page (<see cref="PageSnapshot"/>). It is a passive clip, as the original's sandboxed frame was:
/// no scrolling, no links, no focus — a glance, not a browser tab on the wallpaper.
/// </para>
/// <para>
/// Renders happen only while the widget can be seen. A pinned element folded into a stack or a
/// background tab is not re-rendered until it is shown again, and then only if it is stale.
/// </para>
/// </summary>
internal sealed partial class WebElementView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetContextActions
{
    private const string Scope = "web-element";

    /// <summary>Below this height the message drops its icon; below the width, its body size.</summary>
    private const double IconNeedsHeight = 110;

    private const double BodyNeedsWidth = 170;

    private readonly Image _picture = new() { Stretch = Stretch.Fill };
    private readonly Border _fit;
    private readonly Border _messageHost;
    private readonly TextBlock _message = new();
    private readonly FontIcon _messageIcon;
    private ShownWatch? _shown;

    private Ticker? _ticker;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval;
    private WebElementConfig _config = new();
    private IWidgetSurface? _surface;

    /// <summary>The address and selector the picture on screen belongs to.</summary>
    private (string Url, string Selector) _shownKey;

    /// <summary>[S-478] The last good render, kept on screen through a failed refresh.</summary>
    private bool _hasPicture;
    private DateTimeOffset _renderedAt = DateTimeOffset.MinValue;
    private SnapshotStatus? _lastStatus;
    private string? _lastDetail;

    private CancellationTokenSource? _inFlight;
    private int _generation;
    private bool _detached;

    public WebElementView()
    {
        var scaler = new Viewbox
        {
            Child = _picture,
            Stretch = Stretch.Uniform,

            // A pinned element is shown at its own size, or smaller to fit; never blown up past it,
            // which only makes a crisp capture soft.
            StretchDirection = StretchDirection.DownOnly,
        };

        // Top-aligned under the header, as content reads, and rounded to the control radius: a
        // square-cornered rectangle of someone else's page looks pasted onto the card.
        _fit = new Border
        {
            Child = scaler,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, Fluent.Space2),
            Visibility = Visibility.Collapsed,
        };

        var empty = Fluent.EmptyState(Glyphs.WebElement, _message, out _messageIcon);
        _messageHost = new Border { Child = empty, VerticalAlignment = VerticalAlignment.Center };

        Children.Add(_fit);
        Children.Add(_messageHost);

        // [S-462] A passive clip: nothing on it is clickable, so it never steals a click from the
        // card or the desktop.
        _picture.IsHitTestVisible = false;

        SizeChanged += OnSizeChanged;
    }

    // ---- IWidgetView ---------------------------------------------------------------------------

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        _shown ??= new ShownWatch(this, OnShownChanged);
        ApplyConfig(config);
    }

    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as WebElementConfig ?? new WebElementConfig();
        PublishTitle();

        // A different page or element makes what is on screen wrong, not merely old.
        var key = (_config.ResolvedUrl.Url, _config.EffectiveSelector);
        if (key != _shownKey)
        {
            _inFlight?.Cancel();
            _inFlight = null;
            ClearPicture();
            _renderedAt = DateTimeOffset.MinValue;
            _lastStatus = null;
            _lastDetail = null;
            _shownKey = key;
        }

        Resubscribe();
        RefreshIfDue();
        Render();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _shown?.Dispose();
        _shown = null;
        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;
        _inFlight?.Cancel();
        _inFlight = null;
        ClearPicture();
        _ticker = null;
    }

    /// <summary>[S-463] Re-render now, whatever the interval says.</summary>
    public IReadOnlyList<WidgetAction> ContextActions =>
        [new WidgetAction(Glyphs.Refresh, "Refresh", () => RefreshNow())];

    // ---- refreshing ------------------------------------------------------------------------------

    /// <summary>
    /// [S-475] Ticked at the refresh interval through the shared ticker. The tick only asks; whether
    /// a render actually starts depends on visibility and on how old the picture is.
    /// </summary>
    private void Resubscribe()
    {
        var interval = _config.RefreshInterval;

        if (_ticker is null || (_subscription is not null && interval == _subscribedInterval))
        {
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker.Subscribe(interval, RefreshIfDue);
    }

    private void OnShownChanged(bool shown)
    {
        if (shown)
        {
            RefreshIfDue();
        }
        else
        {
            // Hidden mid-render: the browser is let go now rather than at the end of the budget.
            _inFlight?.Cancel();
            _inFlight = null;
        }
    }

    private void RefreshIfDue()
    {
        if (_detached || _shown is not { IsShown: true } || _inFlight is not null)
        {
            return;
        }

        // A little under the interval, so a tick that lands a moment early is not skipped and the
        // picture left a whole extra interval old.
        if (DateTimeOffset.Now - _renderedAt < _config.RefreshInterval - TimeSpan.FromSeconds(30))
        {
            return;
        }

        RefreshNow();
    }

    private void RefreshNow()
    {
        if (_detached)
        {
            return;
        }

        var resolved = _config.ResolvedUrl;
        var selector = _config.EffectiveSelector;

        if (!resolved.IsValid || selector.Length == 0 || _shown is not { IsShown: true }
            || !Uri.TryCreate(resolved.Url, UriKind.Absolute, out var uri))
        {
            Render();
            return;
        }

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;
        Render();

        _ = CaptureAsync(uri, selector, XamlRoot?.RasterizationScale ?? 1, generation, cts);
    }

    private async Task CaptureAsync(Uri uri, string selector, double scale, int generation, CancellationTokenSource cts)
    {
        try
        {
            var result = await PageSnapshot.CaptureAsync(uri, selector, scale, cts.Token).ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            // Stamped whatever the outcome: a page that is down is asked again at the next
            // interval, not on every tick in between.
            _renderedAt = DateTimeOffset.Now;
            _lastStatus = result.Status;
            _lastDetail = result.Detail;

            if (result.Status == SnapshotStatus.Ok && result.Png is { } png)
            {
                await ShowAsync(png, result.CssWidth, result.CssHeight).ConfigureAwait(true);
            }
            else if (result.Status == SnapshotStatus.NotFound)
            {
                // The element is gone from the page: the old picture is no longer what it shows.
                ClearPicture();
            }
            else
            {
                Log.Warn(Scope, $"{uri.Host}: {result.Detail}");
            }

            Render();
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded, hidden or detached; whoever cancelled decides what happens next.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
                Render();
            }

            cts.Dispose();
        }
    }

    private async Task ShowAsync(byte[] png, double width, double height)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            if (_detached)
            {
                return;
            }

            // Sized in DIPs to the element's CSS size: the capture was taken at this display's
            // scale, so one DIP here is one CSS pixel there and the picture is pixel-sharp.
            _picture.Source = bitmap;
            _picture.Width = width;
            _picture.Height = height;
            _hasPicture = true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log.Warn(Scope, "could not show the rendered element", ex);
            _lastStatus = SnapshotStatus.Failed;
            _lastDetail = "The page could not be captured";
        }
    }

    private void ClearPicture()
    {
        _picture.Source = null;
        _hasPicture = false;
    }

    // ---- drawing ---------------------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        var resolved = _config.ResolvedUrl;
        string? message;

        if (!resolved.IsValid || _config.EffectiveSelector.Length == 0)
        {
            message = resolved.Problem is WebUrlProblem.None or WebUrlProblem.Empty
                ? "Pick an element from a web page in settings"
                : WebInput.Explain(resolved.Problem);
        }
        else if (_hasPicture)
        {
            // [S-478] Last good stays up through a failed refresh.
            message = null;
        }
        else if (_lastStatus == SnapshotStatus.NotFound)
        {
            message = "That element is not on the page any more. Pick it again in settings";
        }
        else if (_lastStatus == SnapshotStatus.Failed && _inFlight is null)
        {
            message = _lastDetail ?? "The page could not be rendered";
        }
        else
        {
            message = "Rendering the page…";
        }

        _fit.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
        _messageHost.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

        if (message is null)
        {
            var age = FetchState.Freshness(_renderedAt, DateTimeOffset.Now).ToLowerInvariant();
            ToolTipService.SetToolTip(_fit, _lastStatus == SnapshotStatus.Failed
                ? $"Last refresh failed: {_lastDetail}. Showing the render from {age}."
                : $"Rendered {age}");
            return;
        }

        _message.Text = message;
        _messageIcon.Visibility = ActualHeight > 0 && ActualHeight < IconNeedsHeight ? Visibility.Collapsed : Visibility.Visible;
        _message.Apply(ActualWidth > 0 && ActualWidth < BodyNeedsWidth ? FluentType.Caption : FluentType.Body);
    }

    /// <summary>[S-461] The page's host, unless the user named the widget.</summary>
    private void PublishTitle()
    {
        var title = WebElementRules.Title(_config.Url);
        _surface?.SetHeaderTitle(title.Length == 0 ? null : title);
    }
}
