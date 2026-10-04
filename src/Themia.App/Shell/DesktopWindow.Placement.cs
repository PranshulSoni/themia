using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.App.Shell.Config;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;
using Windows.Graphics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Themia.App.Shell;

/// <summary>
/// Window plumbing: the borderless, transparent, never-activated HWND, where it sits on its
/// display, its backdrop and theme, and showing and closing it.
/// </summary>
internal sealed partial class DesktopWindow
{
    private void ConfigureWindow()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow = appWindow;
        _presenter = appWindow.Presenter as OverlappedPresenter;
        ApplyChromeless();

        appWindow.IsShownInSwitchers = false;
        _ = _appWindow; // retained for placement and future presenter work

        ApplyPlacement(appWindow);

        // A fully transparent backdrop is what lets the wallpaper show between the cards. Without
        // it the window paints its own opaque surface over the whole monitor.
        TryApplyTransparentBackdrop();

        ApplyTheme();

        // Hand the surface to the z-order controller for good: edit mode makes the window
        // activatable, and an activatable window is raised above its neighbours when clicked unless
        // something says otherwise.
        DesktopZOrder.Track(_hwnd);
        DesktopLayer.Apply(_hwnd, interactive: false);
    }

    /// <summary>
    /// Put the window on its monitor's work area.
    /// <para>
    /// <c>AppWindow.MoveAndResize</c> takes <b>physical</b> pixels, which is what
    /// <c>GetMonitorInfo</c> reports, so the work area goes across unconverted. The display scale
    /// only matters inside the window, where XAML lays out in DIPs — see <see cref="Metrics"/>.
    /// </para>
    /// </summary>
    private void ApplyPlacement(AppWindow appWindow)
    {
        var area = _monitor.WorkArea;
        appWindow.MoveAndResize(new RectInt32(area.Left, area.Top, area.Width, area.Height));
    }

    /// <summary>
    /// Strip the border and title bar.
    /// <para>
    /// Re-applied whenever the window's styles are touched, not just at creation: toggling edit mode
    /// changes the extended styles and activation, and the presenter restores its default frame in
    /// response — which is how the surface grew a title bar the moment you started editing.
    /// </para>
    /// </summary>
    private void ApplyChromeless()
    {
        if (_presenter is not null)
        {
            _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            _presenter.IsResizable = false;
            _presenter.IsMinimizable = false;
            _presenter.IsMaximizable = false;
            _presenter.IsAlwaysOnTop = false;
        }

        // The presenter alone leaves WS_CAPTION in place, so finish the job at the Win32 level.
        DesktopLayer.MakeChromeless(_hwnd);
    }

    /// <summary>
    /// Offset from the window's top-left to the client area's top-left, in physical pixels.
    /// <para>
    /// <c>SetWindowRgn</c> works in window coordinates while the widgets are laid out in client
    /// coordinates; without this the mask is shifted by the frame thickness and stops lining up with
    /// the cards it is meant to trace.
    /// </para>
    /// </summary>
    private (int X, int Y) ClientToWindowOffset()
    {
        if (!Win32.GetWindowRect(_hwnd, out var window))
        {
            return (0, 0);
        }

        var origin = default(Win32.Point);
        if (!Win32.ClientToScreen(_hwnd, ref origin))
        {
            return (0, 0);
        }

        return (origin.X - window.Left, origin.Y - window.Top);
    }

    private void TryApplyTransparentBackdrop()
    {
        try
        {
            _window.SystemBackdrop = new TransparentBackdrop();
        }
        catch (Exception ex)
        {
            // Without a transparent backdrop the surface would be an opaque sheet over the desktop,
            // which is worse than no widgets at all — so say so loudly rather than ship a black screen.
            Log.Error(Scope, "transparent backdrop unavailable; the widget surface will not be see-through", ex);
        }
    }

    private void ApplyTheme()
    {
        _root.RequestedTheme = Design.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public void ShowSurface()
    {
        // Shown without activation: starting the app must not pull focus away from whatever the
        // user is doing, and the surface belongs behind their windows anyway.
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        AppWindow.GetFromWindowId(windowId).Show(activateWindow: false);

        DesktopZOrder.Reconcile();
        Relayout();
    }

    public void CloseSurface()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        DesktopZOrder.Forget(_hwnd);
        _host.StateChanged -= OnStateChanged;
        _host.EditModeChanged -= OnEditModeChanged;
        _host.SelectionChanged -= OnSelectionChanged;
        _host.PlacementChanged -= OnPlacementChanged;

        foreach (var card in _cards)
        {
            card.Detach();
        }

        _textInputWanted.Clear();

        _cards.Clear();
        _window.Close();
    }
}
