namespace Themia.Core.Mail;

/// <summary>
/// [S-552] The one-line preview of an IMAP message, from its header block and the start of its
/// <c>BODY[TEXT]</c>.
/// <para>
/// The body of a multipart message is the whole MIME structure — the "This is a multi-part message
/// in MIME format." preamble, every boundary and every part's own headers — so it is walked as
/// MIME rather than scrubbed (the classic app's issue #114). The first <c>text/plain</c> leaf wins,
/// <c>text/html</c> is the fallback; the chosen part is decoded from its transfer encoding and
/// charset <b>before</b> white space is collapsed, so <c>=20</c> and <c>=C3=A9</c> never reach the
/// user. Attachments are skipped even when they are text, non-text leaves are ignored, and a
/// structure with no usable text part previews empty — never boundary soup.
/// </para>
/// <para>
/// Bounded: 6 levels, 32 parts, 16 KB decoded per part. A preview needs 200 characters and a
/// hostile structure must not be able to spin the fetch.
/// </para>
/// </summary>
public static class MimePreview
{
    /// <summary>The longest preview, in characters.</summary>
    public const int MaxLength = 200;

    /// <summary>How much of one part is decoded.</summary>
    public const int PartScanLimit = 16 * 1024;

    private const int MaxDepth = 6;
    private const int MaxParts = 32;

    /// <summary>The preview from a header block and a raw body given as a byte string.</summary>
    public static string Build(string headers, string bodyByteString)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(bodyByteString);

        var parts = new List<(bool Html, string Text)>();
        Collect(
            MailHeaders.FieldRaw(headers, "content-type"),
            MailHeaders.FieldRaw(headers, "content-transfer-encoding"),
            MailHeaders.FieldRaw(headers, "content-disposition"),
            bodyByteString,
            0,
            parts);

        // text/plain is what the sender wrote for exactly this purpose; HTML is the same words in markup.
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var plain = parts.FindIndex(p => !p.Html);
        var (isHtml, text) = plain >= 0 ? parts[plain] : parts[0];

        return MimeText.OneLine(isHtml ? MimeText.StripHtml(text) : text, MaxLength);
    }

    /// <summary>The preview from raw bytes as the server sent them.</summary>
    public static string Build(string headers, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Build(headers, MimeText.ByteString(body));
    }

    /// <summary>
    /// Walk one MIME entity, appending each decoded text leaf in document order. An empty
    /// <paramref name="contentType"/> means the entity declared none, which is text/plain (RFC 2045 §5.2).
    /// </summary>
    private static void Collect(
        string contentType,
        string transferEncoding,
        string disposition,
        string body,
        int depth,
        List<(bool Html, string Text)> output)
    {
        if (depth > MaxDepth || output.Count >= MaxParts)
        {
            return;
        }

        var mime = MimeType(contentType);

        if (mime.StartsWith("multipart/", StringComparison.Ordinal))
        {
            // No boundary: nothing can be split out, and the body is delimiter soup.
            if (Parameter(contentType, "boundary") is not { } boundary)
            {
                return;
            }

            foreach (var part in SplitParts(body, boundary))
            {
                var (partHeaders, partBody) = SplitHeaders(part);
                Collect(
                    MailHeaders.FieldRaw(partHeaders, "content-type"),
                    MailHeaders.FieldRaw(partHeaders, "content-transfer-encoding"),
                    MailHeaders.FieldRaw(partHeaders, "content-disposition"),
                    partBody,
                    depth + 1,
                    output);
            }

            return;
        }

        // A forwarded message carries its own headers; its text is still the mail's text.
        if (mime == "message/rfc822")
        {
            var (innerHeaders, innerBody) = SplitHeaders(body);
            Collect(
                MailHeaders.FieldRaw(innerHeaders, "content-type"),
                MailHeaders.FieldRaw(innerHeaders, "content-transfer-encoding"),
                MailHeaders.FieldRaw(innerHeaders, "content-disposition"),
                innerBody,
                depth + 1,
                output);
            return;
        }

        if (disposition.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase)
            || !mime.StartsWith("text/", StringComparison.Ordinal))
        {
            return;
        }

        var raw = body.Length > PartScanLimit ? body[..PartScanLimit] : body;
        byte[] bytes;

        switch (transferEncoding.Trim().ToLowerInvariant())
        {
            case "quoted-printable":
                bytes = MimeText.QuotedPrintable(raw);
                break;

            case "base64":
                // A body that does not decode is corrupt; previewing the base64 itself would be the
                // same bug as boundary soup, so the part is dropped.
                if (MimeText.Base64(raw) is not { } decoded)
                {
                    return;
                }

                bytes = decoded;
                break;

            default:
                bytes = MimeText.Bytes(raw);
                break;
        }

        // An entity that declared no type gets the tag strip: senders who omit the header are the
        // ones who put raw HTML in the body. A declared text/plain is trusted verbatim, so a
        // plain-text "<https://link>" is not eaten as a tag.
        var html = mime == "text/html" || contentType.Trim().Length == 0;
        output.Add((html, MimeText.Decode(bytes, Parameter(contentType, "charset"))));
    }

    /// <summary>The media type, lower case, without parameters. Absent is text/plain.</summary>
    public static string MimeType(string contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        var head = contentType.Split(';')[0].Trim();
        return head.Length == 0 ? "text/plain" : head.ToLowerInvariant();
    }

    /// <summary>One <c>; name=value</c> parameter of a structured field, quote-aware. Null when absent or empty.</summary>
    public static string? Parameter(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(name);

        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var c in value)
        {
            if (c == '"')
            {
                quoted = !quoted;
                current.Append(c);
            }
            else if (c == ';' && !quoted)
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        segments.Add(current.ToString());

        foreach (var segment in segments.Skip(1))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || !segment[..eq].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var v = segment[(eq + 1)..].Trim();
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            {
                v = v[1..^1];
            }

            if (v.Length > 0)
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>
    /// A multipart body's parts (RFC 2046 §5.1.1), headers included. The preamble and epilogue are
    /// dropped; an unterminated last part — a truncated fetch — is kept as far as it arrived.
    /// </summary>
    public static IReadOnlyList<string> SplitParts(string body, string boundary)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(boundary);

        var delimiter = "--" + boundary;
        var closer = delimiter + "--";
        var parts = new List<string>();
        int? open = null;
        var position = 0;

        while (position < body.Length)
        {
            var newline = body.IndexOf('\n', position);
            var lineEnd = newline < 0 ? body.Length : newline + 1;
            var line = body.AsSpan(position, lineEnd - position).TrimEnd("\r\n \t");
            var lineStart = position;
            position = lineEnd;

            var isDelimiter = line.SequenceEqual(delimiter);
            var isCloser = line.SequenceEqual(closer);
            if (!isDelimiter && !isCloser)
            {
                continue;
            }

            if (open is { } start)
            {
                parts.Add(body[start..lineStart]);
                open = null;
            }

            if (isCloser || parts.Count >= MaxParts)
            {
                return parts;
            }

            open = position;
        }

        if (open is { } last && last < body.Length)
        {
            parts.Add(body[last..]);
        }

        return parts;
    }

    /// <summary>
    /// An entity's header block and body, split at the first empty line. An entity whose first line
    /// is not a header is all body.
    /// </summary>
    public static (string Headers, string Body) SplitHeaders(string part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var firstEnd = part.IndexOf('\n', StringComparison.Ordinal);
        var first = (firstEnd < 0 ? part : part[..firstEnd]).TrimEnd('\r');

        if (first.Length > 0 && !LooksLikeHeader(first))
        {
            return (string.Empty, part);
        }

        var position = 0;
        while (position < part.Length)
        {
            var newline = part.IndexOf('\n', position);
            var lineEnd = newline < 0 ? part.Length : newline + 1;

            if (part.AsSpan(position, lineEnd - position).TrimEnd("\r\n").IsEmpty)
            {
                return (part[..position], part[lineEnd..]);
            }

            position = lineEnd;
        }

        return (part, string.Empty);
    }

    /// <summary><c>Name: value</c> — a non-empty field name with no white space in it.</summary>
    private static bool LooksLikeHeader(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && !line[..colon].Any(char.IsWhiteSpace);
    }
}
