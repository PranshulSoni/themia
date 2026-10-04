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

/// <summary>One tab's prose: an opening paragraph, its sections, and an optional closing tip.</summary>
public sealed record HelpPage(HelpTab Tab, string Label, string Intro, IReadOnlyList<HelpSection> Sections, string? Tip);

/// <summary>
/// [S-482] What the Help widget says, as data rather than as controls, so the view only draws it and
/// the text can be checked without a window.
/// <para>
/// Written for <i>this</i> app, not carried over from the original. The original guide avoided
/// keyboard shortcuts on purpose, because it had a tray icon and a view bar to point at. This app
/// has neither: it is wallpaper with widgets on it, and Ctrl+Alt+W, a double-click and launching it
/// again are the only ways in. A guide that did not say so would leave the reader with no way to
/// change anything. Prose uses <see cref="RichText"/> markers: <c>*strong*</c> for a term or a key,
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
                    "*Edit mode* is where you change things. Press *Ctrl+Alt+W* or double-click any widget, and again, or _Done_ on the toolbar, when you have finished.",
                    "Themia has no window and no tray icon. If another app owns the shortcut, *start Themia again*: the copy that is already running opens edit mode.",
                    "*Right-click* a widget for its menu: _Configure…_, _Copy_, _Duplicate_, _Delete_ and more.",
                    "Every *display* shows a view of its own, so each screen can carry a different layout.",
                ]),
            ],
            "Done with this guide? Right-click it and choose _Delete_."),

        new(
            HelpTab.Widgets,
            "Widgets",
            "Every widget is a card you can place, size and set up the way you like.",
            [
                new("Working with widgets",
                [
                    "*Add*: in edit mode, choose _Add widget_ on the toolbar and pick one, then click where it should go. *Esc* or a right-click cancels.",
                    "*Move and resize*: drag a widget, or one of its eight handles. Hold *Alt* to place it freely, off the snap grid.",
                    "*Set up*: right-click a widget and choose _Configure…_.",
                    "*Copy and paste*: *Ctrl+C*, *Ctrl+X* and *Ctrl+V*. A paste lands at the pointer, on whichever display it is over.",
                    "*Remove*: click a widget and press *Delete*. *Esc* leaves edit mode.",
                ]),
                new("What you can add",
                [
                    "*Time*: clocks, countdowns, a focus timer, sun and moon.",
                    "*Mail & calendar*: mail, events, tasks and contacts from your accounts.",
                    "*System*: processor, memory, disks, network, volume and more.",
                    "*Information*, *Media* and *Launch*: weather, feeds, music, pictures, apps and shortcuts.",
                    "*Desk*, *Trackers* and *Layout*: notes, habits and mood, and groups, stacks and tabs that hold other widgets.",
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
                    "In edit mode, the _View_ menu on the toolbar puts another view on the display you are on.",
                    "Or add a *View switcher* widget: one click switches the display it sits on.",
                    "Each display remembers its own view, so two screens can show two layouts.",
                ]),
                new("Managing",
                [
                    "_New view_, at the bottom of the _View_ menu, adds one and shows it.",
                    "Rename or delete a view in _Settings_, under _Views_.",
                    "Drag a widget onto another display to move it into the view that display is showing.",
                ]),
            ],
            null),

        new(
            HelpTab.Customize,
            "Customize",
            "_Settings_, on the edit toolbar, is where the look of every widget lives.",
            [
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
                    "*Gap between widgets* and *snap to grid*, under _Layout_.",
                    "Each widget's own _Configure…_ page also says where its header sits and how its card is filled.",
                ]),
            ],
            null),
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
