# Mood Tracker: SPEC

Log how you feel, when you felt it, and the thought behind it; read the pattern
back as a calendar heatmap. Requested in themia-releases#90.

State is persisted in the widget's own config bag (`entries`), per-instance —
the same local-storage shape the To Do widget's `local` mode uses ([todo S-41]),
so nothing here touches Tauri, IPC or an account. Mirror `[S-NN]` tags in tests.

- **[S-01]** The scale is a FIXED five points (1 awful … 5 great), one lucide
  face each. Levels persist as numbers, so a user-resizable scale would silently
  re-interpret existing history; the names come from `moodTracker.level.*` so
  they translate.
- **[S-02]** `entries: {id, at, level, note}[]` is read through `parseEntries`,
  which owns the trust boundary: a non-array (absent / corrupt config) yields
  `[]`; an entry with a non-string `id`/`at`, an unparseable `at`, or a `level`
  outside 1–5 is DROPPED while its valid siblings are kept; a non-string `note`
  becomes `''`. The result is always sorted newest-first. Never throws.
- **[S-03]** One click on a face records an entry stamped with the instant of
  the click (`new Date()`, not the polled clock) and persists via
  `updateWidgetConfig` merged onto the existing config, never replacing it.
  Clicking again logs a SECOND entry — the widget is a timestamped log, not a
  once-a-day checkbox. The list is capped at `MAX_ENTRIES` (2000) by pruning the
  oldest, because it lives inside the layout file that every widget move
  rewrites.
- **[S-04]** The face that reads as selected, and the entry the thought box
  edits, is the most recent entry on the LOCAL current day (`latestOn`); with
  nothing logged today, no face is selected.
- **[S-05]** The thought box writes on `change` (blur / Enter), not per
  keystroke: its value is read straight from config, so a per-keystroke write
  would fight the caret. It is disabled, with a distinct placeholder, until
  something is logged today. Notes are trimmed. Hidden by `showNote: false`.
- **[S-06]** Day keys are LOCAL calendar days (`dateKey`), not UTC, so a mood
  logged at 11pm counts for that day. `dayTotals` returns one slot per day for
  the window ending today (inclusive), oldest first, with `count: 0` for days
  that hold nothing — entries outside the window are ignored, never folded into
  an edge day.
- **[S-07]** A day's heat bucket is `0` when nothing was logged, otherwise its
  average rounded into 1–5. A logged day is NEVER bucket 0, so an awful day and
  an unlogged day never share a colour. The five buckets are mixed from the
  theme's `--destructive` / `--warning` / `--success` tokens (no colour
  literals), and bucket 0 is `--border`.
- **[S-08]** The heatmap is 7 cells wide and the window is wrapped
  chronologically, so a FIXED weekday lands in each column — no week-start
  setting; the window's own dates decide. Leading `null` slots pad the window so
  it ends at the end of a row, and the weekday header takes one sample date per
  column. Today's cell carries an inset ring.
- **[S-09]** The footer average is the mean level over every entry in the
  window, or absent (replaced by the empty hint) when the window holds none —
  never a misleading `0`.
- **[S-10]** The footer streak is consecutive days ending today on which
  something was logged. Today not yet logged does NOT break it (the day isn't
  over); an earlier gap does. It is bounded by the shown window. Both figures
  are hidden by `showStats: false`.
- **[S-11]** `historyWeeks` is one of 4 / 8 / 12 / 26 (default 8), narrowed by
  `parseHistoryWeeks`; anything else falls back to the default.
- **[S-12]** Hovering a day shows its date, average and every entry on it with
  the thought attached, as a native multi-line tooltip. Right-clicking a day
  lists those entries in the context menu; deleting one sits behind a submenu,
  so a stray click on the list can never destroy an entry. A day with nothing
  logged opens no menu.
- **[S-13]** The widget re-reads the clock every 60 s so the window rolls over
  at midnight without a remount (same rule as Habit Tracker `[S-08]`).
- **[S-14]** Clearing all history from the config panel confirms through the
  native `ask` dialog first, and the button is disabled when there is nothing to
  clear.
- **[S-15]** Header toggled via the widget context menu.
- **[S-16]** `storageMode` names where entries are kept, narrowed by
  `parseStorageMode`; anything unrecognised falls back to the default. It is a
  closed set, so adding a second backend is a compile error at every switch
  rather than a silently-unhandled string. Today `local` is the only member.
  The config panel's Source tab shows it through the SAME `AccountToggleList`
  the To Do widget uses ([todo S-47]), with "On this device" as the leading
  row — but its switch is DISABLED, because a widget with one source has
  nothing to switch to and an enabled switch would invite a click that could
  only take the widget's storage away. `providerFilter` is empty, so no account
  rows are fetched.
- **[S-17]** The config panel is split Source / Display like To Do's: Source
  holds the storage row and the history count + clear action ([S-14]); Display
  holds the window length ([S-11]) and the two visibility toggles.
