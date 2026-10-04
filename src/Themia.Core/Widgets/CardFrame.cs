using Themia.Core.Geometry;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What the window's hit region needs to know about one card ([S-872]).</summary>
/// <param name="Shown">False for a card nothing is drawn of: hidden by its widget outside edit mode ([S-873]), or inside a collapsed group.</param>
/// <param name="BandOnly">Only the header band paints: a see-through group area with members in it ([S-872]).</param>
/// <param name="Header">Where the header (the band) is.</param>
/// <param name="Bar">The band's thickness in the same units as the rect.</param>
public readonly record struct CardHit(bool Shown, bool BandOnly, HeaderPlacement Header, double Bar);

/// <summary>
/// [S-871]/[S-872] Geometry the card drawn and the card clickable can differ by: a collapsed card
/// shrinks to its header bar, and the window's hit region follows what is painted, not the rect the
/// widget owns. Pure, so the shell's region code and its tests agree on every case.
/// </summary>
public static class CardFrame
{
    /// <summary>
    /// [S-871] The cell a collapsed card occupies: its header bar, along the edge the header is on,
    /// <paramref name="bar"/> thick (the bar plus the gap around it). The stored rect is untouched, so
    /// expanding restores the card exactly. A hidden header collapses to a top bar: a collapsed card
    /// with no bar would be nothing to click to open it again.
    /// </summary>
    public static PixelRect Collapsed(PixelRect cell, HeaderPlacement header, double bar)
    {
        var across = Math.Min(Math.Max(0, bar), cell.Height);
        var along = Math.Min(Math.Max(0, bar), cell.Width);

        return header switch
        {
            HeaderPlacement.Bottom => cell with { Y = cell.Y + cell.Height - across, Height = across },
            HeaderPlacement.Left => cell with { Width = along },
            HeaderPlacement.Right => cell with { X = cell.X + cell.Width - along, Width = along },
            _ => cell with { Height = across },
        };
    }

    /// <summary>
    /// [S-872] The part of a card's painted rect that takes clicks, or null for none. A card that
    /// draws nothing takes nothing, and a see-through group area gives only its header band — a
    /// window over a region the user sees the desktop through would leave the icons there dead.
    /// </summary>
    public static PixelRect? Hit(PixelRect painted, CardHit card)
    {
        if (!card.Shown || painted.Width <= 0 || painted.Height <= 0)
        {
            return null;
        }

        if (!card.BandOnly)
        {
            return painted;
        }

        return card.Header == HeaderPlacement.Hidden ? null : Collapsed(painted, card.Header, card.Bar);
    }
}
