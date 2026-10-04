using System.Text;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-472] Pulls whole frames out of an MJPEG (<c>multipart/x-mixed-replace</c>) byte stream.
/// <para>
/// Push-based and allocation-light: the caller appends whatever the socket returned, in whatever
/// sized pieces it arrived, and asks for frames. Nothing here does I/O, so every way a camera can
/// split, pad or mislabel a stream is a unit test rather than a thing to reproduce on a LAN.
/// </para>
/// <para>
/// A part's <c>Content-Length</c> is trusted when it is there, because a JPEG can contain the
/// boundary bytes by chance and the length is the only way to step over them. Without one, the part
/// runs to the next delimiter. Without a boundary at all — a server that sends the multipart type
/// and forgets its parameter — frames are cut at the JPEG start and end markers instead.
/// </para>
/// <para>
/// Bounded: a frame that grows past <see cref="MaxFrameBytes"/> without completing sets
/// <see cref="Overflowed"/>, and the caller treats the stream as broken rather than letting a host
/// that never sends a delimiter fill this process's memory.
/// </para>
/// </summary>
public sealed class MjpegFrameReader
{
    /// <summary>A 4K JPEG at high quality is a couple of megabytes; four times that is not a frame.</summary>
    public const int DefaultMaxFrameBytes = 8 * 1024 * 1024;

    /// <summary>Headers of one part. A camera sends three short lines; past this it is not headers.</summary>
    private const int MaxHeaderBytes = 16 * 1024;

    private static readonly byte[] Soi = [0xFF, 0xD8];
    private static readonly byte[] Eoi = [0xFF, 0xD9];

    private readonly byte[]? _delimiter;
    private byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    /// <summary>Where the next search for a delimiter may resume, so a large frame is not rescanned per chunk.</summary>
    private int _scanFrom;

    private State _state = State.SeekDelimiter;
    private int _bodyLength = -1;

    private enum State
    {
        SeekDelimiter,
        Headers,
        Body,
    }

    /// <param name="boundary">The boundary from the reply's <c>Content-Type</c>, without the leading <c>--</c>; null if it named none.</param>
    /// <param name="maxFrameBytes">The largest frame accepted before the stream is declared broken.</param>
    public MjpegFrameReader(string? boundary, int maxFrameBytes = DefaultMaxFrameBytes)
    {
        _delimiter = string.IsNullOrEmpty(boundary) ? null : Encoding.ASCII.GetBytes("--" + boundary);
        MaxFrameBytes = maxFrameBytes;
    }

    public int MaxFrameBytes { get; }

    /// <summary>A frame outgrew <see cref="MaxFrameBytes"/>. Sticky: the stream is not trusted again.</summary>
    public bool Overflowed { get; private set; }

    /// <summary>Bytes held and not yet handed out as a frame. Exposed for the bound tests.</summary>
    public int Buffered => _end - _start;

    /// <summary>Add bytes as they arrived from the socket.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (Overflowed || data.IsEmpty)
        {
            return;
        }

        if (_end + data.Length > _buffer.Length)
        {
            Compact();

            if (_end + data.Length > _buffer.Length)
            {
                var needed = _end + data.Length;
                var limit = MaxFrameBytes + MaxHeaderBytes + 1024;

                if (needed > limit)
                {
                    Overflowed = true;
                    return;
                }

                Array.Resize(ref _buffer, Math.Min(limit, Math.Max(needed, _buffer.Length * 2)));
            }
        }

        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
    }

    /// <summary>The next complete frame, or false when more bytes are needed first.</summary>
    public bool TryReadFrame(out byte[] frame)
    {
        frame = [];

        if (Overflowed)
        {
            return false;
        }

        if (_delimiter is null)
        {
            return TryReadByMarkers(out frame);
        }

        while (true)
        {
            switch (_state)
            {
                case State.SeekDelimiter:
                    if (!SeekDelimiter())
                    {
                        return false;
                    }

                    break;

                case State.Headers:
                    if (!ReadHeaders())
                    {
                        return false;
                    }

                    break;

                case State.Body:
                    if (!ReadBody(out var body))
                    {
                        return false;
                    }

                    _state = State.SeekDelimiter;

                    // An empty part is a keep-alive some servers send between frames, not a frame.
                    if (body.Length > 0)
                    {
                        frame = body;
                        return true;
                    }

                    break;
            }
        }
    }

    private bool SeekDelimiter()
    {
        var span = _buffer.AsSpan(_start, _end - _start);
        var at = span.IndexOf(_delimiter);

        if (at < 0)
        {
            // Preamble or junk. Keep only a tail that could be the start of a split delimiter.
            var keep = Math.Min(span.Length, _delimiter!.Length - 1);
            _start = _end - keep;
            return false;
        }

        _start += at + _delimiter!.Length;
        _state = State.Headers;
        return true;
    }

    /// <summary>
    /// Skip the rest of the delimiter line, then read header lines up to the blank one. Only
    /// <c>Content-Length</c> matters; the part's own <c>Content-Type</c> is left to the decoder,
    /// which knows a JPEG when it sees one better than a header does.
    /// </summary>
    private bool ReadHeaders()
    {
        var span = _buffer.AsSpan(_start, _end - _start);

        // The delimiter line: "--boundary" then optional padding, then a line break. A close
        // delimiter ("--boundary--") ends the stream; what follows is epilogue.
        var lineEnd = span.IndexOf((byte)'\n');
        if (lineEnd < 0)
        {
            return GiveUpIfTooLong(span.Length);
        }

        var pos = lineEnd + 1;
        var length = -1;

        while (true)
        {
            var rest = span[pos..];

            // A headerless part: the JPEG starts straight after the delimiter line.
            if (rest.Length >= 2 && rest[0] == 0xFF && rest[1] == 0xD8)
            {
                break;
            }

            var nl = rest.IndexOf((byte)'\n');
            if (nl < 0)
            {
                return GiveUpIfTooLong(span.Length);
            }

            var line = rest[..nl];
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            pos += nl + 1;

            if (line.IsEmpty)
            {
                break;
            }

            if (TryContentLength(line, out var parsed))
            {
                length = parsed;
            }
        }

        if (length > MaxFrameBytes)
        {
            Overflowed = true;
            return false;
        }

        _start += pos;
        _bodyLength = length;
        _scanFrom = _start;
        _state = State.Body;
        return true;
    }

    private bool GiveUpIfTooLong(int held)
    {
        if (held > MaxHeaderBytes)
        {
            // Not a part header. Drop back to looking for the next delimiter rather than waiting
            // for a line break that is not coming.
            _start += held - Math.Min(held, _delimiter!.Length - 1);
            _state = State.SeekDelimiter;
            return true;
        }

        return false;
    }

    private bool ReadBody(out byte[] body)
    {
        body = [];
        var held = _end - _start;

        if (_bodyLength >= 0)
        {
            if (held < _bodyLength)
            {
                return false;
            }

            body = _buffer.AsSpan(_start, _bodyLength).ToArray();
            _start += _bodyLength;
            return true;
        }

        // No length: the part runs to the next delimiter. The search resumes where the last one
        // stopped (less a delimiter's worth, in case one straddled the chunk boundary).
        var from = Math.Max(_start, _scanFrom - _delimiter!.Length);
        var at = _buffer.AsSpan(from, _end - from).IndexOf(_delimiter);

        if (at < 0)
        {
            _scanFrom = _end;

            if (held > MaxFrameBytes)
            {
                Overflowed = true;
            }

            return false;
        }

        var stop = from + at;

        // The line break before the delimiter belongs to the delimiter, not to the picture.
        var bodyEnd = stop;
        if (bodyEnd > _start && _buffer[bodyEnd - 1] == (byte)'\n')
        {
            bodyEnd--;
            if (bodyEnd > _start && _buffer[bodyEnd - 1] == (byte)'\r')
            {
                bodyEnd--;
            }
        }

        body = _buffer.AsSpan(_start, bodyEnd - _start).ToArray();
        _start = stop;
        return true;
    }

    /// <summary>The fallback for a stream with no boundary: cut at the JPEG start and end markers.</summary>
    private bool TryReadByMarkers(out byte[] frame)
    {
        frame = [];
        var span = _buffer.AsSpan(_start, _end - _start);
        var soi = span.IndexOf(Soi);

        if (soi < 0)
        {
            // Keep a trailing 0xFF: it may be the first half of a marker split across chunks.
            var keep = span.Length > 0 && span[^1] == 0xFF ? 1 : 0;
            _start = _end - keep;
            return false;
        }

        _start += soi;
        span = _buffer.AsSpan(_start, _end - _start);

        var eoi = span[Soi.Length..].IndexOf(Eoi);
        if (eoi < 0)
        {
            if (span.Length > MaxFrameBytes)
            {
                Overflowed = true;
            }

            return false;
        }

        var length = Soi.Length + eoi + Eoi.Length;
        frame = span[..length].ToArray();
        _start += length;
        return true;
    }

    private static bool TryContentLength(ReadOnlySpan<byte> line, out int length)
    {
        length = -1;
        var colon = line.IndexOf((byte)':');

        if (colon <= 0)
        {
            return false;
        }

        var name = Encoding.ASCII.GetString(line[..colon]).Trim();
        if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = Encoding.ASCII.GetString(line[(colon + 1)..]).Trim();
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length)
            && length >= 0;
    }

    private void Compact()
    {
        if (_start == 0)
        {
            return;
        }

        var held = _end - _start;
        Buffer.BlockCopy(_buffer, _start, _buffer, 0, held);
        _scanFrom = Math.Max(0, _scanFrom - _start);
        _start = 0;
        _end = held;
    }
}
