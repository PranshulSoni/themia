using System.Globalization;
using System.Text;

namespace Themia.Core.Mail;

/// <summary>One value in an IMAP response (RFC 3501 §4): an atom, a string, a literal, NIL or a list.</summary>
public abstract record ImapValue
{
    /// <summary>The value as text: an atom or string as itself, a literal as cp1252-tolerant text, NIL as null.</summary>
    public virtual string? Text => null;
}

/// <summary>An atom, including a bracketed section as one atom: <c>BODY[TEXT]&lt;0&gt;</c>, <c>[UIDVALIDITY 7]</c>.</summary>
public sealed record ImapAtom(string Value) : ImapValue
{
    public override string? Text => Value;
}

/// <summary>A quoted string, unescaped.</summary>
public sealed record ImapString(string Value) : ImapValue
{
    public override string? Text => Value;
}

/// <summary>A literal: the raw octets, untouched.</summary>
public sealed record ImapLiteral(byte[] Bytes) : ImapValue
{
    public override string? Text => MimeText.HeaderText(Bytes);
}

/// <summary>A parenthesised list.</summary>
public sealed record ImapList(IReadOnlyList<ImapValue> Items) : ImapValue;

/// <summary>NIL.</summary>
public sealed record ImapNil : ImapValue
{
    public static ImapNil Instance { get; } = new();
}

/// <summary>A whole response: its tag (<c>*</c>, <c>+</c> or a command tag) and what follows it.</summary>
public sealed record ImapResponse(string Tag, IReadOnlyList<ImapValue> Values)
{
    /// <summary>Atom <paramref name="index"/> upper-cased, or empty.</summary>
    public string Word(int index)
        => index < Values.Count && Values[index] is ImapAtom atom ? atom.Value.ToUpperInvariant() : string.Empty;
}

/// <summary>
/// [S-553] Framing and parsing IMAP responses, as pure functions over bytes so they can be tested
/// with a server's exact output.
/// <para>
/// The transport reads a line; when it ends in <c>{n}</c> it reads exactly n more octets and the
/// rest of the line after them, and hands the whole thing — line, literal, line — to
/// <see cref="Parse"/>. Literals stay bytes: a message header or body is decoded by its own
/// charset later, never by the transport.
/// </para>
/// <para>
/// The parser is lenient where servers are sloppy (an unbalanced bracket in human text, a stray
/// close paren) and strict where it matters (a literal's length is honoured exactly, never searched
/// for).
/// </para>
/// </summary>
public static class ImapParser
{
    /// <summary>The largest literal the client will accept; anything bigger is not a preview fetch.</summary>
    public const int MaxLiteral = 8 * 1024 * 1024;

    /// <summary>
    /// The length of the literal a line announces at its end (<c>{123}</c> or the non-synchronising
    /// <c>{123+}</c>), or null when it announces none. The line excludes its CRLF.
    /// </summary>
    public static int? LiteralLength(ReadOnlySpan<byte> line)
    {
        if (line.Length < 3 || line[^1] != (byte)'}')
        {
            return null;
        }

        var open = line.LastIndexOf((byte)'{');
        if (open < 0)
        {
            return null;
        }

        var digits = line[(open + 1)..^1];
        if (digits.Length > 0 && digits[^1] == (byte)'+')
        {
            digits = digits[..^1];
        }

        if (digits.Length is 0 or > 9)
        {
            return null;
        }

        var value = 0;
        foreach (var b in digits)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return null;
            }

            value = (value * 10) + (b - '0');
        }

        return value;
    }

    /// <summary>Parse one complete response (literals inlined after their <c>{n}\r\n</c>).</summary>
    public static ImapResponse Parse(ReadOnlySpan<byte> response)
    {
        var position = 0;
        SkipSpaces(response, ref position);

        var tagStart = position;
        while (position < response.Length && response[position] is not ((byte)' ' or (byte)'\r' or (byte)'\n'))
        {
            position++;
        }

        var tag = Encoding.ASCII.GetString(response[tagStart..position]);
        var values = new List<ImapValue>();
        ParseValues(response, ref position, values, depth: 0);
        return new ImapResponse(tag, values);
    }

    private static void ParseValues(ReadOnlySpan<byte> s, ref int position, List<ImapValue> into, int depth)
    {
        while (true)
        {
            SkipSpaces(s, ref position);
            if (position >= s.Length)
            {
                return;
            }

            var c = s[position];

            if (c is (byte)'\r' or (byte)'\n')
            {
                // The end of a line that carried no literal: the response is complete.
                position++;
                continue;
            }

            if (c == (byte)')')
            {
                position++;
                if (depth > 0)
                {
                    return;
                }

                // A stray close paren in human text at the top level: ignore it.
                continue;
            }

            if (c == (byte)'(')
            {
                position++;
                var items = new List<ImapValue>();
                if (depth < 32)
                {
                    ParseValues(s, ref position, items, depth + 1);
                }

                into.Add(new ImapList(items));
                continue;
            }

            if (c == (byte)'"')
            {
                into.Add(new ImapString(Quoted(s, ref position)));
                continue;
            }

            if (c == (byte)'{' && LiteralAt(s, ref position) is { } literal)
            {
                into.Add(new ImapLiteral(literal));
                continue;
            }

            var atom = Atom(s, ref position);
            into.Add(atom.Equals("NIL", StringComparison.OrdinalIgnoreCase) ? ImapNil.Instance : new ImapAtom(atom));
        }
    }

    private static void SkipSpaces(ReadOnlySpan<byte> s, ref int position)
    {
        while (position < s.Length && s[position] is (byte)' ' or (byte)'\t')
        {
            position++;
        }
    }

    private static string Quoted(ReadOnlySpan<byte> s, ref int position)
    {
        position++; // the opening quote
        var bytes = new List<byte>();

        while (position < s.Length)
        {
            var c = s[position++];

            if (c == (byte)'\\' && position < s.Length)
            {
                bytes.Add(s[position++]);
                continue;
            }

            if (c == (byte)'"')
            {
                break;
            }

            bytes.Add(c);
        }

        return MimeText.HeaderText([.. bytes]);
    }

    /// <summary>A literal at <paramref name="position"/>: <c>{n}</c> CRLF then n octets. Null when it is not one.</summary>
    private static byte[]? LiteralAt(ReadOnlySpan<byte> s, ref int position)
    {
        var close = s[position..].IndexOf((byte)'}');
        if (close < 0)
        {
            return null;
        }

        var announced = LiteralLength(s[position..(position + close + 1)]);
        if (announced is not { } length)
        {
            return null;
        }

        var start = position + close + 1;

        // CRLF (or a bare LF from a sloppy server) separates the announcement from the octets.
        if (start < s.Length && s[start] == (byte)'\r')
        {
            start++;
        }

        if (start < s.Length && s[start] == (byte)'\n')
        {
            start++;
        }

        var end = Math.Min(s.Length, start + length);
        position = end;
        return s[start..end].ToArray();
    }

    /// <summary>
    /// An atom, running to a space, a paren or the line end — except inside square brackets, where
    /// spaces and parens belong to the atom (<c>BODY[HEADER.FIELDS (FROM)]</c>, <c>[UIDVALIDITY 7]</c>).
    /// </summary>
    private static string Atom(ReadOnlySpan<byte> s, ref int position)
    {
        var start = position;
        var brackets = 0;

        while (position < s.Length)
        {
            var c = s[position];

            if (c is (byte)'\r' or (byte)'\n')
            {
                break;
            }

            if (c == (byte)'[')
            {
                brackets++;
            }
            else if (c == (byte)']' && brackets > 0)
            {
                brackets--;
            }
            else if (brackets == 0 && c is (byte)' ' or (byte)'(' or (byte)')')
            {
                break;
            }

            position++;
        }

        if (position == start)
        {
            // Not a character any production starts with: consume it so the parse always advances.
            position++;
        }

        return MimeText.HeaderText(s[start..position].ToArray());
    }
}

/// <summary>One message out of a <c>FETCH</c> response.</summary>
public sealed record ImapFetched(int Sequence, uint? Uid, IReadOnlyList<string> Flags, byte[]? Header, byte[]? Text)
{
    public bool Seen => Flags.Any(f => f.Equals(@"\Seen", StringComparison.OrdinalIgnoreCase));
}

/// <summary>One mailbox out of a <c>LIST</c> response.</summary>
public sealed record ImapMailbox(string Name, IReadOnlyList<string> Attributes);

/// <summary>[S-553] The typed readings of the untagged responses the mail client uses.</summary>
public static class ImapData
{
    /// <summary><c>* 12 EXISTS</c> → 12.</summary>
    public static int? Exists(ImapResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Tag == "*" && response.Word(1) == "EXISTS" && Number(response.Values[0]) is { } n && n <= int.MaxValue
            ? (int)n
            : null;
    }

    /// <summary><c>* OK [UIDVALIDITY 3857529045] …</c> → 3857529045.</summary>
    public static uint? UidValidity(ImapResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Tag != "*" || response.Word(0) != "OK" || response.Values.Count < 2 || response.Values[1] is not ImapAtom code)
        {
            return null;
        }

        var text = code.Value.Trim('[', ']');
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("UIDVALIDITY", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }

    /// <summary>A <c>* n FETCH (…)</c> response, or null for anything else.</summary>
    public static ImapFetched? Fetch(ImapResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Tag != "*" || response.Word(1) != "FETCH" || response.Values.Count < 3
            || Number(response.Values[0]) is not { } sequence || sequence > int.MaxValue
            || response.Values[2] is not ImapList items)
        {
            return null;
        }

        uint? uid = null;
        IReadOnlyList<string> flags = [];
        byte[]? header = null;
        byte[]? text = null;

        for (var i = 0; i + 1 < items.Items.Count; i += 2)
        {
            if (items.Items[i] is not ImapAtom name)
            {
                continue;
            }

            var value = items.Items[i + 1];
            var key = name.Value.ToUpperInvariant();

            if (key == "UID")
            {
                uid = Number(value) is { } u && u <= uint.MaxValue ? (uint)u : null;
            }
            else if (key == "FLAGS")
            {
                flags = value is ImapList list ? [.. list.Items.Select(f => f.Text).OfType<string>()] : [];
            }
            else if (key is "RFC822.HEADER" or "BODY[HEADER]")
            {
                header = Octets(value);
            }
            else if (key.StartsWith("BODY[TEXT]", StringComparison.Ordinal) || key == "RFC822.TEXT")
            {
                text = Octets(value);
            }
        }

        return new ImapFetched((int)sequence, uid, flags, header, text);
    }

    /// <summary><c>* LIST (\HasNoChildren \Sent) "/" "Sent Items"</c>, or null for anything else.</summary>
    public static ImapMailbox? List(ImapResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Tag != "*" || response.Word(0) is not ("LIST" or "XLIST") || response.Values.Count < 4
            || response.Values[1] is not ImapList attributes || response.Values[3].Text is not { } name)
        {
            return null;
        }

        return new ImapMailbox(name, [.. attributes.Items.Select(a => a.Text).OfType<string>()]);
    }

    /// <summary><c>* STATUS "INBOX" (MESSAGES 231 UNSEEN 3)</c>, or null for anything else.</summary>
    public static MailFolderStatus? Status(ImapResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Tag != "*" || response.Word(0) != "STATUS" || response.Values.Count < 3
            || response.Values[^1] is not ImapList items)
        {
            return null;
        }

        long? total = null;
        long? unread = null;

        for (var i = 0; i + 1 < items.Items.Count; i += 2)
        {
            var key = items.Items[i].Text?.ToUpperInvariant();
            var number = Number(items.Items[i + 1]);

            if (key == "MESSAGES")
            {
                total = number;
            }
            else if (key == "UNSEEN")
            {
                unread = number;
            }
        }

        return total is { } t && unread is { } u ? new MailFolderStatus(t, u) : null;
    }

    private static long? Number(ImapValue value)
        => value is ImapAtom atom && long.TryParse(atom.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static byte[]? Octets(ImapValue value) => value switch
    {
        ImapLiteral literal => literal.Bytes,
        ImapString text => Encoding.UTF8.GetBytes(text.Value),
        _ => null,
    };
}
