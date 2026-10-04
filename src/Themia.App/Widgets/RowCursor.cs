using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-810] The keyboard cursor of a developer list (the GitHub widgets): a click on a row focuses it,
/// Up / Down / Home / End move the focus, Enter opens the row's page, the Menu key opens its menu,
/// Escape — or the shell's keyboard lease ending — lets go.
/// <para>
/// The focus is real XAML focus on the row with the system focus rect, like Tasks and Habits, so a
/// focused row costs no layout (an inset ring would add to rows whose heights the views count on).
/// It follows its row across redraws by the row's link. Which row a key lands on is
/// <see cref="ItemCursor"/>'s, tested in Core. The view rebuilds rows through <see cref="Clear"/>
/// and <see cref="Add"/>, calls <see cref="Restore"/> once they are in the tree, and forwards
/// <see cref="IWidgetKeys.HandleKey"/> here.
/// </para>
/// </summary>
internal sealed class RowCursor
{
    private readonly List<Entry> _rows = [];

    /// <summary>The focused row's identity (its link), or null when no row has the cursor.</summary>
    private string? _focused;

    /// <summary>Forget the rows (they are being rebuilt). The focused identity stays for <see cref="Restore"/>.</summary>
    public void Clear() => _rows.Clear();

    /// <summary>Take a row, in reading order. A row with no link is focusable but opens nothing.</summary>
    public void Add(Border row, string? url, Func<MenuFlyout>? menu)
    {
        var id = url ?? "#" + _rows.Count.ToString(CultureInfo.InvariantCulture);

        row.IsTabStop = true;
        row.UseSystemFocusVisuals = true;
        row.Tapped += (_, _) => Focus(id);
        _rows.Add(new Entry(row, id, url, menu));
    }

    /// <summary>After a redraw, the focus goes back to its row; a row that left the list lets it go.</summary>
    public void Restore()
    {
        if (_focused is not { } id)
        {
            return;
        }

        if (IndexOf(id) is { } index)
        {
            _rows[index].Row.Focus(FocusState.Keyboard);
        }
        else
        {
            _focused = null;
        }
    }

    public bool HandleKey(WidgetKey key)
    {
        var index = _focused is { } id ? IndexOf(id) : null;

        switch (key)
        {
            case WidgetKey.Escape:
                var had = _focused is not null;
                _focused = null;
                return had;

            case WidgetKey.Enter when index is { } open && _rows[open].Url is { } url:
                DevRows.Open(url);
                return true;

            case WidgetKey.Menu when index is { } at && _rows[at].Menu is { } menu:
                menu().ShowAt(_rows[at].Row);
                return true;

            case WidgetKey.Up or WidgetKey.Down or WidgetKey.Home or WidgetKey.End:
                if (ItemCursor.Move(_rows.Count, index, key) is not { } next)
                {
                    return false;
                }

                Focus(_rows[next].Id);
                return true;

            default:
                return false;
        }
    }

    private void Focus(string id)
    {
        _focused = id;

        if (IndexOf(id) is { } index)
        {
            var row = _rows[index].Row;
            row.Focus(FocusState.Keyboard);
            row.StartBringIntoView();
        }
    }

    private int? IndexOf(string id)
    {
        var index = _rows.FindIndex(r => r.Id == id);
        return index >= 0 ? index : null;
    }

    private sealed record Entry(Border Row, string Id, string? Url, Func<MenuFlyout>? Menu);
}
