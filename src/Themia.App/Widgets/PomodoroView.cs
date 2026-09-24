using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    /// <summary>The Fluent icon size, everywhere one appears in this widget.</summary>
    private const double IconSize = 16;

    private readonly TextBlock _title = new();
    private readonly Grid _header;
    private readonly TextBlock _phase;
    private readonly FontIcon _phaseIcon = new();
    private readonly StackPanel _phaseRow;
    private readonly TextBlock _clock;
    private readonly Grid _clockHost = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _runButton = new();
    private readonly FontIcon _runIcon = new();
    private readonly Button _resetButton = new();
    private readonly FontIcon _resetIcon = new();
    private readonly StackPanel _controls;

    // The box the countdown is sized against, so a layout pass that changed nothing does not
    // re-pick the ramp step.
    private double _clockWidth;
    private double _clockHeight;
    private int _clockCharacters;

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

        // The standard pane header: a 16px icon and a Body Strong title in the secondary text
        // colour, at a fixed ramp step rather than a fraction of the tile.
        _header = Fluent.Header(Glyphs.Pomodoro, _title);
        _header.Margin = new Thickness(0, 0, 0, Fluent.Space2);

        Grid.SetRow(_header, 0);

        // The phase reads as a Fluent badge line — a 16px icon and one Body Strong word in the
        // phase's own theme colour — rather than as a pill this file drew for itself.
        _phase = Fluent.Text(FluentType.BodyStrong);
        _phase.VerticalAlignment = VerticalAlignment.Center;

        _phaseIcon.FontSize = IconSize;
        _phaseIcon.Margin = new Thickness(0, 0, Fluent.Space1, 0);
        _phaseIcon.VerticalAlignment = VerticalAlignment.Center;

        _phaseRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _phaseIcon, _phase },
        };

        Grid.SetRow(_phaseRow, 1);

        // The countdown is the hero of the tile: the largest ramp step that fits, and past the top
        // of the ramp a multiple of its Display step that keeps Fluent's own line-height ratio.
        _clock = Fluent.Text(FluentType.Display);
        _clock.HorizontalAlignment = HorizontalAlignment.Center;
        _clock.VerticalAlignment = VerticalAlignment.Center;

        // Tabular figures, so the seconds do not shuffle the whole readout sideways once a second.
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_clock, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);

        // Its own host, so the countdown is sized against the room actually left over rather than
        // against a guessed fraction of the whole tile.
        _clockHost.Children.Add(_clock);
        _clockHost.SizeChanged += (_, _) => ApplyTypeScale();

        Grid.SetRow(_clockHost, 2);

        // [S-04] The bar is the elapsed fraction of the phase, so a glance says how far in you are
        // without reading the digits. A stock ProgressBar: its height, track and corner radius are
        // the theme's, not this file's.
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid.SetRow(_progress, 3);

        // Stock Windows 11 buttons with a 16px FontIcon for content, square on the 4px grid. The
        // primary action takes the accent style, the way the confirming button of any Windows
        // dialog does.
        _runIcon.FontSize = IconSize;
        _runButton.Content = _runIcon;
        _runButton.Padding = new Thickness(0);
        _runButton.Click += (_, _) => SetRunning(!_running);

        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle)
            && accentStyle is Style style)
        {
            _runButton.Style = style;
        }
        else
        {
            Log.Warn(Scope, "AccentButtonStyle is missing; the start button falls back to the standard button style");
        }

        _resetIcon.FontSize = IconSize;
        _resetIcon.Glyph = Glyphs.Reset;
        _resetButton.Content = _resetIcon;
        _resetButton.Padding = new Thickness(0);
        _resetButton.Click += (_, _) => Reset();

        ToolTipService.SetToolTip(_resetButton, "Reset");

        _controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = Fluent.Space2,
            Children = { _runButton, _resetButton },
        };

        Grid.SetRow(_controls, 4);

        Children.Add(_header);
        Children.Add(_phaseRow);
        Children.Add(_clockHost);
        Children.Add(_progress);
        Children.Add(_controls);

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
        // colour as well as by the word. Accent *text* fill rather than the accent control fill:
        // the two differ precisely so that coloured text stays legible on a card.
        var badge = work
            ? FluentLayout.Themed("AccentTextFillColorPrimaryBrush", Colors.DodgerBlue)
            : FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.MediumSeaGreen);

        _phase.Foreground = badge;
        _phaseIcon.Foreground = badge;

        // The bar keeps the control fill, which is the colour Windows fills a progress bar with.
        _progress.Foreground = work
            ? FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue)
            : FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.MediumSeaGreen);

        _runIcon.Glyph = _running ? Glyphs.Pause : Glyphs.Play;
        ToolTipService.SetToolTip(_runButton, _running ? "Pause" : "Start");

        // A longer phase is a longer readout, so the step is re-picked when the digits change
        // width. The guard inside makes the once-a-second call free when they have not.
        ApplyTypeScale();
    }

    /// <summary>
    /// Decide what a tile this size shows, and which ramp step the countdown is drawn at.
    /// <para>
    /// Only the countdown scales, and it scales by choosing a step. Everything else is a fixed
    /// Fluent size — a 16px icon, a Body Strong line, a 32 or 40px square button — and a tile too
    /// short for all of it drops parts rather than shrinking them, which is what Windows does and
    /// what sizing every element to a fraction of the tile did not.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        var height = ActualHeight;

        // Under about 90 DIPs the title is most of the widget; under 140 the phase word is
        // competing with the digits for the last usable line.
        _header.Visibility = height < 90 ? Visibility.Collapsed : Visibility.Visible;
        _phaseRow.Visibility = height < 140 ? Visibility.Collapsed : Visibility.Visible;

        var roomy = height >= 140;

        _phaseRow.Margin = new Thickness(0, 0, 0, Fluent.Space1);
        _progress.Margin = new Thickness(0, roomy ? Fluent.Space3 : Fluent.Space2, 0, roomy ? Fluent.Space3 : Fluent.Space2);
        _controls.Margin = new Thickness(0, roomy ? Fluent.Space1 : 0, 0, 0);

        // The standard Windows control height, and its compact variant on a small tile. Square,
        // because the content is one 16px glyph.
        var side = height >= 260 ? Fluent.RowHeight : Fluent.RowHeightCompact;

        _runButton.Width = side;
        _runButton.Height = side;
        _resetButton.Width = side;
        _resetButton.Height = side;

        var width = _clockHost.ActualWidth;
        var clockHeight = _clockHost.ActualHeight;

        // "25:00" is five, "120:00" is six — the readout's own length, so a long phase does not
        // overrun the tile that a short one fitted.
        var characters = Math.Max(5, _clock.Text.Length);

        // A layout pass that moved nothing does not re-pick the step. This runs on every tick as
        // well as on every resize, and re-applying the ramp once a second is work for nothing.
        if (Math.Abs(width - _clockWidth) < 0.5
            && Math.Abs(clockHeight - _clockHeight) < 0.5
            && characters == _clockCharacters)
        {
            return;
        }

        _clockWidth = width;
        _clockHeight = clockHeight;
        _clockCharacters = characters;

        _clock.Apply(FluentType.Hero(width, clockHeight, characters));
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
