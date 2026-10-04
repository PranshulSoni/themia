using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.App.Services.Web;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;
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
/// [S-1316]/[S-1317] The picture uses the room it is given: a small element is blown up (captured
/// larger, so it stays sharp) as the widget grows; a large one in a small widget shows its top-left
/// part at its own size under a soft fade rather than all of it shrunk to a smudge.
/// </para>
/// <para>
/// Renders happen only while the widget can be seen. A pinned element folded into a stack or a
/// background tab is not re-rendered until it is shown again, and then only if it is stale. The
/// last good render is kept across restarts ([S-1319]) and shown, with its age, until a new one
/// replaces it.
/// </para>
/// </summary>
internal sealed partial class WebElementView : Grid, IWidgetView, IWidgetSurfaceAware, IWidgetContextActions, IWidgetHeaderContent, IWidgetSizeAware
{
    private const string Scope = "web-element";

    /// <summary>Below this height the message drops its icon; below the width, its body size.</summary>
    private const double IconNeedsHeight = 110;

    private const double BodyNeedsWidth = 170;

    /// <summary>The depth of the fade over a cut-off edge, and how many steps draw it.</summary>
    private const double FadeDepth = 24;

    private const int FadeSteps = 12;

    /// <summary>The picture, cropped and faded by copies of one image (one bitmap, shared by all of them).</summary>
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Border _fit;
    private readonly Border _messageHost;
    private readonly TextBlock _message = new();
    private readonly FontIcon _messageIcon;

    private readonly TextBlock _freshness = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _footer;
    private readonly TextBlock _footerHost = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _footerAge = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _footerFailed;
    private readonly TextBlock _footerFailedText = Fluent.Text(FluentType.Caption);

    private readonly Brush _freshBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _staleBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);

    private ShownWatch? _shown;
    private Ticker? _ticker;
    private IDisposable? _subscription;
    private WebElementConfig _config = new();
    private IWidgetSurface? _surface;
    private WidgetArea _area;

    /// <summary>The address and selector the picture on screen belongs to.</summary>
    private (string Url, string Selector) _shownKey;

    /// <summary>[S-478] The last good render, kept on screen through a failed refresh.</summary>
    private BitmapImage? _bitmap;
    private double _cssWidth;
    private double _cssHeight;

    /// <summary>[S-1317] The zoom the picture on screen was captured at, and the box it was captured for.</summary>
    private double _capturedZoom = 1;
    private Size _capturedFor;

    /// <summary>[S-1319] The picture came from the saved cache at launch: stale until a render replaces it.</summary>
    private bool _restored;

    private DateTimeOffset _renderedAt = DateTimeOffset.MinValue;
    private SnapshotStatus? _lastStatus;
    private string? _lastDetail;

    /// <summary>What the canvas was last built for, so a layout pass that changes nothing rebuilds nothing.</summary>
    private (BitmapImage? Bitmap, ElementPlacement Placement) _drawn;

    private CancellationTokenSource? _inFlight;
    private int _generation;
    private bool _detached;

    public WebElementView()
    {
        _fit = new Border
        {
            Child = _canvas,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Visibility = Visibility.Collapsed,
        };

        var empty = Fluent.EmptyState(Glyphs.WebElement, _message, out _messageIcon);
        _messageHost = new Border { Child = empty, VerticalAlignment = VerticalAlignment.Center };

        // [S-1318] The XL footer: where the picture is from, how old it is, and a failed refresh.
        var warning = Fluent.Icon(Glyphs.Warning, "SystemFillColorCautionBrush", FluentType.Caption.FontSize);
        _footerFailedText.Foreground = _staleBrush;
        _footerFailedText.Text = Str.WidgetsC_WebElement_LastRefreshFailed.Text();
        _footerFailed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1, Children = { warning, _footerFailedText } };
        _footerHost.TextTrimming = TextTrimming.CharacterEllipsis;
        _footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space3,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, Fluent.Space2),
            Visibility = Visibility.Collapsed,
            Children = { _footerHost, _footerAge, _footerFailed },
        };

        Children.Add(_fit);
        Children.Add(_messageHost);
        Children.Add(_footer);
    }

    /// <summary>[S-1318] The render's age, trailing the header, in the caution colour when stale or failed.</summary>
    public FrameworkElement? HeaderTrailing => _freshness;

    // ---- IWidgetView ---------------------------------------------------------------------------

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        Strings.Changed -= OnLanguage;
        Strings.Changed += OnLanguage;
        _shown ??= new ShownWatch(this, OnShownChanged);
        ApplyConfig(config);

        // One minute: the age text moves on, and a render is started when one is due. The tick only
        // asks; whether a render happens depends on visibility and the picture's age.
        _subscription ??= ticker.Subscribe(TimeSpan.FromMinutes(1), OnTick);
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
            _ = RestoreAsync(key);
        }

        Lay();
        RefreshIfDue();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        Strings.Changed -= OnLanguage;
        _shown?.Dispose();
        _shown = null;
        _subscription?.Dispose();
        _subscription = null;
        _inFlight?.Cancel();
        _inFlight = null;
        ClearPicture();
        _shownKey = default;
        _ticker = null;
    }

    /// <summary>[S-463] Re-render now, whatever the interval says.</summary>
    public IReadOnlyList<WidgetAction> ContextActions =>
        [new WidgetAction(Glyphs.Refresh, Str.Common_Refresh.Text(), RefreshNow)];

    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
        RefreshIfDue();
    }

    // ---- refreshing ------------------------------------------------------------------------------

    private void OnTick()
    {
        UpdateAge();
        RefreshIfDue();
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

    /// <summary>
    /// [S-475] Start a render when one is due: the picture is older than the interval, came from the
    /// saved cache ([S-1319]), or is drawn much larger than it was captured ([S-1317]).
    /// </summary>
    private void RefreshIfDue()
    {
        if (_detached || _shown is not { IsShown: true } || _inFlight is not null || !_area.IsMeasured)
        {
            return;
        }

        // A little under the interval, so a tick that lands a moment early is not skipped and the
        // picture left a whole extra interval old.
        var due = (_restored && _lastStatus is null) || DateTimeOffset.Now - _renderedAt >= _config.RefreshInterval - TimeSpan.FromSeconds(30);

        if (due || Outgrown())
        {
            RefreshNow();
        }
    }

    /// <summary>
    /// [S-1317] The widget grew past what the picture was captured for, enough to draw it soft. Only
    /// for a box larger than the last capture's, so a capture that could not go larger (the zoom or
    /// texture cap) is never asked for again at the same size.
    /// </summary>
    private bool Outgrown()
    {
        if (_bitmap is null || _restored)
        {
            return false;
        }

        var box = PictureBox();
        if (box.Width <= _capturedFor.Width + 1 && box.Height <= _capturedFor.Height + 1)
        {
            return false;
        }

        var placement = ElementFitRules.Place(box.Width, box.Height, _cssWidth, _cssHeight, _config.EffectiveFit);
        return ElementFitRules.NeedsRecapture(placement.Scale, _capturedZoom);
    }

    private void RefreshNow()
    {
        if (_detached)
        {
            return;
        }

        var resolved = _config.ResolvedUrl;
        var selector = _config.EffectiveSelector;

        if (!resolved.IsValid || selector.Length == 0 || _shown is not { IsShown: true } || !_area.IsMeasured
            || !Uri.TryCreate(resolved.Url, UriKind.Absolute, out var uri))
        {
            Lay();
            return;
        }

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;
        var box = PictureBox();
        Lay();

        var target = new SnapshotTarget(box.Width, box.Height, _config.EffectiveFit, XamlRoot?.RasterizationScale ?? 1);
        _ = CaptureAsync(uri, selector, target, generation, cts);
    }

    private async Task CaptureAsync(Uri uri, string selector, SnapshotTarget target, int generation, CancellationTokenSource cts)
    {
        try
        {
            var result = await PageSnapshot.CaptureAsync(uri, selector, target, cts.Token).ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            // Stamped whatever the outcome: a page that is down is asked again at the next
            // interval, not on every tick in between.
            var now = DateTimeOffset.Now;
            _renderedAt = now;
            _lastStatus = result.Status;
            _lastDetail = result.Detail;

            switch (result.Status)
            {
                case SnapshotStatus.Ok when result.Png is { } png:
                    if (await ShowAsync(png, result.CssWidth, result.CssHeight, () => generation == _generation).ConfigureAwait(true))
                    {
                        _restored = false;
                        _capturedZoom = result.Zoom;
                        _capturedFor = new Size(target.BoxWidth, target.BoxHeight);
                        ElementRenderCache.Remember(_shownKey.Url, selector, png, result.CssWidth, result.CssHeight, result.Zoom, now);
                    }

                    break;

                case SnapshotStatus.NotFound:
                    // The element is gone from the page: the old picture is no longer what it shows.
                    ClearPicture();
                    ElementRenderCache.Forget(_shownKey.Url, selector);
                    break;

                case SnapshotStatus.Refused:
                    // [S-1314] Refused by the network guard: the user has to act, so the reason
                    // replaces the picture rather than annotating it.
                    Log.Info(Scope, $"{uri.Host}: {result.Detail}");
                    ClearPicture();
                    break;

                default:
                    Log.Warn(Scope, $"{uri.Host}: {result.Detail}");
                    break;
            }
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
            }

            cts.Dispose();
            Lay();

            // The widget may have grown while this render was out; that is the moment to look again.
            RefreshIfDue();
        }
    }

    /// <summary>[S-1319] Show the saved render of this element, if there is one, until a render replaces it.</summary>
    private async Task RestoreAsync((string Url, string Selector) key)
    {
        if (key.Selector.Length == 0 || ElementRenderCache.Peek(key.Url, key.Selector) is not { } saved)
        {
            return;
        }

        // Superseded by a fresh render, a change of element, or a detach while the file was read.
        bool StillWanted() => !_detached && _shownKey == key && _bitmap is null && _lastStatus is null;

        var png = await ElementRenderCache.ReadAsync(saved).ConfigureAwait(true);

        if (png is null || !StillWanted())
        {
            return;
        }

        if (await ShowAsync(png, saved.CssWidth, saved.CssHeight, StillWanted).ConfigureAwait(true))
        {
            _restored = true;
            _renderedAt = saved.At;
            _capturedZoom = saved.Zoom;
            _capturedFor = default;
            Lay();
        }
    }

    /// <summary>Decode a picture and make it the one on screen, unless <paramref name="stillWanted"/> says it was superseded meanwhile.</summary>
    private async Task<bool> ShowAsync(byte[] png, double width, double height, Func<bool> stillWanted)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            if (_detached || !stillWanted())
            {
                return false;
            }

            _bitmap = bitmap;
            _cssWidth = width;
            _cssHeight = height;
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log.Warn(Scope, "could not show the rendered element", ex);
            _lastStatus = SnapshotStatus.Failed;
            _lastDetail = Str.WidgetsC_WebElement_CaptureFailed.Text();
            return false;
        }
    }

    private void ClearPicture()
    {
        _bitmap = null;
        _restored = false;
        _capturedZoom = 1;
        _capturedFor = default;
        _drawn = default;
        _canvas.Children.Clear();
    }

    // ---- layout ----------------------------------------------------------------------------------

    /// <summary>The footer's cost: a caption line and the gap above it.</summary>
    private static double FooterCost => FluentType.Caption.LineHeight + Fluent.Space2;

    private bool FooterShown => _bitmap is not null && ElementFitRules.ShowsFooter(_area);

    /// <summary>The box the picture is drawn in: the slot less the side and bottom margins, and the footer when it shows.</summary>
    private Size PictureBox()
    {
        var box = _area.Deflate(Fluent.Space2, 0);
        var height = box.Height - Fluent.Space2 - (ElementFitRules.ShowsFooter(_area) ? FooterCost : 0);
        return new Size(Math.Max(0, box.Width), Math.Max(0, height));
    }

    /// <summary>
    /// The ladder: the picture (whole, upscaled, or a 1:1 crop under a fade) always; the age in the
    /// header always; the footer line at XL. Messages shed their icon, then their size.
    /// </summary>
    private void Lay()
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
                ? Str.WidgetsC_WebElement_PickInSettings.Text()
                : WebInput.Explain(resolved.Problem);
        }
        else if (_bitmap is not null)
        {
            // [S-478] Last good stays up through a failed refresh.
            message = null;
        }
        else if (_lastStatus == SnapshotStatus.NotFound)
        {
            message = Str.WidgetsC_WebElement_NotOnPage.Text();
        }
        else if ((_lastStatus is SnapshotStatus.Failed or SnapshotStatus.Refused) && _inFlight is null)
        {
            message = _lastDetail ?? Str.WidgetsC_WebElement_RenderFailed.Text();
        }
        else
        {
            message = Str.WidgetsC_WebElement_Rendering.Text();
        }

        _fit.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
        _messageHost.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        _footer.Visibility = message is null && FooterShown ? Visibility.Visible : Visibility.Collapsed;

        if (message is null)
        {
            DrawPicture();
        }
        else
        {
            _message.Text = message;
            _messageIcon.Visibility = _area.IsMeasured && _area.Height < IconNeedsHeight ? Visibility.Collapsed : Visibility.Visible;
            _message.Apply(_area.IsMeasured && _area.Width < BodyNeedsWidth ? FluentType.Caption : FluentType.Body);
        }

        UpdateAge();
    }

    /// <summary>[S-1316] Size, crop and fade the picture for the box it has.</summary>
    private void DrawPicture()
    {
        if (_bitmap is null || !_area.IsMeasured)
        {
            return;
        }

        var box = PictureBox();
        var placement = ElementFitRules.Place(box.Width, box.Height, _cssWidth, _cssHeight, _config.EffectiveFit);

        // A crop reads from its top-left corner; a whole element sits in the middle of its box.
        _fit.HorizontalAlignment = placement.Cropped ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _fit.VerticalAlignment = placement.Cropped ? VerticalAlignment.Top : VerticalAlignment.Center;
        _fit.Width = placement.Width;
        _fit.Height = placement.Height;
        _fit.Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, Fluent.Space2 + (FooterShown ? FooterCost : 0));

        if (_drawn == (_bitmap, placement))
        {
            return;
        }

        _drawn = (_bitmap, placement);
        BuildCanvas(_bitmap, placement);
    }

    /// <summary>
    /// The picture as one image clipped to the box, plus — for each cut-off edge — thin copies of the
    /// same image at falling opacity: a soft fade that needs no knowledge of what colour is behind
    /// the card (it may be the wallpaper). Every copy shares the one bitmap. Where both edges fade,
    /// the corner takes the right edge's fade.
    /// </summary>
    private void BuildCanvas(BitmapImage bitmap, ElementPlacement placement)
    {
        _canvas.Children.Clear();
        _canvas.Width = placement.Width;
        _canvas.Height = placement.Height;

        var drawnWidth = _cssWidth * placement.Scale;
        var drawnHeight = _cssHeight * placement.Scale;
        var fadeRight = placement.FadeRight ? Math.Min(FadeDepth, placement.Width / 3) : 0;
        var fadeBottom = placement.FadeBottom ? Math.Min(FadeDepth, placement.Height / 3) : 0;

        Add(new Rect(0, 0, placement.Width - fadeRight, placement.Height - fadeBottom), 1);

        for (var step = 0; step < FadeSteps; step++)
        {
            var opacity = 1 - ((step + 0.5) / FadeSteps);

            if (fadeRight > 0)
            {
                var band = fadeRight / FadeSteps;
                Add(new Rect(placement.Width - fadeRight + (step * band), 0, band, placement.Height), opacity);
            }

            if (fadeBottom > 0)
            {
                var band = fadeBottom / FadeSteps;
                Add(new Rect(0, placement.Height - fadeBottom + (step * band), placement.Width - fadeRight, band), opacity);
            }
        }

        void Add(Rect clip, double opacity)
        {
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                return;
            }

            _canvas.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                Width = drawnWidth,
                Height = drawnHeight,
                Opacity = opacity,
                IsHitTestVisible = false,
                Clip = new RectangleGeometry { Rect = clip },
            });
        }
    }

    // ---- freshness -------------------------------------------------------------------------------

    /// <summary>
    /// [S-1318] The render's age in the header (and the XL footer), in the caution colour while it is
    /// a saved picture not yet re-rendered, after a failed refresh, or past three intervals.
    /// </summary>
    /// <summary>[S-1110] The fixed footer word, the message and the age line again.</summary>
    private void OnLanguage(Locale locale)
    {
        _footerFailedText.Text = Str.WidgetsC_WebElement_LastRefreshFailed.Text();
        Lay();
        UpdateAge();
    }

    private void UpdateAge()
    {
        if (_bitmap is null || _renderedAt == DateTimeOffset.MinValue)
        {
            _freshness.Text = string.Empty;
            ToolTipService.SetToolTip(_freshness, null);
            return;
        }

        var now = DateTimeOffset.Now;
        var failed = _lastStatus == SnapshotStatus.Failed;
        var stale = failed || _restored || FetchState.IsStale(_renderedAt, now, _config.RefreshInterval);
        var age = FetchState.Freshness(_renderedAt, now);

        _freshness.Text = age;
        _freshness.Foreground = stale ? _staleBrush : _freshBrush;

        var explanation = failed
            ? Str.WidgetUi_RefreshFailedSince.Format(("reason", _lastDetail), ("time", age))
            : _restored
                ? Str.WidgetsC_WebElement_SavedRenderAgain.Format(("age", age))
                : Str.WidgetsC_WebElement_Rendered.Format(("age", age));

        ToolTipService.SetToolTip(_freshness, explanation);
        ToolTipService.SetToolTip(_fit, explanation);

        _footerHost.Text = WebElementRules.Title(_config.Url);
        _footerAge.Text = Str.WidgetsC_WebElement_Rendered.Format(("age", age));
        _footerAge.Foreground = stale ? _staleBrush : _freshBrush;
        _footerFailed.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_footerFailed, failed ? _lastDetail : null);
    }

    /// <summary>[S-461] The page's host, unless the user named the widget.</summary>
    private void PublishTitle()
    {
        var title = WebElementRules.Title(_config.Url);
        _surface?.SetHeaderTitle(title.Length == 0 ? null : title);
    }
}
