using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>The Pomodoro settings rows. Each writes straight to the live config and saves.</summary>
internal static partial class WidgetSettings
{
    public static void Pomodoro(List<FrameworkElement> rows, PomodoroConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Pomodoro"));

        // [S-01] Seeded from the clamped accessors, so a value that got onto disk out of range shows
        // as the value actually in use rather than snapping when the slider is first touched.
        rows.Add(FluentLayout.Row(Glyphs.Pomodoro, "Focus", "How long a work phase runs",
            FluentLayout.Slider(config.EffectiveWorkMinutes, 1, 180, 1, " min",
                value => { config.WorkMinutes = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Break", "How long a break runs",
            FluentLayout.Slider(config.EffectiveBreakMinutes, 1, 180, 1, " min",
                value => { config.BreakMinutes = (int)Math.Round(value); save(); })));

        // [S-06] The cue sounds on the phase change only — not on start, pause or reset.
        rows.Add(FluentLayout.Row(Glyphs.Sound, "Sound", "A short cue when the phase changes",
            FluentLayout.Toggle(config.Alarm, value => { config.Alarm = value; save(); })));
    }
}
