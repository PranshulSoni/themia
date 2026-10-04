using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Shell;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1288] The keyboard focus of a row in a widget's list (Mail, OneDrive; the file explorer next):
/// the ring that marks it and the keys the list answers. Which row a key lands on is
/// <see cref="ListKeys"/>'s; the view keeps the index and carries out the command.
/// <para>
/// A click on a row focuses it; Escape lets go. Keys reach a view only while its surface can take
/// the keyboard — the shell's keyboard lease (WP11) grants that on a click in a list widget and
/// takes it back on a click outside; until it lands, a click still marks the row, and the view's
/// <c>KeyDown</c> handler is already in place for the lease to feed.
/// </para>
/// </summary>
internal static class RowFocus
{
    /// <summary>The ring's width: inset, like Windows' own list focus, so it never shifts the text.</summary>
    public const double Ring = 1.5;

    /// <summary>Give a row the transparent ring every row carries, so focusing one moves nothing.</summary>
    public static void Prepare(Border row)
    {
        row.BorderThickness = new Thickness(Ring);
        row.BorderBrush = Transparent();
    }

    /// <summary>Show or hide the ring on a prepared row.</summary>
    public static void Mark(Border row, bool focused)
    {
        row.BorderBrush = focused ? FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue) : Transparent();

        if (focused)
        {
            row.StartBringIntoView();
        }
    }

    /// <summary>The list key a platform key means, or null for a key the list leaves alone.</summary>
    public static ListKey? Key(VirtualKey key) => key switch
    {
        VirtualKey.Up => ListKey.Up,
        VirtualKey.Down => ListKey.Down,
        VirtualKey.PageUp => ListKey.PageUp,
        VirtualKey.PageDown => ListKey.PageDown,
        VirtualKey.Home => ListKey.Home,
        VirtualKey.End => ListKey.End,
        VirtualKey.Enter => ListKey.Enter,
        VirtualKey.Delete => ListKey.Delete,
        VirtualKey.F2 => ListKey.Rename,
        VirtualKey.Escape => ListKey.Escape,
        _ => null,
    };

    private static Microsoft.UI.Xaml.Media.Brush Transparent() => FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
}
