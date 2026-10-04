using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Themia.App.Interop;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Services.Web;

/// <summary>How a render ended.</summary>
internal enum SnapshotStatus
{
    /// <summary>The element was found and <see cref="SnapshotResult.Png"/> holds its picture.</summary>
    Ok,

    /// <summary>[S-477] The page loaded and the selector matched nothing on it.</summary>
    NotFound,

    /// <summary>[S-478] The page did not load, timed out, or the browser could not start.</summary>
    Failed,
}

/// <summary>One render: the picture and its size in CSS pixels, or why there is none.</summary>
internal sealed record SnapshotResult(SnapshotStatus Status, byte[]? Png, double CssWidth, double CssHeight, string? Detail);

/// <summary>
/// [S-476] Renders a page in an isolated, off-screen WebView2, lets its JavaScript run and settle,
/// and returns a <b>picture</b> of one element.
/// <para>
/// A picture, not markup. The original lifted the element's HTML out of the page and re-rendered it
/// in a sandboxed frame, which needed a sanitiser, URL rewriting, stylesheet inlining and a font
/// fallback — and still never looked quite like the page. Here the browser that ran the page draws
/// the element itself, fonts and all, and the only thing that crosses into the app is a PNG. No
/// remote HTML is ever parsed or shown by this process.
/// </para>
/// <para>
/// One render at a time across every widget, through one gate, and the browser is closed as soon
/// as the queue is empty. A pinned element refreshing every fifteen minutes costs a browser for a
/// few seconds every fifteen minutes and nothing in between: WebView2's processes are the heaviest
/// thing this app can start, and an idle one would cost more than the entire rest of the desktop.
/// </para>
/// <para>
/// UI thread only: WebView2 is single-threaded and parents its window to an HWND owned by this
/// thread's message loop.
/// </para>
/// </summary>
internal static class PageSnapshot
{
    private const string Scope = "web-element";

    /// <summary>
    /// The page is laid out at a desktop width, so a site serves the layout the user picked the
    /// element from rather than its phone layout.
    /// </summary>
    private const int ViewportWidth = 1280;

    private const int ViewportHeight = 900;

    /// <summary>The whole render — start, load, settle, capture — or it is reported as failed.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The window class every Win32 build ships with, so no window procedure of ours is involved.</summary>
    private const string HostClass = "Static";

    /// <summary>
    /// Waits for the DOM to go quiet (600 ms without a mutation, 8 s at most — the original's
    /// [S-16] settle rule), scrolls the element into view so lazy content loads, waits again briefly,
    /// and returns where it is as a JSON string. Never throws: a bad selector is "not found".
    /// </summary>
    private const string ProbeScript = """
        (async (sel) => {
          const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
          const settle = async (quiet, cap) => {
            let last = performance.now();
            const start = last;
            let mo = null;
            try {
              mo = new MutationObserver(() => { last = performance.now(); });
              mo.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
            } catch (e) { }
            while (performance.now() - last < quiet && performance.now() - start < cap) { await sleep(100); }
            if (mo) { mo.disconnect(); }
          };
          if (document.readyState !== 'complete') {
            await Promise.race([new Promise((r) => addEventListener('load', r, { once: true })), sleep(8000)]);
          }
          await settle(600, 8000);
          let el = null;
          try { el = document.querySelector(sel); } catch (e) { el = null; }
          if (!el) { return JSON.stringify({ found: false }); }
          try { el.scrollIntoView({ block: 'start', inline: 'start' }); } catch (e) { }
          await settle(300, 2000);
          try { if (document.fonts && document.fonts.ready) { await Promise.race([document.fonts.ready, sleep(2000)]); } } catch (e) { }
          const r = el.getBoundingClientRect();
          return JSON.stringify({ found: true, x: r.left + window.scrollX, y: r.top + window.scrollY, w: r.width, h: r.height });
        })
        """;

    /// <summary>A background page must not pop windows; dialogs are already cancelled by the host.</summary>
    private const string QuietScript = "try { window.open = function () { return null; }; } catch (e) { }";

    /// <summary>Render <paramref name="url"/> and capture the element <paramref name="selector"/> matches.</summary>
    /// <param name="scale">The display's rasterization scale, so the picture is sharp at 1:1.</param>
    public static async Task<SnapshotResult> CaptureAsync(Uri url, string selector, double scale, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(true);

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(Budget);

            try
            {
                return await RenderAsync(url, selector, Math.Clamp(scale, 1, 3), budget.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return Failed("The page took too long to load");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static SnapshotResult Failed(string detail) => new(SnapshotStatus.Failed, null, 0, 0, detail);

    private static async Task<SnapshotResult> RenderAsync(Uri url, string selector, double scale, CancellationToken token)
    {
        var environment = await WebViewHost.CreateEnvironmentAsync().ConfigureAwait(true);

        if (environment is null)
        {
            return Failed("The WebView2 runtime is not available");
        }

        using var window = HostWindow.Create(ViewportWidth, ViewportHeight);

        if (window is null)
        {
            return Failed("Could not create the render window");
        }

        CoreWebView2Controller? controller = null;

        try
        {
            controller = await environment
                .CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window.Handle))
                .AsTask(token)
                .ConfigureAwait(true);

            // One CSS pixel per physical pixel in the window; the sharpness comes from the device
            // scale emulated below, not from the off-screen window's monitor.
            controller.ShouldDetectMonitorScaleChanges = false;
            controller.RasterizationScale = 1.0;
            controller.Bounds = new Rect(0, 0, ViewportWidth, ViewportHeight);
            controller.IsVisible = true;

            var core = controller.CoreWebView2;
            WebViewHost.LockDown(core);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.Settings.IsWebMessageEnabled = false;

            await core.AddScriptToExecuteOnDocumentCreatedAsync(QuietScript).AsTask(token).ConfigureAwait(true);
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", DeviceMetrics(scale))
                .AsTask(token).ConfigureAwait(true);

            var navigation = await NavigateAsync(core, url, token).ConfigureAwait(true);
            if (navigation is not null)
            {
                return Failed(navigation);
            }

            var probe = await ProbeAsync(core, selector, token).ConfigureAwait(true);

            // A page that redirects itself in script replaces the document the probe ran in; once
            // more against the page it landed on is enough for every redirect worth following.
            if (probe.Status == ElementProbeStatus.Invalid)
            {
                probe = await ProbeAsync(core, selector, token).ConfigureAwait(true);
            }

            switch (probe.Status)
            {
                case ElementProbeStatus.NotFound:
                    return new SnapshotResult(SnapshotStatus.NotFound, null, 0, 0, null);

                case ElementProbeStatus.Invalid:
                    return Failed("The page did not finish loading");
            }

            var reply = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", Screenshot(probe.Box))
                .AsTask(token).ConfigureAwait(true);
            var png = WebElementRules.ParseScreenshot(reply);

            return png is null
                ? Failed("The page could not be captured")
                : new SnapshotResult(SnapshotStatus.Ok, png, probe.Box.Width, probe.Box.Height, null);
        }
        catch (COMException ex)
        {
            Log.Warn(Scope, "rendering a page failed", ex);
            return Failed("The browser stopped while rendering");
        }
        finally
        {
            // Closing the last view is what ends the browser process. Every path comes through here.
            controller?.Close();
        }
    }

    /// <summary>Navigate and wait for the load to finish. Null on success, else a sentence for the widget.</summary>
    private static async Task<string?> NavigateAsync(CoreWebView2 core, Uri url, CancellationToken token)
    {
        var done = new TaskCompletionSource<CoreWebView2WebErrorStatus?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
            => done.TrySetResult(args.IsSuccess ? null : args.WebErrorStatus);

        core.NavigationCompleted += OnCompleted;

        try
        {
            using var registration = token.Register(() => done.TrySetCanceled(token));
            core.Navigate(url.AbsoluteUri);
            var error = await done.Task.ConfigureAwait(true);

            return error switch
            {
                null => null,
                CoreWebView2WebErrorStatus.HostNameNotResolved or CoreWebView2WebErrorStatus.CannotConnect
                    or CoreWebView2WebErrorStatus.ConnectionAborted or CoreWebView2WebErrorStatus.ConnectionReset
                    or CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.ServerUnreachable => "Can't reach the page",
                CoreWebView2WebErrorStatus.Timeout => "The page timed out",
                CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or CoreWebView2WebErrorStatus.CertificateExpired
                    or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or CoreWebView2WebErrorStatus.CertificateRevoked
                    or CoreWebView2WebErrorStatus.CertificateIsInvalid => "The page's certificate is not valid",

                // A 404 or 500 still renders a document; whether the element is on it is the probe's call.
                CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse => null,
                _ => $"The page did not load ({error})",
            };
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
        }
    }

    private static async Task<ElementProbe> ProbeAsync(CoreWebView2 core, string selector, CancellationToken token)
    {
        string reply;

        try
        {
            reply = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", Evaluate(selector))
                .AsTask(token).ConfigureAwait(true);
        }
        catch (COMException)
        {
            // The document the probe was running in went away mid-call: a script redirect.
            return new ElementProbe(ElementProbeStatus.Invalid, default);
        }

        return WebElementRules.ParseProbe(WebElementRules.ParseEvaluateString(reply));
    }

    // ---- DevTools parameters ---------------------------------------------------------------------
    //
    // Written with Utf8JsonWriter rather than formatted strings: the selector is user text and goes
    // into the expression as a JSON string literal, escaped by the writer, never spliced as code.

    private static string Evaluate(string selector)
        => Json(writer =>
        {
            writer.WriteString("expression", $"{ProbeScript}({Literal(selector)})");
            writer.WriteBoolean("awaitPromise", true);
            writer.WriteBoolean("returnByValue", true);
        });

    private static string DeviceMetrics(double scale)
        => Json(writer =>
        {
            writer.WriteNumber("width", ViewportWidth);
            writer.WriteNumber("height", ViewportHeight);
            writer.WriteNumber("deviceScaleFactor", scale);
            writer.WriteBoolean("mobile", false);
        });

    private static string Screenshot(ElementBox box)
        => Json(writer =>
        {
            writer.WriteString("format", "png");
            writer.WriteBoolean("captureBeyondViewport", true);
            writer.WriteStartObject("clip");
            writer.WriteNumber("x", Math.Round(box.X, 2));
            writer.WriteNumber("y", Math.Round(box.Y, 2));
            writer.WriteNumber("width", Math.Ceiling(box.Width));
            writer.WriteNumber("height", Math.Ceiling(box.Height));
            writer.WriteNumber("scale", 1);
            writer.WriteEndObject();
        });

    /// <summary>A JSON string literal, which is also a valid JavaScript one.</summary>
    private static string Literal(string text)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(text);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Json(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// The window the off-screen browser is parented to: a plain <c>Static</c> popup, a tool window
    /// so it is never on the taskbar or in Alt+Tab, shown without activation far off every display.
    /// Destroyed on dispose, on every path.
    /// </summary>
    private sealed class HostWindow : IDisposable
    {
        private HostWindow(nint handle) => Handle = handle;

        public nint Handle { get; private set; }

        public static HostWindow? Create(int width, int height)
        {
            var handle = Win32.CreateWindowEx(
                Win32.WsExToolWindow | Win32.WsExNoActivate,
                HostClass,
                null,
                Win32.WsPopup,
                -32000,
                -32000,
                width,
                height,
                0,
                0,
                Win32.GetModuleHandle(null),
                0);

            if (handle == 0)
            {
                Log.Error(Scope, $"could not create the render window (win32 {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)})");
                return null;
            }

            Win32.ShowWindow(handle, Win32.SwShowNoActivate);
            return new HostWindow(handle);
        }

        public void Dispose()
        {
            if (Handle != 0)
            {
                Win32.DestroyWindow(Handle);
                Handle = 0;
            }
        }
    }
}
