using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Shell;
using Themia.Core.Diagnostics;

namespace Themia.App.Widgets;

/// <summary>
/// One line of an item's context menu: a command, a checkable command, or a separator.
/// </summary>
/// <param name="Glyph">Segoe Fluent glyph, or null for none.</param>
/// <param name="Text">The label.</param>
/// <param name="Action">What a click does; null makes this entry a separator.</param>
/// <param name="Danger">Destructive (delete, clear): drawn in the critical colour.</param>
/// <param name="Disabled">Shown but not clickable, e.g. "Move up" on the first row.</param>
/// <param name="Checked">Non-null makes the entry a toggle showing this state.</param>
/// <param name="Children">
/// [S-1456] Non-null makes the entry a submenu of these entries (its own <paramref name="Action"/>
/// is ignored) — e.g. a destructive command one level down, so a stray click cannot reach it.
/// </param>
internal readonly record struct ItemMenuEntry(
    string? Glyph,
    string Text,
    Action? Action,
    bool Danger = false,
    bool Disabled = false,
    bool? Checked = null,
    IReadOnlyList<ItemMenuEntry>? Children = null)
{
    /// <summary>A divider between groups. Leading, trailing and doubled separators are dropped.</summary>
    public static ItemMenuEntry Separator => new(null, string.Empty, null);

    public bool IsSeparator => Action is null && Children is null;
}

/// <summary>
/// The context menu of one item inside a widget — a row, a file, a day — as opposed to the card's
/// own menu.
/// <para>
/// Every list widget needs one, and each used to build its <see cref="MenuFlyout"/> by hand. This
/// is the one place that does it, so they all look and behave alike: shown at the pointer, the
/// event marked handled so the card's menu does not open on top of it, and a click that throws is
/// logged instead of taking the process down from inside a XAML event.
/// </para>
/// </summary>
internal static class ItemMenu
{
    private const string Scope = "item-menu";

    /// <summary>
    /// Build the flyout. Separators collapse so callers can add groups conditionally without
    /// tracking whether the previous group ended up empty.
    /// </summary>
    public static MenuFlyout Build(IEnumerable<ItemMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var menu = new MenuFlyout();
        Fill(menu.Items, entries);
        return menu;
    }

    /// <summary>Add <paramref name="entries"/> to a menu or a submenu, collapsing separators.</summary>
    private static void Fill(IList<MenuFlyoutItemBase> items, IEnumerable<ItemMenuEntry> entries)
    {
        var pendingSeparator = false;

        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                pendingSeparator = items.Count > 0;
                continue;
            }

            if (pendingSeparator)
            {
                items.Add(new MenuFlyoutSeparator());
                pendingSeparator = false;
            }

            items.Add(entry.Children is { } children ? (MenuFlyoutItemBase)CreateSubmenu(entry, children) : CreateItem(entry));
        }
    }

    private static MenuFlyoutSubItem CreateSubmenu(ItemMenuEntry entry, IReadOnlyList<ItemMenuEntry> children)
    {
        var sub = new MenuFlyoutSubItem { Text = entry.Text, IsEnabled = !entry.Disabled };

        if (entry.Glyph is not null)
        {
            sub.Icon = new FontIcon { Glyph = entry.Glyph };
        }

        Fill(sub.Items, children);
        return sub;
    }

    /// <summary>
    /// Show the menu at the pointer for a right-tap on <paramref name="target"/>. An empty list
    /// leaves the event unhandled, so the card's own menu opens instead.
    /// </summary>
    public static void Show(UIElement target, RightTappedRoutedEventArgs args, IEnumerable<ItemMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(args);

        var menu = Build(entries);
        if (menu.Items.Count == 0)
        {
            return;
        }

        menu.ShowAt(target, args.GetPosition(target));
        args.Handled = true;
    }

    /// <summary>
    /// Give <paramref name="target"/> a menu whose entries are read fresh on every right-tap (so a
    /// "Mark as done" can follow the item's state). Dispose the result to unsubscribe — on
    /// <c>Detach</c>, or when the row is recycled.
    /// </summary>
    public static IDisposable Attach(UIElement target, Func<IEnumerable<ItemMenuEntry>> entries)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entries);

        void OnRightTapped(object sender, RightTappedRoutedEventArgs args)
        {
            try
            {
                Show(target, args, entries());
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "could not open an item menu", ex);
            }
        }

        target.RightTapped += OnRightTapped;
        return new Subscription(() => target.RightTapped -= OnRightTapped);
    }

    private static MenuFlyoutItem CreateItem(ItemMenuEntry entry)
    {
        MenuFlyoutItem item = entry.Checked is { } isChecked
            ? new ToggleMenuFlyoutItem { IsChecked = isChecked }
            : new MenuFlyoutItem();

        item.Text = entry.Text;
        item.IsEnabled = !entry.Disabled;

        if (entry.Glyph is not null)
        {
            item.Icon = new FontIcon { Glyph = entry.Glyph };
        }

        if (entry.Danger)
        {
            var critical = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);
            item.Foreground = critical;
            if (item.Icon is not null)
            {
                item.Icon.Foreground = critical;
            }
        }

        var action = entry.Action;
        item.Click += (_, _) =>
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                // The label is not logged: it can carry user content (a file or task name).
                Log.Error(Scope, "a menu command failed", ex);
            }
        };

        return item;
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
