using Microsoft.UI.Xaml.Media;

namespace Themia.App.Shell;

/// <summary>
/// The material behind the widget surface.
/// <para>
/// A WinUI window cannot be made fully transparent with the shipped API: the SDK offers only Mica
/// and Desktop Acrylic, and the <see cref="SystemBackdrop"/> extension point wants a brush from the
/// system compositor, which a WinUI window does not hand out. With no backdrop at all the window
/// composes opaque black, which over a whole monitor means covering the desktop.
/// </para>
/// <para>
/// So the surface uses acrylic and lets the <b>window region</b> do the shaping instead
/// (<c>DesktopLayer.SetHitRegion</c>): outside edit mode the window is clipped to the widget rects,
/// so acrylic is visible only <i>inside the cards</i> — which is exactly the Fluent material a
/// widget should sit on, and it comes free rather than costing one HWND per widget. In edit mode
/// the clip is dropped and the whole desktop frosts over, which doubles as the "you are editing"
/// scrim.
/// </para>
/// </summary>
internal static class WidgetBackdrop
{
    public static SystemBackdrop Create() => new DesktopAcrylicBackdrop();
}
