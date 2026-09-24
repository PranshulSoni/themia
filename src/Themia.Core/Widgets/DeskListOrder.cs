namespace Themia.Core.Widgets;

/// <summary>
/// Moving an item up or down a list the user is editing.
/// <para>
/// Its own type because two settings pages need exactly this — the colour palette and the quote
/// list — and a second copy of a swap is a second place for an off-by-one at the ends of the list,
/// which is the only part of it anyone ever gets wrong.
/// </para>
/// </summary>
public static class DeskListOrder
{
    /// <summary>
    /// Move the item at <paramref name="index"/> by <paramref name="delta"/> places, and say whether
    /// anything moved.
    /// <para>
    /// An out-of-range index or an edge is a no-op rather than an exception: the up arrow on the
    /// first row is pressable, and should simply do nothing rather than crash a settings page.
    /// </para>
    /// </summary>
    public static bool Move<T>(IList<T>? items, int index, int delta)
    {
        if (items is null || delta == 0 || index < 0 || index >= items.Count)
        {
            return false;
        }

        var target = index + delta;

        if (target < 0 || target >= items.Count)
        {
            return false;
        }

        (items[index], items[target]) = (items[target], items[index]);
        return true;
    }
}
