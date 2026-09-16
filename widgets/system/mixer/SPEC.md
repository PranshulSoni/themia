# Volume Mixer: SPEC

Per-application volume control for the default output device, via WASAPI audio
sessions (`IAudioSessionManager2`). Requested in themia-releases#112: "various
apps that are producing sound to the pc … an option to change their volume …
the ability to make groups for applications with multiple sources eg; chrome
with multiple tabs".

Distinct from the `volume` widget, which owns the endpoint's MASTER level only.
This widget can show that same master level as its first row ([S-08]) but its
subject is the per-app sessions. Behavior tags are stable IDs: append a new
`[S-NN]`, never renumber or reuse. Mirror the tags in test names.

## Backend (`widget-mixer`)

- **[S-01]** Commands:
  - `mixer_get_sessions(grouped)` → `MixerApp[]`
    (`{ key, name, path, level, muted, active, is_system, sessions }`).
  - `mixer_set_volume(key, level)` — level clamped 0..1.
  - `mixer_set_mute(key, muted)`.
- **[S-02]** All three are `async` + `spawn_blocking`. Enumerating sessions
  walks every process holding one, and resolving an app's display name reads
  its version resource FROM DISK the first time that binary is seen; a sync
  command would run that inline on the WebView2 main thread.
- **[S-03]** `grouped` (the issue's headline ask) collapses every session
  sharing an executable into ONE row: `level` is the loudest of the group,
  `muted` only when EVERY session is muted, `active` when ANY is, and
  `sessions` reports how many the row drives (rendered as a count chip). A
  browser with three noisy tabs is one slider. `grouped: false` keeps one row
  per session, keyed by its session instance identifier.
- **[S-04]** Row name, in the order Task Manager prefers: version-info
  FileDescription ("Google Chrome") → executable stem ("chrome") → the
  session's own display name, skipped when it is an unexpanded resource
  reference (`@%SystemRoot%\…,-202`). No usable name yields `""` and the
  FRONTEND supplies the localized placeholder — no English string is ever baked
  into the backend, including "System sounds" (`is_system` carries that).
  FileDescription lookups are cached per exe path (poisoned lock recovered, not
  unwrapped) so a poll does not re-read version resources every tick.
- **[S-05]** A set command applies to every session whose GROUP key or SESSION
  key equals the given key — the two key spaces are disjoint (an exe path, a
  session instance id, the `system-sounds` sentinel), so one command serves both
  modes without being told which produced the key. A key matching nothing is a
  no-op, NOT an error: an app can exit between the poll that drew the row and
  the drag that moves it. A session that fails mid-enumeration is skipped, never
  fatal — one dying app must not blank the whole mixer. `Expired` sessions are
  dropped (the app is gone and Windows has not reaped it yet).
- **[S-06]** Ordering is by name, case-insensitively, with system sounds pinned
  last. Deliberately NOT by activity or level: a row that reorders itself the
  moment an app starts or stops playing slides out from under the slider the
  user is dragging.
- COM is balanced per call (`ComGuard` = CoInitialize/CoUninitialize), every
  shell-allocated string is freed (`CoTaskString`), every process handle closed
  (`ProcessHandle`), and no `.unwrap()` exists outside tests.

## Display

- **[S-07]** Polls `mixer_get_sessions` on the shared system-widget interval,
  default 2 s (`widgets/system/refreshInterval.ts`, which is the ONE place that
  default lives).
- **[S-08]** `showMaster` (default on) pins the endpoint's master volume as the
  first row, reusing `volume_get` / `volume_set` / `volume_set_mute`. Its key is
  the `__master__` sentinel, disjoint from every backend key space, so the
  setters dispatch on the key instead of carrying a parallel flag. It is never
  removed by the [S-10] filter.
- **[S-09]** `showSystemSounds` (default off) includes the Windows system-sounds
  row.
- **[S-10]** `onlyActive` (default off) keeps just the apps rendering audio
  right now, rather than every app holding a session. It never hides system
  sounds, whose session is Inactive between chimes and would blink in and out.
- **[S-13]** `showIcons` (default on) draws each app's real icon, fetched once
  per executable through `get_file_icons_batch` at 40 px. A path that yields no
  icon is never re-requested, and the row falls back to a generic glyph
  (speaker for master, bell for system sounds, window for an app).
- Empty list → a localized "No app is playing audio" placeholder, never a blank
  card. A failing backend keeps the LAST list on screen rather than blanking it,
  and a repeated poll failure is logged ONCE, not once per tick: a machine with
  no output device fails every poll, which would otherwise write a log line
  every couple of seconds for as long as the widget is on the desktop. The next
  success re-arms the report.

## Interaction

- **[S-11]** Dragging a slider writes through a single-flight, newest-value-wins
  writer, one per row: the next write is sent when the previous one RESOLVES, so
  a drag emitting ~100 events per second does not queue 100 blocking commands
  and land a stale value after the user let go. It is event-driven, with no
  wall-clock throttle. Until a poll confirms the new level, the dragged value is
  held as an override so the polled (older) value cannot fight the thumb; the
  override is dropped once a poll reports that level, or once the row's app
  exits. A rejected write is logged and must not wedge the queue.
  The list renders with `<Index>`, NOT `<For>`: both a poll and a drag produce
  fresh row objects, and `For` keys by identity, so it tears down and rebuilds
  the whole list — including the `<input>` under the user's pointer. That drops
  the drag after the first mouse move, and again on every poll tick.
- **[S-12]** The mute button toggles mute for the row (all of a group's sessions
  together), then refreshes — the command resolves only after Windows applied
  it, so the next read is the truth rather than an optimistic guess. A muted row
  displays 0 % while keeping its stored level, so unmuting restores where the
  slider was; dragging a muted row above zero unmutes it, matching the volume
  widget.
- Right-clicking a row offers Mute/Unmute and Reset to 100 %.

## Verification

CI has no audio endpoint, so the WASAPI path is covered by two `#[ignore]`d
diagnostics in the crate — `cargo test -p widget-mixer -- --ignored --nocapture`
dumps the live rows and round-trips a real app's level. The grouping, ordering
and name-resolution rules are pure functions with ordinary unit tests, as are
the row filters, the level overrides and the single-flight writer on the
frontend.
