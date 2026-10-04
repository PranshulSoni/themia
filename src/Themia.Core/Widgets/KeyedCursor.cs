namespace Themia.Core.Widgets;

/// <summary>Where a <see cref="KeyedCursor"/> is after a key, and whether the key meant anything.</summary>
/// <param name="Key">The item under the cursor, or null for none.</param>
/// <param name="Used">False for a key the list has no use for, so it goes on to the surface.</param>
public readonly record struct KeyedCursorResult(string? Key, bool Used);

/// <summary>
/// [S-1565] The keyboard cursor of a list whose order changes under it — a process ranking that
/// re-sorts on every sample. It remembers the <i>item</i> (a pid), not the row, so the cursor rides
/// with its process as it climbs or falls, and a process that left the list leaves no cursor on
/// whatever took its row.
/// <para>
/// Up/Down step (clamped, a list is not a carousel), Home/End jump to the ends, Escape clears. With
/// no cursor — or one whose item has gone — a forward step or Home lands on the first item, a back
/// step or End on the last. Left/Right are not a list's: they go on to the surface. Pure, so the
/// arithmetic at the ends of a reshuffling list is tested here and not rediscovered in each view.
/// </para>
/// </summary>
public static class KeyedCursor
{
    /// <summary>The row of <paramref name="current"/> in <paramref name="keys"/>, or null when it is not there.</summary>
    public static int? IndexOf(IReadOnlyList<string> keys, string? current)
    {
        if (current is null)
        {
            return null;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], current, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    /// <param name="keys">The items as shown, in order.</param>
    /// <param name="current">The item under the cursor, or null.</param>
    /// <param name="key">The key pressed.</param>
    public static KeyedCursorResult Move(IReadOnlyList<string> keys, string? current, WidgetKey key)
    {
        if (key == WidgetKey.Escape)
        {
            return new KeyedCursorResult(null, current is not null);
        }

        if (key is not (WidgetKey.Up or WidgetKey.Down or WidgetKey.Home or WidgetKey.End) || keys.Count == 0)
        {
            return new KeyedCursorResult(current, false);
        }

        var next = ItemCursor.Move(keys.Count, IndexOf(keys, current), key);
        return next is { } index ? new KeyedCursorResult(keys[index], true) : new KeyedCursorResult(current, false);
    }
}
