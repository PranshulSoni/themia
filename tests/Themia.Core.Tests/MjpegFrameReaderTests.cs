using System.Text;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-472] The MJPEG part reader. Every case here is something a real camera does: split frames
/// across reads at arbitrary points, send or omit <c>Content-Length</c>, use bare LF line ends, put
/// junk before the first boundary, or forget the boundary altogether.
/// </summary>
public class MjpegFrameReaderTests
{
    private static readonly byte[] FrameA = Jpeg(0x11, 40);
    private static readonly byte[] FrameB = Jpeg(0x22, 300);

    /// <summary>A fake JPEG: start marker, a body, end marker. The reader never decodes it.</summary>
    private static byte[] Jpeg(byte fill, int bodyLength)
    {
        var bytes = new byte[bodyLength + 4];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        Array.Fill(bytes, fill, 2, bodyLength);
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }

    private static byte[] Part(string boundary, byte[] frame, bool withLength, string newline = "\r\n")
    {
        var head = $"--{boundary}{newline}Content-Type: image/jpeg{newline}"
            + (withLength ? $"Content-Length: {frame.Length}{newline}" : string.Empty)
            + newline;

        return [.. Encoding.ASCII.GetBytes(head), .. frame, .. Encoding.ASCII.GetBytes(newline)];
    }

    private static List<byte[]> Drain(MjpegFrameReader reader)
    {
        var frames = new List<byte[]>();
        while (reader.TryReadFrame(out var frame))
        {
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>Feed in fixed-size chunks, draining after each, the way the socket loop does.</summary>
    private static List<byte[]> FeedInChunks(MjpegFrameReader reader, byte[] stream, int chunk)
    {
        var frames = new List<byte[]>();
        for (var i = 0; i < stream.Length; i += chunk)
        {
            reader.Append(stream.AsSpan(i, Math.Min(chunk, stream.Length - i)));
            frames.AddRange(Drain(reader));
        }

        return frames;
    }

    [Fact]
    public void S472_frames_with_a_content_length_come_out_whole()
    {
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Part("frame", FrameA, true), .. Part("frame", FrameB, true)]);

        var frames = Drain(reader);

        Assert.Equal(2, frames.Count);
        Assert.Equal(FrameA, frames[0]);
        Assert.Equal(FrameB, frames[1]);
    }

    [Fact]
    public void S472_frames_without_a_length_run_to_the_next_delimiter()
    {
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Part("frame", FrameA, false), .. Part("frame", FrameB, false), .. Encoding.ASCII.GetBytes("--frame\r\n")]);

        var frames = Drain(reader);

        Assert.Equal(2, frames.Count);
        Assert.Equal(FrameA, frames[0]);
        Assert.Equal(FrameB, frames[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void S472_a_frame_split_at_every_possible_byte_still_comes_out_whole(bool withLength)
    {
        byte[] stream = [.. Part("b", FrameA, withLength), .. Part("b", FrameB, withLength), .. Part("b", FrameA, withLength)];

        foreach (var chunk in new[] { 1, 2, 3, 7, 64 })
        {
            var frames = FeedInChunks(new MjpegFrameReader("b"), stream, chunk);

            // Without a length the last part is only known to be over when the next delimiter
            // arrives, so it is still held; with one it is complete as soon as its bytes are in.
            Assert.Equal(withLength ? 3 : 2, frames.Count);
            Assert.Equal(FrameA, frames[0]);
            Assert.Equal(FrameB, frames[1]);
        }
    }

    [Fact]
    public void S472_bare_lf_line_ends_are_accepted()
    {
        var reader = new MjpegFrameReader("x");
        reader.Append([.. Part("x", FrameA, true, "\n"), .. Part("x", FrameB, false, "\n"), .. Encoding.ASCII.GetBytes("--x\n")]);

        var frames = Drain(reader);

        Assert.Equal(2, frames.Count);
        Assert.Equal(FrameA, frames[0]);
        Assert.Equal(FrameB, frames[1]);
    }

    [Fact]
    public void S472_junk_before_the_first_boundary_is_skipped()
    {
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Encoding.ASCII.GetBytes("preamble nobody asked for\r\n"), .. Part("frame", FrameA, true)]);

        Assert.Equal(FrameA, Assert.Single(Drain(reader)));
    }

    [Fact]
    public void S472_a_length_is_trusted_over_delimiter_bytes_inside_the_picture()
    {
        // A JPEG can contain the boundary bytes by chance. The length is how the reader steps over them.
        byte[] tricky = [0xFF, 0xD8, .. Encoding.ASCII.GetBytes("\r\n--frame\r\n"), 0xFF, 0xD9];
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Part("frame", tricky, true), .. Part("frame", FrameA, true)]);

        var frames = Drain(reader);

        Assert.Equal(2, frames.Count);
        Assert.Equal(tricky, frames[0]);
        Assert.Equal(FrameA, frames[1]);
    }

    [Fact]
    public void S472_a_headerless_part_starts_at_the_jpeg_marker()
    {
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Encoding.ASCII.GetBytes("--frame\r\n"), .. FrameA, .. Encoding.ASCII.GetBytes("\r\n--frame\r\n")]);

        Assert.Equal(FrameA, Assert.Single(Drain(reader)));
    }

    [Fact]
    public void S472_an_empty_keepalive_part_is_not_a_frame()
    {
        var reader = new MjpegFrameReader("frame");
        reader.Append([.. Part("frame", [], true), .. Part("frame", FrameA, true)]);

        Assert.Equal(FrameA, Assert.Single(Drain(reader)));
    }

    [Fact]
    public void S472_without_a_boundary_frames_are_cut_at_the_jpeg_markers()
    {
        var reader = new MjpegFrameReader(null);
        byte[] stream = [.. Encoding.ASCII.GetBytes("--whatever\r\nContent-Type: image/jpeg\r\n\r\n"), .. FrameA, .. Encoding.ASCII.GetBytes("\r\n--whatever\r\n\r\n"), .. FrameB];

        var frames = FeedInChunks(reader, stream, 5);

        Assert.Equal(2, frames.Count);
        Assert.Equal(FrameA, frames[0]);
        Assert.Equal(FrameB, frames[1]);
    }

    [Fact]
    public void S472_a_stream_that_never_delimits_overflows_instead_of_growing_forever()
    {
        var reader = new MjpegFrameReader("frame", maxFrameBytes: 1024);
        reader.Append(Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\n\r\n"));

        var junk = new byte[512];
        for (var i = 0; i < 20 && !reader.Overflowed; i++)
        {
            reader.Append(junk);
            Drain(reader);
        }

        Assert.True(reader.Overflowed);
        Assert.False(reader.TryReadFrame(out _));
    }

    [Fact]
    public void S472_a_declared_length_past_the_cap_overflows_at_once()
    {
        var reader = new MjpegFrameReader("frame", maxFrameBytes: 1024);
        reader.Append(Encoding.ASCII.GetBytes("--frame\r\nContent-Length: 999999\r\n\r\n"));

        Assert.False(reader.TryReadFrame(out _));
        Assert.True(reader.Overflowed);
    }

    [Fact]
    public void S472_a_long_stream_does_not_accumulate_what_it_has_already_handed_out()
    {
        var reader = new MjpegFrameReader("frame");
        var part = Part("frame", FrameB, true);
        var count = 0;

        for (var i = 0; i < 2000; i++)
        {
            reader.Append(part);
            count += Drain(reader).Count;
        }

        Assert.Equal(2000, count);
        Assert.True(reader.Buffered < part.Length);
    }

    [Fact]
    public void S472_junk_with_no_delimiter_is_discarded_not_hoarded()
    {
        var reader = new MjpegFrameReader("frame");
        var junk = new byte[4096];

        for (var i = 0; i < 100; i++)
        {
            reader.Append(junk);
            Drain(reader);
        }

        Assert.False(reader.Overflowed);
        Assert.True(reader.Buffered < 16);
    }
}
