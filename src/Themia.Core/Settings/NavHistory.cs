namespace Themia.Core.Settings;

/// <summary>
/// Browser-style back/forward history for the settings window. [S-1547]
/// <para>
/// Ported from the original's <c>navHistory.ts</c>: the window's current page is the source of truth,
/// and this records the sequence of the user's navigations so back (the title bar's arrow, Alt+Left,
/// the mouse's back button) and forward (Alt+Right, the forward button) step through them. Pushing
/// while standing on an older entry drops the forward tail; a repeat of the current entry is
/// coalesced, so clicking the page you are on does not add a step that "does nothing" on the way back.
/// </para>
/// </summary>
/// <typeparam name="T">The page identity; compared with its own equality (records compare by value).</typeparam>
public sealed class NavHistory<T>
    where T : class
{
    private readonly List<T> _stack = [];

    /// <summary>Index of the entry on screen; -1 while empty.</summary>
    private int _index = -1;

    public bool CanBack => _index > 0;

    public bool CanForward => _index < _stack.Count - 1;

    /// <summary>The entry on screen, or null before the first navigation.</summary>
    public T? Current => _index >= 0 ? _stack[_index] : null;

    /// <summary>Record a navigation the user made, dropping anything ahead of the current entry.</summary>
    public void Push(T target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_index >= 0 && EqualityComparer<T>.Default.Equals(_stack[_index], target))
        {
            return;
        }

        _stack.RemoveRange(_index + 1, _stack.Count - _index - 1);
        _stack.Add(target);
        _index = _stack.Count - 1;
    }

    /// <summary>Step back; the entry now current, or null at the start (nothing changes).</summary>
    public T? Back()
    {
        if (!CanBack)
        {
            return null;
        }

        _index--;
        return _stack[_index];
    }

    /// <summary>Step forward; the entry now current, or null at the end (nothing changes).</summary>
    public T? Forward()
    {
        if (!CanForward)
        {
            return null;
        }

        _index++;
        return _stack[_index];
    }

    /// <summary>
    /// Drop every entry the predicate says is gone — a deleted widget's page — so back never lands
    /// on a page that no longer exists. Adjacent duplicates left behind by the removal are merged.
    /// </summary>
    public void RemoveWhere(Func<T, bool> gone)
    {
        ArgumentNullException.ThrowIfNull(gone);

        var kept = new List<T>();
        var newIndex = -1;

        for (var i = 0; i < _stack.Count; i++)
        {
            var entry = _stack[i];
            var survives = !gone(entry);

            if (survives && (kept.Count == 0 || !EqualityComparer<T>.Default.Equals(kept[^1], entry)))
            {
                kept.Add(entry);
            }

            // Stand on the current entry if it survived, else on the nearest survivor before it.
            if (i == _index)
            {
                newIndex = kept.Count - 1;
            }
        }

        _stack.Clear();
        _stack.AddRange(kept);

        // Nothing survived before the current entry: the first survivor after it, if any.
        _index = newIndex < 0 && _stack.Count > 0 ? 0 : newIndex;
    }
}
