using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>Which widgets the Add-widget picker offers.</summary>
public static class WidgetPickerRules
{
    /// <summary>
    /// [S-950] Whether the picker lists a kind. The Web element widget is discoverable only in
    /// developer mode, as in the original (web-element SPEC S-33): it is a pre-release widget.
    /// <para>
    /// The gate is on <i>discovery</i> only. A web element already on the desktop keeps rendering,
    /// and copy, paste and duplicate still work on it: hiding a picker entry must never touch what
    /// the user already has.
    /// </para>
    /// </summary>
    public static bool IsListed(WidgetKind kind, bool developerMode)
        => developerMode || kind != WidgetKind.WebElement;
}
