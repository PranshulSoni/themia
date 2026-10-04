using System.Globalization;
using System.Text;

namespace Themia.Core.Mail;

/// <summary>
/// [S-550] Reading an RFC 5322 header block: folded fields, RFC 2047 encoded words, the From field
/// and the Date field. Ported from the classic app's IMAP path, and only the IMAP path uses it —
/// Graph and Gmail hand back fields that are already decoded.
/// <para>
/// Every function here is total: hostile or broken input degrades to an empty or verbatim value,
/// never to an exception, because one bad message must not blank the whole list.
/// </para>
/// </summary>
public static class MailHeaders
{
    /// <summary>A header field's value, unfolded and with RFC 2047 words decoded. Empty when absent.</summary>
    public static string Field(string raw, string field) => DecodeWords(FieldRaw(raw, field));

    /// <summary>
    /// The same without RFC 2047 decoding. Structured fields (<c>Content-Type</c>,
    /// <c>Content-Transfer-Encoding</c>, <c>Content-Disposition</c>) are never encoded words, and their
    /// parameters may legally contain <c>=?</c> — a MIME boundary may — so decoding them can only
    /// corrupt the value.
    /// </summary>
    public static string FieldRaw(string raw, string field)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(field);

        var prefix = field + ":";
        var result = new StringBuilder();
        var capturing = false;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // The blank line ends the header block; anything after it is a body.
            if (line.Length == 0)
            {
                break;
            }

            if (line[0] is ' ' or '\t')
            {
                if (capturing)
                {
                    result.Append(' ').Append(line.Trim());
                }

                continue;
            }

            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // A repeated field replaces the earlier one, as the classic app did.
                result.Clear().Append(line[prefix.Length..].Trim());
                capturing = true;
            }
            else
            {
                capturing = false;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Decode RFC 2047 encoded words (<c>=?charset?B|Q?text?=</c>) in an unstructured value. White
    /// space between two adjacent encoded words is dropped, as the RFC requires; any other text is
    /// kept. A malformed word is left exactly as it was written.
    /// </summary>
    public static string DecodeWords(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!value.Contains("=?", StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var rest = value.AsSpan();
        var previousWasEncoded = false;

        while (true)
        {
            var start = rest.IndexOf("=?", StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(rest);
                break;
            }

            var before = rest[..start];
            if (!(previousWasEncoded && before.IsWhiteSpace()))
            {
                result.Append(before);
            }

            rest = rest[start..];

            // The word's end is the "?=" after the third '?', not the first "?=" anywhere: a Q word
            // whose text starts with "=C3" puts a "?=" right after the encoding letter.
            var end = WordEnd(rest);
            if (end < 0)
            {
                result.Append(rest);
                break;
            }

            result.Append(DecodeWord(rest[..end].ToString()));
            rest = rest[end..];
            previousWasEncoded = true;
        }

        return result.ToString();
    }

    /// <summary>The length of the encoded word at the start of <paramref name="text"/>, or -1.</summary>
    private static int WordEnd(ReadOnlySpan<char> text)
    {
        var charsetEnd = text[2..].IndexOf('?');
        if (charsetEnd < 0)
        {
            return -1;
        }

        var encodingEnd = 2 + charsetEnd + 1;
        var afterEncoding = text[encodingEnd..].IndexOf('?');
        if (afterEncoding < 0)
        {
            return -1;
        }

        var textStart = encodingEnd + afterEncoding + 1;
        var close = text[textStart..].IndexOf("?=", StringComparison.Ordinal);
        return close < 0 ? -1 : textStart + close + 2;
    }

    /// <summary>One encoded word. Anything not shaped like one comes back unchanged.</summary>
    public static string DecodeWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length < 7 || !word.StartsWith("=?", StringComparison.Ordinal) || !word.EndsWith("?=", StringComparison.Ordinal))
        {
            return word;
        }

        var parts = word[2..^2].Split('?', 3);
        if (parts.Length != 3)
        {
            return word;
        }

        // RFC 2231 allows a language after the charset: "utf-8*en".
        var charset = parts[0].Split('*')[0];
        var text = parts[2];

        switch (parts[1].ToUpperInvariant())
        {
            case "B":
                var bytes = MimeText.Base64(text);
                return bytes is null ? text : MimeText.Decode(bytes, charset);

            case "Q":
                return MimeText.Decode(QBytes(text), charset);

            default:
                return text;
        }
    }

    /// <summary>The RFC 2047 "Q" encoding: <c>_</c> is a space, <c>=XX</c> a byte, anything else itself.</summary>
    public static byte[] QBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new List<byte>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '_')
            {
                output.Add((byte)' ');
                continue;
            }

            if (c == '=' && i + 2 < text.Length
                && byte.TryParse(text.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            {
                output.Add(b);
                i += 2;
                continue;
            }

            // A character outside Latin-1 cannot be in a well-formed word; keep it as UTF-8.
            if (c > 0xFF)
            {
                output.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                continue;
            }

            output.Add((byte)c);
        }

        return [.. output];
    }

    /// <summary>
    /// Split a From value into a name and an address: <c>"Ann Lee" &lt;ann@x.com&gt;</c>, or a bare
    /// address with no name.
    /// </summary>
    public static (string Name, string Address) ParseFrom(string from)
    {
        ArgumentNullException.ThrowIfNull(from);

        var value = from.Trim();
        var open = value.LastIndexOf('<');

        if (open >= 0)
        {
            var name = value[..open].Trim().Trim('"').Trim();
            var address = value[(open + 1)..].TrimEnd('>').Trim();
            return (name, address);
        }

        // "ann@x.com (Ann Lee)": the comment form some old clients still write.
        var paren = value.IndexOf('(', StringComparison.Ordinal);
        if (paren > 0 && value.EndsWith(')') && value[..paren].Contains('@', StringComparison.Ordinal))
        {
            return (value[(paren + 1)..^1].Trim(), value[..paren].Trim());
        }

        return value.Contains('@', StringComparison.Ordinal) ? (string.Empty, value) : (value, string.Empty);
    }

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>
    /// [S-551] An RFC 5322 date as a true instant, or null when it is not one.
    /// <para>
    /// The zone is applied, not dropped: a <c>+0200</c> header rendered two hours off was the classic
    /// app's issue #45(d). Numeric zones (<c>+HHMM</c>, <c>+HH:MM</c>), the UTC names and the US names
    /// RFC 5322 §4.3 defines are honoured; any other name means "offset unknown", which the RFC
    /// says to read as UTC. A two-digit year is read the obsolete-syntax way (below 50 is 20xx).
    /// </para>
    /// </summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value;
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            text = text[(comma + 1)..];
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return null;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || parts[1].Length < 3)
        {
            return null;
        }

        var month = Array.IndexOf(Months, parts[1][..3].ToLowerInvariant()) + 1;
        if (month == 0 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        if (parts[2].Length <= 2)
        {
            year += year < 50 ? 2000 : 1900;
        }
        else if (parts[2].Length == 3)
        {
            year += 1900;
        }

        var time = parts[3].Split(':');
        if (time.Length < 2
            || !int.TryParse(time[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(time[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
        {
            return null;
        }

        var second = 0;
        if (time.Length > 2 && !int.TryParse(time[2], NumberStyles.None, CultureInfo.InvariantCulture, out second))
        {
            return null;
        }

        var offset = parts.Length > 4 ? ZoneOffset(parts[4]) : TimeSpan.Zero;

        if (year is < 1 or > 9999 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 60)
        {
            return null;
        }

        // A leap second is the last second of the minute as far as a mail list cares.
        second = Math.Min(second, 59);

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // An instant past the representable range once the offset is applied.
            return null;
        }
    }

    /// <summary>The offset a zone token names; an unknown one is UTC (RFC 5322 §4.3, "-0000").</summary>
    public static TimeSpan ZoneOffset(string zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        switch (zone.ToUpperInvariant())
        {
            case "UT" or "UTC" or "GMT" or "Z":
                return TimeSpan.Zero;
            case "EDT":
                return TimeSpan.FromHours(-4);
            case "EST" or "CDT":
                return TimeSpan.FromHours(-5);
            case "CST" or "MDT":
                return TimeSpan.FromHours(-6);
            case "MST" or "PDT":
                return TimeSpan.FromHours(-7);
            case "PST":
                return TimeSpan.FromHours(-8);
        }

        if (zone.Length < 5 || zone[0] is not ('+' or '-'))
        {
            return TimeSpan.Zero;
        }

        var digits = new string([.. zone.Skip(1).Where(char.IsAsciiDigit)]);
        if (digits.Length != 4)
        {
            return TimeSpan.Zero;
        }

        var hours = (digits[0] - '0') * 10 + (digits[1] - '0');
        var minutes = (digits[2] - '0') * 10 + (digits[3] - '0');
        if (hours > 23 || minutes > 59)
        {
            return TimeSpan.Zero;
        }

        var span = new TimeSpan(hours, minutes, 0);
        return zone[0] == '-' ? -span : span;
    }
}
