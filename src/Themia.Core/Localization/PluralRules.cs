namespace Themia.Core.Localization;

/// <summary>CLDR plural categories, in the index order the generated tables store forms in.</summary>
public enum PluralCategory
{
    Zero = 0,
    One = 1,
    Two = 2,
    Few = 3,
    Many = 4,
    Other = 5,
}

/// <summary>
/// [S-1103] Which plural form a whole number takes in each language (CLDR cardinal rules, integers
/// only — the UI counts things, it does not pluralise "1.5 items").
/// <para>
/// Why it is not "1 → one, else other": French and Portuguese say "0 fichier" (0 is singular),
/// Polish has three forms (1 plik, 2–4 pliki, 5+ plików, but 22 pliki), and Japanese and Chinese
/// have no plural at all. A string that ignores this reads wrong to every Polish user.
/// </para>
/// </summary>
public static class PluralRules
{
    public static PluralCategory For(Locale locale, long count)
    {
        // CLDR rules are on the absolute value: "-1 minute" is singular too.
        var n = count == long.MinValue ? long.MaxValue : Math.Abs(count);

        switch (locale)
        {
            case Locale.Ja:
            case Locale.Zh:
                return PluralCategory.Other;

            case Locale.Fr:
            case Locale.Pt:
                // i = 0,1
                return n <= 1 ? PluralCategory.One : PluralCategory.Other;

            case Locale.Pl:
            {
                if (n == 1)
                {
                    return PluralCategory.One;
                }

                var mod10 = n % 10;
                var mod100 = n % 100;
                if (mod10 is >= 2 and <= 4 && mod100 is not (>= 12 and <= 14))
                {
                    return PluralCategory.Few;
                }

                // Every other integer: 0, 5–21, 25–31, 112–114 …
                return PluralCategory.Many;
            }

            default:
                // en, de, es, it: n = 1.
                return n == 1 ? PluralCategory.One : PluralCategory.Other;
        }
    }

    /// <summary>The categories a language uses for whole numbers (what a translator must supply).</summary>
    public static IReadOnlyList<PluralCategory> Categories(Locale locale) => locale switch
    {
        Locale.Ja or Locale.Zh => [PluralCategory.Other],
        Locale.Pl => [PluralCategory.One, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other],
        _ => [PluralCategory.One, PluralCategory.Other],
    };
}
