using Themia.Core.Localization;

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
/// <para>
/// The guide is a shape of <see cref="Str"/> ids, resolved in a language each time it is read: a
/// language switch reads it again and gets the new words, and every language keeps the same tabs,
/// sections, bullets and keys.
/// </para>
/// </summary>
public static class HelpGuide
{
    private sealed record SectionText(Str Title, Str[] Bullets);

    private sealed record PageText(HelpTab Tab, Str Label, Str Intro, SectionText[] Sections, Str? Tip);

    private static readonly PageText[] Guide =
    [
        new(
            HelpTab.Basics,
            Str.Help_Tabs_Basics,
            Str.WidgetsB_Help_Basics_Intro,
            [
                new(Str.Help_Basics_GoodToKnow,
                [
                    Str.WidgetsB_Help_Basics_EditMode,
                    Str.WidgetsB_Help_Basics_StartAgain,
                    Str.WidgetsB_Help_Basics_RightClick,
                    Str.WidgetsB_Help_Basics_Displays,
                ]),
                new(Str.WidgetsB_Help_Basics_Keyboard,
                [
                    Str.WidgetsB_Help_Basics_KeyboardClick,
                    Str.WidgetsB_Help_Basics_KeyboardEsc,
                ]),
            ],
            Str.WidgetsB_Help_Basics_Tip),

        new(
            HelpTab.Widgets,
            Str.Help_Tabs_Widgets,
            Str.WidgetsB_Help_Widgets_Intro,
            [
                new(Str.WidgetsB_Help_Widgets_Adding,
                [
                    Str.WidgetsB_Help_Widgets_AddPick,
                    Str.WidgetsB_Help_Widgets_AddPlace,
                ]),
                new(Str.WidgetsB_Help_Widgets_Arranging,
                [
                    Str.WidgetsB_Help_Widgets_Drag,
                    Str.WidgetsB_Help_Widgets_Select,
                    Str.WidgetsB_Help_Widgets_SelectionActs,
                    Str.WidgetsB_Help_Widgets_Clipboard,
                    Str.WidgetsB_Help_Widgets_Layering,
                    Str.WidgetsB_Help_Widgets_DeleteEsc,
                ]),
                new(Str.WidgetsB_Help_Widgets_Containers,
                [
                    Str.WidgetsB_Help_Widgets_ContainerKinds,
                    Str.WidgetsB_Help_Widgets_ContainerDrop,
                    Str.WidgetsB_Help_Widgets_ContainerDelete,
                ]),
                new(Str.Help_Widgets_WhatYouCanAdd,
                [
                    Str.WidgetsB_Help_Widgets_KindsTime,
                    Str.WidgetsB_Help_Widgets_KindsMail,
                    Str.WidgetsB_Help_Widgets_KindsSystem,
                    Str.WidgetsB_Help_Widgets_KindsInfo,
                    Str.WidgetsB_Help_Widgets_KindsDesk,
                ]),
            ],
            null),

        new(
            HelpTab.Views,
            Str.Help_Tabs_Views,
            Str.WidgetsB_Help_Views_Intro,
            [
                new(Str.WidgetsB_Help_Views_Switching,
                [
                    Str.WidgetsB_Help_Views_ViewBar,
                    Str.WidgetsB_Help_Views_ViewMenu,
                    Str.WidgetsB_Help_Views_Switcher,
                    Str.WidgetsB_Help_Views_PerDisplay,
                ]),
                new(Str.WidgetsB_Help_Views_Bar,
                [
                    Str.WidgetsB_Help_Views_BarDrag,
                    Str.WidgetsB_Help_Views_BarSettings,
                ]),
                new(Str.WidgetsB_Help_Views_Managing,
                [
                    Str.WidgetsB_Help_Views_ManageNew,
                    Str.WidgetsB_Help_Views_ManageRename,
                    Str.WidgetsB_Help_Views_ManageMove,
                ]),
            ],
            null),

        new(
            HelpTab.Customize,
            Str.Help_Tabs_Customize,
            Str.WidgetsB_Help_Customize_Intro,
            [
                new(Str.WidgetsB_Help_Customize_Finding,
                [
                    Str.WidgetsB_Help_Customize_FindBox,
                ]),
                new(Str.WidgetsB_Help_Customize_Look,
                [
                    Str.WidgetsB_Help_Customize_Theme,
                    Str.WidgetsB_Help_Customize_Colours,
                    Str.WidgetsB_Help_Customize_Shape,
                ]),
                new(Str.WidgetsB_Help_Customize_Text,
                [
                    Str.WidgetsB_Help_Customize_TextSize,
                ]),
                new(Str.WidgetsB_Help_Customize_Layout,
                [
                    Str.WidgetsB_Help_Customize_GapSnap,
                    Str.WidgetsB_Help_Customize_PerWidget,
                ]),
            ],
            null),
    ];

    /// <summary>[S-1531] The keys card: the key as it is written in the language, then what it does.</summary>
    private static readonly (Str Keys, Str Meaning)[] KeyText =
    [
        (Str.WidgetsB_Help_Keys_EditMode_Keys, Str.WidgetsB_Help_Keys_EditMode_Does),
        (Str.WidgetsB_Help_Keys_AddToSelection_Keys, Str.WidgetsB_Help_Keys_AddToSelection_Does),
        (Str.WidgetsB_Help_Keys_SelectAll_Keys, Str.WidgetsB_Help_Keys_SelectAll_Does),
        (Str.WidgetsB_Help_Keys_Clipboard_Keys, Str.WidgetsB_Help_Keys_Clipboard_Does),
        (Str.WidgetsB_Help_Keys_Duplicate_Keys, Str.WidgetsB_Help_Keys_Duplicate_Does),
        (Str.WidgetsB_Help_Keys_Delete_Keys, Str.WidgetsB_Help_Keys_Delete_Does),
        (Str.WidgetsB_Help_Keys_OffGrid_Keys, Str.WidgetsB_Help_Keys_OffGrid_Does),
        (Str.WidgetsB_Help_Keys_ShowView_Keys, Str.WidgetsB_Help_Keys_ShowView_Does),
        (Str.WidgetsB_Help_Keys_Escape_Keys, Str.WidgetsB_Help_Keys_Escape_Does),
    ];

    /// <summary>The guide in the current language, read afresh on every call.</summary>
    public static IReadOnlyList<HelpPage> Pages => PagesIn(Strings.Current);

    /// <summary>
    /// [S-1531] The keys, as the extra-large Help widget lists them beside the page, in the current
    /// language: the key, then what it does. Plain text, no markers.
    /// </summary>
    public static IReadOnlyList<HelpKey> Keys => KeysIn(Strings.Current);

    /// <summary>The keys card's heading in <paramref name="locale"/>.</summary>
    public static string KeysTitle(Locale locale) => Strings.Get(locale, Str.WidgetsB_Help_Keys_Title);

    /// <summary>The guide in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<HelpPage> PagesIn(Locale locale)
    {
        var pages = new List<HelpPage>(Guide.Length);

        foreach (var page in Guide)
        {
            pages.Add(Resolve(page, locale));
        }

        return pages;
    }

    /// <summary>The keys card in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<HelpKey> KeysIn(Locale locale)
    {
        var keys = new List<HelpKey>(KeyText.Length);

        foreach (var (key, meaning) in KeyText)
        {
            keys.Add(new HelpKey(Strings.Get(locale, key), Strings.Get(locale, meaning)));
        }

        return keys;
    }

    public static HelpPage Page(HelpTab tab) => Page(tab, Strings.Current);

    public static HelpPage Page(HelpTab tab, Locale locale)
    {
        foreach (var page in Guide)
        {
            if (page.Tab == tab)
            {
                return Resolve(page, locale);
            }
        }

        return Resolve(Guide[0], locale);
    }

    /// <summary>Every string the guide and its keys card read, for the completeness and marker tests.</summary>
    public static IEnumerable<Str> TextIds
    {
        get
        {
            foreach (var page in Guide)
            {
                yield return page.Label;
                yield return page.Intro;

                foreach (var section in page.Sections)
                {
                    yield return section.Title;

                    foreach (var bullet in section.Bullets)
                    {
                        yield return bullet;
                    }
                }

                if (page.Tip is { } tip)
                {
                    yield return tip;
                }
            }

            yield return Str.WidgetsB_Help_Keys_Title;

            foreach (var (key, meaning) in KeyText)
            {
                yield return key;
                yield return meaning;
            }
        }
    }

    private static HelpPage Resolve(PageText page, Locale locale)
    {
        var sections = new List<HelpSection>(page.Sections.Length);

        foreach (var section in page.Sections)
        {
            var bullets = new string[section.Bullets.Length];

            for (var i = 0; i < bullets.Length; i++)
            {
                bullets[i] = Strings.Get(locale, section.Bullets[i]);
            }

            sections.Add(new HelpSection(Strings.Get(locale, section.Title), bullets));
        }

        return new HelpPage(
            page.Tab,
            Strings.Get(locale, page.Label),
            Strings.Get(locale, page.Intro),
            sections,
            page.Tip is { } tip ? Strings.Get(locale, tip) : null);
    }
}
