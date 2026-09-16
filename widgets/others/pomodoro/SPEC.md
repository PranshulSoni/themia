# Pomodoro: SPEC

Experimental widget. Focus/break countdown timer. Run state is
ephemeral (resets on reload); durations live in config. Mirror `[S-NN]` in tests.

- **[S-01]** Config: `workMin` (default 25), `breakMin` (default 5), clamped to
  1..180 minutes in the config UI.
- **[S-02]** Start/pause toggles the 1 Hz countdown (`createPolling`, paused when
  the app is inactive). Reset stops and returns to a fresh work phase.
- **[S-03]** On reaching zero the phase flips (work↔break) and the new phase's
  full duration is loaded; the timer keeps running.
- **[S-04]** The progress bar reflects elapsed fraction of the current phase.
- **[S-05]** Header toggled via the widget context menu.
- **[S-06]** The phase flip in [S-03] sounds an audible alarm, announcing the
  phase being ENTERED: the cue ascends into focus and descends into a break.
  Only the running instance alarms (run state is per-window, [S-02]), so the same
  widget shown on two monitors cannot echo. Nothing sounds on start, pause or
  reset.
- **[S-07]** Config: `alarmSound` (`off` plus twelve ringtones, listed gentle →
  insistent: `chime`, `marimba`, `crystal`, `pluck`, `droplet`, `birdsong`,
  `zen`, `bell`, `gong`, `arcade`, `digital`, `buzzer`; default `chime`) and
  `alarmVolume` (0..100, default 70; the row is hidden while
  the sound is `off`). Both resolve to their default when the persisted value is
  missing or invalid. Changing either previews the resulting cue.
- **[S-08]** All sounds are level-matched to one peak bound, so the choice is
  about character rather than loudness, and no sound clips at volume 100.
