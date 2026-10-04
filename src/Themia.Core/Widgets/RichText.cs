namespace Themia.Core.Widgets;

/// <summary>What a run of guide prose is drawn as.</summary>
public enum RichKind
{
    Text = 0,

    /// <summary>A term being defined, or a key to press: bold.</summary>
    Strong = 1,

    /// <summary>Something on screen the reader has to find, such as a button: italic.</summary>
    Emphasis = 2,
}

/// <summary>One run of prose and how it is drawn.</summary>
public readonly record struct RichSpan(RichKind Kind, string Text);

/// <summary>
/// [S-482] Inline emphasis for prose, as the original's guide wrote it: <c>*strong*</c> and
/// <c>_emphasis_</c>, nothing else.
/// <para>
/// Not markup. The prose is split into plain runs that the view draws with real text elements, so
/// nothing in it is ever interpreted. Markers do not nest and do not cross a line break, and an
/// unpaired marker stays literal — a lone <c>*</c> is drawn as <c>*</c> rather than swallowing the
/// rest of the sentence.
/// </para>
/// </summary>
public static class RichText
{
    public static IReadOnlyList<RichSpan> Parse(string? text)
    {
        var spans = new List<RichSpan>();
        if (string.IsNullOrEmpty(text))
        {
            return spans;
        }

        var plainStart = 0;
        var i = 0;

        while (i < text.Length)
        {
            var marker = text[i];
            if (marker is not ('*' or '_'))
            {
                i++;
                continue;
            }

            var close = FindClose(text, i + 1, marker);
            if (close < 0)
            {
                i++;
                continue;
            }

            if (i > plainStart)
            {
                spans.Add(new RichSpan(RichKind.Text, text[plainStart..i]));
            }

            spans.Add(new RichSpan(marker == '*' ? RichKind.Strong : RichKind.Emphasis, text[(i + 1)..close]));
            i = close + 1;
            plainStart = i;
        }

        if (plainStart < text.Length)
        {
            spans.Add(new RichSpan(RichKind.Text, text[plainStart..]));
        }

        return spans;
    }

    /// <summary>The closing marker for a span opened just before <paramref name="from"/>, or -1.</summary>
    private static int FindClose(string text, int from, char marker)
    {
        for (var j = from; j < text.Length; j++)
        {
            var c = text[j];

            if (c == '\n')
            {
                return -1;
            }

            if (c == marker)
            {
                // An empty pair ("**") is two literal markers, not an empty span.
                return j > from ? j : -1;
            }
        }

        return -1;
    }
}
