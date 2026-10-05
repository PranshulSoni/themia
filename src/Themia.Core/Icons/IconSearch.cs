using Themia.Core.Widgets;

namespace Themia.Core.Icons;

/// <summary>
/// [S-1802] The icon picker's search: matches a query against each glyph's name, its Gallery tags,
/// any extra words the caller adds (the localised names of the suggested icons) and its code point,
/// and ranks the hits best first.
/// <para>
/// Built once per picker and asked on every keystroke; the expensive half (normalising 1,500 names
/// and their tags) is done here, in the constructor.
/// </para>
/// </summary>
public sealed class IconSearch
{
    // Match qualities, best to worst. A name hit outranks the same kind of tag hit, and every prefix
    // outranks every substring, so "ca" puts Calendar and Camera above Location.
    private const int CodeExact = 100;     // "e80f"
    private const int NameExact = 100;     // "home" -> Home
    private const int NamePrefix = 80;     // "cal" -> Calendar
    private const int NameWord = 75;       // "button" -> Global Nav Button
    private const int NameWordPrefix = 65; // "but" -> Global Nav Button
    private const int TagExact = 60;       // "house" -> Home
    private const int TagWord = 55;        // "fidelity" -> "wireless fidelity"
    private const int TagPrefix = 45;      // "hou" -> "house"
    private const int NameSubstring = 30;  // "endar" -> Calendar
    private const int TagSubstring = 20;   // "ous" -> "house"

    private readonly Entry[] _entries;

    /// <param name="icons">The set to search, in the order an empty query lists it after <paramref name="leading"/>.</param>
    /// <param name="extraTerms">More words per code point, searched like tags (the suggested icons' localised names).</param>
    /// <param name="leading">Code points listed first when nothing is typed, and preferred on a tie.</param>
    public IconSearch(
        IReadOnlyList<FluentIcon> icons,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? extraTerms = null,
        IReadOnlyList<int>? leading = null)
    {
        ArgumentNullException.ThrowIfNull(icons);

        var rank = new Dictionary<int, int>();
        if (leading is not null)
        {
            for (var i = 0; i < leading.Count; i++)
            {
                rank.TryAdd(leading[i], i);
            }
        }

        _entries = new Entry[icons.Count];
        for (var i = 0; i < icons.Count; i++)
        {
            var icon = icons[i];
            var tags = new List<string>(icon.Tags.Length + 2);
            tags.AddRange(icon.Tags);
            if (extraTerms is not null && extraTerms.TryGetValue(icon.Code, out var extra))
            {
                tags.AddRange(extra);
            }

            var normalizedTags = tags.Select(WidgetSearch.Normalize).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            _entries[i] = new Entry(
                icon,
                i,
                rank.TryGetValue(icon.Code, out var lead) ? lead : int.MaxValue,
                icon.Code.ToString("x4", System.Globalization.CultureInfo.InvariantCulture),
                WidgetSearch.Normalize(icon.Name),
                WidgetSearch.Tokenize(icon.DisplayName),
                normalizedTags,
                [.. normalizedTags.Select(WidgetSearch.Tokenize)]);
        }
    }

    /// <summary>How many icons the search covers.</summary>
    public int Count => _entries.Length;

    /// <summary>
    /// The icons matching <paramref name="query"/>, best first. Every word of the query must match
    /// somewhere; ties go to a suggested icon, then the shorter name, then the code point. An empty
    /// query is every icon: the suggested ones first, then the rest in their given order.
    /// </summary>
    public IReadOnlyList<FluentIcon> Find(string? query)
    {
        var tokens = Tokens(query);
        if (tokens.Length == 0)
        {
            return [.. _entries
                .OrderBy(e => e.Leading)
                .ThenBy(e => e.Order)
                .Select(e => e.Icon)];
        }

        var hits = new List<(Entry Entry, int Score)>();
        foreach (var entry in _entries)
        {
            var total = 0;
            foreach (var token in tokens)
            {
                var score = Score(entry, token);
                if (score == 0)
                {
                    total = 0;
                    break;
                }

                total += score;
            }

            if (total > 0)
            {
                hits.Add((entry, total));
            }
        }

        return [.. hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Entry.Leading)
            .ThenBy(h => h.Entry.Name.Length)
            .ThenBy(h => h.Entry.Icon.Code)
            .Select(h => h.Entry.Icon)];
    }

    /// <summary>
    /// The query's words, normalised like the names. A query of only punctuation ("$", "\") has no
    /// words but is still something typed: it is kept whole, so the Gallery's symbol tags match.
    /// </summary>
    private static string[] Tokens(string? query)
    {
        // A code point ("E80F") is one token; the word splitter would cut it at the digit-to-capital turn.
        var whole = WidgetSearch.Normalize(query);
        if (whole.Length == 4 && whole.All(char.IsAsciiHexDigit))
        {
            return [whole];
        }

        var tokens = WidgetSearch.Tokenize(query);
        if (tokens.Length > 0)
        {
            return tokens;
        }

        return whole.Length > 0 ? [whole] : [];
    }

    private static int Score(Entry entry, string token)
    {
        if (token.Length == 4 && string.Equals(entry.Code, token, StringComparison.Ordinal))
        {
            return CodeExact;
        }

        if (string.Equals(entry.Name, token, StringComparison.Ordinal))
        {
            return NameExact;
        }

        if (entry.Name.StartsWith(token, StringComparison.Ordinal))
        {
            return NamePrefix;
        }

        var best = 0;
        foreach (var word in entry.NameWords)
        {
            best = Math.Max(best, string.Equals(word, token, StringComparison.Ordinal) ? NameWord
                : word.StartsWith(token, StringComparison.Ordinal) ? NameWordPrefix : 0);
        }

        for (var i = 0; i < entry.Tags.Length && best < TagExact; i++)
        {
            var tag = entry.Tags[i];
            if (string.Equals(tag, token, StringComparison.Ordinal))
            {
                best = Math.Max(best, TagExact);
                continue;
            }

            if (tag.StartsWith(token, StringComparison.Ordinal))
            {
                best = Math.Max(best, TagPrefix);
            }

            foreach (var word in entry.TagWords[i])
            {
                best = Math.Max(best, string.Equals(word, token, StringComparison.Ordinal) ? TagWord
                    : word.StartsWith(token, StringComparison.Ordinal) ? TagPrefix : 0);
            }
        }

        if (best > 0)
        {
            return best;
        }

        if (entry.Name.Contains(token, StringComparison.Ordinal))
        {
            return NameSubstring;
        }

        return entry.Tags.Any(tag => tag.Contains(token, StringComparison.Ordinal)) ? TagSubstring : 0;
    }

    private sealed record Entry(
        FluentIcon Icon,
        int Order,
        int Leading,
        string Code,
        string Name,
        string[] NameWords,
        string[] Tags,
        string[][] TagWords);
}
