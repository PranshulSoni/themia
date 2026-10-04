namespace Themia.Core.Widgets;

/// <summary>
/// [S-1530] The searches made from the Search widget since Themia started, newest first.
/// <para>
/// Kept in memory only and never written anywhere: what somebody searched for is theirs, and a
/// desktop widget has no business leaving a log of it in a settings file. A query is trimmed; a
/// blank one is not a search; searching again for something already in the list (ignoring case)
/// moves it to the front rather than listing it twice; the oldest falls off past
/// <see cref="Capacity"/>. Not thread-safe: the UI thread owns it.
/// </para>
/// </summary>
public sealed class RecentSearches
{
    /// <summary>The most the list holds; the widget shows as many as its ladder has rows for.</summary>
    public const int Capacity = 10;

    private readonly List<string> _items = [];

    /// <summary>Raised after every change, so every Search widget redraws the one list.</summary>
    public event Action? Changed;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Record a search. Returns false for a blank query, which changes nothing.</summary>
    public bool Add(string? query)
    {
        var text = (query ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return false;
        }

        _items.RemoveAll(item => item.Equals(text, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, text);

        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Take one search out (its row's menu). Returns whether it was there.</summary>
    public bool Remove(string? query)
    {
        if (query is null || _items.RemoveAll(item => item.Equals(query, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Forget them all (the card's menu). Returns whether there was anything to forget.</summary>
    public bool Clear()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Up / Down in the box walk the list like a shell's history: from nothing, Up takes the newest;
    /// each further Up an older one, Down a newer one, and Down past the newest is back to nothing
    /// (null). Returns the index to show, or null for the empty box.
    /// </summary>
    public int? Step(int? current, bool older)
    {
        if (_items.Count == 0)
        {
            return null;
        }

        if (current is not { } at || at < 0 || at >= _items.Count)
        {
            return older ? 0 : null;
        }

        if (older)
        {
            return Math.Min(_items.Count - 1, at + 1);
        }

        return at == 0 ? null : at - 1;
    }
}
