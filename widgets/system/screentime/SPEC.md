# Screen Time: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never
renumber or reuse existing tags. When behavior is removed, delete the bullet but
don't reuse its tag.

## Display

- [S-01] Header shows the widget label (default "Screen Time"); the secondary
  slot hosts the day navigation, mirroring the calendar widget's header-nav
  pattern (`‹ title ›` text-chevron buttons, fixed-width centered title span,
  `●` today button in `var(--primary)`). Narrow containers (<= 220px) hide the
  today dot and shrink the title; containers <= 80px tall hide the whole
  header (both thresholds match the calendar widget).
- [S-02] Large readout of the shown period's total screen time, part of the
  `graphic` metric ([S-57]).
- [S-03] `apps` metric: one row per app, sorted by seconds descending, capped at
  `maxApps` (default 7), showing the app's icon, display name and duration. It
  is the legend beside whatever the style draws; the widget has no
  bare-list style of its own.
- [S-04] Durations format as `Nh Nm` at >= 1 hour, `Nm` at >= 1 minute, `<1m`
  below (pure helper `formatDuration` in `screentimeDisplay.ts`).
- [S-06] A day with no recorded usage renders a muted "No activity yet"
  placeholder instead of an empty list.
- [S-35] `displayStyle` config picks HOW the period is drawn: `doughnut`
  (default) and `bars` draw a day; `bars` and `heatmap` ([S-43]) draw a window;
  `none` draws nothing ([S-53]). `bars` means "split by app" in both: one
  horizontal bar for a day ([S-50]), one vertical bar per day for a window
  ([S-48]). It is independent of WHAT is covered ([S-46]). A stored `list` — the
  pre-[S-56] default — clamps to `doughnut`, whose legend shows the same rows.
- [S-57] `visibleMetrics` holds only the two EXTRA figures: `average` (on by
  default, [S-52]) and `switches` (off, [S-55]). What the body is MADE of is not
  a metric — the drawing is `displayStyle` (`none` = none) and the app list is
  `maxApps` (0 = none), [S-53] — so there is one control per thing, not two.
  A set persisted before that also named the drawing (`graphic`, or its older
  name `total`) and the list (`apps`): `applyLegacyMetrics` carries those two
  opinions onto the settings that replaced them, and `migrateMetrics` drops the
  retired keys while leaving `average` at its default (a legacy set predates it,
  so it cannot have switched it off). Both are no-ops on a set written after the
  change.
- [S-53] One switch per part: `displayStyle: none` draws no chart (the
  readout and the app list remain), and `maxApps: 0` lists no apps (the drawing
  remains). Selecting `none` and 0 together leaves just the readout. Because a
  cap of 0 would fold every app into a single grey "Other" segment, the DRAWING
  keeps its own cap: `chartCap` falls back to the default 7 when `maxApps` is 0,
  so hiding the list never changes what the chart looks like.
- [S-52] In a WINDOW period, the average per day is shown twice: beside the big
  readout (the window's own average) and on every legend row (that app's). The
  divisor is the WINDOW LENGTH — the number of days the response covers, not the
  days the app was actually used — so idle days count as zero and the per-app
  averages sum to the window's overall daily average. Format is `{value}/day`,
  muted; the row's is also appended to its tooltip. In a legend row it sits
  LEFT of the total, which stays the row's last column. The headline average is
  hidden while a day or app is FOCUSED ([S-44]/[S-40]), where a window average
  would not apply to the number beside it. A day period has no average at all
  (its total already is one day). On narrow containers the row averages go first
  (<= 240px), the headline one later (<= 170px). The whole set is the `average`
  metric ([S-57]).
- [S-55] `switches` metric (default OFF): how many times the foreground app
  CHANGED over the shown period, beside the readout ("87 switches"; the singular
  has its own key). The backend counts a change only when the app actually
  differs from the open one and only while accruing — a switch behind a lock or
  an idle screen is not the user switching, and the first app of a session is a
  start, not a switch. Hidden apps ([S-39]) are NOT subtracted: a switch is a
  moment, not a bucket, so the figure is the period's raw count. Zero renders
  nothing. A window sums its days.
- [S-56] The app list always renders `maxApps` rows: real entries first, then
  invisible filler rows (`visibility: hidden`, so unhoverable, unclickable and
  never read out) to make up the difference. A quiet day with fewer apps than the
  cap therefore leaves the chart above it exactly where it was. A list already at
  or past that many rows — the aggregate "Other" entry can push it one over — gets
  no filler.
- [S-58] `percent` metric (default OFF): each legend row also shows the app's
  share of the SHOWN period total (`shareOf`, rounded), so the figure always
  agrees with the drawing beside it — hidden apps ([S-39]) are out of both the
  total and the share. A period with no usage has no share to state (0).
- [S-59] Goals are DAILY limits, in seconds: `goalSecs` for the whole screen
  time and `appGoals` (exe -> seconds) per app. Both are coerced at the trust
  boundary — anything not a positive finite number is "no goal", never a
  zero-second limit everything instantly exceeds. The limit is shown beside the
  figure it judges ("of 4h"), and both turn `var(--destructive)` once exceeded.
  A WINDOW period compares the average per day against the daily limit (its
  total would blow past a daily limit by construction), and says so ("of 4h/day").
  A hovered day or app replaces the readout, so the comparison is hidden while
  focused ([S-44]/[S-40]) rather than judging a number it does not describe. The
  panel edits limits in whole minutes (15-minute steps, 0 = off, max 24h) and
  stores seconds; a per-app limit is added by exe key and removed with an ✕.
- [S-51] Legend colour key: in the colour-coded styles (`doughnut`, and `bars`
  on either axis) each legend row carries a small dot in the app's `appColor`,
  DIRECTLY after the name — the name shrinks to its text and the free space is
  absorbed after the dot, so the dot hugs the title while the numbers stay
  right-aligned. The icons themselves stay unringed ([S-36]). The "Other" entry
  has no dot — its icon slot is already a swatch of the same colour — and the
  `heatmap` style has no dot at all, since its cells are keyed by intensity, not
  by app.
- [S-50] Day + `bars`: ONE horizontal bar spanning the body, split into the same
  `donutSegments` the ring would draw (top `maxApps` apps + "Other"), so it
  always fills the full width and each app keeps its `appColor`. Like the
  stacked bars ([S-42]) it has no track fill of its own. Hovering a
  segment lights it, dims the rest, and swaps the readout to that app's name and
  duration ([S-40] keyed by app); right-clicking one offers "Hide from list"
  ([S-39]).
- [S-46] `period` and `displayStyle` are two independent config fields.
  `period` is `day` (default), `week` or `month` and picks the DATA — one day
  ([S-11] nav, [S-30]) versus a rolling window ([S-41]). `displayStyle` picks the
  DRAWING. Because a style either draws apps or draws days, `stylesFor` pairs
  `day` with `doughnut`/`bars` and `week`/`month` with `bars`/`heatmap`; a month
  of bars and a week of heat cells are both valid, so neither axis implies the
  other. `resolveView` narrows both persisted values at the trust boundary and
  `clampStyle` repairs an impossible pair to that period's first style, so a
  corrupt config renders a real body rather than an empty one. Pre-[S-46]
  configs, where `displayStyle` itself held the period, are absorbed by the same
  resolver: `'week'` → week + bars, `'month'` → month + heatmap; an explicit
  `period` always wins, so the legacy reading stops applying as soon as the panel
  writes both fields (it writes them together, [S-35c]).
- [S-36] Doughnut layout: an SVG ring whose wedges are the top `maxApps` apps by
  share of the day total plus a single aggregate "Other" wedge for the
  remainder, so the ring always sums to the whole day. Wedge order and offsets
  are computed from cumulative SECONDS (`donutSegments`), not summed fractions,
  so boundaries stay seam-exact. Each app's color is a stable hash of its exe
  key (`appColor`), so an app keeps its color across days and reorderings; the
  "Other" wedge uses the `--sctw-other-color` token. The day total
  ([S-02]/[S-04]) is centered in the ring's hole (part of `graphic`, so it comes
  and goes with the ring), and a legend below lists each wedge with its icon, name,
  and duration. Legend icons carry NO colored ring — the wedge color is conveyed
  by the ring itself and the hover highlight ([S-40]), not by a halo around the
  icon; only the "Other" entry (which has no icon) shows a color swatch.
  On short containers the legend is dropped first, then the
  ring shrinks — the centered total is kept longest.
  The ring's hole IS this style's readout — there is no readout row above it —
  so the figures that sit beside a total in the other styles stack under it
  here: the daily limit ([S-59]) and the switch count ([S-55]). Focusing an app
  ([S-40]) replaces the whole group, exactly as it replaces the readout row.
- [S-37] App icons render in the legend rows of every style. Each
  bucket's icon is extracted from its persisted exe path ([S-38]) via one
  batched `get_file_icons_batch` call (size 32), keyed back to the exe; each
  path is fetched at most once per widget. A bucket with no resolved icon
  (old store, failed extraction, still loading) falls back to a colored initial
  chip whose color is the app's stable `appColor`. Icons are display-only and
  never persisted in widget config.
- [S-39] Hiding an app: right-clicking a legend row, a ring wedge or a
  horizontal-bar segment offers "Hide from list", which appends its exe key to the
  widget's `hiddenApps` config (deduped; the aggregate "Other" wedge can't be
  hidden). Hidden buckets are dropped from every view, and the
  shown total + ring re-base to `sumSecs` of the remaining apps — so the number
  in the centre always equals the sum of what is drawn. The config panel lists
  the hidden apps (exe key, prettified) with an ✕ to un-hide. `parseHidden`
  coerces the persisted value at the trust boundary (drops non-strings/empties),
  never a half-built list.
- [S-40] Doughnut hover: hovering a wedge OR its legend row marks that entry
  active and dims the others (bidirectional, driven by one `hovered` signal
  keyed by segment key), and swaps the ring's centre from the total to the
  hovered app's name + duration + rounded percent. Leaving restores the total.
  Each wedge also carries a native SVG `<title>` (name — duration · percent) for
  an OS tooltip. The centre overlay is `pointer-events: none` so the wedges
  beneath stay hoverable.
- [S-41] The `week`/`month` periods ([S-46]) cover a ROLLING window that ENDS
  TODAY — the last 7 / last 30 days, never the current calendar week or month.
  They read `screentime_get_range { days }` ([S-45]), which anchors the window on the
  BACKEND clock (no date is sent), so a frontend/backend midnight disagreement
  can never shift it. Days with no usage stay in the response as explicit zeros,
  so a gap renders as a gap. `parseRange` coerces the response at the IPC trust
  boundary (drops entries without a date key; a non-array yields an empty
  window), and each day's shown seconds are re-derived with
  `filterHidden` + `sumSecs`, so hiding an app ([S-39]) shrinks the chart and its
  readout together. The header slot shows the span ("Last 7 days" / "Last 30
  days") instead of the day nav: there is no single day to navigate. A response
  for a window the user has since switched away from is dropped. A PERIOD change
  refetches immediately instead of waiting out the poll interval; a pure style
  change redraws what is already loaded (same data, different drawing).
- [S-42] Window + `bars`: a vertical bar per day of the period, oldest left,
  today rightmost. The track behind a bar is unfilled — only the bar itself is
  drawn, so the height a bar does not reach stays empty rather than reading as a
  second, greyer bar, and a zero day renders as nothing at all. Total bar height
  is the day's share of the window's
  BUSIEST day — a share of the largest, not of the total, so the busiest day
  always fills its track. Each column is labelled with its
  localized short weekday, and today's label uses `var(--primary)`; past 7
  columns (a month of bars) the labels are dropped and the gutters tighten, since
  one label per column would be unreadable.
- [S-48] Those bars are STACKED by app. `aggregateApps` sums the window's days
  into one bucket list, which feeds both the legend ([S-03]) and the stack order:
  `stackForDay` splits each day in that same legend order, so a colour means the
  same app in every bar, and apps below the `maxApps` cap fold into the legend's
  "Other" part — a day's parts therefore always sum to its shown total, and each
  part's fraction is against the busiest day so bar heights stay comparable.
  Parts stack up from the bottom (busiest app at the base). Hovering a legend row
  dims that app's parts in every other bar and vice versa ([S-40] keyed by app),
  while hovering the COLUMN still swaps the readout to that day ([S-44]).
- [S-43] `heatmap` style: 7 cells wide. Because the window is wrapped
  chronologically at 7 columns and ends on the last cell, every column is a
  fixed weekday without any week-start setting — the window's own dates decide,
  and the header row samples each column's weekday. Leading slots before the
  window starts are blank (`heatmapCells` pads them). A cell's intensity is
  `heatLevel`: 0 for NO usage, else quartiles of the busiest day (so any usage
  is at least level 1 and the busiest day is always level 4), rendered as
  `color-mix` of `var(--primary)` so the scale re-themes. Today's cell carries an
  inset outline. At two rows or fewer (a week of heat cells) the row height is
  capped and the grid sits at the top, so cells stay chips instead of stretching
  over the whole body.
- [S-44] Window hover: hovering a bar or heat cell marks it active, dims the
  others, and swaps the readout from the window total to that day's duration
  plus its localized date — the [S-40] idea keyed by date. Every bar/cell also
  carries a native `title` (date — duration).
- [S-07] Polls `screentime_get_day` (or `screentime_get_range` in the window
  periods [S-41]) every 30 s via `createPolling` key `"screentime"` (no interval
  config; the backend only accrues in 30 s ticks).
- [S-08] Demo mode renders a static fixture (`demoScreentime`) instead of
  backend data.
- [S-11] Day navigation ([S-01] header nav): `prev` steps one day back, `next`
  one day forward, the `●` today button jumps back to today. The selection is
  pinned as a concrete YYYY-MM-DD key (null = follow today), so the shown day
  NEVER shifts when local midnight passes while the user is viewing history.
  The UI window is today plus 89 calendar days back; `prev` disables at that
  cap, `next` at today. (The backend retains the 90 most recent dates WITH
  USAGE [S-28], which can span more calendar days; days beyond the UI window
  are unreachable by design.) Navigating clears the shown stats until the
  selected day's data arrives, and a late response is dropped unless its
  pinned date still matches. The selected day is TRANSIENT view state (a
  signal, never written to config).
- [S-12] The nav label localizes: `t('common.today')` / `t('common.yesterday')`
  / `toLocaleDateString(lang(), { weekday: 'short', month: 'short', day:
  'numeric' })`. It derives from the pinned key AND a today-key re-sampled on
  every poll, so after midnight a pinned "Yesterday" self-corrects to its date
  within one poll. Following today queries the backend without a date
  argument, so "today" is decided by the backend clock at midnight.

## Configuration

- [S-09] `visibleMetrics` multi-select over metrics `average` and `switches`
  ([S-57]); `average` defaults on, `switches` off.
- [S-10] `maxApps` stepper, range 0..`MAX_APPS_LIMIT` (50), default 7; 0 lists
  no apps ([S-53]). It
  also caps the segment count before the "Other" remainder ([S-36]/[S-48]).
- [S-35c] Two dropdowns, one per axis ([S-46]): "Period" (Day / Week (last 7
  days) / Month (last 30 days)) and "Style", whose options are `stylesFor(period)`
  — List/Doughnut/Bars/None for a day, Bars/Heatmap/None for a window. Changing
  the period writes BOTH fields (the style clamped into the new period), so the
  persisted pair is always one the widget can draw. `hiddenApps` still applies
  ([S-41]).
- [S-39c] A "Hidden apps" section lists the apps hidden via [S-39] with an ✕ to
  un-hide each; empty state reads "No hidden apps."

## Data / backend (tracking)

- [S-20] Foreground app changes are recorded from the existing
  `EVENT_SYSTEM_FOREGROUND` WinEvent hook (event-driven, no extra hook). The
  foreground pid resolves to its executable path; the lowercase exe stem (e.g.
  `chrome`) is the accrual bucket key. Themia's own process is never recorded
  (the hook skips own process; `note_foreground` also guards on pid). The app
  that is foreground at startup is seeded once so tracking starts before the
  first switch.
- [S-47] Some apps ship an internal entry-point identifier as their
  `FileDescription` — WhatsApp's is `WhatsApp.Root`. `sanitize_name` drops a
  trailing meaningless dotted segment (`.root`/`.app`/`.exe`/`.main`/
  `.entrypoint`/`.program`, case-insensitive) from an identifier-shaped name (no
  whitespace, dotted), so it reads as the app. Real dotted names keep their dots
  ("Node.js JavaScript Runtime" has whitespace; "Microsoft.Todos" has no junk
  tail). A name that is STILL identifier-shaped afterwards falls back to the
  exe's version-info `ProductName` (`version_string` reads any StringFileInfo
  field), and only then to the description as-is / `prettify_exe`. Because the
  strip lives in `sanitize_name`, a store that already cached `WhatsApp.Root`
  heals on load, exactly like the [S-21] control-character garbage.
- [S-21] App display names come from the exe's version-info `FileDescription`,
  cached per bucket key in the store; fallback is the capitalized exe stem.
  The version-info read is file IO, so it runs on a short-lived worker thread,
  never on the WinEvent hook thread; the name may appear one refresh late. The
  raw `FileDescription` is truncated at its first NUL/control unit — trusting
  `VerQueryValueW`'s returned length leaked adjacent version-info bytes into the
  name (e.g. "Spotify\0…FileV"). Names are also sanitized on store load
  (`sanitize_name`), so a store written before that fix self-heals, and a bucket
  still missing a name after this can be re-resolved by the startup backfill
  ([S-38]) from a running process (e.g. "githubdesktop" → "GitHub Desktop").
- [S-22] A 30 s sampler tick accrues elapsed foreground time into the current
  local date's bucket. Open intervals are therefore at most one tick long; a
  crash or kill loses at most one tick plus the flush interval.
- [S-23] Session lock pauses accrual (`WM_WTSSESSION_CHANGE` in the display
  watcher); only unlock resumes it. Foreground changes while locked update the
  remembered app but accrue nothing.
- [S-24] Input idle >= 5 min pauses accrual (`GetLastInputInfo` sampled on the
  tick); renewed input resumes on the next tick. Idle/resume boundaries are
  therefore accurate to one tick (~30 s), and roughly the first 5 min of an
  idle stretch still count as usage.
- [S-25] An interval spanning local midnight splits at midnight using the
  stamp's seconds-into-day; the remainder is credited to the new date. (DST
  shifts within a tick are not corrected; error is bounded by one tick.)
- [S-26] Store persists to `app_data_dir/usage_stats.json` with
  `schema_version`, a `days` map (`date -> exe -> secs`) and a `names` map.
  Writes are atomic (unique temp file + rename) and single-writer (all
  mutation happens under the one tracker lock).
- [S-27] A corrupt or version-mismatched store file loads as a typed default
  with a loud `log::warn`; an absent file is a silent default. Never a
  half-built store. The unreadable file is preserved by renaming it to
  `usage_stats.json.corrupt` (best effort) so the next flush cannot silently
  destroy recoverable history (e.g. after a version downgrade). Day keys that
  are not `YYYY-MM-DD` and empty exe keys are dropped on load, so junk can
  never become immortal and evict real days from retention.
- [S-28] Retention: only the most recent 90 dates are kept; older keys are
  pruned on save.
- [S-29] Flush policy: the sampler saves when dirty and >= 60 s have passed
  since the last save; lock and app exit (`RunEvent::Exit`) flush immediately.
- [S-55d] The store also keeps a `switches` map (`date -> count`), incremented
  on a real foreground CHANGE while accruing ([S-55]). Additive on the same terms
  as `paths` ([S-38]) — `#[serde(default, skip_serializing_if)]`, so a store
  without it loads as a valid v1 store and the bytes are unchanged when empty,
  needing no `schema_version` bump. Junk day keys and zero counts drop on load,
  retention prunes a counter with the day it belongs to ([S-28]), and a merge
  ADDS both sides' counts (both counted real switches). It is a COUNT only — no
  timestamps, so it adds no timing detail to the file ([S-34]).
- [S-30] `screentime_get_day { date? }` returns
  `{ date, total_secs, apps: [{ exe, name, secs, path? }], switch_count }` (apps sorted desc),
  including the still-open live interval up to now; `date` defaults to today.
  `path` is the last-known exe path for the bucket ([S-38]) or absent.
- [S-45] `screentime_get_range { days }` returns one `[S-30]` `DayStats` per day
  of the window ending on the backend's today, OLDEST FIRST, including days with
  no usage (as zero-app entries). `days` is clamped to `1..=90` (retention
  [S-28]), so a caller can never ask for more history than the store keeps.
  Live-inclusive like [S-30], and only today's entry can carry the open interval
  (`split_secs` credits it to its own date). Date stepping is calendar-aware
  (`shift_date`, Hinnant civil/days conversions); a malformed key is returned
  unchanged rather than becoming a half-built date.
- [S-31] Tracking covers only time while Themia is running; the widget shows
  Themia-collected data, not OS-level history.
- [S-32] An interval longer than two ticks is never credited: the sampler
  ticks every 30 s while the machine runs, so a larger anchor-to-now gap means
  suspend/hibernate, a forward clock jump, or a stalled process. The interval
  re-anchors without crediting, losing at most one tick of real usage instead
  of crediting hours of phantom usage to the pre-sleep foreground app.
- [S-38] The store also keeps a `paths` map (`exe -> full exe path`), recorded
  on every foreground switch ([S-20]) so an app's icon ([S-37]) can be extracted
  for historical days, not just apps seen live this session. At sampler startup
  a one-time backfill (`backfill_running_paths`) fills the path (and, if missing,
  the name [S-21]) for any history bucket whose process is running RIGHT NOW,
  via a single `CreateToolhelp32Snapshot` scan — so icons for already-used apps
  appear without waiting for each to be re-foregrounded. The scan and the
  version-info reads happen off the tracker lock; only the map writes take it.
  Buckets whose app isn't currently running keep their icon until next
  foregrounded. It is ADDITIVE to
  schema_version 1: the field is `#[serde(default, skip_serializing_if =
  "is_empty")]`, so a pre-[S-38] store loads with an empty map (never treated as
  corrupt [S-27]) and, when no paths are known, the on-disk bytes are identical
  to the old format (the golden test [S-26] still holds). Junk entries (empty
  key or value) are dropped on load, mirroring the day/exe hygiene. A new
  optional field needs no schema_version bump; a removed/renamed one does. The
  path is never required for accrual correctness.
- [S-33] Time spent focused on Themia's own windows is not credited: the
  WinEvent hook skips own-process transitions, so the sampler checks the
  foreground pid each tick and closes the open interval when it is Themia's,
  at tick granularity (at most one tick of Themia time credits to the
  previously focused app).
- [S-34] Usage history is PRIVATE to the machine: it lives only in
  `usage_stats.json` (Rust side), never in widget config, layouts, or any
  localStorage key, and therefore never in the settings export
  (`importExport.ts` exports localStorage only). The widget reads history
  exclusively through `screentime_get_day`; the history offset is a transient
  signal ([S-11]). A screentime widget's persisted config keys are exactly
  the configured ones (`label`, `maxApps`, `visibleMetrics`, `period`, `displayStyle`,
  `hiddenApps`, header fields). `hiddenApps` holds only exe KEYS (e.g.
  `"chrome"`), never durations or dates, so no usage data leaks into the export.
  App icons ([S-37]) are fetched live and never persisted.
