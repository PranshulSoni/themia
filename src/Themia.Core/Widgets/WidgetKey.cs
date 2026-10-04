namespace Themia.Core.Widgets;

/// <summary>
/// The keys a widget may be handed while it holds the keyboard: the navigation set the classic
/// widgets answered through their low-level keyboard hook (arrows, Home / End, Enter, Escape).
/// <para>
/// A closed set rather than a raw virtual-key code, so a widget's key map is a pure function that
/// can be tested here, and the shell's keyboard lease (which decides <i>whether</i> a widget gets a
/// key) is the only place that knows about Win32 key codes.
/// </para>
/// </summary>
public enum WidgetKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Enter,
    Escape,
}
