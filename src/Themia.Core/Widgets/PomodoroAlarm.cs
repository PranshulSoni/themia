using System.Buffers.Binary;

namespace Themia.Core.Widgets;

/// <summary>
/// The phase-flip cue, synthesised as a small PCM wave. [S-06]
/// <para>
/// Synthesised rather than shipped as an asset, and built here rather than in the view, for three
/// reasons: a .wav in the package is bytes the AOT build cannot shrink, the cue has to differ by
/// direction (ascending into focus, descending into a break) which is a property of the notes rather
/// than of the file, and as a pure function the whole thing is testable without a sound card.
/// </para>
/// <para>
/// This is not the original's twelve Web Audio ringtones. There is no Web Audio here, and a
/// synthesiser with twelve voices is a feature in its own right, not a port of one; the setting is
/// therefore a plain on/off (<see cref="Themia.Core.Model.PomodoroConfig.Alarm"/>) and this is the
/// one cue it plays.
/// </para>
/// </summary>
public static class PomodoroAlarm
{
    /// <summary>Samples per second. CD rate: every Windows audio path takes it without resampling.</summary>
    public const int SampleRate = 44100;

    /// <summary>
    /// Peak amplitude as a fraction of full scale. Well under 1, so the cue never clips and never
    /// startles: it is a notification, not an alert.
    /// </summary>
    public const double Peak = 0.35;

    private const int NoteMilliseconds = 150;
    private const int GapMilliseconds = 30;
    private const int AttackMilliseconds = 8;

    /// <summary>
    /// The notes of the cue, in the order they sound. Ascending into focus and descending into a
    /// break, so the direction alone says which phase is starting without anyone having to look.
    /// <para>D5, F#5, A5 — a plain D major triad, which is unambiguous on any speaker.</para>
    /// </summary>
    public static double[] Notes(PomodoroPhase entering) => entering == PomodoroPhase.Work
        ? [587.33, 739.99, 880.00]
        : [880.00, 739.99, 587.33];

    /// <summary>
    /// The cue for the phase being <b>entered</b>, as a complete 16-bit mono RIFF/WAVE file.
    /// <para>
    /// A whole file rather than raw samples because the only player that needs no dependency and no
    /// AOT-hostile projection — <c>PlaySound</c> with <c>SND_MEMORY</c> — takes a file image.
    /// </para>
    /// </summary>
    public static byte[] BuildWave(PomodoroPhase entering)
    {
        var notes = Notes(entering);
        var noteSamples = SampleRate * NoteMilliseconds / 1000;
        var gapSamples = SampleRate * GapMilliseconds / 1000;
        var attackSamples = Math.Max(1, SampleRate * AttackMilliseconds / 1000);
        var totalSamples = notes.Length * (noteSamples + gapSamples);

        const int HeaderBytes = 44;
        var dataBytes = totalSamples * 2;
        var buffer = new byte[HeaderBytes + dataBytes];

        WriteHeader(buffer, dataBytes);

        var offset = HeaderBytes;

        foreach (var frequency in notes)
        {
            var step = 2 * Math.PI * frequency / SampleRate;

            for (var i = 0; i < noteSamples; i++)
            {
                // Attack and decay both taper to silence, so no note starts or ends on a step — a
                // discontinuity in a waveform is audible as a click, which is exactly the sound a
                // gentle cue must not make.
                var attack = Math.Min(1.0, i / (double)attackSamples);
                var decay = 1.0 - (i / (double)noteSamples);
                var value = Math.Sin(i * step) * attack * decay * decay * Peak;

                BinaryPrimitives.WriteInt16LittleEndian(
                    buffer.AsSpan(offset),
                    (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue));

                offset += 2;
            }

            // The gap is left as the zeros the array already holds.
            offset += gapSamples * 2;
        }

        return buffer;
    }

    private static void WriteHeader(byte[] buffer, int dataBytes)
    {
        const short Channels = 1;
        const short BitsPerSample = 16;
        const short BlockAlign = Channels * BitsPerSample / 8;

        var span = buffer.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);

        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);       // PCM fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);        // PCM, uncompressed
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);

        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
    }
}
