using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Themia.App.Interop;
using Themia.App.Services.Web;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Widgets;
using Windows.Graphics;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-479] The live page the user picks an element from.
/// <para>
/// A real browser on the real site, because the element worth pinning is usually one a script
/// drew: the user can dismiss a cookie banner, open a menu, even navigate somewhere else first, and
/// the page behaves exactly as it does in a browser. Only after <b>Pick element</b> does a click
/// stop being a click: the element under the pointer is outlined, and the next click pins it and
/// closes the window. Escape goes back to browsing.
/// </para>
/// <para>
/// The page gets nothing from the app. The script injected into it can only post a message, and
/// the host accepts exactly two shapes of message (<see cref="WebElementRules.ParsePickerMessage"/>),
/// and a pick only while the toolbar is in pick mode — so a page that posts a fake pick of its own
/// achieves nothing unless the user was about to pick anyway.
/// </para>
/// </summary>
internal sealed class ElementPickerWindow
{
    private const string Scope = "web-element";

    /// <summary>
    /// The page side of the picker. Mirrors the original's <c>picker-inject.js</c>: the same
    /// <c>cssPath</c> (a unique id anchor, else a <c>tag:nth-of-type(n)</c> chain that survives
    /// unrelated sibling inserts), the same outline, the same Escape. The toolbar lives in the host
    /// window rather than in the page, so the page cannot restyle or cover it.
    /// </summary>
    private const string PickerScript = """
        (function () {
          if (window.top !== window) return;
          if (window.__themiaPicker) return;
          window.__themiaPicker = true;
          var HILITE = '__themia_pick_hilite__', PICKING = '__themia_picking__';
          var picking = false, hovered = null;
          function isUsableId(el, id) {
            if (!/^[A-Za-z][\w-]*$/.test(id)) return false;
            try { return el.ownerDocument.querySelectorAll('#' + id).length === 1; } catch (e) { return false; }
          }
          function cssPath(el) {
            if (!el || el.nodeType !== 1) return '';
            var parts = [], node = el;
            while (node && node.nodeType === 1) {
              var tag = node.tagName.toLowerCase();
              if (tag === 'html') break;
              var id = node.getAttribute('id');
              if (id && isUsableId(node, id)) { parts.unshift('#' + id); return parts.join(' > '); }
              var seg = tag, parent = node.parentElement;
              if (parent) {
                var same = Array.prototype.filter.call(parent.children, function (c) { return c.tagName === node.tagName; });
                if (same.length > 1) seg += ':nth-of-type(' + (same.indexOf(node) + 1) + ')';
              }
              parts.unshift(seg);
              node = node.parentElement;
            }
            return parts.join(' > ');
          }
          function labelOf(el) {
            var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
            return t ? t.slice(0, 80) : el.tagName.toLowerCase();
          }
          function send(o) { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) { } }
          function clear() { if (hovered) { hovered.classList.remove(HILITE); hovered = null; } }
          function style() {
            if (document.getElementById('__themia_pick_style__')) return;
            var s = document.createElement('style');
            s.id = '__themia_pick_style__';
            s.textContent = 'html.' + PICKING + ' *{cursor:crosshair !important;}' +
              '.' + HILITE + '{outline:2px solid #60cdff !important;outline-offset:-2px !important;background:rgba(96,205,255,0.12) !important;}';
            (document.head || document.documentElement).appendChild(s);
          }
          window.__themiaSetPick = function (on) {
            picking = !!on;
            style();
            document.documentElement.classList.toggle(PICKING, picking);
            if (!picking) clear();
          };
          document.addEventListener('mouseover', function (e) {
            if (!picking) return;
            var t = e.target;
            if (!t || t.nodeType !== 1) return;
            if (hovered && hovered !== t) hovered.classList.remove(HILITE);
            if (t.tagName !== 'HTML' && t.tagName !== 'BODY') { t.classList.add(HILITE); hovered = t; }
          }, true);
          document.addEventListener('click', function (e) {
            if (!picking) return;
            var t = e.target;
            if (!t || t.nodeType !== 1) return;
            e.preventDefault();
            e.stopPropagation();
            clear();
            var sel = cssPath(t);
            if (sel) send({ kind: 'themia-picked', selector: sel, label: labelOf(t), url: location.href });
          }, true);
          document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && picking) { e.preventDefault(); window.__themiaSetPick(false); send({ kind: 'themia-browsing' }); }
          }, true);
        })();
        """;

    private static ElementPickerWindow? _current;

    private readonly Window _window;
    private readonly WebView2 _web = new();
    private readonly ToggleButton _pick;
    private readonly TextBlock _address;
    private readonly TextBlock _hint;
    private readonly Button _back;
    private readonly Action<PickerMessage> _picked;
    private readonly Uri _start;

    /// <summary>[S-1314] Every page the user browses to here goes past the network guard first.</summary>
    private readonly NavigationGate _gate = new();

    /// <summary>Why the last navigation was refused, shown in place of the hint until the next one goes through.</summary>
    private string? _refusal;
    private bool _closed;

    private ElementPickerWindow(Uri start, Action<PickerMessage> picked)
    {
        _start = start;
        _picked = picked;

        _back = new Button
        {
            Content = new FontIcon { Glyph = Glyphs.Previous, FontSize = 14 },
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(_back, Str.Common_Back.Text());
        _back.Click += (_, _) =>
        {
            if (_web.CoreWebView2 is { CanGoBack: true } core)
            {
                core.GoBack();
            }
        };

        _address = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        _address.VerticalAlignment = VerticalAlignment.Center;

        _hint = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        _hint.VerticalAlignment = VerticalAlignment.Center;

        _pick = new ToggleButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space2,
                Children = { new FontIcon { Glyph = Glyphs.Pick, FontSize = 14 }, new TextBlock { Text = Str.Shell_Picker_PickElement.Text() } },
            },
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pick.Checked += (_, _) => _ = ApplyPickModeAsync();
        _pick.Unchecked += (_, _) => _ = ApplyPickModeAsync();

        var cancel = new Button { Content = Str.Common_Cancel.Text(), VerticalAlignment = VerticalAlignment.Center };
        cancel.Click += (_, _) => _window?.Close();

        var toolbar = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            Padding = new Thickness(Fluent.Space3, Fluent.Space2, Fluent.Space3, Fluent.Space2),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Background = FluentLayout.Themed("LayerFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
        };

        Place(toolbar, _back, 0);
        Place(toolbar, _address, 1);
        Place(toolbar, _hint, 2);
        Place(toolbar, _pick, 3);
        Place(toolbar, cancel, 4);

        Grid.SetRow(_web, 1);

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
            Children = { toolbar, _web },
        };

        _window = new Window { Title = Str.Shell_Picker_ElementWindowTitle.Text(), Content = root };
        _window.Closed += (_, _) =>
        {
            _closed = true;
            _web.Close();

            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };

        SetHint();

        static void Place(Grid grid, FrameworkElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    /// <summary>
    /// Open the picker on <paramref name="url"/>. One at a time: a second request closes the first,
    /// as the original did, so two pickers never race to write the same widget.
    /// </summary>
    public static void Open(Uri url, Action<PickerMessage> picked)
    {
        _current?._window.Close();

        var picker = new ElementPickerWindow(url, picked);
        _current = picker;
        picker.Size();
        picker._window.Activate();
        _ = picker.StartAsync();
    }

    private void Size()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var dpi = Win32.GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        _window.AppWindow.Resize(new SizeInt32((int)Math.Round(1180 * scale), (int)Math.Round(820 * scale)));

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "themia.ico");
        if (File.Exists(icon))
        {
            _window.AppWindow.SetIcon(icon);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            var environment = await WebViewHost.CreateEnvironmentAsync().ConfigureAwait(true);

            if (environment is null || _closed)
            {
                _hint.Text = environment is null ? Str.Shell_Picker_NoWebView.Text() : string.Empty;
                return;
            }

            await _web.EnsureCoreWebView2Async(environment);

            if (_closed)
            {
                return;
            }

            var core = _web.CoreWebView2;
            WebViewHost.LockDown(core);

            // The user is browsing here, so pop-ups open in place rather than nowhere — but only
            // to an address the widget itself would accept.
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                var target = WebInput.Url(args.Uri);

                if (target.IsValid)
                {
                    core.Navigate(target.Url);
                }
            };

            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnMessage;
            core.SourceChanged += (_, _) => ShowAddress();
            core.HistoryChanged += (_, _) => _back.IsEnabled = core.CanGoBack;
            core.NavigationCompleted += (_, _) => _ = ApplyPickModeAsync();

            await core.AddScriptToExecuteOnDocumentCreatedAsync(PickerScript);

            _pick.IsEnabled = true;
            core.Navigate(_start.AbsoluteUri);
            ShowAddress();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            Log.Error(Scope, "could not start the element picker", ex);
            _hint.Text = Str.Shell_Picker_PageFailed.Text();
        }
    }

    private void ShowAddress()
    {
        if (_web.CoreWebView2 is { } core)
        {
            _address.Text = core.Source;
        }
    }

    private void SetHint()
    {
        _hint.Text = _refusal ?? (_pick.IsChecked == true
            ? Str.Shell_Picker_PickHint.Text()
            : Str.Shell_Picker_BrowseHint.Text());
        _hint.Foreground = FluentLayout.Themed(_refusal is null ? "TextFillColorTertiaryBrush" : "SystemFillColorCautionBrush", Microsoft.UI.Colors.Gray);
    }

    /// <summary>
    /// [S-1314] The original's <c>check_url</c> on the picker: a main-frame navigation to a host the
    /// gate has not cleared is cancelled, checked, and started again only if the guard allows it.
    /// A refusal is said in the toolbar and the page stays where it was. (A form post is re-sent as
    /// a plain load after the check — the price of a check that has to wait for DNS.)
    /// </summary>
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!NavigationGate.TryTarget(args.Uri, out var target))
        {
            args.Cancel = true;
            Refuse(Str.Shell_Picker_HttpOnly.Text());
            return;
        }

        if (_gate.IsCleared(target))
        {
            _refusal = null;
            SetHint();
            return;
        }

        args.Cancel = true;
        _ = CheckThenGoAsync(sender, target);
    }

    private async Task CheckThenGoAsync(CoreWebView2 core, Uri target)
    {
        var verdict = await _gate.CheckAsync(target, CancellationToken.None).ConfigureAwait(true);

        if (_closed)
        {
            return;
        }

        if (verdict.Allowed)
        {
            core.Navigate(target.AbsoluteUri);
            return;
        }

        Log.Info(Scope, $"the picker refused {target.Host}");
        Refuse(verdict.Reason ?? Str.Shell_Picker_PageRefused.Text());
    }

    private void Refuse(string reason)
    {
        _refusal = reason;
        SetHint();
    }

    /// <summary>Tell the page which mode the toolbar is in; again after every navigation, since a new page starts browsing.</summary>
    private async Task ApplyPickModeAsync()
    {
        SetHint();

        if (_closed || _web.CoreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            var on = _pick.IsChecked == true ? "true" : "false";
            await core.ExecuteScriptAsync($"window.__themiaSetPick && window.__themiaSetPick({on})");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // ignore: the page navigated away mid-call; NavigationCompleted applies the mode again.
        }
    }

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? text;

        try
        {
            text = args.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Not a string: not one of ours.
            return;
        }

        var message = WebElementRules.ParsePickerMessage(text);

        switch (message.Kind)
        {
            case PickerMessageKind.Browsing:
                _pick.IsChecked = false;
                break;

            case PickerMessageKind.Picked when _pick.IsChecked == true:
                _picked(message);
                _window.Close();
                break;

            default:
                break;
        }
    }
}
