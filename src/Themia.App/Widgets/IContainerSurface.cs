using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>
/// What a container view (group, stack, tabs) may ask of the card it is drawn in, beyond
/// <see cref="IWidgetSurface"/>: act on one of its children, offer to add one, and — for a group —
/// shrink the card to its header bar. Only the three containers cast their surface to this.
/// </summary>
internal interface IContainerSurface
{
    /// <summary>Whether the surface is in edit mode: rows and tabs are inert then (the card owns the pointer).</summary>
    bool EditMode { get; }

    /// <summary>The card's header placement, so a group can point its chevron the right way.</summary>
    HeaderPlacement Header { get; }

    /// <summary>The card's background choice, so a group can clear its wash for "None".</summary>
    WidgetSurface Surface { get; }

    /// <summary>[S-871] Shrink the card to its header bar, or give it its rect back. The stored rect is untouched.</summary>
    void SetCollapsed(bool collapsed);

    /// <summary>[S-866] Act on one child: open its settings, move it among its siblings, take it out.</summary>
    void ChildAction(string childId, ContainerChildAction action);

    /// <summary>[S-866] Whether a child can move <paramref name="delta"/> places (the menu disables the ends).</summary>
    bool CanMoveChild(string childId, int delta);

    /// <summary>[S-866] Put a child at <paramref name="index"/> among its siblings (a drag-reorder in edit mode).</summary>
    void MoveChildTo(string childId, int index);

    /// <summary>[S-870] Open the widget picker with this container chosen as where the new widget goes.</summary>
    void AddInside();

    /// <summary>[S-863] Show this glyph in the header instead of the catalogue's; null for the catalogue's.</summary>
    void SetHeaderGlyph(string? glyph);
}

/// <summary>
/// [S-863] A view that paints its own body — a group's area — so the card paints its fill only behind
/// the header bar, and that may give only that bar to the hit region ([S-872]).
/// </summary>
internal interface IWidgetArea
{
    /// <summary>[S-872] Whether only the header band takes clicks right now.</summary>
    bool BandOnly { get; }

    /// <summary>[S-861] The header band was clicked outside edit mode (not on a button in it).</summary>
    void BandTapped();
}

/// <summary>A container view that changes with edit mode: hints shown, rows made inert or draggable.</summary>
internal interface IContainerChrome
{
    void ApplyEditMode(bool editing);
}
