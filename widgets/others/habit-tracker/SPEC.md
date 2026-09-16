# Habit Tracker: SPEC

Experimental widget. Daily habit check-offs with streaks. State is
persisted in widget config (`habits`, `log`). Mirror `[S-NN]` tags in tests.

- **[S-01]** `habits: {id,name}[]` are managed in config. `log: Record<habitId,
  string[]>` holds the local `YYYY-MM-DD` days each habit was completed.
- **[S-02]** Clicking a habit's checkbox — and ONLY the checkbox — toggles TODAY
  in its log and persists via `updateWidgetConfig` (merged onto existing config,
  never replacing it). The row itself carries no click handler, so clicking the
  name or the streak can never complete a habit by accident; the name click is
  the rename ([S-14]).
- **[S-03]** Day keys are LOCAL calendar days (`dateKey`), not UTC.
- **[S-04]** Streak = consecutive completed days ending today. Today not-yet-done
  does not break the streak (counting starts at yesterday); a prior gap does.
- **[S-05]** Removing a habit in config also drops its `log` entry (no orphans).
- **[S-06]** Header toggled via the widget context menu.
- **[S-07]** Reset cadence (`resetPeriod`) is `hourly | daily | weekly | monthly |
  custom`. `custom` clears every `customMinutes` minutes (1–1440, sub-hour allowed),
  bucketed by fixed epoch-anchored windows. `hourly` buckets by local
  `YYYY-MM-DD-HH`. Streaks count consecutive completed buckets under the same cadence.
- **[S-08]** The checkbox clears LIVE when its period rolls over: the widget polls
  a reactive clock (`resetTickMs`: 15 s for hourly/custom, 60 s otherwise) so a box
  checked in a past period un-checks without a remount. A toggle records the SAME
  bucket the checkbox reads (the polled `now()` via `current()`), so the box flips
  immediately; writing the live-clock bucket instead left it unresponsive for a
  full tick after every rollover.
- **[S-09]** Habit ORDER is user-controlled (issue #106): the config panel's rows
  carry a grip handle, and dragging one moves that habit into the hovered row's
  slot (`moveHabit`), keeping every other habit's relative order. The widget
  renders `habits` in stored order, so the config list and the widget always
  agree. The reorder is live (rows shuffle under the cursor, persisted per move)
  and a no-op move writes nothing. `log` is keyed by habit id, so reordering
  never moves a completion or resets a streak.
- **[S-10]** The header defaults to `compact` (`defaultHeaderMode` in the
  registry, mirrored by the widget's own `props.headerMode ?? 'compact'`
  fallback so the picker preview and the grid agree). A habit list is read at a
  glance; the tall header only costs rows. An instance that stored an explicit
  `headerMode` keeps it.
- **[S-22]** Habits are DELETED from the widget: the row's context menu carries
  Delete under Rename (separated, `danger`), and the Delete key removes the
  focused row ([S-17]). No dialog — it sits behind a right-click or a focused
  row, not a stray click, which is the bargain To Do makes for a task. An open
  editor for that row is cancelled first, so a pending rename can't commit onto
  a habit that no longer exists, and `setHabitListHabits` prunes the habit's log
  with it ([S-20]) so its streak can't be inherited by a later habit reusing the
  id. The highlight is pulled back inside the list afterwards.
- **[S-23]** The config panel holds NO habit editor. Habits are added, renamed,
  reordered and deleted in the widget ([S-16]/[S-14]/[S-13]/[S-22]) — where the
  user is looking at them — so a second editor in config was a parallel path to
  the same data. Config owns the SOURCE (which list) and the widget's settings;
  the list's contents belong to the widget. This retired [S-11]'s config draft
  row along with `habitRows` / `DRAFT_HABIT_ID`.
- **[S-24]** Deleting a habit LIST always confirms through the native dialog,
  empty or not — it takes every habit and streak in it, and the X sits one row
  from the name field the user was just typing in. An empty list gets the shared
  noun-free body (`common.deleteEmptyConfirm`), because "and its 0 habits" is
  not a sentence.
- **[S-12]** The widget FITS its habits to the tile: rows share the body height
  equally (`flex: 1 1 0`) and each row is a size container, so the checkbox,
  name and streak scale to the slice the row actually got. Scrolling is the last
  resort — it begins only when the equal slice would fall below the legibility
  floor (`--habit-row-min`). Row growth is capped so a short list reads as a
  list, not as banners, and every ceiling is multiplied by `--font-scale`.
- **[S-13]** Habits reorder from the WIDGET by DRAGGING a row, without opening
  config. The gesture is driven by POLLING the OS cursor (`get_cursor_state`,
  ~60 fps), the same mechanism the folder and shortcut widgets use ([S-65]
  there), because neither browser drag route exists in a widget window: it is
  `WS_EX_NOACTIVATE`, so Chromium withholds mousemove while a button is held,
  and Rust owns the window's OLE drop target (`register_drop_target`), so
  WebView2 never delivers `dragover` to the DOM — which is why the CONFIG window
  disables that handler for its own grip ([S-09]). It reuses `gesture.ts`'s 5px
  threshold, so a click on the name still renames ([S-14]); a press on the
  checkbox or the rename input arms nothing. The drag reorders a LOCAL copy and
  commits once on release (`sameHabitOrder` → a drag that comes home writes
  nothing): persisting every hop would be a config write plus a cross-window
  broadcast each time, and an interrupted drag would leave it half-applied. The
  widget and the config grip move habits through the same `moveHabit`, so the
  two can never disagree. In edit mode no drag arms: the grid owns that gesture.
  The row menu keeps only Rename.
- **[S-14]** Habits are RENAMED from the widget too, without opening config:
  clicking a habit's name (or the row menu's Rename, or F2 on the focused row)
  swaps it for an inline input seeded with the current name; Enter or blur
  commits, Escape cancels. The caret lands WHERE THE CLICK DID
  (`caretOffsetFromPoint` → `focusWithCaret`, `src/input/caret.ts`) and NOTHING
  is selected: opening with the whole name selected armed the next keystroke to
  wipe it, which is not what clicking into text means anywhere else. Routes with
  no click point park the caret at the end. A blank or unchanged name commits
  nothing — removing a habit is config's job, and `log` is keyed by habit id, so
  a habit lost here would strand its whole streak history (`renameHabit` never
  touches an id). The widget and the config panel write the same `habits` array
  through the same `renameHabit`, so the two can never disagree. In edit mode the
  rename is suppressed, like the row menu ([S-13]): the grid owns that gesture.
  Tests: `caret.test.ts`.
- **[S-15]** Each row ends in a HISTORY strip of dots — oldest left, the current
  period rightmost — filled with `--primary` when that bucket is in the habit's
  `log` and left as a faint slot when it isn't. The strip reads `recentHistory`,
  which walks the same `prevPeriod` ladder as `streak`, so the dots and the
  flame can never disagree about what "one back" means; under the default daily
  cadence that ladder is exactly the last N days, and under any other cadence it
  is the last N of THAT period. A dot's tooltip is formatted in the cadence's
  own units. The dots are derived purely from `log` — the strip stores no
  completion state of its own. It is the LAST element in the row (after the
  streak) so its dots form a clean right-aligned column that the variable-width
  streak count cannot jog sideways.
  - The COUNT is config (`historyDots`, `DEFAULT_HISTORY_DOTS` = 7, range
    `MIN_HISTORY_DOTS` 0 – `MAX_HISTORY_DOTS` 31, a month of daily buckets). 0
    removes the strip from the row entirely — not an empty element still
    claiming a flex gap — because a habit list is allowed to be just a list.
    `clampHistoryDots` runs at BOTH the config boundary and inside
    `recentHistory`: a stored count is the bound of a render loop, and a
    hand-edited layout file can hold anything. `NaN`/`Infinity`/absent are junk
    and fall back to the default; an in-range-but-fractional or out-of-range
    number clamps.
  - The strip is what YIELDS width, never the name: `flex: 0 1 auto` against a
    `min-width: 4em` floor on the name, so the oldest dots fall off the left
    edge (masked, so a dot leaves whole rather than being cut in half) as the
    tile narrows. This replaced a fixed container-query hide, which could not
    survive the count becoming configurable — no single breakpoint is right for
    both 1 dot and 31. Dots never shrink (`flex-shrink: 0`); a squished dot
    reads as an oval. [S-12] is the same fit-first bargain on the height axis.
- **[S-16]** Habits are ADDED from the widget — the only place they can be
  ([S-23]): the list ends in an "Add habit" row that turns into an inline input
  on click, commits on Enter or blur, and cancels on Escape — the same shape as
  To Do's add row ([S-32] there). It goes through `addHabit`, which trims and
  refuses a blank name, so a nameless habit cannot be created. The new habit APPENDS, at
  the bottom, where the row that made it is. The add row is fixed-height
  (`flex: 0 0 auto`), so it never joins the equal-slice share-out that sizes the
  habit rows ([S-12]) and adding it costs the habits above it nothing. It is
  hidden in edit mode, like the rename and the drag ([S-13]): the grid owns the
  widget while it is being arranged. With no habits yet, the empty hint moves
  INSIDE the list, above the add row — the first habit is created right there,
  so pointing the user at the config gear would be the long way round. Test:
  `habit.test.ts` (`addHabit [S-16]`).
- **[S-18]** Habits are stored as device-wide **lists**, not per widget. A
  `HabitList` (`{id, name, habits, log}`) lives in the shared pool under the
  `habit-lists` storage key; the widget's config carries only `localListId`
  pointing into it. So two widgets can track the same habits, a pop-out agrees
  with its tile, and a list outlives the widget that made it. A list owns its
  `log` as well as its `habits` because the log is keyed by habit id — splitting
  them would let streaks outlive the habits they belong to, or bleed into
  another list that reused an id. Both the check-off and every habit mutation
  land in the SELECTED list, and are no-ops when none is selected rather than
  writes that would have to invent one. The config panel's **Source** tab shows
  the same `AccountToggleList` every account-bound widget shows, with "On this
  device" as its leading row (Mood Tracker [S-16]); habits have no account
  backend, so it is the ONE source and its switch is disabled. Persistence
  follows the shared-store contract: schema'd load (corrupt → loud fallback),
  writes through `sharedStorage`, and a cross-window reload that `reconcile`s so
  rows keep their identity and the DOM its focus.
- **[S-19]** The list picker + editor renders **inside that source row's card**,
  through the leading option's `extra` slot — the counterpart of the calendar
  widget's per-account `perAccountExtra` picker, and for the same reason: which
  list this widget shows is a question *about* that source, not a setting beside
  it. One row per list with a pick dot, an editable name, a habit count, and a
  remove button; a non-empty list confirms through the native dialog before
  deletion, since its streaks go with it. Creating or renaming reaches every
  widget already showing that list; deleting is a device-wide action.
- **[S-20]** Removing a habit prunes its log entries, in `setListHabits`, so no
  mutation path can forget it and a later habit reusing an id can never inherit
  a dead streak. With no list selected the habits tab shows a "pick a list"
  prompt instead of an editor whose writes would silently no-op.
- **[S-21]** A widget carrying pre-pool `habits`/`log` in its config migrates
  them once into a real list whose id is DERIVED from the widget id, so a tile
  and its pop-out — which mount together and both see the legacy bag — converge
  on one list instead of seeding two; `seedHabitList` then leaves it alone if it
  already exists. The bag is cleared in the same config write that points at the
  new list, so the habits exist in exactly one place afterwards and the
  migration can never run twice. A widget with no legacy data lands on the
  device's first list, creating one only if there is none.
- **[S-17]** KEYBOARD focus and TAB navigation, routed through the shared Rust
  hook (`registerWidgetKeyboard`) because a widget window never takes Win32
  focus and DOM keydown therefore never fires. Tab moves the focus to the next
  row and Shift+Tab to the previous, WRAPPING at both ends (the arrows clamp;
  Tab is a "next one" key, not a directional one); Home/End jump to the ends;
  Escape clears the highlight. Enter checks the focused habit off — the same
  thing its checkbox does, still the only thing that completes one ([S-02]) —
  and F2 renames it ([S-14]), matching To Do. While an editor is open, Tab
  commits it and opens the NEXT row's editor (Shift+Tab the previous); past the
  last row it falls into the add row ([S-16]), which is where the next name
  would go anyway. In the add row, Tab commits and stays open for the next
  habit, Shift+Tab commits and closes. The highlight clears when the user clicks
  outside any row or the app loses focus (`onAppBlur`), mirroring To Do [S-37].
  No keys are handled in edit mode: the grid owns the widget. Tab reaches the
  widget via the hook whitelist (`widget_kb_action`, VK 0x09; test
  `n35_widget_kb_action_classification`) and as a plain DOM keydown while an
  input is focused.
