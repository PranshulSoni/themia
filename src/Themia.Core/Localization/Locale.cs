using Themia.Core.Model;

namespace Themia.Core.Localization;

/// <summary>
/// [S-1100] A language the app's text actually comes in: what <see cref="AppLanguage"/> resolves to
/// once "follow Windows" has been decided. Every string table and formatting table is keyed by it.
/// </summary>
public enum Locale
{
    En = 0,
    De = 1,
    Es = 2,
    Fr = 3,
    It = 4,
    Ja = 5,
    Pl = 6,
    Pt = 7,
    Zh = 8,
}

/// <summary>[S-1108] Turning the language setting (and Windows' language list) into a <see cref="Locale"/>.</summary>
public static class LanguageResolver
{
    /// <summary>Every locale, in enum order.</summary>
    public static IReadOnlyList<Locale> All { get; } =
        [Locale.En, Locale.De, Locale.Es, Locale.Fr, Locale.It, Locale.Ja, Locale.Pl, Locale.Pt, Locale.Zh];

    /// <summary>The two-letter code, which is also the locale file's name (<c>de</c> → de.json).</summary>
    public static string Code(Locale locale) => locale switch
    {
        Locale.De => "de",
        Locale.Es => "es",
        Locale.Fr => "fr",
        Locale.It => "it",
        Locale.Ja => "ja",
        Locale.Pl => "pl",
        Locale.Pt => "pt",
        Locale.Zh => "zh",
        _ => "en",
    };

    /// <summary>
    /// The locale to show: the chosen language, or for <see cref="AppLanguage.System"/> the first of
    /// Windows' preferred UI languages that Themia has, else English.
    /// <para>
    /// Walks the whole list rather than only its head (the original read one locale): someone whose
    /// Windows runs in a language Themia lacks, with German as their second choice, gets German —
    /// the same fallback Windows itself applies.
    /// </para>
    /// </summary>
    public static Locale Resolve(AppLanguage language, IEnumerable<string> preferredUiLanguages)
    {
        ArgumentNullException.ThrowIfNull(preferredUiLanguages);

        return language switch
        {
            AppLanguage.English => Locale.En,
            AppLanguage.German => Locale.De,
            AppLanguage.Spanish => Locale.Es,
            AppLanguage.French => Locale.Fr,
            AppLanguage.Italian => Locale.It,
            AppLanguage.Japanese => Locale.Ja,
            AppLanguage.Polish => Locale.Pl,
            AppLanguage.Portuguese => Locale.Pt,
            AppLanguage.Chinese => Locale.Zh,
            _ => FromSystem(preferredUiLanguages),
        };
    }

    /// <summary>The first tag in the list that names a language Themia has; English when none does.</summary>
    public static Locale FromSystem(IEnumerable<string> preferredUiLanguages)
    {
        ArgumentNullException.ThrowIfNull(preferredUiLanguages);

        foreach (var tag in preferredUiLanguages)
        {
            if (FromTag(tag) is { } locale)
            {
                return locale;
            }
        }

        return Locale.En;
    }

    /// <summary>
    /// A BCP-47 tag (<c>zh-CN</c>, <c>zh-Hant-TW</c>, <c>pt_BR</c>, <c>DE</c>) to a locale, by its
    /// primary language subtag; null for a language Themia does not have. Every Chinese tag maps to
    /// <see cref="Locale.Zh"/> (Simplified), as in the original (issue #63).
    /// </summary>
    public static Locale? FromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var span = tag.AsSpan().Trim();
        var end = span.IndexOfAny('-', '_');
        var primary = end < 0 ? span : span[..end];

        foreach (var locale in All)
        {
            if (primary.Equals(Code(locale), StringComparison.OrdinalIgnoreCase))
            {
                return locale;
            }
        }

        return null;
    }
}
