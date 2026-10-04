namespace Themia.Core.Widgets;

/// <summary>The four tabs of the Help widget, in the order they are shown. [S-481]</summary>
public enum HelpTab
{
    Basics = 0,
    Widgets = 1,
    Views = 2,
    Customize = 3,
}

/// <summary>A heading and the bullets under it.</summary>
public sealed record HelpSection(string Title, IReadOnlyList<string> Bullets);

/// <summary>[S-1531] One key and what it does, for the keys card.</summary>
public sealed record HelpKey(string Keys, string Meaning);

/// <summary>One tab's prose: an opening paragraph, its sections, and an optional closing tip.</summary>
public sealed record HelpPage(HelpTab Tab, string Label, string Intro, IReadOnlyList<HelpSection> Sections, string? Tip);

/// <summary>
/// [S-482] What the Help widget says, as data rather than as controls, so the view only draws it and
/// the text can be checked without a window.
/// <para>
/// Written for <i>this</i> app as it is now, not carried over from the original ([S-1532]): edit mode
/// on Ctrl+Alt+W, the picker's search, a new, pasted, duplicated or moved widget following the
/// pointer until a click lands it, Ctrl+click and the rubber band for several widgets, the keyboard
/// a clicked list borrows, containers, bring to front and send to back, the view bar and the
/// settings search. Prose uses <see cref="RichText"/> markers: <c>*strong*</c> for a term or a key,
/// <c>_emphasis_</c> for something on screen to find.
/// </para>
/// </summary>
public static class HelpGuide
{
    public static IReadOnlyList<HelpPage> Pages { get; } =
    [
        new(
            HelpTab.Basics,
            "Basics",
            "Themia puts *widgets* on your desktop: a clock, your calendar, the weather, a note. They sit on the wallpaper, under every window, and stay out of the way until you want to change something.",
            [
                new("Good to know",
                [
                    "*Edit mode* is where you change things. Press *Ctrl+Alt+W*, double-click a widget, or choose _Edit layout_ on any widget's menu. Press it again, or _Done_ on the toolbar, when you have finished.",
                    "Themia has no window and no tray icon. If another app owns the shortcut, *start Themia again*: the copy that is already running opens edit mode.",
                    "*Right-click* a widget for its menu: _Configure…_, _Copy_, _Duplicate_, _Bring to front_, _Delete_ and more.",
                    "Every *display* shows a view of its own, so each screen can carry a different layout.",
                ]),
                new("The keyboard",
                [
                    "Widgets never take the keyboard from the app you are in by themselves. *Click* a list, a search box or a calendar and its keys work: the arrows, *Enter*, and the *Menu* key for an item's menu.",
                    "*Esc*, or a click anywhere else, hands the keyboard back to the app you were in.",
                ]),
            ],
            "Done with this guide? Right-click it and choose _Delete_."),

        new(
            HelpTab.Widgets,
            "Widgets",
            "Every widget is a card you can place, size and set up the way you like. All of this happens in edit mode.",
            [
                new("Adding",
                [
                    "Choose _Add widget_ on the toolbar. *Type to search*, or browse the categories, and pick one with a click or *Enter*.",
                    "The new widget *follows the pointer*, onto any display. Click where it should go; *Esc* or a right-click cancels.",
                ]),
                new("Arranging",
                [
                    "*Drag* a widget to move it, or one of its eight handles to resize it. Hold *Alt* to place it off the snap grid.",
                    "*Select several*: *Ctrl+click* them, or drag a box around them from an empty part of the desktop. *Ctrl+A* selects every widget on the display.",
                    "Dragging, deleting, copying and layering act on everything selected.",
                    "*Ctrl+C*, *Ctrl+X* and *Ctrl+V* copy, cut and paste; *Ctrl+D* duplicates. A paste, a duplicate and _Move_ follow the pointer too, until you click.",
                    "_Bring to front_ and _Send to back_ decide which of two overlapping widgets is on top.",
                    "*Delete* removes what is selected. *Esc* clears the selection, and a second *Esc* leaves edit mode.",
                ]),
                new("Containers",
                [
                    "A *Group* draws one frame behind the widgets in it, a *Stack* lists them as rows that open and close, and *Tabs* shows one at a time.",
                    "Drop a widget onto a container to put it in, or use _Move into_ on its menu. _Add widget inside…_ on a container's menu creates one there.",
                    "Deleting a container keeps the widgets that were in it.",
                ]),
                new("What you can add",
                [
                    "*Time*: clocks, countdowns, a focus timer, sun and moon.",
                    "*Mail & calendar*: mail, events, tasks, contacts and files from the accounts you add in _Settings_, under _Accounts_.",
                    "*System*: processor, memory, disks, network, volume and more.",
                    "*Information*, *Media* and *Launch*: weather, feeds, music, pictures, apps and shortcuts.",
                    "*Desk*, *Trackers* and *Layout*: notes, habits and mood, and the containers above.",
                ]),
            ],
            null),

        new(
            HelpTab.Views,
            "Views",
            "A *view* is a whole desktop layout. Keep one for work and one for the evening, and switch between them in one go.",
            [
                new("Switching",
                [
                    "The *view bar*, at the bottom left of each display, lists your views: click one to put it on that display.",
                    "In edit mode the _View_ menu on the toolbar does the same, and *Ctrl+1* to *Ctrl+9* show the first nine views.",
                    "Or add a *View switcher* widget wherever you like.",
                    "Each display remembers its own view, so two screens can show two layouts.",
                ]),
                new("The view bar",
                [
                    "In edit mode, *drag* the bar to a corner or the middle of an edge, drag a view along it to change the order, or right-click a view to duplicate it or open its settings.",
                    "Whether the bar shows, where, how big, with or without names, and which views it offers: _Settings_, on the display's page.",
                ]),
                new("Managing",
                [
                    "_New view_ and _Duplicate this view_ are at the bottom of the _View_ menu.",
                    "Rename a view, give it an icon or delete it in _Settings_, on the view's page.",
                    "Drag a widget onto another display to move it into the view that display is showing.",
                ]),
            ],
            null),

        new(
            HelpTab.Customize,
            "Customize",
            "_Settings_, on the edit toolbar, is where the look of every widget lives.",
            [
                new("Finding a setting",
                [
                    "Type into *Find a setting*, at the top of the Settings window. It searches every page, every view and every widget you use.",
                ]),
                new("The look",
                [
                    "*Theme*: light, dark, or the same as Windows.",
                    "*Colours*: the accent and the widget background. Leave them empty to follow the theme.",
                    "*Shape*: corner radius, border and opacity, for every widget at once.",
                ]),
                new("Text and size",
                [
                    "*Font*, *text size* and *widget scale* make everything easier to read without moving anything.",
                ]),
                new("Layout",
                [
                    "*Gap between widgets* and *snap to grid*, under _Layout_; _Snap_ on the edit toolbar turns the grid on and off.",
                    "Each widget's own _Configure…_ page also says where its header sits and how its card is filled.",
                ]),
            ],
            null),
    ];

    /// <summary>
    /// [S-1531] The keys, as the extra-large Help widget lists them beside the page: the key, then
    /// what it does. Plain text, no markers.
    /// </summary>
    public static IReadOnlyList<HelpKey> Keys { get; } =
    [
        new("Ctrl+Alt+W", "Edit mode on and off"),
        new("Ctrl+click", "Add to the selection"),
        new("Ctrl+A", "Select all on this display"),
        new("Ctrl+C, X, V", "Copy, cut, paste"),
        new("Ctrl+D", "Duplicate"),
        new("Delete", "Delete what is selected"),
        new("Alt", "Hold while dragging: off the grid"),
        new("Ctrl+1 to 9", "Show a view (edit mode)"),
        new("Esc", "Cancel, or give the keyboard back"),
    ];

    public static HelpPage Page(HelpTab tab)
    {
        foreach (var page in Pages)
        {
            if (page.Tab == tab)
            {
                return page;
            }
        }

        return Pages[0];
    }
}
