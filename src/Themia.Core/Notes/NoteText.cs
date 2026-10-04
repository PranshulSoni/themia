using System.Globalization;
using System.Text;

namespace Themia.Core.Notes;

/// <summary>The smallest replacement turning one text into another.</summary>
/// <param name="Start">Where the two texts first differ.</param>
/// <param name="RemoveLength">How many characters of the old text go, from <paramref name="Start"/>.</param>
/// <param name="Insert">What comes in their place.</param>
public readonly record struct TextChange(int Start, int RemoveLength, string Insert)
{
    public bool IsEmpty => RemoveLength == 0 && Insert.Length == 0;
}

/// <summary>Words and characters in a note, for its status line.</summary>
public readonly record struct NoteCounts(int Words, int Characters);

/// <summary>Whether what is on screen is on disk yet, for the status line.</summary>
public enum NoteSaveState
{
    /// <summary>On disk.</summary>
    Saved,

    /// <summary>Typed, and waiting for the debounce or the write.</summary>
    Saving,

    /// <summary>The last write failed; the text is still on screen and the next change tries again.</summary>
    Failed,
}

/// <summary>
/// The small text arithmetic the note editor needs between the control and the markdown rules:
/// minimal replacements, which lines need drawing again, and the status line's counts.
/// </summary>
public static class NoteText
{
    /// <summary>
    /// [S-1361] The one replacement that turns <paramref name="before"/> into
    /// <paramref name="after"/>: common prefix and suffix kept, the middle swapped. Applying an
    /// edit as this rather than as a whole new text keeps the scroll position, the undo-free
    /// formatting of every untouched line, and an IME's composition elsewhere. Never splits a
    /// surrogate pair.
    /// </summary>
    public static TextChange Diff(string before, string after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var limit = Math.Min(before.Length, after.Length);
        var prefix = 0;
        while (prefix < limit && before[prefix] == after[prefix])
        {
            prefix++;
        }

        if (prefix > 0 && char.IsHighSurrogate(before[prefix - 1]))
        {
            prefix--;
        }

        var suffix = 0;
        while (suffix < limit - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        if (suffix > 0 && char.IsLowSurrogate(before[before.Length - suffix]))
        {
            suffix--;
        }

        return new TextChange(prefix, before.Length - prefix - suffix, after.Substring(prefix, after.Length - prefix - suffix));
    }

    /// <summary>
    /// [S-1361] The lines of <paramref name="after"/> that must be drawn again after an edit: every
    /// line the replacement touched, and every other line whose parse changed — opening a fence
    /// turns every line below it into code without touching their text. The rest keep the
    /// formatting the control already carries with their characters.
    /// </summary>
    /// <returns>Line indices into <paramref name="afterLines"/>, ascending.</returns>
    public static List<int> LinesToRedraw(string before, IReadOnlyList<NoteLine> beforeLines, string after, IReadOnlyList<NoteLine> afterLines)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(beforeLines);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(afterLines);

        var change = Diff(before, after);
        var redraw = new List<int>();

        if (change.IsEmpty)
        {
            return redraw;
        }

        var firstTouched = NoteEditing.LineIndexAt(after, change.Start);
        // The line of the last inserted character: an insert ending in a newline does not touch the
        // line after it.
        var lastTouched = change.Insert.Length > 0 ? NoteEditing.LineIndexAt(after, change.Start + change.Insert.Length - 1) : firstTouched;
        var lineDelta = afterLines.Count - beforeLines.Count;

        for (var i = 0; i < afterLines.Count; i++)
        {
            if (i >= firstTouched && i <= lastTouched)
            {
                redraw.Add(i);
                continue;
            }

            // Lines before the edit keep their index; lines after it moved by the lines it added.
            var old = i < firstTouched ? i : i - lineDelta;
            if (old < 0 || old >= beforeLines.Count || !afterLines[i].Matches(beforeLines[old]))
            {
                redraw.Add(i);
            }
        }

        return redraw;
    }

    /// <summary>
    /// [S-1362] Words are runs of non-space text holding at least one letter or digit, so list
    /// markers and rules (<c>-</c>, <c>---</c>, <c>[ ]</c>) are not counted as words. Characters are
    /// user-perceived characters, newlines excluded.
    /// </summary>
    public static NoteCounts Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new NoteCounts(0, 0);
        }

        var words = 0;
        var inWord = false;
        var wordHasContent = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (inWord && wordHasContent)
                {
                    words++;
                }

                inWord = false;
                wordHasContent = false;
                continue;
            }

            inWord = true;
            wordHasContent |= Rune.IsLetterOrDigit(rune);
        }

        if (inWord && wordHasContent)
        {
            words++;
        }

        var characters = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            if (elements.GetTextElement() is not ("\n" or "\r" or "\r\n"))
            {
                characters++;
            }
        }

        return new NoteCounts(words, characters);
    }

    /// <summary>[S-1362] "1 word", "12 words · 64 characters", with the save state when asked for.</summary>
    public static string Status(NoteCounts counts, bool detail, NoteSaveState save)
    {
        var words = counts.Words == 1 ? "1 word" : string.Create(CultureInfo.InvariantCulture, $"{counts.Words:N0} words");

        if (!detail)
        {
            return words;
        }

        var characters = counts.Characters == 1 ? "1 character" : string.Create(CultureInfo.InvariantCulture, $"{counts.Characters:N0} characters");
        var saved = save switch
        {
            NoteSaveState.Saving => "Saving…",
            NoteSaveState.Failed => "Not saved",
            _ => "Saved",
        };

        return $"{words} · {characters} · {saved}";
    }
}
