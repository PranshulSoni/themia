namespace Themia.Core.Widgets;

/// <summary>
/// [S-803] Which <see cref="WidgetKey"/> a platform key press means. The one place that knows the
/// Win32 virtual-key codes, so the mapping — the modifiers in particular — is tested here and not
/// rediscovered by every list.
/// </summary>
public static class WidgetKeys
{
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkPrior = 0x21;
    private const int VkNext = 0x22;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkDelete = 0x2E;
    private const int VkApps = 0x5D;
    private const int VkF2 = 0x71;
    private const int VkF10 = 0x79;
    private const int VkM = 0x4D;
    private const int VkAdd = 0x6B;
    private const int VkSubtract = 0x6D;
    private const int VkOemPlus = 0xBB;
    private const int VkOemMinus = 0xBD;

    /// <summary>
    /// The widget key for a virtual-key code, or null for one no widget answers. Ctrl and Alt chords
    /// are never a widget's (they are the user's shortcuts, and Alt+arrows is window navigation);
    /// Shift only matters where it changes the meaning — Tab backwards, F10 as the Menu key — and
    /// otherwise leaves the key alone.
    /// </summary>
    public static WidgetKey? FromVirtualKey(int virtualKey, bool shift, bool ctrl, bool alt)
    {
        if (ctrl || alt)
        {
            return null;
        }

        return virtualKey switch
        {
            VkLeft => WidgetKey.Left,
            VkRight => WidgetKey.Right,
            VkUp => WidgetKey.Up,
            VkDown => WidgetKey.Down,
            VkPrior => WidgetKey.PageUp,
            VkNext => WidgetKey.PageDown,
            VkHome => WidgetKey.Home,
            VkEnd => WidgetKey.End,
            VkTab => shift ? WidgetKey.ShiftTab : WidgetKey.Tab,
            VkReturn => WidgetKey.Enter,
            VkEscape => WidgetKey.Escape,
            VkSpace when !shift => WidgetKey.Space,
            VkDelete when !shift => WidgetKey.Delete,
            VkF2 when !shift => WidgetKey.Rename,
            VkApps => WidgetKey.Menu,
            VkF10 when shift => WidgetKey.Menu,

            // [S-1225] Shift is ignored: '+' is Shift+'=' on most layouts, and either means "up".
            VkAdd or VkOemPlus => WidgetKey.Plus,
            VkSubtract or VkOemMinus => WidgetKey.Minus,
            VkM when !shift => WidgetKey.Mute,
            _ => null,
        };
    }
}
