using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// A focus and break timer.
/// <para>
/// The run state — phase, seconds left, whether it is going — lives in this instance and nowhere
/// else. It is deliberately not persisted and not shared ([S-02]): the same widget shown on two
/// monitors is two independent timers, which is also why only the instance that is actually running
/// can sound the cue, so a two-monitor desktop cannot echo it.
/// </para>
/// </summary>
internal sealed partial class PomodoroView : Grid, IWidgetView
{
    private const string Scope = "pomodoro";

    private readonly TextBlock _title = new();
    private readonly FontIcon _titleIcon = new();
    private readonly TextBlock _phase = new();
    private readonly FontIcon _phaseIcon = new();
    private readonly TextBlock _clock = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _runButton = new();
    private readonly FontIcon _runIcon = new();
    private readonly Button _resetButton = new();
    private readonly FontIcon _resetIcon = new();

    private Ticker? _ticker;
    private PomodoroConfig _config = new();
    private PomodoroState _state;
    private IDisposable? _subscription;

    /// <summary>
    /// Swallows the callback <see cref="Ticker.Subscribe"/> makes straight away.
    /// <para>
    /// That immediate fire exists so a clock paints the moment it appears, which is right for a view
    /// that renders the time. This subscriber does not render a tick, it <i>is</i> the tick, so
    /// letting the subscribe-time call through would take a second off the clock the instant the user
    /// pressed start.
    /// </para>
    /// </summary>
    private bool _skipFirstTick;

    private bool _running;

    public PomodoroView()
    {
        // The card no longer pads its content; a view that is not a list carries its own inset.
        Padding = WidgetLayout.Inset;

        _state = PomodoroCycle.Fresh(_config);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // title
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // phase
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // clock
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // progress
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // controls

        _title.Text = "Pomodoro";
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;

        _titleIcon.Glyph = Glyphs.Pomodoro;
        _titleIcon.Foreground = _title.Foreground;
        _titleIcon.Margin = new Thickness(0, 0, 6, 0);
        _titleIcon.VerticalAlignment = VerticalAlignment.Center;

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _titleIcon, _title },
        };

        Grid.SetRow(header, 0);

        _phase.VerticalAlignment = VerticalAlignment.Center;
        _phaseIcon.Margin = new Thickness(0, 0, 5, 0);
        _phaseIcon.VerticalAlignment = VerticalAlignment.Center;

        var phaseRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            Children = { _phaseIcon, _phase },
        };

        Grid.SetRow(phaseRow, 1);

        _clock.FontWeight = FontWeights.SemiBold;
        _clock.Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        _clock.HorizontalAlignment = HorizontalAlignment.Center;
        _clock.VerticalAlignment = VerticalAlignment.Center;

        // Tabular figures, so the seconds do not shuffle the whole readout sideways once a second.
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_clock, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);

        Grid.SetRow(_clock, 2);

        // [S-04] The bar is the elapsed fraction of the phase, so a glance says how far in you are
        // without reading the digits.
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.HorizontalAlignment = HorizontalAlignment.Stretch;
        _progress.Margin = new Thickness(0, 4, 0, 6);

        Grid.SetRow(_progress, 3);

        _runButton.Content = _runIcon;
        _runButton.Padding = new Thickness(0);
        _runButton.Click += (_, _) => SetRunning(!_running);

        _resetButton.Content = _resetIcon;
        _resetButton.Padding = new Thickness(0);
        _resetIcon.Glyph = Glyphs.Reset;
        _resetButton.Click += (_, _) => Reset();

        ToolTipService.SetToolTip(_resetButton, "Reset");

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children = { _runButton, _resetButton },
        };

        Grid.SetRow(controls, 4);

        Children.Add(header);
        Children.Add(phaseRow);
        Children.Add(_clock);
        Children.Add(_progress);
        Children.Add(controls);

        SizeChanged += (_, _) => ApplyTypeScale();
        Render();
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as PomodoroConfig ?? new PomodoroConfig();

        // While the timer is stopped the clock follows the configured length, so changing it in
        // settings moves the digits and the bar together. While it is running the countdown owns
        // the remaining seconds and reloads them itself at the next flip; overwriting them here
        // would restart the phase under the user.
        if (!_running)
        {
            _state = new PomodoroState(_state.Phase, PomodoroCycle.PhaseSeconds(_state.Phase, _config));
        }

        Render();
    }

    /// <summary>
    /// [S-02] Start and pause. The subscription exists only while the timer runs: a paused pomodoro
    /// has nothing to do once a second, and the shared ticker stops entirely once nothing is
    /// subscribed.
    /// </summary>
    private void SetRunning(bool running)
    {
        if (_running == running)
        {
            return;
        }

        _running = running;

        if (running)
        {
            _skipFirstTick = true;
            _subscription = _ticker?.Subscribe(TimeSpan.FromSeconds(1), Tick);
        }
        else
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        Render();
    }

    /// <summary>[S-02] Reset stops and returns to a fresh work phase. Nothing sounds. [S-06]</summary>
    private void Reset()
    {
        SetRunning(false);
        _state = PomodoroCycle.Fresh(_config);
        Render();
    }

    private void Tick()
    {
        if (_skipFirstTick)
        {
            _skipFirstTick = false;
            return;
        }

        if (!_running)
        {
            return;
        }

        var tick = PomodoroCycle.Advance(
            _state,
            PomodoroCycle.PhaseSeconds(PomodoroPhase.Work, _config),
            PomodoroCycle.PhaseSeconds(PomodoroPhase.Break, _config));

        _state = new PomodoroState(tick.Phase, tick.RemainingSeconds);

        // [S-06] The flip is the only thing that sounds, and it announces the phase being ENTERED.
        if (tick.Flipped && _config.Alarm)
        {
            PomodoroSound.Play(tick.Phase);
        }

        Render();
    }

    private void Render()
    {
        var work = _state.Phase == PomodoroPhase.Work;
        var total = PomodoroCycle.PhaseSeconds(_state.Phase, _config);

        _clock.Text = PomodoroCycle.Clock(_state.RemainingSeconds);
        _phase.Text = PomodoroCycle.PhaseLabel(_state.Phase);
        _phaseIcon.Glyph = work ? Glyphs.Pomodoro : Glyphs.Timer;
        _progress.Value = PomodoroCycle.Progress(_state, total) * 100;

        // Which phase you are in is the first thing to read off this widget, so it is carried by
        // colour as well as by the word. Both are theme tokens, so they follow light, dark and the
        // user's accent.
        var accent = work
            ? FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue)
            : FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.MediumSeaGreen);

        _phase.Foreground = accent;
        _phaseIcon.Foreground = accent;
        _progress.Foreground = accent;

        _runIcon.Glyph = _running ? Glyphs.Pause : Glyphs.Play;
        ToolTipService.SetToolTip(_runButton, _running ? "Pause" : "Start");
    }

    /// <summary>
    /// Size the readout to the tile. The clock leads and everything else stays support, because a
    /// timer whose label is as big as its digits reads as two headlines.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        _clock.FontSize = TypeScale.Fit(width, height, 0.26, 0.20, 16, 96);
        _title.FontSize = TypeScale.Fit(width, height, 0.070, 0.085, 10, 18);
        _titleIcon.FontSize = _title.FontSize;
        _phase.FontSize = TypeScale.Fit(width, height, 0.080, 0.095, 11, 22);
        _phaseIcon.FontSize = _phase.FontSize;

        var button = TypeScale.Fit(width, height, 0.17, 0.16, 26, 48);
        _runButton.Width = button;
        _runButton.Height = button;
        _resetButton.Width = button;
        _resetButton.Height = button;
        _runIcon.FontSize = Math.Max(10, button * 0.42);
        _resetIcon.FontSize = _runIcon.FontSize;

        _progress.Height = Math.Clamp(height * 0.035, 3, 8);
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
        _running = false;
    }

}

/// <summary>
/// The phase-flip cue. [S-06]
/// <para>
/// <c>PlaySound</c> from winmm, handed the wave <see cref="PomodoroAlarm"/> synthesises, rather than
/// a <c>MediaPlayer</c> or an <c>AudioGraph</c>: those are whole media pipelines, initialised
/// asynchronously and rooted by the AOT compiler in full, to play half a second of a sine. This is
/// one P/Invoke and a byte array.
/// </para>
/// <para>
/// Played synchronously on a pool thread with the buffer pinned for exactly the duration of the
/// call. The asynchronous flag would return immediately and keep reading the buffer afterwards,
/// which means either leaking a pin for the life of the process or handing the sound driver a
/// pointer that the GC is free to move.
/// </para>
/// </summary>
internal static partial class PomodoroSound
{
    private const string Scope = "pomodoro-alarm";

    private const uint SndMemory = 0x0004;
    private const uint SndSync = 0x0000;

    /// <summary>Never substitute the default beep: a failed cue is better than the wrong one.</summary>
    private const uint SndNoDefault = 0x0002;

    // Two fixed cues, so the synthesis runs at most once each per session rather than once per flip.
    private static readonly Lazy<byte[]> WorkCue = new(() => PomodoroAlarm.BuildWave(PomodoroPhase.Work));
    private static readonly Lazy<byte[]> BreakCue = new(() => PomodoroAlarm.BuildWave(PomodoroPhase.Break));

    public static void Play(PomodoroPhase entering)
    {
        var wave = entering == PomodoroPhase.Work ? WorkCue.Value : BreakCue.Value;

        _ = Task.Run(() =>
        {
            var pin = GCHandle.Alloc(wave, GCHandleType.Pinned);

            try
            {
                if (!PlaySound(pin.AddrOfPinnedObject(), 0, SndMemory | SndSync | SndNoDefault))
                {
                    Log.Warn(Scope, "PlaySound refused the phase cue");
                }
            }
            catch (Exception ex)
            {
                // A silent cue is a nuisance; an unobserved exception on a pool thread is a crash.
                Log.Error(Scope, "playing the phase cue threw", ex);
            }
            finally
            {
                pin.Free();
            }
        });
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint data, nint module, uint flags);
}
