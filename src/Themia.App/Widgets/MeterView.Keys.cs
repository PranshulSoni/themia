using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1567] Keyboard navigation in a meter's breakdown list (CPU's and memory's top processes, the
/// drives): Up/Down/Home/End move a cursor over the rows shown, Enter does what a click on the row
/// does (a drive opens in Explorer; a process row does nothing), Escape — also sent whenever the
/// keyboard lease ends — drops it. The cursor remembers its item, not its row
/// (<see cref="KeyedCursor"/>), so it stays on a process while the ranking reshuffles under it.
/// <para>
/// Only a meter showing its list takes the keyboard (<see cref="WantsKeys"/>): a click on a small
/// tile, or on a meter with no list, never takes it from the foreground app.
/// </para>
/// </summary>
internal sealed partial class MeterView
{
    /// <summary>The identity of each list row on screen, top first: its tooltip (a pid, a path), else its name.</summary>
    private readonly List<string> _listKeys = [];

    /// <summary>What each row on screen does when opened; null for a row that does nothing.</summary>
    private readonly List<Action?> _listOpen = [];

    /// <summary>The item under the cursor, or null.</summary>
    private string? _listCursor;

    /// <summary>The row that carries the ring, or -1; rows are reused, so it is re-marked only when it moves.</summary>
    private int _listMarked = -1;

    /// <inheritdoc />
    public bool WantsKeys => !_detached && _listKeys.Count > 0;

    /// <inheritdoc />
    public bool HandleKey(WidgetKey key)
    {
        if (_detached)
        {
            return false;
        }

        if (key == WidgetKey.Enter)
        {
            if (KeyedCursor.IndexOf(_listKeys, _listCursor) is not { } at || _listOpen[at] is not { } open)
            {
                return false;
            }

            try
            {
                open();
            }
            catch (Exception ex)
            {
                // The row's action is the kind's code; nothing it throws may reach the surface.
                Log.Error(Scope, "a meter row's action failed", ex);
            }

            return true;
        }

        var result = KeyedCursor.Move(_listKeys, _listCursor, key);
        _listCursor = result.Key;
        MarkListCursor();
        return result.Used;
    }

    /// <summary>After the list was drawn: remember what is on screen and put the cursor back on its item.</summary>
    private void ShowListCursor(IReadOnlyList<MeterRow> rows, int count)
    {
        _listKeys.Clear();
        _listOpen.Clear();

        for (var i = 0; i < count && i < rows.Count; i++)
        {
            _listKeys.Add(rows[i].Tooltip ?? rows[i].Name);
            _listOpen.Add(rows[i].Open);
        }

        MarkListCursor();
    }

    private void MarkListCursor()
    {
        var at = KeyedCursor.IndexOf(_listKeys, _listCursor) ?? -1;

        if (at == _listMarked)
        {
            return;
        }

        if (_listMarked >= 0 && _listMarked < _listRows.Count)
        {
            RowFocus.Mark(_listRows[_listMarked].Host, false);
        }

        if (at >= 0 && at < _listRows.Count)
        {
            RowFocus.Mark(_listRows[at].Host, true);
        }

        _listMarked = at;
    }
}
