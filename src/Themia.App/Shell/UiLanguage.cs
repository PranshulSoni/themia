using Microsoft.UI.Xaml;
using Themia.Core.Localization;

namespace Themia.App.Shell;

/// <summary>
/// [S-1110] The language tag XAML text is laid out with. It picks the font fallback for CJK text
/// (Yu Gothic UI for Japanese, Microsoft YaHei UI for Chinese) and hyphenation; left alone it follows
/// Windows' display language, not the one chosen in Themia, and Japanese would be drawn with
/// Chinese glyph shapes on an English Windows.
/// </summary>
internal static class UiLanguage
{
    public static string Tag(Locale locale) => locale switch
    {
        Locale.Zh => "zh-Hans",
        Locale.Pt => "pt-BR",
        _ => LanguageResolver.Code(locale),
    };

    /// <summary>Set the current language on a window's root; its children inherit it.</summary>
    public static void Apply(FrameworkElement? root)
    {
        if (root is not null)
        {
            root.Language = Tag(Strings.Current);
        }
    }
}
