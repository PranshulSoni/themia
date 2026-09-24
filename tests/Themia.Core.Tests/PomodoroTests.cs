using System.Buffers.Binary;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The pomodoro cycle. Pure, so the flip at the end of a phase is reachable without running a widget
/// for twenty-five minutes — which is the only reason [S-03] and [S-06] are testable at all.
/// </summary>
public class PomodoroCycleTests
{
    private const int Work = 1500; // 25 min
    private const int Break = 300; //  5 min

    // -----------------------------------------------------------------------------------------
    // [S-01] durations, clamped
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(25, 1500)]
    [InlineData(1, 60)]
    [InlineData(180, 10800)]
    [InlineData(0, 60)]         // a persisted 0 would otherwise flip once a second for ever
    [InlineData(-5, 60)]
    [InlineData(9999, 10800)]
    public void S01_a_phase_length_is_clamped_to_one_to_a_hundred_and_eighty_minutes(int configured, int expectedSeconds)
    {
        var config = new PomodoroConfig { WorkMinutes = configured, BreakMinutes = configured };

        Assert.Equal(expectedSeconds, PomodoroCycle.PhaseSeconds(PomodoroPhase.Work, config));
        Assert.Equal(expectedSeconds, PomodoroCycle.PhaseSeconds(PomodoroPhase.Break, config));
    }

    [Fact]
    public void S02_a_fresh_cycle_is_a_full_work_phase()
    {
        var state = PomodoroCycle.Fresh(new PomodoroConfig { WorkMinutes = 30, BreakMinutes = 7 });

        Assert.Equal(PomodoroPhase.Work, state.Phase);
        Assert.Equal(1800, state.RemainingSeconds);
    }

    // -----------------------------------------------------------------------------------------
    // [S-03] the flip
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S03_a_tick_with_time_left_only_decrements()
    {
        Assert.Equal(
            new PomodoroTick(PomodoroPhase.Work, 1499, Flipped: false),
            PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Work, Work), Work, Break));

        Assert.Equal(
            new PomodoroTick(PomodoroPhase.Break, 1, Flipped: false),
            PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Break, 2), Work, Break));
    }

    [Fact]
    public void S03_work_flips_to_a_full_break_and_keeps_running()
        => Assert.Equal(
            new PomodoroTick(PomodoroPhase.Break, Break, Flipped: true),
            PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Work, 1), Work, Break));

    [Fact]
    public void S03_break_flips_to_a_full_work_phase_and_keeps_running()
        => Assert.Equal(
            new PomodoroTick(PomodoroPhase.Work, Work, Flipped: true),
            PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Break, 1), Work, Break));

    [Fact]
    public void S03_one_remaining_is_the_last_second_not_a_spare_one()
    {
        // The off-by-one that would show "0:00" for a whole second before flipping.
        var tick = PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Work, 1), 60, 60);

        Assert.True(tick.Flipped);
        Assert.Equal(60, tick.RemainingSeconds);
    }

    [Fact]
    public void S03_it_flips_exactly_once_per_phase_across_a_whole_cycle()
    {
        var state = new PomodoroState(PomodoroPhase.Work, 3);
        var flips = new List<PomodoroPhase>();

        // 3 → 2 → 1 → flip(break, 2) → 1 → flip(work, 2) → 1
        for (var i = 0; i < 6; i++)
        {
            var tick = PomodoroCycle.Advance(state, 2, 2);

            if (tick.Flipped)
            {
                flips.Add(tick.Phase);
            }

            state = new PomodoroState(tick.Phase, tick.RemainingSeconds);
        }

        Assert.Equal(new[] { PomodoroPhase.Break, PomodoroPhase.Work }, flips);
    }

    [Fact]
    public void S03_a_zero_length_phase_still_loads_at_least_one_second()
    {
        // Defence against the 1 Hz alarm loop: a phase of no seconds would flip on every tick.
        var tick = PomodoroCycle.Advance(new PomodoroState(PomodoroPhase.Work, 1), 0, 0);

        Assert.True(tick.Flipped);
        Assert.Equal(1, tick.RemainingSeconds);
    }

    // -----------------------------------------------------------------------------------------
    // [S-04] progress
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1500, 1500, 0.0)]   // nothing elapsed at the start of a phase
    [InlineData(750, 1500, 0.5)]
    [InlineData(1, 1500, 0.999333)]
    [InlineData(0, 1500, 1.0)]      // full at the flip
    public void S04_progress_is_the_elapsed_fraction_of_the_phase(int remaining, int total, double expected)
        => Assert.Equal(expected, PomodoroCycle.Progress(new PomodoroState(PomodoroPhase.Work, remaining), total), 5);

    [Fact]
    public void S04_progress_stays_within_range_when_the_durations_change_mid_phase()
    {
        // Shortening the phase in settings while it runs leaves `remaining` above the new total for
        // one tick. An unclamped fraction would drive the bar backwards past its own start.
        Assert.Equal(0.0, PomodoroCycle.Progress(new PomodoroState(PomodoroPhase.Work, 5000), 1500));
        Assert.Equal(1.0, PomodoroCycle.Progress(new PomodoroState(PomodoroPhase.Work, -20), 1500));
        Assert.Equal(0.0, PomodoroCycle.Progress(new PomodoroState(PomodoroPhase.Work, 10), 0));
    }

    [Theory]
    [InlineData(1500, "25:00")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(5, "0:05")]
    [InlineData(0, "0:00")]
    [InlineData(-3, "0:00")]        // never renders a negative clock
    [InlineData(10800, "180:00")]
    public void S02_the_readout_is_minutes_and_padded_seconds(int remaining, string expected)
        => Assert.Equal(expected, PomodoroCycle.Clock(remaining));
}

/// <summary>[S-06] the phase-flip cue.</summary>
public class PomodoroAlarmTests
{
    [Fact]
    public void S06_the_cue_ascends_into_focus_and_descends_into_a_break()
    {
        var work = PomodoroAlarm.Notes(PomodoroPhase.Work);
        var rest = PomodoroAlarm.Notes(PomodoroPhase.Break);

        // The direction is the whole message: it says which phase is starting without being read.
        Assert.Equal(work, rest.Reverse());
        Assert.True(work[0] < work[1] && work[1] < work[2], "the focus cue must ascend");
        Assert.True(rest[0] > rest[1] && rest[1] > rest[2], "the break cue must descend");
    }

    [Theory]
    [InlineData(PomodoroPhase.Work)]
    [InlineData(PomodoroPhase.Break)]
    public void S06_the_cue_is_a_well_formed_16_bit_mono_wave(PomodoroPhase phase)
    {
        var wave = PomodoroAlarm.BuildWave(phase);
        var span = wave.AsSpan();

        Assert.Equal("RIFF"u8.ToArray(), span[..4].ToArray());
        Assert.Equal("WAVE"u8.ToArray(), span[8..12].ToArray());
        Assert.Equal("fmt "u8.ToArray(), span[12..16].ToArray());
        Assert.Equal("data"u8.ToArray(), span[36..40].ToArray());

        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(span[20..]));                    // PCM
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(span[22..]));                    // mono
        Assert.Equal(PomodoroAlarm.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(span[24..]));
        Assert.Equal((short)16, BinaryPrimitives.ReadInt16LittleEndian(span[34..]));                   // bit depth

        // The two declared sizes must agree with the buffer, or every player truncates or overruns.
        var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(span[40..]);
        Assert.Equal(wave.Length - 44, dataBytes);
        Assert.Equal(wave.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(span[4..]));
        Assert.Equal(0, dataBytes % 2);
    }

    [Theory]
    [InlineData(PomodoroPhase.Work)]
    [InlineData(PomodoroPhase.Break)]
    public void S06_no_sample_reaches_the_peak_bound_so_the_cue_cannot_clip(PomodoroPhase phase)
    {
        var wave = PomodoroAlarm.BuildWave(phase);
        var bound = (int)Math.Ceiling(PomodoroAlarm.Peak * short.MaxValue);
        var loudest = 0;

        for (var offset = 44; offset + 1 < wave.Length; offset += 2)
        {
            loudest = Math.Max(loudest, Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset))));
        }

        Assert.True(loudest <= bound, $"peak {loudest} exceeded the bound {bound}");

        // And it is actually audible rather than a buffer of silence.
        Assert.True(loudest > bound / 4, $"peak {loudest} is too quiet to be a cue");
    }

    [Fact]
    public void S06_the_cue_starts_and_ends_in_silence()
    {
        // A waveform that starts or stops mid-swing clicks, which is the one thing a gentle cue
        // must not do.
        var wave = PomodoroAlarm.BuildWave(PomodoroPhase.Work);

        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(wave.Length - 2)));
    }

    [Fact]
    public void S06_the_cue_is_short_enough_to_be_a_notification()
    {
        var wave = PomodoroAlarm.BuildWave(PomodoroPhase.Work);
        var seconds = (wave.Length - 44) / 2.0 / PomodoroAlarm.SampleRate;

        Assert.InRange(seconds, 0.2, 1.5);
    }
}
