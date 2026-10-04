# Themia for WinUI: SPEC

Pins the behaviour this slice actually implements. Tags are `[S-NN]`, and where a rule is carried
over from the original Themia the tag number is kept the same so the two can be read side by side.

Mirror these tags in test names (e.g. `S09_clamps_a_widget_fully_on_screen`).

Owning files: `Themia.Core/Geometry/*`, `Themia.Core/Model/*`, `Themia.Core/Storage/*`,
`Themia.App/Shell/*`, `Themia.App/Interop/*`.

---

## Geometry model

- **[S-01]** A widget's position and size are stored as **fractions of its screen**: `x, y ∈ [0, 1]`
  is the top-left corner, `w, h ∈ (0, 1]` the size. `x`/`w` are fractions of screen **width**,
  `y`/`h` of screen **height** — independent axes, so aspect distortion across screens is accepted,
  not corrected.
- **[S-02]** Geometry is resolution-independent: the same layout opened on a larger screen renders
  every widget proportionally larger in both position and size. No stored pixel geometry, no stored
  grid.
- **[S-03]** The persisted document is `{ schemaVersion, views, activeViewId, monitorViews, design }`.
  A view is `{ id, name, widgets }`.

## Rendering

- **[S-04]** Each widget renders at `left = x·W`, `top = y·H`, inset on every side by
  `widgetGap / 2`, so neighbours show a gap without changing their logical rect. Logical edges are
  rounded to a shared whole-pixel boundary *before* the gap inset, so adjacent widgets still meet
  exactly.
- **[S-05]** The DIP space a fraction resolves into is the window's **real client rect divided by
  the window's real rasterization scale** — not `ActualWidth`. Changing display scale resizes the
  content without changing the client's pixel size, so no `SizeChanged` fires and `ActualWidth` can
  hold a stale pre-DPI value; trusting it rendered every widget 1.5x too far out on a 150% display.
- **[S-32]** A widget instance can drop its card: `WidgetSurface.Transparent` removes the fill and
  the border. `Translucent` keeps a tinted, partly see-through card. Neither is acrylic per widget —
  acrylic is a window backdrop, so per-widget acrylic would cost one window per widget.

## Grid snap aid

- **[S-06]** The grid is an **aid**, never storage. `design.gridSnap` (default on) rounds the rect
  chosen during drag, resize and placement to the nearest grid line. Holding **Alt** suspends it for
  the duration of the gesture. Storage is always the raw fraction.
- **[S-06b]** The cell target is **10 DIP**, and the minimum widget **32 DIP**, matching the
  original app. This only sets how many snap lines exist; the drawn pitch is screen/cols, so it
  follows the display. At that density the overlay stroke has to be very faint, or the mesh reads as
  a grey wash over the desktop rather than as a grid.
- **[S-06a]** The cell overlay is drawn only in edit mode, and only when `gridSnap` is on.

## Constraints

- **[S-08]** Minimum widget size is a **pixel floor** (`MinWidgetPx`, 64 DIP), converted to a
  fractional floor at the moment of resize, so it means the same physical size on every monitor.
- **[S-09]** Widgets are clamped fully on-screen (`0 ≤ x`, `0 ≤ y`, `x + w ≤ 1`, `y + h ≤ 1`) for
  drag, resize, placement and load. Clamping is **total**: a non-finite or inverted rect collapses to
  a valid one rather than propagating NaN into storage.
- **[S-31]** Edit mode draws **eight** handles: four corners and four sides. A corner drags one edge
  on each axis; a side handle drags **only its own edge**, leaving the opposite edge and the whole
  other axis untouched. An edge dragged past its opposite stops at the [S-08] floor instead of
  inverting the rect.

## Defaults & placement

- **[S-10]** A widget definition declares its default size in **pixels**. At placement that is
  converted to a fraction of the current screen, so a newly dropped widget has a consistent physical
  size on any monitor, and scales per [S-02] thereafter.
- **[S-11]** Placement centres the widget on the drop point, then snaps ([S-06]) and clamps ([S-09]).

## Views & displays

- **[S-12]** Views are switched as a whole. Each holds its own widget list. The last view cannot be
  deleted — a desktop with no view has nowhere to put a widget.
- **[S-13]** The per-widget settings page edits the widget's **live** config and saves as each field
  is committed — there is no Cancel. It does not mirror the config into local state and sync it
  back. Live, not a copy, because several widgets write their own config as they are used (a note's
  text, a habit's check-offs): a settings page holding an older copy would overwrite those on save.
- **[S-13a]** Because the page edits the object a view already holds, a view must never decide what
  changed by comparing that object with itself. A view that reacts to particular settings keeps its
  own snapshot of the settings it last acted on and compares against that. Comparing the live object
  was the bug: a new feed address, stock symbol, weather place or folder sort stayed unfetched until
  the next scheduled refresh.
- **[S-18]** `monitorViews` maps a display's device name to a view id; a display with no entry shows
  the active view. An entry pointing at a deleted view is dropped at load, not resolved.
- **[S-19]** Showing a view puts it on **one display**: that display's own assignment changes, and
  the active view follows so a display plugged in later starts on the last view chosen. Switching
  only the active view is not enough and was the bug: every display gets an assignment at startup
  ([S-18]) and an assignment always wins, so the View menu changed nothing on screen.

## Window layering

- **[S-20]** The widget surface is **never above another application**, in any mode. It is a desktop
  layer: widgets belong under the user's windows, and editing them does not change that. Outside
  edit mode it also carries `WS_EX_NOACTIVATE`, so clicking a widget neither focuses it nor raises it
  over the user's work. `WS_EX_TOOLWINDOW` keeps it out of the taskbar and Alt-Tab. The one exception
  is [S-33], where there is no other application on screen to be above.
- **[S-20a]** Edit mode drops `WS_EX_NOACTIVATE` so the surface can take keyboard focus for the
  shortcuts ([S-70]) — and an activatable window is raised when clicked, so the window procedure is
  subclassed and every `WM_WINDOWPOSCHANGING` is rewritten back to the anchor. Event-driven, so there
  is no timer re-asserting the z-order and no window in which the surface is visibly on top.
  - A consequence the user chose: **edit mode is only reachable where the desktop is visible.** If
    another window covers a widget, that widget cannot be clicked.
- **[S-20b]** The frame is stripped at the **Win32** level (`WS_POPUP`, clearing `WS_CAPTION` /
  `WS_THICKFRAME` / `WS_SYSMENU`), and re-applied whenever the styles are touched.
  `OverlappedPresenter.SetBorderAndTitleBar(false, false)` does **not** clear `WS_CAPTION`: the
  surface kept a caption and a 3px border the whole time, which the click-through region happened to
  clip away, so the title bar appeared the moment edit mode dropped the region.
- **[S-21]** The surface is clipped with `SetWindowRgn` to the union of the widget rects, so every
  click that is not on a widget lands on the desktop underneath. The region is rebuilt on every
  layout pass, in **physical** pixels, relative to the **window** — client and window coordinates
  differ by the frame, so the client origin is added or the whole mask sits shifted from the widgets
  it traces.
- **[S-22]** Only a genuine style change passes `SWP_FRAMECHANGED`. Sending it on an ordinary
  z-order reassert forces a non-client recalculation, which hands the borderless window its frame
  back.
- **[S-23]** The window is placed with `AppWindow.MoveAndResize` using the monitor's **physical**
  work area. Display scale only applies inside the window, where XAML lays out in DIPs ([S-05]).
- **[S-23a]** The resting anchor is the **desktop window** (`Progman`), not `HWND_BOTTOM`: the
  surface is inserted against it so it travels with it, and falls back to the bottom only when
  Explorer is not up yet.
- **[S-33]** While Windows is genuinely showing the desktop, the surface is lifted to the top of the
  topmost band and dropped back the moment it is not. Show Desktop raises the desktop window *past*
  normal z-order, above anything anchored below it, so the widgets would otherwise vanish at the one
  moment the user asked to see their desktop. There is nothing else on screen for them to cover
  then, so this does not contradict [S-20]; it is the only way to honour it and still be visible.
- **[S-33a]** "Genuinely" is two signals: the desktop layer owns the foreground, **and** no ordinary
  application window is still on screen. Foreground alone is also true of a plain click on the
  wallpaper, and acting on it would throw the widgets in front of the user's work every time they
  clicked the desktop. There is deliberately no third "and something got hidden" signal: it would
  only matter when nothing was on screen to begin with, where lifting the widgets covers nothing,
  and both candidate signals measured unreliable — Show Desktop **minimises** on this build rather
  than cloaking, and the cloak count is permanently non-zero anyway because suspended UWP frames sit
  cloaked in the background all the time.
- **[S-33b]** A **topmost** window does not count as an application still on screen. Show Desktop
  cannot hide one, so its presence says nothing about whether Show Desktop is in effect. This is not
  hypothetical: a consent prompt from the hearth hub is exactly such a window, and on its own it kept
  the lift from ever firing.
- **[S-33c]** A transient shell surface (Start, Search, Task View, the taskbar) taking the foreground
  while the lift is held changes **nothing** — not even a re-raise. Dropping the lift there would
  bury the widgets under the still-elevated desktop layer, and dismissing Start hands the foreground
  to the taskbar rather than to the desktop, so they would never come back. Not re-raising is what
  lets Start, activated after us in the same band, stay on top. The decision is a pure function
  (`ZOrderPolicy.Decide`) so it can be tested without a window manager.
- **[S-33d]** `SetWindowPos(HWND_TOPMOST)` often reports success without `WS_EX_TOPMOST` actually
  taking, and a window that only thinks it is topmost stays under the elevated desktop layer. The
  lift therefore checks the bit and, when it is missing, sets it directly and re-applies the position
  with `SWP_FRAMECHANGED`.
  - Verified on two displays: during a real Show Desktop both surfaces read topmost and above the
    desktop window, and both return to the desktop layer when it ends. The original app documents a
    wry/WebView2 limitation where only the first window in a process can be promoted; that does not
    apply here.
- **[S-34]** The surface is kept **uncloaked**. A fullscreen exclusive application hides windows by
  asking DWM to cloak them — a path that goes through neither `SC_MINIMIZE` nor `WM_SHOWWINDOW`, so
  no window procedure sees it. A cloaked window is still `IsWindowVisible`, still not iconic, still
  at whatever z-order it was put at, and simply never composited: no amount of z-order work recovers
  it. `DWMWA_CLOAK` is cleared on every reconcile. Note this is **not** what fixes Show Desktop on
  this build of Windows, which minimises instead ([S-33a]); it is the guard for the fullscreen case.
- **[S-34a]** The cloak hook has a backstop behind it. An out-of-context `WinEvent` is silently
  dropped when the callback cannot be delivered inside the hook timeout — a fullscreen game is
  exactly that load — and a dropped cloak event would leave the surface invisible forever. A 1 s
  timer re-asserts the invariant: one attribute read per surface, a write only when it is broken.
- **[S-34b]** After any shell event the reconcile runs at 100 ms for a second before settling back to
  1 s. A secondary monitor's `WorkerW` can elevate into the topmost band *after* the surfaces are
  lifted, so the lift has to be re-asserted a few times to stick.
- **[S-28]** While the surface is pinned for Show Desktop, a **widget surface taking the foreground
  holds the pin**, exactly as Start or the taskbar does. Clicking a widget makes the surface the
  foreground, so the desktop no longer owns it and Show Desktop stops reading as active — but
  nothing has been restored and the desktop is still elevated. Dropping the pin there buried the
  widgets under it on the first click after Win+D.

## Transparency

- **[S-27]** The surface is genuinely transparent: no backdrop that paints, so the wallpaper (and
  whatever else is behind) shows everywhere the widgets do not.
  - The SDK ships only Mica and Desktop Acrylic, and a window with `SystemBackdrop = null` composes
    **opaque black** — which over a whole monitor is a sheet across the desktop. So a custom
    `SystemBackdrop` installs a transparent composition brush instead.
  - The brush must come from a **system** compositor: the projection types
    `ICompositionSupportsSystemBackdrop.SystemBackdrop` as `Windows.UI.Composition.CompositionBrush`,
    not the `Microsoft.UI.Composition` one.
  - That compositor needs a **system** `DispatcherQueue` on the UI thread, which is a different type
    from the `Microsoft.UI.Dispatching` one WinUI already provides. Without it, constructing the
    compositor fails with "The caller must initialize DispatcherQueue on this thread".
- **[S-27a]** Because the window paints nothing, a widget card must paint its **own** base. Fluent's
  `CardBackgroundFillColor` is a ~5% white overlay in dark theme, designed to sit on an opaque
  window; used alone it made the card invisible and left the clock unreadable over whatever was
  behind. `SolidBackgroundFillColorBase` is the surface that overlay expects, so it is the base here.
  The same applies to the snap grid, which uses a text brush rather than a control stroke.
- **[S-27b]** Fill and content are separate elements, so the surface-opacity setting fades the card
  without fading the text on it.
- **[S-27c]** The opaque base is painted **under** the card stroke, not only inside it. The stroke
  is a faint overlay; with the base stopping at its inner edge, every card had a nearly clear outer
  ring, and two cards at a 0px gap showed wallpaper between them.

## Ticking

- **[S-24]** Every widget shares **one** timer. Its cadence is the finest any subscriber asked for;
  it drops to once a minute when nothing needs seconds, and stops entirely when nothing is
  subscribed or the surface is hidden.
- **[S-25]** Each wake is re-aligned to the next real boundary plus a small bias, rather than using a
  repeating timer. Waking exactly on the boundary rounds down about half the time, showing the
  previous second for a whole tick; a late wake must not push the schedule permanently off.
- **[S-26]** A subscriber fires when its period index changes, so a once-a-minute widget sharing a
  one-second timer still only runs on the minute.

## Persistence

- **[S-40]** Writes are **atomic**: serialise to a sibling temp file, then move over the target, so a
  crash mid-write cannot leave a truncated document.
- **[S-41]** There is a **single writer**, and saves are coalesced — a drag that mutates geometry
  sixty times a second produces one write.
- **[S-42]** `FlushAsync` takes the write gate *before* reading the pending snapshot, so a flush that
  finds nothing queued still waits for an in-flight write to land. Reading the snapshot first let a
  shutdown flush return while a debounced save was mid-write, i.e. "flushed" without durability.
- **[S-43]** Load distinguishes **absent** from **corrupt**. A missing file is a fresh profile. A
  file that is empty, unparseable, or from a newer `schemaVersion` is corrupt: defaults are returned,
  loudly, and the original is **moved aside, never deleted**.
- **[S-44]** A loaded document is repaired back inside its invariants rather than rejected — dangling
  ids, duplicate ids, config that does not match its widget kind, out-of-range design values and
  off-screen geometry are all fixed, and every repair is reported.
- **[S-45]** Closed sets are enums, not strings (`WidgetKind`, `WidgetSurface`, `AppTheme`,
  `ClockFace`). Widget config is a discriminated union with the discriminator written into the JSON,
  so a config can never be silently read as the wrong widget's shape. An unknown widget kind is
  dropped with a report, because this build cannot render it at all.

## Clock

- **[S-50]** 24-hour time is zero-padded; 12-hour time is not, and carries AM/PM. Midnight and noon
  read as **12**, not 0.
- **[S-51]** Hands sweep rather than step: the hour hand advances with the minutes, the minute hand
  with the seconds.
- **[S-52]** A clock without seconds (and with a digital face) asks for a **1-minute** cadence. An
  analog face always needs seconds, because its second hand sweeps.
- **[S-53]** An unknown or malformed time-zone id degrades to the local zone rather than throwing —
  this runs on every tick.
- **[S-54]** Formatting is done explicitly, not with culture patterns: the app builds with
  `InvariantGlobalization`, so culture format strings would not honour the config.

## Failure rules

- **[S-60]** No Win32 callback may let a managed exception unwind into native code. The command
  window's `WndProc` and the monitor enumeration callback each wrap their whole body and fall
  through.
- **[S-61]** Every GDI handle is freed on all paths. `SetWindowRgn` takes ownership of the region on
  success, so the handle is deleted only while still owned and on every failure path.
- **[S-62]** No error is silent. Every catch either handles, logs, or is annotated with why ignoring
  it is correct.
- **[S-63]** One failing display, widget or tick subscriber must not cost the user the others:
  surface creation, tick dispatch and log sinks all isolate per-item failures.
- **[S-64]** Themia puts **nothing** in the notification area. It is wallpaper with widgets on it,
  not a program the user runs, and a permanent tray entry says otherwise. That removes the menu the
  app used to hang everything off, so what it was the only route to has to live somewhere else:
  edit mode on a global hotkey, and everything that menu offered on the edit toolbar.
- **[S-65]** **Ctrl+Alt+W** toggles edit mode, registered `MOD_NOREPEAT` so a held key does not
  toggle it dozens of times a second. Another program may already own the combination; that is
  logged as an error and leaves a working app, because double-clicking a widget and launching
  Themia again both still reach edit mode. The hotkey is unregistered on shutdown — one that
  outlives its window is a combination nothing can claim until the session ends, including the copy
  the user starts next.
- **[S-66]** Launching Themia while it is running **activates the running copy** instead of starting
  a second. Two processes drawing two widget layers over one wallpaper is indistinguishable from
  one, and with no tray entry there is nothing left to tell them apart by. The running copy is found
  by its command window's class name, which must therefore stay stable across versions.
- **[S-67]** Exit is offered in one place, the edit toolbar's overflow, and not beside **Done**: it
  is the only toolbar action that cannot be undone and **Done** is the one next to it the user
  presses constantly.

## Clipboard & transfer between displays

- **[S-70]** In edit mode the surface handles **Ctrl+C / Ctrl+X / Ctrl+V**, **Delete** and
  **Escape**. The cards are focusable, because with nothing focusable in the tree no `KeyDown` is
  raised at all and the shortcuts never fire. Clicking empty surface also takes focus, so the
  shortcuts keep working after clicking away from a widget.
- **[S-71]** The clipboard is **in-process**, not the system clipboard: the payload is a live widget
  configuration, and putting it on the system clipboard would clobber the user's actual clipboard.
- **[S-72]** A clipboard entry is a **snapshot**. It survives the original being moved,
  reconfigured or deleted.
- **[S-73]** A **cut is a mark**, not an immediate removal: the original stays until a paste lands,
  so changing your mind costs nothing. A cut is consumed by one paste (pasting it twice would mean
  moving one widget to two places); a copy stays available for repeat pastes.
- **[S-74]** A paste is centred on the cursor, then snapped and clamped ([S-06]/[S-09]), so pasting
  onto a differently-shaped display still lands a fully on-screen widget. It goes to the view of the
  display under the cursor.
- **[S-75]** A paste removes the original and adds the copy in **one** mutation, so a cut across
  views can neither duplicate the widget nor lose it. Pasting a cut back into its own view leaves
  exactly one widget.
- **[S-76]** A drag that ends with the pointer over another display hands the widget to that
  display's surface, keeping its identity. Only when the destination shows a **different** view —
  when both show the same view the widget is already on both, so moving it would be a no-op that
  rewrote its position against the wrong screen.
- **[S-77]** The drop **point**, not the dragged rect, decides the destination: the rect is clamped
  to its own screen ([S-09]) and so can never reach across the display boundary.
- **[S-78]** While a widget is dragged, a **ghost** shows where it would land: on the display under
  the pointer, only when that display would really receive it ([S-76]), with the card on the screen
  it is leaving faded. Only a move hands a widget across; a resize that ends over another screen is
  still a resize.

## System meters

- **[S-80]** CPU, memory, disk and battery read straight from Win32 (`GetSystemTimes`,
  `GlobalMemoryStatusEx`, `GetDiskFreeSpaceEx`, `GetSystemPowerStatus`), not from
  `PerformanceCounter`: that pulls in the whole perf-counter stack, is slow to its first value and is
  not trim-safe, none of which a widget sampling one number a second can justify.
- **[S-81]** CPU load is a **delta** between two samples of the system's cumulative timers, so the
  first tick has nothing to subtract and reports 0 rather than inventing a figure. `GetSystemTimes`
  already counts idle inside its kernel total, so busy is `(kernel + user) - idle`.
- **[S-82]** A meter that cannot be read says so (`--`, dimmed) rather than showing 0%. A machine
  with no battery reports 255 for the charge, which is "unknown", not "empty".
- **[S-82a]** "Not present on this machine" and "present but unreadable right now" are different
  states and look different: the first is a proper empty state, the second a dimmed `--`. A desktop
  with no battery is not a battery meter that failed. The reading carries a flag saying which;
  matching on the caption text, as a first attempt did, made a display decision depend on the exact
  wording of a string in another file.
- **[S-83]** *(Superseded by S-1120/S-1126: the cadence is a setting with per-kind defaults, and
  the battery draws its history.)* Cadence follows what the number actually does: CPU and memory once a second, disk and
  battery once a minute. Battery draws no history - at a one-minute cadence a flat line reads as a
  broken graph rather than a stable charge.
- **[S-84]** Byte sizes use **binary** units, because that is what Windows reports; a widget
  disagreeing with File Explorer about the size of the same drive reads as a bug. A percentage never
  rounds a non-zero load down to 0% nor an incomplete one up to 100%, because both read as "nothing
  is happening" when something is.

## Where account data comes from

- **[S-89]** Mail, calendar, contacts, tasks, OneDrive, GitHub and Claude usage are synced by Themia
  itself, from accounts it signs in to and stores on this PC ([S-500]..[S-539]). There is no external
  hub. The hearth-hub design that came before ([S-90]..[S-99], [S-180]..[S-187]) is retired: a widget
  that depended on another program running and granting consent showed nothing whenever it was not,
  and the user wanted Themia to own its data. Nothing of that build's data is read ([S-1050]).

## Processes, network and ping

- **[S-100]** Process CPU comes from one `NtQuerySystemInformation(SystemProcessInformation)`
  snapshot, not from per-process handles. Windows refuses a handle to anything elevated, protected
  or in another session — about a third of an ordinary desktop, including Defender, Search and the
  service hosts — so a handle-based list structurally cannot contain the actual CPU hogs. The handle
  path survives as the documented degradation when the call fails, and either way a row whose value
  is genuinely unknown says `--` rather than `0%`.
- **[S-101]** The snapshot is parsed defensively: every entry offset, thread-array span and chain
  link is bounds-checked against the buffer, and a chain that does not parse stops the walk rather
  than reading arbitrary memory as a process. A layout change therefore degrades to the fallback
  instead of misreporting.
- **[S-102]** The snapshot buffer is pinned and reused. The kernel writes image names *into* it and
  hands back pointers into it, so a moving collector would invalidate the names mid-read, and
  reallocating it per tick would be the most expensive thing the widget does.
- **[S-103]** An image name is materialised once per process, not once per sample: a pid whose start
  time still matches is the same process with the same name.
- **[S-104]** Process identity is (pid, start time as `FILETIME`) on both paths, so switching paths
  does not look like a recycled pid.
- **[S-105]** The list fills its tile with **correctly sized rows**, not stretched ones: the row
  height is Fluent's, and the configured count is clamped to how many of them the tile can actually
  draw. A bigger tile shows more rows, never taller ones — a list whose rows grow with the window is
  a web idea, and next to Explorer it reads as the wrong application. Row visuals are rewritten in
  place; rebuilding the list each sample made it blink twice a second.
- **[S-106]** Ping is sampled asynchronously and never blocked on from the tick. One probe is in
  flight at a time; a probe that overruns its interval delays the next reading rather than stacking
  up.
- **[S-107]** A late ping completion is discarded once the view is detached or the host has changed.
  Changing the host cancels the in-flight probe, clears the sparkline and re-probes at once.
- **[S-108]** A timeout or an unreachable host is a displayed state, not an error: logged once per
  transition, never once per tick, and the caption keeps the host so `--` says what failed.
- **[S-109]** The ping host is validated at **both** boundaries — the settings box and the probe. An
  implausible value is never persisted, and an empty box means the default host, named in the panel,
  never "probe nothing".
- **[S-110]** The latency bar is scaled against a 300 ms ceiling, not the adapter's link speed: a
  negotiated line rate says nothing about round-trip time and would pin every reading at the bottom.

- **[S-111a]** A sparkline's x-axis is a fixed window of slots, not "spread whatever samples are held
  across the full width". The newest sample is pinned to the right edge and each older one steps left
  by exactly one slot, so the trace grows in from the right and reaches the left edge only when the
  window is full. Spreading them instead makes the graph rescale on every sample for the first
  minute of a widget's life, which reads as the data moving when only the buffer is filling. The
  original app carries the same note against the same bug.
- **[S-111b]** The y-axis is the full range, never the range actually observed. A meter rescaled to
  its own recent minimum and maximum turns a steady 47% into a dramatic zigzag of noise, which is the
  opposite of what a glanceable readout is for.
- **[S-111c]** A meter's caption is laid out in a star column, not a horizontal stack. A stack
  measures its children with unbounded width, so the caption never learns how much room it has and
  its trimming never fires: on a narrow tile it ran past the edge of the card and was cut mid-word.

## Notes and calculator

- **[S-120]** The calculator's arithmetic is a hand-written recursive-descent parser in
  `Themia.Core`. The original could not use `eval` because the webview CSP blocked it; here the
  reason differs and the conclusion is the same — a Native AOT binary has no scripting engine and
  must not grow one.
- **[S-121]** Grammar: `+ - * /`, `^` right-associative, unary sign, parentheses, decimals.
  Precedence low to high: `+ -`, `* /`, unary, `^`.
- **[S-122]** Input is sanitised to `0-9 + - * / ( ) . , % ^` and space; every comma is dropped as a
  thousands separator, so `1,5` is fifteen and never one-and-a-half, and `%` becomes `/100`.
- **[S-123]** Evaluation is **total**: any input returns empty, a finite value, or an error. Nesting
  is capped at 64, because parentheses and unary signs recurse and a `StackOverflowException` cannot
  be caught — it takes the process with it.
- **[S-124]** Numbers are parsed and written invariant. The app builds with `InvariantGlobalization`,
  and a result written with a decimal comma could not be typed back into the box it came from.
- **[S-125]** Calculator history is in-memory only, newest first, capped at 8. It is never persisted:
  a scratchpad is not a record, and typed text does not belong in the state file.
- **[S-126]** The decimal-places setting applies to the live preview and to every history row already
  on screen; trailing zeros are dropped and a value that rounds to zero is never written `-0`.
- **[S-127]** A note **is** its widget's config, so it travels with copy and paste, with the view, and
  with a backup. There is no note store, no note list, no markdown and no sync — the port is the
  plain-text core of the original, not the original.
- **[S-128]** The note is written on a 600 ms debounce and flushed on blur and on detach; the
  debounce is a write-rate limit, not a correctness path. An incoming config value never overwrites
  an unflushed local edit.
- **[S-129]** The editor's chrome is removed through the `TextBox`'s own resource keys, not its
  `Background`: the stock template's visual states assign the border element directly and outrank a
  local value, so the field would light up solid on hover.
- **[S-130]** "Clear notes" confirms in a dialog. The text is unrecoverable, so one click is not
  enough, and failing to show the dialog leaves the note alone.
- **[S-131]** The calculator drops its history under 120 DIPs of height, so a small tile shows the
  sum being typed rather than two clipped rows above it. The header rule that used to live here is
  the card's now ([S-402]) — every widget had reached the same conclusion with its own threshold.

## Countdown and pomodoro

- **[S-140]** A countdown row is a label, the absolute local target, and the duration. A target in
  the past is not hidden: it reads "… ago" in the theme's critical colour with the label stepped
  back, because how long ago a deadline went is the thing you want to know.
- **[S-141]** `auto` picks the coarsest unit that still says something: "in a moment", "N mins",
  "Nh Nm" (or "N hrs" on the hour), "Nd Nh" (or "N days" on the day), and past a week just days.
  Under a minute in the past is "just now", never "in a moment ago". The explicit formats keep their
  zero components, because a fixed shape is what makes a column of durations readable.
- **[S-142]** The one-second cadence is bought only by a timer that actually shows seconds;
  everything else re-letters twice a minute, and a widget with no timers does not wake at all.
- **[S-143]** Rows are built when the list changes and only re-lettered on a tick. Rebuilding them
  each tick drops the row under the pointer, which at one second is every second.
- **[S-144]** A cleared, impossible or non-existent (DST-skipped) target leaves the timer pointing
  where it did. Writing "now" is what the original did, and it silently retargeted a timer mid-edit.
- **[S-150]** Pomodoro run state — phase, seconds left, running — lives in the view instance and is
  never persisted. The same widget on two monitors is two independent timers, which is also why only
  a running instance can sound the cue.
- **[S-151]** At zero the phase flips and the new phase's full duration loads, still running. One
  second remaining is the last second, not a spare one.
- **[S-152]** The cue sounds on the flip alone and announces the phase being *entered*: ascending
  into focus, descending into a break. Nothing sounds on start, pause or reset.
- **[S-153]** The cue is synthesised in Core and played through `winmm!PlaySound` from memory, not
  through `MediaPlayer` or `AudioGraph`: those are whole media pipelines the AOT compiler roots in
  full in order to play half a second of a sine. It is played on a pool thread with the buffer
  pinned for exactly the duration of the call, so nothing stays pinned for the life of the process.
- **[S-154]** There is one cue, not the original's twelve ringtones and a volume slider. A
  twelve-voice synthesiser is a feature in its own right rather than a port of one, so the setting is
  an honest on/off.
- **[S-155]** The 1 Hz subscription exists only while the timer runs, and the callback the shared
  ticker makes at subscribe time is swallowed: that immediate fire is right for a view that renders
  the time and wrong for one that *is* the tick.

## Sun and moon

- **[S-160]** The astronomy is pure functions in `Themia.Core.Astronomy` taking an explicit instant,
  working in UTC throughout. `InvariantGlobalization` means IANA ids do not resolve, so display
  converts with the machine's local zone alone and no time-zone dependency exists.
- **[S-161]** Sunrise and sunset come from the standard sunrise equation, good to a minute or two,
  with no network. Polar day and polar night return null times plus a marker, and the tile shows an
  em dash with 24h or 0h of daylight.
- **[S-162]** The day reported is the **solar** day at the configured longitude, not the UTC day, so
  sunrise and sunset belong to the local day at every longitude and in both hemispheres.
- **[S-163]** The next event is the earliest one strictly after now, scanning the solar day before,
  of and after it; null inside the polar circles. The countdown therefore never sticks at `0m`.
- **[S-164]** Moon phase and illumination come from the synodic month off a known new moon, in eight
  buckets — a linear model, within 0.02 of a cycle of the real syzygies.
- **[S-165]** The sub-solar and sub-lunar points come from instantaneous right ascension and
  declination against GMST, never the rounded day index the sunrise equation uses, which would freeze
  them for a whole day.
- **[S-166]** Sky position gives which side of the horizon a body is on plus a progress running 0 at
  rise, 0.5 at the meridian and 1 at setting, normalised by that body's own semi-diurnal arc so the
  ends land exactly on rise and set.
- **[S-167]** The drawn curve is the sun's **real** path, not a semicircle: it peaks at the true noon
  altitude, crosses zero exactly at the ends of the semi-diurnal arc, flattens in winter and towers
  in summer. The axis tops out at the highest the sun ever reaches at that latitude.
- **[S-168]** The night stretch continues the same way round the sky — setting on the right, nadir,
  rising on the left — because a body sets on one side, passes underneath and comes back up the
  other. Its depth is compressed against the daylight scale on purpose: a true-to-scale winter
  midnight would be deeper than the whole widget, and nobody reads a number off it.
- **[S-169]** One lit-limb outline draws both the corner badge and the row glyph, so the two cannot
  disagree. It is mirrored for a southern observer, keeps a hairline rim on the unlit part so a thin
  crescent still reads as a moon, sits *beside* the graphic rather than on the solar arc, and dims
  below the horizon.
- **[S-170]** The two display settings are independent, except that hiding the graphic forces the
  rows on: a widget showing nothing is never the answer. The graphic itself is never dropped — it is
  sized down to a 46px floor and keeps drawing. Only extras shed, cheapest-first by width: the
  countdown, then the label icons and the moon percentage, then the daylight label, then the number
  strip. (An earlier version of this line had the daylight label going before the icons. The code
  was right and the spec was wrong: "12h 6m" says more than two glyphs do.)
- **[S-171]** There is no geocoding service here and widgets make no network calls, so the settings
  take latitude, longitude and place name directly. Input that is not a number in range is
  **rejected** — the last good value stands and the row says why — never coerced to 0, which would
  silently move the user to the Gulf of Guinea.

## Rows and the card edge

- **[S-190]** The card applies **no** content padding. Each view insets its own parts, and a list row
  applies the inset as its own *padding*, so the row — and the highlight it draws under the pointer —
  reaches both edges of the widget while its text stays exactly where it was. A row inset from the
  card edge reads as a box inside a box, and a highlight that stops short of the edge reads as a
  button rather than as a row.
- **[S-190a]** A full-bleed row therefore has no corner radius and no negative margin. The negative
  margins that used to cancel the card's padding are gone rather than adjusted: something pulling
  itself back out of a padding that no longer exists would now hang over the edge.
- **[S-190b]** A list has **no separators at all**. Windows 11 separates rows with space and with
  the fill under the pointer — Settings, Explorer and the Widgets board all do — and a rule between
  every row is the single clearest tell of a web table. The one rule that survives anywhere is the
  calculator's, which divides a scrolling region from a fixed command area the way a dialog does.
- **[S-191]** Headers, footers, empty states and readouts keep the inset — they are labels, not rows.
  A day heading in the calendar is a label and lines up with the header above it.

## Looking like Windows

- **[S-210]** Type comes from the **Fluent ramp**, never from a number computed off the tile. A widget
  still scales with its box — a resizable widget has to — but it scales by choosing a step, so every
  size on screen is one Windows itself uses. The web app's `clamp(min, min(Ncqw, Mcqh), max)` is what
  made the port read as a dashboard that happened to be running here.
- **[S-210a]** A hero readout on a very large tile may scale past Display, keeping Fluent's own
  line-height ratio, because the ramp stops at 68px and a clock filling a quarter of a screen
  legitimately does not.
- **[S-211]** Colour comes from **named brushes**, never from an opacity applied to a text block.
  `Opacity = 0.65` is not a colour; it is a colour the theme cannot change, and in light theme it is
  the wrong one. The single exception is the wash under a chart line, which is a translucent *area* —
  and even there the alpha belongs to the shape, never to the shared brush every other accent surface
  paints with.
- **[S-212]** Spacing is the 4px grid. No 6px, no 7px, no 3px.
- **[S-213]** A list row is a **fixed** Fluent height, standard or compact. A bigger tile shows more
  rows, never taller ones.
- **[S-214]** A card is a tint **on** something, not a flat fill: the opaque surface colour Fluent
  expects, with the real `CardBackgroundFillColor` overlay on top. That overlay is the lift that makes
  a Windows card look raised, and on its own — over a transparent window — it is nearly invisible.
- **[S-215]** A chart is **full-bleed**: it reaches the card's left, right and bottom edges, and is
  clipped to its corner radius. A chart floating inside a margin reads as a picture of a chart. Text
  carries the inset instead, exactly as a list row does.
- **[S-215b]** The graph and the bar share one grid row rather than taking one each. They are
  alternatives — one shows exactly when the other does not — and as separate rows the grid's row
  spacing still sat between them, leaving a gap under a chart that is supposed to touch the
  bottom of the card. A collapsed row has no height; it still has a gap.
- **[S-215a]** The chart is a band of bounded height at the foot of the tile, and the **reading**
  takes the slack. Giving the graph the slack put a 350px band on a tall tile that a young trace had
  no way to fill, with the number stranded at the top.
- **[S-216]** A meter whose history moves slower than its window can fill draws no graph by default.
  Free space sampled once a minute is a bare sliver for most of an hour and a flat line for the rest;
  the bar says the same thing immediately. The setting stays, for a drive that really is filling up.

## The widget header

- **[S-400]** The header belongs to the **card**, not to the widget. One builder means one look; the
  alternative was thirty-odd widgets each with their own chance to disagree about its margin, its
  shedding threshold and its title — and nowhere to put a setting like "draw it at the bottom" that
  every one of them would have had to honour.
- **[S-400a]** The card knows the icon (the catalogue has it) and the title (the instance has it).
  The only thing it cannot know is what goes on the right — a badge, a count, a busy ring — so that
  is the whole of what a view contributes, through `IWidgetHeaderContent`. It is read **once**, when
  the header is built, so the element has to exist by then.
- **[S-401]** Header placement is per widget: top, bottom, or hidden. Switching it moves a row index,
  not a rebuild, so it costs nothing and cannot restart what the widget is doing.
- **[S-402]** The header sheds on a tile too short to carry it, **once**, in the card. Every widget
  had independently drawn that conclusion with a slightly different threshold.
- **[S-403]** The title is the user's own when they set one, otherwise the catalogue's. A widget has
  no say in it: a view that wants to label itself differently is a view that has drifted.

## Containers

- **[S-410]** Group, Stack and Tabs hold other widgets. Membership is a `ParentId` on the child, never
  a list of children on the container, so a widget cannot be in two containers at once and a
  container cannot name a child that has been deleted.
- **[S-411]** A **group** does not lay its children out. They keep the place on the grid the user
  chose and the group draws behind them, which is why dropping a widget into a group does not move
  it. A **stack** and a **tab strip** own their children's geometry outright.
- **[S-411a]** Neither ever writes back to a child's stored rect, so a widget taken out of one
  returns to exactly the place it had before it went in.
- **[S-412]** A container does not create its children — the surface does, exactly as it creates
  every other card. A widget inside a stack is the same object with the same lifetime as one on the
  desktop, so nothing has to be special-cased to keep it ticking, and nothing restarts when it moves.
- **[S-413]** A group collapses by **hiding** its children, never by rearranging them, so expanding
  puts every one back where it was.
- **[S-414]** A closed stack row and an unselected tab **collapse** their child rather than merely
  covering it: a widget nobody can see should not be measuring itself on every layout pass.
- **[S-415]** A group is moved to the back of the widget layer, because the layer paints in child
  order and a group added after its members would cover them.
- **[S-416]** "Move into" is built from what the tree will actually accept, not from every container
  on the desktop. An option that refuses itself when clicked is worse than no option.
- **[S-417]** Deleting a container **keeps** its children and hands them to whatever held it.
  Removing the box is not removing the things in it, and the alternative is a dozen widgets gone on
  one click on a surface with no undo.
- **[S-418]** A container cannot be put inside itself or inside its own descendant — that detaches a
  branch into a ring nothing can reach and no layout pass terminates on — and a branch is measured by
  its own height when it moves, because it moves as a whole. Nesting stops at a depth.
- **[S-419]** The state file can name a parent that is gone, a parent that is not a container, or a
  cycle: none of which the UI can produce, all of which a hand edit or a partial write can leave
  behind. Loading repairs each to a widget on the desktop — a state the layout can always draw — and
  says how many rather than repairing silently. Membership is repaired **last**, after ids are known
  unique, because the tree is walked by id.

## Adding a widget

- **[S-420]** Widgets are added from **Add widget** on the edit toolbar: a picker listing every
  widget in sections, the sections split into columns by `ColumnLayout` — reading order kept, no
  section torn from its heading, the tallest column as short as a contiguous split allows. There is
  no second way in from the settings window.
- **[S-421]** Picking a widget does **not** add it and does not open its settings. It starts
  placing: a ghost of the widget at its default size follows the pointer across every display, the
  cards stop taking input so a click over one places the new widget rather than grabbing the old,
  and a line in place of the toolbar says what the click will do. Escape or a right-click cancels;
  leaving edit mode cancels.
- **[S-422]** A left click places the widget on the display it lands on, in that display's view, at
  the rect computed from the click itself — not from the last pointer move — centred, snapped and
  clamped ([S-11]). Alt bypasses the snap, as when dragging.
- **[S-423]** A ghost traces the **painted** card, inside the gap, with the card's own corner radius:
  it shows what the user will see once the widget is there, not the cell it owns.

## App widgets and About

Help, Themia and the view switcher, ported from `widgets/others/{help,themia,view-switcher}` on
`main`, and the settings window's About page. Owning files: `Widgets/HelpView.cs`,
`Widgets/ThemiaView.cs`, `Widgets/ViewSwitcherView.cs`, `Shell/SurfaceDisplay.cs`,
`Shell/Config/ConfigPages.About.cs`, `Shell/Gallery/SettingsRender.cs`,
`Themia.Core/Widgets/{HelpGuide,RichText,ViewSwitcherRules}.cs`,
`Themia.Core/Storage/SettingsTransfer.cs`.

- **[S-480]** Help has nothing to configure (was [S-13]). Its header is on, as on any widget; the
  open tab is not saved, because the guide is read, not set up.
- **[S-481]** Four tabs in order — Basics, Widgets, Views, Customize — on a `SelectorBar`, Basics
  first (was [S-06]/[S-07]). Below 400px wide a tab carries its icon alone and names itself in the
  tooltip; below 160px the strip goes (four icons no longer fit, and a cut-off tab looks broken) and
  the open page just scrolls; below 240px the prose drops from Body to Caption.
- **[S-482]** The guide is data (`HelpGuide`), not controls. Emphasis is `*strong*` / `_emphasis_`,
  split by `RichText` into plain runs — never parsed as markup; markers do not nest or cross a line,
  and an unpaired marker stays literal (was [S-14]). Every marker in the shipped guide is paired.
- **[S-483]** The guide describes **this** app, and so — unlike the original's [S-05] — it teaches the
  keys: Ctrl+Alt+W, double-clicking a widget and launching Themia again as the ways into edit mode,
  Add widget and click-to-place, Alt to bypass the snap, Ctrl+C/X/V, Delete, Esc, dragging between
  displays, per-display views. The original could point at a tray icon and a view bar; this app has
  neither, so a guide without the keys would leave no way to change anything.
- **[S-484]** Themia draws the mark (`Assets/themia-logo.png`, from `src-tauri/icons` on `main`), the
  name and `v` + the running version, centred (was [S-01]/[S-03]). The version is known before the
  first frame, so there is no empty "v". The mark is decoded logically at its drawn size, in 32-DIP
  steps; the version, then the name, give way on a tile too short for them.
- **[S-485]** Themia and the view switcher start with their header **hidden** (`WidgetInstance.
  DefaultHeader`; was the original's `noHeader` / `defaultHeaderMode: 'none'`). Only a default: the
  user can turn either header on.
- **[S-486]** Neither Help nor Themia ticks, polls or fetches.
- **[S-487]** The view switcher switches the view of **the display it is on**, never the app-wide one:
  the card host hands the widget an `IWidgetDisplay` for its display (`SurfaceDisplay`), and a click
  goes through `AppHost.ShowView` ([S-19]), the same mutation as the toolbar's View menu. Where there
  is no display it shows an empty state; the gallery gives it a three-view fixture.
- **[S-488]** It offers the views in the user's view order, filtered by the widget's selection; an
  empty selection means every view, so views added later appear; a selection naming only deleted
  views also means every view. The shown view is the raised button with its initials in the accent.
- **[S-489]** A button falls back to initials: the first letters of the first two words, or the first
  two letters of a single word, upper-cased; "?" for a blank name. Counted in text elements, so a
  composed character is never split.
- **[S-490]** Settings: one switch per view and "Initials only". The last view cannot be switched off;
  switching every view on stores the empty "all" selection again.
- **[S-491]** The buttons fill the card as one row, or one per row — whichever makes each button
  closer to a 3:1 button shape (compared on a log scale), and a stack only when every button gets
  32px. The original only drew a row, which on anything but a strip made tall columns with clipped
  names. Names show when a button is at least 88px wide and "Initials only" is off; otherwise
  initials, with the name in the tooltip. Default size 320×56: one row with names.
- **[S-492]** The card's right-click menu carries "Initials only" / "Show names" (was the original's
  `contextActions`), written to the widget's own config and saved.
- **[S-493]** About is the **last** item of the settings navigation, in its footer under everything
  else. The page follows karpo's About section for section: the name card (64-DIP mark, "Themia",
  "Version x.y.z", ".NET x · Windows App SDK y"), **Made by** Lometics (lometics.com) with its mark and a
  link, **Themia** with Quit Themia (`AppHost.RequestExit`), and **Settings file** with Export,
  Import and Open config file.
- **[S-494]** Export writes the state document — the same JSON as `state.json` — to a file picked
  with `Microsoft.Windows.Storage.Pickers` (window id, works unpackaged), atomically (temp + move).
- **[S-495]** Import reads the picked file through `AppStateStore.Parse`, the startup trust boundary,
  so an import can never accept what a restart would refuse. Empty, non-JSON, newer-schema and
  view-less documents (any other app's JSON would otherwise read as "one blank view") are refused
  with a reason and change nothing.
- **[S-496]** Before anything changes, a dialog says what will: views matched by id, then by name;
  renamed, added and removed views; per view the widgets added, removed and changed; every design
  value before → after. Repairs, displays named in the file that are not connected, and Start with
  Windows staying put are notes, not changes.
- **[S-497]** On confirm, what the user has now is written beside the state file as
  `state.before-import-<yyyyMMdd-HHmmss>.json` — the in-memory state, since a debounced save may not
  have landed — and if that backup cannot be written nothing is imported.
- **[S-498]** The import replaces the state **in place** in one mutation, then gives connected
  displays without an assignment a view as at startup. Start with Windows keeps this PC's value: it
  is a fact about the registry here, not a preference.
- **[S-499]** Debug builds render one settings page to PNG with `--settings-png <AppPage> <out.png>
  [waitMs]`, over a demo state and an `AppStateStore.Detached` store that accepts saves and writes
  nothing, so the render never touches `%APPDATA%`.

Not ported, on purpose: the switcher's per-view context menu (switch / configure / duplicate /
delete view) — the card owns the right-click menu here, and views are managed from the View menu and
the settings window; ignoring clicks in edit mode — no widget here does, and the buttons are not
where a drag starts; view icons — views in this app have names only. Help's localisation ([S-14]'s
locale files) — this build has no localisation. Karpo's "isn't running / Start" card — the settings
window here runs inside the app, so it is never open while the app is not.

## Web widgets: video stream and web element

Ported from `widgets/online/{video-stream,web-element}` on `main`. Owning files:
`Widgets/VideoStreamView.cs`, `Widgets/WebElementView.cs`, `Widgets/RotatedBox.cs`,
`Widgets/ShownWatch.cs`, `Widgets/IWidgetContextActions.cs`, `Services/Web/StreamSession.cs`,
`Services/Web/PageSnapshot.cs`, `Services/Web/WebViewHost.cs`, `Shell/Config/ElementPickerWindow.cs`,
`Shell/Config/WidgetSettings.Web.cs`, `Shell/Gallery/GalleryFixtures.cs`,
`Themia.Core/Widgets/{VideoStreamRules,MjpegFrameReader,WebElementRules}.cs`,
`Themia.Core/Model/Configs/WebWidgetConfigs.cs`.

Both widgets run only while they can be seen. A paused camera, and either widget folded into a closed
stack row, an unselected tab or a collapsed group (`ShownWatch` follows the card's ancestors), holds no
socket, no decoder and no browser; `Detach` releases everything.

- **[S-460]** Video stream title: the user's label, else the stream's `host:port` (`Uri.Authority`,
  so a non-default port is kept), else the widget name (was [S-01]).
- **[S-461]** Web element title: the user's label, else the page host without `www.`, else the widget
  name (was [S-11]).
- **[S-462]** The pinned element is a passive picture: not hit-testable, no scrolling, no links, no
  focus (was [S-10]). It is shown at its own size or scaled down to fit (`StretchDirection.DownOnly`),
  centred, never scaled up.
- **[S-463]** The web element's right-click menu has **Refresh**: re-render now (was [S-15]).
- **[S-464]** A failed, dropped, stalled or ended stream shows "Stream unavailable. Reconnecting…" and
  retries after 2 s, 4 s, 8 s … capped at 30 s (`VideoStreamRules.RetryDelay`; was [S-05]/[S-06]). The
  counter resets on the first decoded frame and on any restart. The delay is armed by the failure and
  cancelled with the session. A stream that sends no bytes for 30 s counts as dropped — a socket
  deadline, which closes the original's [U-02] without a frame-rate watchdog.
- **[S-465]** The camera's right-click menu has **Pause/Resume**, **Rotate** and **Refresh** (was
  [S-11] and the original's `contextActions`). Refresh reconnects at once with the backoff reset.
  Context actions are a card-level hook: a view implements `IWidgetContextActions` and the card lists
  them under Configure, read fresh each time the menu opens.
- **[S-466]** Fit: whole picture (letterbox, default), fill and crop, stretch (was [S-16]). One
  `Stretch` mapping drives both the decoded-frame `Image` and the video player.
- **[S-467]** The stream address is checked by `WebInput.Url` on every read — http/https only, no
  credentials, no UNC, refused rather than repaired — and the settings box explains a refusal (was
  [S-09]). A refused address shows its reason in place of the picture; an empty one shows "Add a camera
  stream address in settings" (was [S-02]).
- **[S-468]** Rotation 0/90/180/270, from settings or cycled by Rotate (was [S-08]). `RotatedBox` lays
  a sideways picture out against the swapped axes before turning it, so it fits instead of clipping.
  A value the enum does not name reads as none.
- **[S-469]** Paused is persisted, so a parked camera stays parked across restarts and uses no network;
  it shows "Paused" (was [S-07]).
- **[S-470]** `RefreshSeconds` 0–300, default 0 (was [S-17]). 0 holds the stream open at the camera's
  rate; any other value grabs one frame, closes the connection, waits, and grabs again — the only real
  throttle, since a camera pushes frames at its own rate. The cache-buster the original needed for the
  webview's memory cache is gone: `HttpClient` keeps no cache, and requests send
  `Cache-Control: no-cache` for any cache in between.
- **[S-471]** The reply's content type picks the transport (`VideoStreamRules.Classify`):
  `multipart/x-mixed-replace` → MJPEG, parsed here; `image/*` → a snapshot (live mode shows it once,
  interval mode polls it); `video/*` → Windows' `MediaPlayer`; an HLS playlist → `AdaptiveMediaSource`.
  Video is muted, looped, kept out of the system media flyout, and the player is created only for such
  a stream. Anything else — an HTML login page, a 401/403/404 — is "not a camera stream" and is **not**
  retried on a timer.
- **[S-472]** MJPEG frames come out of `MjpegFrameReader`: a part's `Content-Length` is trusted when
  present (a JPEG can contain the boundary bytes), otherwise the part runs to the next delimiter;
  quoted and `--`-prefixed boundaries, LF-only line ends, preambles, header-less parts and keep-alive
  empty parts are handled; with no boundary at all, frames are cut at the JPEG start/end markers. A
  frame past 8 MB, or a stream that never delimits, overflows and is reconnected rather than buffered.
  Frames are decoded by WIC at the size drawn; while one decodes, newer frames are dropped, not queued.
- **[S-473]** Web element `Url` (default empty) is checked by `WebInput.Url` on every read (was [S-07]).
- **[S-474]** `Selector` (default empty) is the pinned element's CSS selector; `PickedLabel` is a
  friendly description shown only in settings (was [S-08]). A selector that is empty, over 1024
  characters or holds a control character is treated as unset. It reaches the page only as a JSON
  string literal, never spliced into script. With no address or selector the widget prompts "Pick an
  element from a web page in settings" (was [S-01]).
- **[S-475]** `RefreshMinutes`, default 15, clamped to 5–120 (was [S-09]/[S-20]), offered as 5/15/30/60
  in settings. The shared ticker asks at that cadence; a render happens only when the widget is
  visible and its picture is due, and a widget shown again after being hidden renders at once if stale.
- **[S-476]** Rendering (replaces [S-03]/[S-04]/[S-16]): `PageSnapshot` loads the page in an
  off-screen WebView2 at a 1280×900 desktop viewport and the display's device scale, lets it settle
  (600 ms without a DOM mutation, 8 s at most, then again briefly after scrolling the element into
  view), and captures **a PNG of the element** with DevTools `Page.captureScreenshot`. No remote HTML
  is parsed or shown by the app, so there is no sanitiser, no URL rewriting and no stylesheet inlining
  — and web fonts, which the original could not load, render. One render at a time across all widgets
  (30 s budget), and the browser is closed after each: none runs between refreshes.
- **[S-477]** A selector that matches nothing, or a zero-sized element, is "not on the page any more"
  — its own state, not an error — and drops the old picture (was [S-05]).
- **[S-478]** A failed render (unreachable, timed out, bad certificate, browser missing, a reply not
  the probe's shape) shows its reason; with a good render already on screen, the picture stays and its
  tooltip says the last refresh failed and how old the picture is (was [S-06]/[S-25]).
- **[S-479]** **Pick element…** in settings opens a picker window on the live page (was
  [S-13]/[S-14]/[S-32]). The toolbar has Back, the address, **Pick element** (a toggle) and Cancel;
  browsing is fully interactive until the toggle is on, then the hovered element is outlined and the
  next click pins it; Escape returns to browsing. The page's own `cssPath` (unique-id anchor, else a
  `tag:nth-of-type(n)` chain) is the original's. Its message is parsed by
  `WebElementRules.ParsePickerMessage` — two shapes only, the address re-checked — and a pick is
  accepted only while the toggle is on. The picked page's address (the user may have navigated)
  replaces the widget's. One picker at a time. Picker and renderer share one WebView2 profile, so a
  cookie banner dismissed in the picker stays dismissed for the renderer.

Not ported, on purpose: the HTML extraction pipeline ([S-03]/[S-04] — `sanitizeForRender`,
`extractElement`, `absolutizeUrls`, stylesheet inlining and their tests), replaced by the screenshot;
the SSRF guard ([S-08b] `check_url`) — this app has no network guard for any widget, and a camera on
the LAN is the video widget's main use; the developer-mode gate ([S-33]) — this app has no developer
mode, so the widget is listed normally; the cached render across restarts ([S-20]) — a restart renders
again once; `cssPath` unit tests — the selector code runs only inside the page and there is no DOM in
the Core tests; the `demoVideoFrame`/`demoWebElementRender` demo fixtures — the debug gallery's
`Kind+fixture` pages (a loopback MJPEG test card, httpbin.org/html) take their place. RTSP stays unsupported
([U-01]); credentials in the address are refused ([U-03]).

## Accounts and sign-in

Themia's own accounts, replacing the hearth hub as the source of mail, calendar, contacts, tasks,
OneDrive, GitHub and Claude usage. Ported from the classic app's `widgets/office/email/src/lib.rs`
(storage, DPAPI, the four OAuth flows, IMAP/DAV verification, retry) and its Email SPEC [S-45],
[S-47], [S-51], [S-53]; code in `src/Themia.Core/Accounts/` (pure) and
`src/Themia.App/Services/Accounts/` + `Shell/Config/AccountsPage.cs`.

- **[S-500]** A closed `AccountProvider` set: Microsoft, Google, IMAP, CalDAV, CardDAV, GitHub, Claude.
  A provider this build does not know is dropped on load and reported, never carried as a string.
- **[S-501]** Providers are written under the classic app's lower-case names, so an imported account
  and a new one read the same on disk.
- **[S-502]** What an account feeds (`AccountFeatures`) and how it proves itself (`AccountAuth`) follow
  from the provider alone. Widgets ask for a feature, never a provider name.
- **[S-503]** `accounts.json` lives beside `state.json` and carries a `schemaVersion`; a newer schema is
  set aside rather than half-read.
- **[S-504]** Every stored entry becomes a whole `Account` or nothing. A field that says *where* the
  account is (provider, IMAP host, DAV address, a non-https GitHub API root) cannot be guessed and its
  entry is dropped; a field it can live without (nickname, IMAP port, GitHub API root) is repaired; a
  missing secret is kept as "sign in again". Every drop and repair is logged. Duplicates keep the first.
- **[S-505]** Ids are built exactly as the classic app built them (`google:<sub>`, `imap:user@host`,
  `caldav:user@authority`, `github:login@apihost`, `claude:default`, the Graph id for Microsoft), so
  signing in again replaces the account (keeping its nickname) and imported ids stay valid.
- **[S-506]** A secret is stored as `dpapi:v1:` + base64 of a DPAPI blob — the classic envelope byte
  for byte. Untagged text and a damaged payload are told apart from a good blob.
- **[S-507]** An access token within five minutes of expiry is treated as expired; a missing or absurd
  `expires_in` becomes an hour (ceiling 30 days); the classic `expires_at: 0` means "unknown".
- **[S-508]** PKCE S256 (RFC 7636 test vector pinned) with a 43-character unreserved verifier, and a
  random state compared in fixed time.
- **[S-509]** Microsoft signs in by device code against `common` with the classic client id and scopes;
  Google's consent URL asks for `access_type=offline` and `prompt=consent` so a refresh token always
  comes back.
- **[S-510]** GitHub: one OAuth device flow for github.com (same scopes as the token path), and a
  personal-access-token form whose API root must be https — the only way in for GitHub Enterprise.
- **[S-511]** Claude: Themia's own authorization-code + PKCE grant on the CLI's client (the usage
  endpoint demands `user:profile`), a hosted `code#state` callback the user pastes back, and the CLI's
  user agent plus `anthropic-beta: oauth-2025-04-20` on every usage request.
- **[S-512]** The pasted Claude value splits into code and optional state; an echoed state must be the
  one issued; an empty or code-less paste says why.
- **[S-513]** The loopback redirect is read from the request line; the state is checked before anything
  else is believed (error included); malformed pairs are skipped; a request that is not a redirect
  (favicon) is answered 404 and the listener keeps waiting.
- **[S-514]** Device-code polling is a pure state machine: pending waits the server's interval,
  `slow_down` adds five seconds for good, expiry/denial/other errors end the flow with a sentence,
  and nothing is polled past the code's lifetime. Microsoft and GitHub share it.
- **[S-515]** DAV multistatus is read by local name (any prefix), properties under a non-2xx propstat
  are absent, a body that is not XML reads as nothing, DTDs are refused.
- **[S-516]** DAV hrefs resolve against the URL the answer came from after redirects; the `.well-known`
  address sits on the origin; a bare host typed by the user becomes https.
- **[S-517]** Adding a CalDAV/CardDAV account discovers the collection (RFC 6764): the typed URL, then
  `.well-known`; a collection is used verbatim, otherwise principal → home set → first usable
  collection (a calendar advertising components must include VEVENT). Credential failure on every
  candidate is reported as such; "signed in but nothing there" wins over a 404 from the fallback.
- **[S-518]** A 429/503 on a repeatable request is retried at most three times, waiting what
  `Retry-After` says (seconds or HTTP date) capped at 30 s, else 1/2/4 s. A POST is never retried.
- **[S-519]** Redirects are followed by hand (the framework drops `Authorization` and rewrites
  PROPFIND): never to a non-http scheme, never https→http, never from a public host to a loopback or
  private address. A bearer token only follows to the same origin; a DAV password may follow an
  https→https hop to another host (iCloud shards). A paging link is only followed on the first page's
  own https origin.
- **[S-520]** Failures are a typed `AccountException` (`AccountError`), carrying the provider's own
  words when its body has any of the known error shapes, and mapping onto the widget `FetchStatus`.
- **[S-521]** IMAP login uses quoted strings, or literals for values a quoted string cannot carry, and
  recognises greetings and tagged answers.
- **[S-522]** Typing an address fills the IMAP server (or DAV address) from a small provider table —
  exact domain or subdomain only, only into fields the user has not typed in, no Outlook.com entry.
- **[S-523]** *(Retired.)* There is no import from classic Themia. The user removed it: accounts
  are signed in afresh here, and the classic app's files are never read.
- **[S-524]** `accounts.json` is written atomically (temp + move) by one writer. A corrupt file is
  moved aside, never deleted; a file that exists but cannot be read makes the store read-only for the
  session so it is never overwritten with an empty list.
- **[S-525]** Secrets are sealed with DPAPI for the current Windows user (`CryptProtectData` via
  `[LibraryImport]`, output wiped and `LocalFree`d on every path). Sealing never falls back to plain
  text. A secret that will not open is reported on the account ("sign in again"), never dropped.
- **[S-526]** One HTTP client for every account request (pooled, user agent, 10 s connect, 30 s per
  request, 16 MB read cap) and one path through it.
- **[S-527]** Google's redirect is a raw listener on `127.0.0.1:<random>`, answering one request; the
  browser wait gives up after five minutes.
- **[S-528]** An IMAP account is saved only after a TLS connect, greeting and LOGIN succeed (then
  LOGOUT); nothing is selected or read.
- **[S-529]** `AccountService` (one, in `AppServices.Accounts`) lists, renames, signs out and adds;
  `AccountsChanged` is raised on the UI thread. A user action whose save fails changes nothing.
- **[S-530]** Token refresh is single-flight per account; whoever waited re-reads the account and uses
  a token another caller already fetched. Only the token fields of the account as it is *now* are
  written, and an account signed out mid-refresh is not resurrected. A revoked grant marks the account
  "sign in again"; nothing ever deletes it.
- **[S-531]** Every authorized request gets its provider's proof; a 401 on an OAuth account triggers
  exactly one refresh and one retry.
- **[S-532]** List helpers follow Graph `@odata.nextLink` and Google `nextPageToken`, item by item
  through a source-generated contract, up to a caller's maximum and 50 pages.
- **[S-533]** DAV requests go as the account with basic auth, `Depth`, and the method and body kept
  across redirects.
- **[S-534]** IMAP credentials are opened only when a session asks for them.
- **[S-535]** Device-code sign-ins verify the account (`/me`, `/user`) before saving.
- **[S-536]** Google's Desktop client secret is never in source: it is compiled in from
  `THEMIA_GOOGLE_CLIENT_SECRET` (a generated constant, validated to look like a secret); without it,
  Google sign-in refuses before the browser opens and token refresh fails with the same sentence.
- **[S-537]** A Claude sign-in is saved only after the fresh token has read the usage endpoint; 401 and
  403 get different guidance, 403 with Anthropic's own reason.
- **[S-539]** Settings → Accounts lists each account (provider icon, name, address, problem), renames
  in a flyout, signs out after a confirmation, and adds through one flow at a time drawn in place:
  device code shown large with copy and open buttons and a live status line; Google waiting on the
  browser; forms for IMAP, CalDAV, CardDAV and a GitHub token; Claude's open-then-paste. Errors appear
  on the page; leaving it cancels a flow.

Not ported: the anonymous endpoint probe and its preset health badges ([S-46], [S-48]) — the presets
here are a fill-in table and the add itself is the check; the two-page preset picker and brand icons
([S-47]) — one form with auto-fill instead; blocking of private/LAN addresses for user-typed DAV and
IMAP hosts (`check_url`) — a user's own NAS is a legitimate server on a desktop, and redirects into
private space are still refused ([S-519]); STARTTLS on port 143 — the classic app spoke implicit TLS
only; RSS "accounts" — feeds are a widget setting here; the `devOnly` gate on Google and GitHub OAuth
— a release decision, not wired. Digest auth for DAV is not supported (basic only, as before).

## System widgets: GPU, mixer and screen time

Ported from `widgets/system/{gpu,mixer,screentime}` on `main`. Owning files:
`Themia.Core/Widgets/{GpuRules,MixerRules,AppNames,ScreenTimeTracker,ScreenTimeRules}.cs`,
`Themia.Core/Storage/ScreenTimeStore.cs`,
`Themia.App/Services/{GpuSampler,AudioSessions,ProcessNames,AppIcons,ScreenTimeService}.cs`,
`Themia.App/Widgets/{GpuView,MixerView,ScreenTimeView*,SparkBand,HugRow}.cs`,
`Themia.App/Shell/Config/WidgetSettings.SystemWidgets.cs`. Tests mirror the tags (`S433_...`).

### GPU

- **[S-430]** The GPU tile reads like the CPU and memory tiles: the load as the hero number, the
  detail lines under it, and the history as a full-bleed sparkline band along the bottom
  (`SparkBand`, 60 samples, the same fixed-slot x-axis). One adapter names the card after itself,
  without vendor and trademark noise (`GpuRules.ShortName`: "RTX 4060 Ti"). Load reads `--` for the
  first sample: it is a rate and needs two readings.
- **[S-431]** Data comes from DXGI (which adapters exist: name, dedicated memory, shared budget,
  LUID) and the PDH GPU counters (`\GPU Engine(*)\Utilization Percentage`,
  `\GPU Adapter Memory(*)\Dedicated Usage` and `Shared Usage`), all through `LibraryImport` and
  vtable calls, so AOT-clean. The original's registry enumeration is replaced: its numbered class
  subkeys include adapters that are gone and stop at the first gap, while DXGI hands over the LUID
  the counters are filed under. One sampler is shared by every GPU widget: opened by the first,
  closed by the last, sampled on the pool, single-flight, and a request within 500 ms of a sample
  reuses it.
- **[S-432]** Pressure colours the trace and the memory bars, never the number: high from 70% load,
  critical from 90% (theme caution and critical brushes).
- **[S-433]** `Adapters` is a list of DXGI positions, in drawing order. Negative and repeated entries
  are dropped; an empty selection draws the primary adapter. The settings picker shows the raw
  selection (no primary-adapter default) and is offered only with two or more adapters. Two or more
  selected adapters stack as panels, each with its own name, reading and history; they scroll.
- **[S-434]** A counter instance belongs to the adapter whose `luid_...` token it carries, found
  anywhere in the name (engine instances start with `pid_...`, which the original's "starts with"
  match missed). Load is the **busiest engine**: per-engine sums across processes, then the
  maximum, which is Task Manager's figure. The original summed the 3D engine only and read near zero
  during video decode or compute.
- **[S-435]** On NVIDIA cards the driver's management library (`nvml.dll`, loaded with
  `NativeLibrary`, called through function pointers) is asked too: its load wins over the counters
  (a borderless full-screen game collapses the 3D counter), and it alone supplies temperature,
  power, fan, clocks and the video engines. Matched to the DXGI adapter by name (the n-th of
  identical cards), never guessed among several. Loaded once per sampler session; any failure hides
  those rows.
- **[S-436]** Samples every `IntervalSeconds` (1 to 60, default 2). Changing the selection rebuilds
  the panels, so a new adapter's history starts fresh.
- **[S-437]** The dedicated row shows only when used is at most the total (or the total is unknown):
  an integrated adapter's 128 MB carve-out "using" 3 GB is dropped rather than drawn. The shared row
  needs both a figure and a budget. Bars: high from 80% full, critical from 90%.
- **[S-438]** `ShowGraph`, `ShowDedicated` and `ShowShared` toggle the band and the memory rows.
  Rows that do not fit are dropped from the bottom before the number shrinks below Subtitle.
- **[S-439]** Driver rows (`ShowTemperature`, `ShowPower`, `ShowFan`, `ShowClocks`,
  `ShowVideoEngines`) are off by default and hide themselves on a card whose driver does not answer.

Not ported: the `graphSpanSec` setting (the band is the 60-sample window every meter here uses); the
one-decimal percentage (whole percent, like the CPU tile); the widget's own `label` (the card's
label is the instance's).

### Mixer

- **[S-440]** Per-app volume for the default output device (render, console role), through Core
  Audio's session API. **No polling**: one MTA worker thread owns every COM call, registers for the
  default device changing (`IMMNotificationClient`), sessions appearing
  (`IAudioSessionNotification`), each session's volume, state and disconnect
  (`IAudioSessionEvents`) and the master volume (`IAudioEndpointVolumeCallback`), and sleeps until
  one of them or a write wakes it; a wake reads once and publishes one snapshot. The four callback
  objects are immortal native vtables of `[UnmanagedCallersOnly]` methods that only set a flag and
  wake the worker; no exception escapes them. Started by the first mixer, stopped (everything
  unregistered and released) by the last.
- **[S-441]** A row's name: the executable's description (cleaned per [S-458]), then its file name,
  then the session's display name unless it is an unexpanded `@...` resource. Names are read from
  disk once per path; icons are extracted once per path off the UI thread (`AppIcons`), with a glyph
  fallback (speaker for master, bell for system sounds, a window for an app).
- **[S-442]** `GroupByApp` (default on) makes every session of one executable one row: the loudest
  level, muted only when all are, active when any is, and a session-count pill. Off: one row per
  session, keyed by its instance identifier.
- **[S-443]** Rows are ordered by name, case-insensitively, system sounds last; never by activity, so
  a row never slides out from under a dragged slider.
- **[S-444]** `ShowMaster` (default on) pins the device's master level first, under the `__master__`
  key, which no session key can equal.
- **[S-445]** `ShowSystemSounds` (default off) adds the system-sounds row; `OnlyActive` (default off)
  hides apps that hold a session but are silent, never system sounds or the master row.
- **[S-446]** Writes go through a newest-value-per-key queue drained by the worker when it is free:
  single-flight, no throttle interval. A dragged level is held on screen until a snapshot reports it
  or its row is gone. A write whose key matches nothing is a no-op; a session failing
  mid-enumeration is skipped; expired sessions are dropped.
- **[S-447]** A muted row reads 0% but keeps its level; dragging a muted row above zero unmutes it.
  The mute button toggles a row (all of a group's sessions); right-click offers Mute/Unmute and
  Reset to 100%. Rows are reused by position so a snapshot never rebuilds the slider under the
  pointer.
- **[S-448]** No device: "No output device". No rows: "No app is playing audio". Failures are logged
  once, and again only when they change.
- **[S-449]** From 360 px a row is one line (name, slider, percent, mute); narrower, the slider goes
  under the name; under 170 px a row is icon, percent and mute.

Not ported: the poll `interval` setting (there is no poll).

### Screen time

- **[S-450]** The widget draws the app's one tracker ([S-458]) through `AppServices.ScreenTime`; it
  has no timer of its own and redraws when the tracker says something changed.
- **[S-451]** `Period` (Day, Week = last 7 days, Month = last 30 days; rolling windows ending today)
  and `Style` are separate. A day draws Doughnut, Bars or None; a window Bars, Heatmap or None. An
  impossible pair (or an unknown value) becomes the period's first style. Changing the period in
  settings writes both fields.
- **[S-452]** The ring's wedges are the busiest apps up to the cap plus one "Other" wedge, offsets
  from cumulative seconds; the total sits in the hole. A day of Bars is one bar split the same way.
  Durations read `3h 24m`, `12m`, `<1m`. `MaxApps` (0 to 50, default 7) caps the legend; 0 lists none
  and the drawing keeps the default cap. Each app's colour is a stable hash of its key; a legend row
  carries the icon (or an initial chip in that colour), the name with the colour dot right after it,
  and the time as its last column. Hovering a wedge, segment or row lights it, dims the rest and
  names it in the readout.
- **[S-453]** `ShowAverage` (on): in a window, the per-day average (divided by the window length, so
  idle days count) beside the total and on each row. `ShowSwitches` (off): how often the foreground
  app changed. `ShowPercent` (off): each app's share of the shown total.
- **[S-454]** Day navigation in the header (back, label, forward, and a today button when away from
  today): today plus 89 days back. A pinned date stays that date across midnight, and returning to
  today unpins. A window shows "Last 7 days" or "Last 30 days" instead. Reading a day includes the
  interval still open, unless paused or older than a suspend leaves it; a range has one entry per
  calendar day, oldest first, gap days as zeros, clamped to retention. Executable paths are kept per
  app so past days have icons; at start, apps in the history with no path or name are filled from
  running processes.
- **[S-455]** Right-click "Hide from list" adds an app key to `HiddenApps`; hidden apps leave every
  view and the totals re-base. Settings list them with a way back.
- **[S-456]** A window of Bars: one bar per day, stacked by app in the legend's order, scaled to the
  busiest day, weekday labels for a week. Heatmap: seven columns, padded so the window ends on the
  last cell, shaded in quartiles of the busiest day (any use is at least level 1), today outlined.
- **[S-457]** Limits are daily, in seconds: `GoalSeconds` for the whole and `AppLimits` per app; zero
  or less is no limit. Shown as "of 4h" beside the figure (a window judges its daily average, "of
  4h/day"); over the limit, the figure turns critical. Edited in quarter hours.
- **[S-458]** Tracking: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` (out of context, skipping
  Themia's own process) names the app (the lower-case executable stem), and a 30 s sampling tick of
  the service's own timer accrues it (not the shared widget ticker, which may pause while widgets are
  hidden). Session lock (`WTSRegisterSessionNotification` on a message-only window) pauses until
  unlock; input idle of 5 minutes (`GetLastInputInfo`) pauses until input returns; Themia's own
  foreground closes the interval. An interval across midnight is split; a gap longer than two ticks
  (suspend, clock jump) is never credited. A switch counts only between different apps while
  accruing. Display names come from the version resource, off the hook's thread, cut at the first
  control character, with an entry-point tail (`WhatsApp.Root`) stripped and the product name
  preferred when the description is identifier-shaped. A loaded history is merged into the live one.
- **[S-459]** The history lives in `usage_stats.json` beside the state file, never in a widget's
  config, so it is never copied, pasted or exported. `{schemaVersion, days, names, paths?,
  switches?}`, the optional maps omitted when empty (bytes pinned by a characterization test). Saved
  atomically (unique temp file and rename), one writer, in order, at most once a minute while dirty,
  on lock, and synchronously at exit; only the newest 90 dates are kept. A corrupt or
  version-mismatched file is moved to `.corrupt` and an empty history started; junk keys are dropped
  on load. The tracker is started by the app host only: the debug gallery installs an in-memory
  fixture (`ScreenTime` is an empty day, `ScreenTime+fixture` a populated month) and never hooks the
  foreground or writes a file.

Not ported: the original's config migrations (`migrateMetrics`, `applyLegacyMetrics`, the pre-period
`displayStyle` values; [S-1050]); the retired `list` style; invisible
filler rows under a short legend, since the drawing here is sized from the tile rather than pushed
by the list.

## Layout pass over the ported widgets

Found by drawing every ported widget at every gallery size (and, with the debug fixtures in
`Shell/Gallery/LayoutFixtures.cs`, with something in it). Sizing rules live in Core with tests in
`WidgetFitTests` (`S64x_`/`S65x_`).

- **[S-640]** A card header's title ends in an ellipsis. A Grid does not clip a child to its cell,
  so an untrimmed title was cut mid-word ("Countdow") or drawn under the trailing element.
- **[S-641]** A hero readout (clock, pomodoro, meters, power, volume) is a ramp step while the box is
  small and, once it holds TitleLarge, that step scaled to fill it, keeping its line-height ratio.
  It used to wait for Display to fit, so every box between the two drew a 40px readout.
- **[S-642]** An empty state sheds to fit: the glyph goes first, then the sentence is held to the
  lines the box has and ends in an ellipsis. Every empty state is `Fluent.EmptyState`; the stack, tabs
  and countdown views no longer draw a text block of their own. The panel never touches the glyph's
  `Visibility`, which several views own.
- **[S-643]** CPU load and network throughput are deltas against a process-wide baseline, so a read
  within 250ms of the last real one is answered from that one. Two CPU tiles used to show 31% and
  0% side by side: the first moved the baseline and the second measured a millisecond.
- **[S-644]** A short pomodoro tile keeps the countdown: a wide one moves the controls beside it, a
  narrow one drops the progress bar. Stacked, a 120px tile had no room left for the digits.
- **[S-645]** The clock insets its readout fully at the sides and by half above and below.
- **[S-646]** A reading's caption (meters, power, weather's condition) moves under the reading when
  it would be trimmed beside it, or when its own line lets the reading be drawn larger.
- **[S-647]** The power headline's Subtitle floor applies only where Subtitle fits across the tile;
  below that the widest step that fits, semibold, down to Body.
- **[S-648]** A process row shows both numbers only from 232px of content; below that only the
  ranked one, and on a narrow tile the columns stop holding open their widest value. The "by CPU"
  label leaves a tile too narrow for it and the title both.
- **[S-649]** The sun strip's icons and countdowns appear only where a strip column can hold them
  (250px and 280px tiles), not where the tile can.
- **[S-650]** Now playing stacks on a tall, narrow tile (art, lines, transport) and moves the
  transport under the lines when beside them it would leave the title under 120px.
- **[S-651]** The scratchpad's header tools shed eraser, then clear; undo stays. A hidden eraser is
  switched off.
- **[S-652]** Glyph code points are checked against the font's cmap. The weather set, Quote and
  Wrap were not in Segoe Fluent Icons (or MDL2) and drew as boxes; the forecast folded rain, snow
  and thunder into its own thermometer icon because of it. Weather now and the forecast share one
  condition→glyph map.
- **[S-653]** The quote may grow to TitleLarge, is held to the lines that fit with an ellipsis, and
  gives up its attribution line on a tile too small for the quote itself.
- **[S-654]** The colour swatch rebuilds when whether values fit changes, not only when the column
  count or density does; the first build before layout had hidden values for good on some sizes.
- **[S-655]** An informational header label ("Just now", "by CPU") gives the title at least 64px and
  is not drawn when less than 40px would be left for it. Through `MaxWidth`, never `Visibility`.
  Commands in the header are not touched; their views shed them.
- **[S-656]** The current-weather reading is sized against the tile less its detail line, not
  against its own height (which pinned the first step chosen); the condition icon goes first on a
  tile too narrow for it.
- **[S-657]** The stock price is fitted to its own text (at least seven characters), not the widest
  price any symbol could have, and the change, currency and session badge go under it when they do
  not fit beside it. The badge is a grid so its label ends in an ellipsis.
- **[S-658]** A forecast row narrower than 160px is the day and its high; the icon and the low go.

Known and left: sparklines grow in from the right for their first minute by design ([Sparkline]);
the scrolling lists (feed, folder, apps, colour swatch on a tall tile) show a partial last row at
the bottom edge as any scrolling list does; a shortcut to a folder or an app draws the file glyph.

## Mail

The Mail widget reads Themia's own accounts ([S-529]) — Microsoft (Graph), Google (Gmail) and IMAP —
instead of the hearth hub; [S-90]/[S-91] no longer describe it. Ported from the classic app's
`widgets/office/email` (its SPEC tags in brackets) and the mail half of its `lib.rs`.

- **[S-540]** `EmailConfig` keeps the classic wire names (`folder: inbox|sentitems|drafts|deleteditems|junkemail`,
  `viewMode: list|compact|row`, `sortOrder: desc|asc`, `listFields`, `rowColumns`, `accountIds`).
  Every closed set is read leniently: an unknown or mistyped value becomes the default and is
  logged, never a throw that would refuse the state file. `WidgetConfig.Repair()` runs at load from
  `AppStateStore.Validate`; for mail it clamps the count (1–50) and preview lines (1–5), keeps at
  least one row column. No hearth-era field is read ([S-1050]).
- **[S-541]** `accountIds` empty means every mail account, including ones added later (the classic
  widget showed "select an account" instead); a non-empty list reads those accounts that are still
  signed in. Settings lists the signed-in mail accounts as switches under "Every mail account" and
  links to the Accounts page; no account is ever signed in from the widget's page. Folders: Inbox,
  Sent, Drafts, Deleted, Junk [S-12].
- **[S-542]** List (sender + time, subject, preview), Compact (sender + time, subject) and Row (one
  line of user-ordered columns under a header row) views [S-14, S-37]; newest or oldest first over
  the same newest N [S-17]. The card title is the folder; the header carries the unread badge
  [S-02], a warning glyph naming an account that failed or needs signing in (tap → Accounts), and a
  busy ring only for a refresh the user asked for. The card menu offers Refresh, the three views, the
  five folders and the order [S-20].
- **[S-543]** List fields Subject / Preview / Account, default all on; Preview only in the list view;
  the account tag shows whenever chosen [S-43]. Row columns From, Subject, Date, Preview, Account,
  reorderable, at least one on [S-16].
- **[S-544]** Message right-click: Open, Reply, Mark read/unread, Archive, Move to Junk, Copy subject,
  Delete [S-21]. Gmail is read through `gmail.readonly`, so its messages offer only Open, Reply and
  Copy [S-22]. Delete moves to Deleted (Graph move, IMAP UID MOVE); only a message already in Deleted
  is removed for good (Graph DELETE; IMAP `\Deleted` + `UID EXPUNGE`, UIDPLUS-scoped). A change shows
  at once and the next probe fetches the truth; a failure is the header warning's tooltip.
- **[S-545]** Graph `mailFolders/{f}/messages` (`$top`, `$select`, newest first); Gmail `messages`
  list then `format=metadata` per message, four at a time, the snippet's entities decoded, web link
  `?authuser=<address>#all/<thread>`; IMAP per [S-553]. One message shape for all three, carrying
  the account, provider, folder and the provider's own id.
- **[S-546]** One `MailService` per app (`AppServices.Mail`). Widgets open shared, ref-counted feeds
  keyed by accounts + folder + count. Each account's folder counts are probed every 2 minutes (Graph
  folder, Gmail label, IMAP `STATUS`) and the list is fetched only when they move or every 15
  minutes [S-25, S-26]; Refresh fetches now. Failures back off on `Backoff` (429 far longer); a state
  change is logged once. The cadence rides the shared ticker, so it stops while the desktop is
  hidden and ends with the last Mail widget.
- **[S-547]** Open: a Microsoft message deep-links to `ms-outlook://emails/<id>` when Outlook's
  protocol is registered, else its Graph web link; Gmail opens its thread; IMAP has no web view and
  opening is a logged no-op with the reason on the header [S-30]. Reply: Outlook's reply link, else
  `mailto:` with `Re:` [S-31]. Only those link shapes reach the shell, through
  `ShellLauncher.OpenMailLink` and `MailLinks.IsLaunchable`.
- **[S-548]** The merged list is the newest N across accounts (undated last); times read now / 5m /
  3h / 2d / Sep 20 in list and compact, a time today / weekday / date in the row view [S-09], and
  re-label every minute without a fetch.
- **[S-549]** Screens, each with its own sentence: no mail account (link to Accounts), chosen
  account gone (link), needs sign-in naming the account (link), loading, offline / unreachable /
  timed out / rate limited, empty folder ("No messages in Junk."), and a list with a warning when
  one account failed [S-04, S-05, S-06].
- **[S-550]** IMAP headers: folded fields unfold, the blank line ends the block, RFC 2047 words
  decode with their own charset (the word ends at the `?=` after the encoding, so a Q word starting
  `=C3` is one word), From splits name and address (including `addr (Name)`).
- **[S-551]** Dates apply their zone to a true instant [#45(d)]; the US zone names RFC 5322 defines are
  honoured (the classic app kept EST as wall time) and any other name reads as UTC; two-digit years
  are 19xx/20xx; an unparseable date is null, never a guess.
- **[S-552]** The IMAP preview walks MIME [S-52]: first text/plain, else text/html, attachments and
  non-text skipped, quoted-printable / base64 / charset decoded before the whitespace collapse, 200
  characters, bounded (6 levels, 32 parts, 16 KB a part). The body stays bytes (Latin-1 chars) until
  a part's charset decodes it, so 8-bit ISO-8859-1 bodies survive. HTML block tags leave a space.
- **[S-553]** IMAP over implicit TLS, one session per job: LOGIN (literal when a value needs one),
  LIST once for folder resolution, `EXAMINE` (read-only) and
  `FETCH n:m (UID FLAGS RFC822.HEADER BODY.PEEK[TEXT]<0.32768>)` — parenthesised [S-38/#45], never
  setting `\Seen`, and capped at 32 KB of text per message. Responses are framed by their literal
  lengths and parsed in Core. A refused LOGIN marks the account "sign in again" unless the server
  says `[UNAVAILABLE]` / `[INUSE]` / `[LIMIT]`.
- **[S-554]** Folder resolution: INBOX; else the SPECIAL-USE mailbox (RFC 6154, not `\Noselect`);
  else the first candidate name present (Gmail's `[Gmail]/…`, Outlook's `Sent Items`, `INBOX.Sent`);
  else the first candidate.
- **[S-555]** IMAP handles are `folder|uid|uidvalidity` (`folder|uid` when the server reported no
  validity); a bare `uid` or anything else is refused.
  Every change SELECTs the listed folder and refuses on a known UIDVALIDITY mismatch.
- **[S-556]** Fitting the tile: the account tag needs 260px, the preview 120px of height (one line
  under 240px), the time 110px, the header's warning glyph 160px; the row view keeps the most
  essential columns whose minimums fit (From, Subject, Date, Account, Preview — so a narrow tile keeps
  From and Date), in the user's order, never fewer than one; the column header needs 140px.
- **[S-557]** Debug gallery: the plain page is the no-account state; `Email+fixture` is a made-up
  two-account inbox (reserved example domains). `THEMIA_MAIL_FIXTURE=signin|offline|loading|empty|partial`
  picks another state for the fixture page.

Not ported, deliberately: keyboard navigation [S-33–S-36, S-42] (the surface is `WS_EX_NOACTIVATE`
and the card owns input; double-click opens), the header sort button [S-23] (the card owns the
header; the order is in the menu and settings), the per-widget header hide and label [S-03, S-19]
(the card's own header placement and label do this for every widget), the scroll fade [S-10],
envelope icons [S-07] (unread is weight plus an accent bar), the "unread only" filter, and the composite `account:id` message ids (the account travels separately).

## Tasks and OneDrive

Ported from `widgets/office/todo` and `widgets/office/onedrive` on `main` and their Graph / Google
calls in `widgets/office/email/src/lib.rs`, off the hearth hub onto Themia's own accounts. Owning
files: `Themia.Core/Tasks/*` (model, due dates, list rules, wire, optimistic edits, Today fan-out,
local store), `Themia.Core/Widgets/DriveRules.cs`, `Themia.Core/Model/Configs/{TasksConfig,OneDriveConfig}.cs`,
`Themia.App/Services/Tasks/*`, `Themia.App/Services/Files/DriveService.cs`,
`Widgets/{TasksView,TasksView.Rows,OneDriveView}.cs`, `Shell/Config/WidgetSettings.{Tasks,OneDrive}.cs`,
`Shell/Gallery/TasksFixture.cs`. Tests: `TaskRulesTests`, `LocalTaskListsTests`, `TaskSyncTests`,
`DriveRulesTests`.

- **[S-590]** A Tasks widget reads Microsoft To Do (Graph) or Google Tasks through an account
  with the Tasks feature, or a list kept on this PC (`TaskSource`, a closed set). An empty account
  id means the first such account and an empty list id its default list, so a widget dropped by a
  signed-in user shows tasks without a trip to settings; a *named* account that has gone is never
  swapped for another — the widget says so. Every non-list state names itself (no account, sign in
  again, account gone, no lists, loading, offline / provider error, no list on this PC) and the ones
  Settings → Accounts can mend link there; a list on this PC needs no account at all.
- **[S-591]** One `TaskService` per app (`AppServices.Tasks`). Widgets on the same account, list and
  completed-filter share one feed and one request. A refresh never stacks on one still out (a forced
  one is remembered and runs after); a tick is skipped when another widget refreshed the feed within
  half the interval; failures back off through `Backoff` (a 429 far longer). Graph and Google are
  read through `GetGraphPagesAsync` / `GetGooglePagesAsync` (400 tasks a list at most); ids are
  escaped into the path; completed tasks are filtered for Graph here and by Google's own
  `showCompleted`; Google's hidden and deleted tasks are dropped; every Graph status but `completed`
  is open work. Refresh is configurable, 30–600 s (default 60 s).
- **[S-592]** The list picker offers the account's default list, Today, then its lists. The lists are
  cached per account (single-flight, failures not cached, cleared when the accounts change).
- **[S-593]** A due date is a calendar day: midnight of the intended day (Graph zoneless, Google UTC
  `Z`, a bare date, or midnight at any offset) is read verbatim; anything else is an instant —
  zoneless is UTC — resolved to the viewer's local day (the To Do mobile encoding, classic #101).
- **[S-594]** Labels: Overdue / Today / Tomorrow / short weekday (2–6 days) / `14 Oct`, counted in day
  numbers so a DST change cannot shift them. Overdue is said in words and in the critical colour, and
  never for a finished task.
- **[S-595]** Completed tasks are hidden unless "Show completed" is on; then they are struck through.
  Completing one with completed hidden makes it leave the list at once (no timed removal).
- **[S-596]** Today (`__today__`, the classic id) is every task due today or overdue across the
  account's lists (at most 50, read one at a time), oldest first, stable within a day; undated tasks
  are not in it. A failing list is skipped and logged; when every list fails the first error is shown
  rather than "nothing due". A task added from Today is created due today in the default (else first)
  list.
- **[S-597]** Completing, reopening, renaming and deleting are optimistic (`TaskEdits`): the change
  shows at once and a refusal takes it back (a deleted task returns to its place), with a warning in
  the header saying why. A confirmed change stays on top of any refresh that started before it was
  confirmed, so a racing poll cannot flash the old state; a change still in flight always stays.
  Writes to one account go out one at a time, in order; a confirmed write nudges that account's other
  feeds (a list and Today) to catch up. The round marker is the one control that completes a task.
- **[S-598]** Neither provider stores a hand-made order, so an account list's order is task ids in the
  widget's config (`ApplyOrder`: known ids first, unknown ones — new tasks — after; a gone id leaves no
  hole, a duplicate cannot double a row). A list on this PC is its own order, and a move leaves hidden
  completed tasks in their slots. Rows move with Move up / Move down in the row's menu.
- **[S-599]** A new task is added from the row at the foot of the list (Enter adds and keeps the box
  open for the next; leaving it adds and closes) and appears at the bottom: optimistically under a
  temporary id that is never written to, then under the provider's id, with the order on screen pinned
  into the config so the next refresh cannot re-sort it to the top.
- **[S-600]** Double-click a task (or Rename in its menu) to rename it in place. Enter or leaving the
  box commits, Escape cancels, a blank or unchanged title writes nothing. The request is a PATCH of
  the title alone, its body written with a JSON writer so a quote cannot break out.
- **[S-601]** A title wraps to "Title lines" (1–6, default 2; two at most on a compact row) and is
  then cut with an ellipsis; the tooltip carries the whole title, its state, due date and priority.
- **[S-602]** Layout: List puts the due date under the title, Compact beside it (under it anyway in a
  column under 240px wide). Rows are Fluent's fixed heights at minimum and grow only for a wrapped
  title; an important task's title is semibold. The size ladder is [S-780].
- **[S-603]** Lists on this PC are one pool in `tasks.json` beside the state (`schemaVersion` 1),
  shared by every widget: renaming a list retitles each widget showing it, and deleting a widget keeps
  its tasks. Load validates every entry (a list needs a non-empty id and a name, a task an id and a
  title; a repeated id keeps the first; a non-boolean `done` is false). Absent is a quiet empty pool;
  unparseable or newer is moved aside and reported; unreadable makes the pool read-only for the
  session. Saves are atomic, single-writer and coalesced, and flushed at exit.
- **[S-604]** The widget's settings: the source box (first account, each account, This PC); for an
  account the list box; for This PC the pool itself — pick a list, rename it as you type (a blank name
  is kept so the box can be cleared), add one (`New list`, `New list 2`…), delete one after a
  confirmation that names its task count. Switching to This PC lands on the list it had, else the
  first, else a new one — only the last creates anything. A deleted list leaves its widgets on "Pick
  or start a list", never on another list's tasks.
- **[S-605]** Retired (the app is unreleased: no hearth-era config is read). Nulls a hand edit leaves in
  a Tasks config's strings, an unknown source or layout, and a broken row order are repaired on load.
- **[S-606]** OneDrive (`WidgetKind.OneDrive = 47`, Launch) lists a Microsoft account's recent files or
  a folder: a kind icon (folder, picture, video, audio, spreadsheet, presentation, document, file —
  from the MIME type, then the extension), the name, and under it `size · 3h ago` for a file or the
  age alone for a folder (Just now / m / h / d ago, then the date). A double-click opens a file's
  OneDrive page through `ShellLauncher`'s allow list; nothing is downloaded.
- **[S-607]** It starts in Recent files or My files (`DriveStart`) and lists 5–50 items (default 15),
  newest first in a folder. One `DriveService` per app serves a listing younger than the caller's
  allowance from memory and joins a request already out for the same address.
- **[S-608]** It refreshes every five minutes on the shared ticker; a changed item count is fetched
  once the slider settles; an account change clears the cache.
- **[S-609]** A single click walks into a folder (only one on this drive — a shared folder from
  another drive opens on the web like a file); a back row above the list returns one level, and past
  the first folder to the configured start; a reply for a place the widget has since left is dropped.
  Changing the account or the start throws the trail away.

Not ported, deliberately: the pop-out window [S-39] (out of scope by user decision), the per-widget
header hide and label [S-02, S-20] and OneDrive's [S-15] (the card's own header does this for every
widget), the 600 ms delay before a completed task leaves the list [S-26] (no wall-clock timing in the
list; it leaves at once), the one-shot `localTasks` config-bag migration [S-59] (the app is
unreleased: no compatibility code), and the single click on a title that renames with the caret at
the click point [S-50] (a click focuses the row for the keyboard; double-click or F2 renames).
Drag-to-reorder, keyboard navigation, Tab between editors, the scroll fade and the "!" badge are
ported in [S-781]..[S-788].

## Calendar and contacts

The classic Calendar and Contacts widgets, on Themia's own accounts ([S-529]) instead of the hearth
hub. Pure rules in `Themia.Core/Office` (tested), one shared source in `Services/Office`, the views in
`CalendarView*.cs` / `ContactsView.cs`, settings in `WidgetSettings.Calendar.cs` / `.Office.cs`.

- **[S-560]** Calendar shows events from Microsoft Graph (`calendarView`, `Prefer:
  outlook.timezone="UTC"`), Google Calendar (`events`, `singleEvents=true`) and CalDAV (`REPORT
  calendar-query`) accounts. An all-day event is held as two dates (first day, exclusive end) and is
  never converted between zones; a timed one as two instants. Clicking an event opens its provider
  page (CalDAV has none: tooltip only).
- **[S-561]** Config: `accountIds`, `calendars`, `calendarColors`, `view` (month | week | day | agenda,
  default month), `weekStart`, `showBirthdays` (on), `agendaCount` (1–100, 20). The context menu
  offers Refresh and the four views (current one checked); switching saves the config.
- **[S-562]** An empty `accountIds` means every calendar account (later ones too — the Mail
  pattern); settings offer "Every calendar account" or a choice where the last cannot be turned off.
  Chosen accounts that were signed out are skipped; when none are left the widget says so with a
  link to Settings › Accounts.
- **[S-563]** Every value is read leniently: a wrong type or unknown name becomes the default
  instead of refusing the state. *(The hearth-era config fold-in that shared this tag is retired,
  [S-1050].)*
- **[S-564]** Week start Monday or Sunday drives the month grid, the week view and the weekday header.
- **[S-565]** Provider datetimes: a zone (`Z`, `±HH:MM`, `±HHMM`, negative offsets included) is kept;
  a zoneless one is UTC. Cancelled events (Graph `isCancelled`, Google `status: cancelled`, iCal
  `STATUS:CANCELLED`) are not listed.
- **[S-566]** Colours: a fixed six-colour palette by fetch-target index; a valid `#rrggbb` override
  per calendar (keyed by calendar id, or account id for a default calendar) wins; provider colours
  are normalised (`#rrggbbaa` → `#rrggbb`, `""`/`auto` → none). Chips carry colour when more than one
  calendar is shown or an override is set. Birthdays use the caution hue.
- **[S-567]** Several calendars per account: settings list each account's calendars (Graph
  `/me/calendars`, Google `calendarList`, CalDAV home-set listing) with a colour (Automatic or one of
  the six) and, when there is more than one, a switch. No selection means the default calendar;
  deselecting the last one goes back to it. A Google sign-in whose scope cannot list calendars
  (403) is offered its primary calendar.
- **[S-568]** Ranges: month = whole weeks around the month (42 cells); week; day; agenda = today and
  the next 30 days. Requested from local midnight to local midnight (the classic app used UTC
  midnight). The header title is the period ("October 2026", "28 Sep – 4 Oct", "Sun 4 Oct").
- **[S-569]** Back / forward step a month (day clamped), week or day; a today button shows when
  today is off screen. The widget follows today across midnight until the user pages away.
- **[S-570]** A day lists birthdays, then all-day events, then timed ones by start; an all-day
  event covers [first, end); a timed one shows on every local day it overlaps.
- **[S-571]** Agenda: grouped by day ("Today", "Tomorrow", "Mon 5 Oct"), at most `agendaCount`
  entries; an event still running from before is filed under today; timed events already over are
  left out. Rows: time column (start over end, "All day", "Now"), title, location · organizer; on a
  tile under 260 px the time folds under the title.
- **[S-572]** Month cells: chips (title, coloured edge) as many as fit, the overflow as "+N" beside
  the day number; on smaller cells up to three dots (two in narrow cells) plus a muted one; on the
  smallest the number alone, strong when the day has something. Today is an accent disc.
- **[S-573]** Day: an all-day bar and a 07:00–22:00 timeline, one list row per hour, overlapping
  events in side-by-side lanes; scrolled to the first event (or now).
- **[S-574]** A month narrower than 154 px or shorter than 112 px, a week narrower than 280 px or
  shorter than 96 px, and a day narrower than 140 px or shorter than 112 px fall back to the agenda.
- **[S-575]** Birthdays (when `showBirthdays`) come from Microsoft (`/me/contacts` `birthday`) and
  Google (People `birthdays`) accounts, held for a day. 29 February falls on the 28th in other
  years; the age is the one turned on that day, and none for a year before 1800 (Outlook's 1604).
  The same person in two accounts is one birthday. A refused address book is a header warning, not
  an error over the events.
- **[S-576]** iCalendar is unfolded and read by content line (quoted parameters, escapes, vCard
  groups); DURATION is honoured; nothing throws on bad input; organizer is its CN, else the address.
- **[S-577]** CalDAV asks the server to `expand` recurrences (instances come in UTC). A master the
  server did not expand is expanded here: DAILY/WEEKLY/MONTHLY/YEARLY with INTERVAL, COUNT, UNTIL,
  BYDAY (incl. ±n), BYMONTHDAY, BYMONTH, WKST, plus EXDATE, RDATE and RECURRENCE-ID overrides
  (moved or cancelled). Any other rule part shows the event once, at its start; at most 1000 instances.
- **[S-578]** A TZID resolves through the object's own VTIMEZONE (offsets and yearly rules), then a
  system zone of that id (Windows ids), then the local clock ([S-187]). A calendar URL from the config
  or a home set or principal from the server is only ever asked with the account's password when it
  is on the account's own origin (scheme, host, port).
- **[S-579]** vCards: FN (else N), the preferred EMAIL and TEL (`PREF=1` / `TYPE=pref`, else the
  first; `mailto:`/`tel:` values stripped), ORG's first part, TITLE, UID, BDAY (incl. `--MMDD`).
- **[S-580]** Contacts lists people from Microsoft (`/me/contacts`), Google (People connections) and
  CardDAV (`addressbook-query`) accounts in one list by name; the same person in two accounts once.
  Config: `accountIds` (empty = every contacts account), `layout`, `columns`, `count`, `defaultSearch`.
  Clicking selects (an accent pill), clicking the background clears.
- **[S-581]** Layouts: list (avatar, name, role, address/number), compact (small avatar, name, one
  field), details (table), grid (avatar cards). Details narrower than 88 px a column fall back to the
  list, a grid under two cards to compact. Under 160 px wide the avatars and the count are dropped.
- **[S-582]** Details columns stay in canonical order (Name, Email, Phone, Company, Title); the last
  one cannot be turned off.
- **[S-583]** `count` is at least 5 (steps of 5, no upper bound); each account is asked for
  clamp(count, 500, 1000) so a search spans the address book; search filters before the cap.
- **[S-584]** A search box (from 150 px tall) asks for the keyboard while focused (Esc clears) and
  starts with `defaultSearch`.
- **[S-585]** Search matches name, email, company or job title, case-insensitively.
- **[S-586]** Avatars: one or two initials on `hsl(hash(name), 38%, 34%)`, white text.
- **[S-587]** Right-click a person: their name and role, Send email / Copy email, Call / Message /
  Copy phone, Copy name, Refresh. `mailto:`, `tel:` and `sms:` are built only from a plain address or
  number (`ContactLinks`) and opened through `ShellLauncher.OpenContactLink`.
- **[S-588]** One shared source answers every widget: an answer is reused while fresh (events 4 min,
  contacts 10 min, calendar lists 30 min, birthdays 24 h), an identical request in flight is
  joined, a failure keeps the last good data and is held (an hour for sign-in / access refusals, 15
  minutes for a rate limit) unless the user refreshes. Calendar polls every 5 minutes, contacts every
  15. States: loading; no account; account gone; sign-in or access needed (with the Accounts link);
  offline and service errors; nothing on. A failure of some accounts with data from others is a
  header warning that opens Accounts when the user can fix it.
- **[S-589]** The debug gallery's `Calendar` and `Contacts` pages show the no-account state;
  `+fixture` installs a fictional work and personal account (a week around today, overlapping
  meetings, a two-day event, three birthdays, two dozen people). `THEMIA_GALLERY_LAYOUT` picks the
  fixture's view or layout. Debug builds only.

*(S-574's fallbacks are now part of the ladder in [S-1260]; S-581's and S-584's size rules are
refined by [S-1270] and [S-1275]; S-568's 42 cells are still fetched, [S-1264] only stops drawing a
week wholly in the next month on a large tile.)*

Not ported: the per-widget header hide (the card owns the header), the reauth button that signed
accounts out (the Accounts page is where sign-in is mended), the birthday-permission toast (there
is no toast pipeline yet; the header warning stays and opens Accounts), and birthdays from CardDAV
(the classic app had none either). Keyboard navigation is structured but not yet reachable: see
[S-1269] / [S-1277].

## Developer widgets: GitHub and Claude usage

Five widgets in the picker's **Developer** section, on Themia's own accounts (Accounts page): the
repository widget (`GitHub`), assigned issues (`GitHubIssues`), notifications
(`GitHubNotifications`), review requests (`GitHubReviews`) and Claude usage (`ClaudeUsage`). Ported
from the classic `widgets/online/github*` (tags `[S-NN]`, `[A-NN]`, `[N-NN]`, `[R-NN]`) and
`widgets/online/claude-usage` plus the Rust `fetch_claude_usage`. Rules live in
`Themia.Core/Developer` with tests in `GitHubRulesTests`, `GitHubPollingTests`, `DevScreensTests`,
`ClaudeUsageTests` and `DeveloperConfigTests` (`S6xx_`).

- **[S-610]** The repository widget shows the repository's name in the card header (the user's own
  label still wins), the newest workflow run's verdict beside it (a coloured dot; the word —
  passing, failing, running, queued — from 300px), stars, forks and open issues (k/M), the latest
  release tag, language · last push · default branch, then Commits, Pull requests, Issues and
  Workflow runs as one-line rows (short hash; `#n` coloured open/merged/closed/draft; `#n`; a
  run's dot, name and branch). Under 180 wide the numbers stack; how the rest shares the tile is
  [S-741]..[S-745]. (classic S-04..S-11)
- **[S-611]** A repository is `owner/name`, a web address of one (github.com, Enterprise, `.git`,
  deeper paths) or `github.com/owner/name` without a scheme; anything else is refused in settings
  with the reason. The host of a pasted address is ignored: requests go to the account's own API
  root, so a token is only ever sent where the user signed in. (classic S-12, S-39)
- **[S-612]** Addresses as the classic app: `/repos/o/r`, `actions/runs?per_page=max(n,1)`,
  `commits?per_page=n`, `pulls?state=all&sort=created&direction=desc&per_page=min(3n+5,100)`,
  `issues?state=open&per_page=n+5` with pull requests dropped, `releases/latest` (a 404 there is
  "no release"). Counts 0–10 (commits 3, pulls 3, issues 3, runs 5); 0 hides a list. Only the
  repository itself is required: a failed side request is logged and drawn as absent, except a
  rate limit or a lost sign-in, which stop the lot. A 404 on the repository reads "not found, or
  this account cannot see it". (classic S-14..S-17, S-24..S-29)
- **[S-613]** Pull requests: filter Open / Closed (merged included) / All, order Newest / Oldest by
  creation, applied to the over-fetched list (classic issue #42, S-40, S-41).
- **[S-614]** No repository set: "Choose a repository: right-click, then Configure."; an invalid one
  names itself. Account states come first. (classic S-01)
- **[S-615]** Assigned issues: `search/issues?q=is:open is:issue archived:false assignee:@me`
  (`(assignee:@me OR mentions:@me)` with mentions on), `sort=updated&order=desc&per_page=count`,
  per account; repository from `repository_url`; count 1–25, default 8. (A-15, A-17, A-18)
- **[S-616]** Review requests: `is:open is:pr review-requested:@me archived:false`; drafts dropped
  unless asked for (twice the count fetched then, so drafts do not starve the list), drawn muted
  with "draft" on the number. Rows add the author. (R-06, R-07, R-16, R-20)
- **[S-617]** Notifications: `notifications?all={showRead}&participating={participatingOnly}&per_page=count`
  per account, count 1–20 (default 5); each item keeps its account. Subject types read as words
  ("Pull request", "CI").
- **[S-618]** A row leads with its reason, a closed set: security critical; review, mention, team,
  assigned and approval caution; author accent; the rest neutral. Read threads are drawn in the
  secondary colour with a tertiary reason; the unread count is in the header. (N-06..N-08)
- **[S-619]** A thread opens its issue or pull request from `subject.url` (`api.github.com` →
  `github.com`, Enterprise `/api/v3` dropped, `pulls` → `pull`); any other shape, including a
  comment URL, opens the repository instead of a page that 404s. (N-22)
- **[S-620]** New: notifications are grouped under their repository (by web address, so the same
  name on two hosts is two groups), groups ordered by their newest thread, each heading with its
  unread count — when the list spans more than one repository and the body is at least 160px tall.
- **[S-621]** Right-click a thread: Open in browser, Mark as read (`PATCH notifications/threads/{id}`,
  disabled when read), Mark all in the repository as read (`PUT repos/{o}/{r}/notifications`),
  through the thread's own account. The change shows at once; a failure puts it back and says why
  in the header warning; success drops that account's cached notification validators and refetches.
  (N-17, N-27)
- **[S-622]** Several accounts merge newest first, cut to the count, the same issue seen by two
  accounts listed once. One account failing still shows the others (with a header warning); every
  account failing is a failure, never "nothing here". (A-17, R-03, R-18, N-21)
- **[S-623]** Accounts: list widgets read the chosen GitHub accounts, or every one when none is
  chosen ("Every GitHub account" in settings, so accounts added later join); the repository widget
  and Claude usage read one (the first when none is chosen). Only ids are stored. (A-07, R-08, N-09,
  S-13, S-37)
- **[S-624]** One `GitHubService` per app (`AppServices.GitHub`): widgets open shared feeds keyed by
  everything that changes the answer (never the token), so equal widgets cost one request; one
  cache of answers keyed by account and address serves every feed; the last widget to let go stops
  a feed. Polling rides the shared ticker. (S-35, A-21, R-23, N-25)
- **[S-625]** Polite polling: every repeat GET is conditional (`If-None-Match` with the last ETag,
  `If-Modified-Since` with the last `Last-Modified`), and a `304` — free against the budget — is
  answered from the cache. Notifications follow `X-Poll-Interval` when it asks for longer than the
  widget's cadence. A 403/429 with `X-RateLimit-Remaining: 0` or `Retry-After` is a rate limit, not
  a refusal (`RateLimitRules`); it, or a success that spent the last request, holds every request
  for that account until `Retry-After` / the reset (at most 2h), and the widget says when it will
  ask again. The account service gained `AccountReply.Headers` and `IsNotModified`, and
  `AccountException.ProviderMessage` / `RetryAt`, for this.
- **[S-626]** Refresh every 5, 15, 30 or 60 minutes (5 by default; Claude 15); a stored value outside
  5–60 is clamped on load. Each widget's right-click menu has Refresh. (S-20, S-23, A-10, A-14, N-13,
  N-18, R-11, R-15)
- **[S-627]** Double-click a row (or the release tag) to open it, through the launcher's allow list;
  the row's tooltip carries its whole title and repository. (S-33, A-19, N-23, R-21)
- **[S-628]** States, each its own sentence: no account ("Sign in to GitHub in Accounts.") and a
  signed-out chosen account offer an "Open Accounts settings" link (`AppServices.ShowAccounts`); an
  account the service found refused says who needs to sign in again; loading; offline; rate limited
  with the time; the provider's own reason for other refusals; and per-widget empty sentences.
- **[S-629]** Counts as GitHub writes them (999, 1.2k, 12k, 3.4M); ages now / 5m / 3h / 2d, then
  "4 Mar" (with the year when not this one).
- **[S-630]** Claude usage: a bar per window — "Session (5h)" and "Weekly (all models)" always,
  "Weekly (Opus)" / "Weekly (Sonnet)" when toggled (a null window is a 0% bar). Each bar has the
  rounded percentage and, at the loosest density, a "resets in …" line that keeps its slot. (S-04,
  S-05)
- **[S-631]** `utilization` is already a percentage: clamped to 0–100, never rescaled (classic U-01).
  Accent under 70%, caution 70–89%, critical from 90%. (S-06)
- **[S-632]** "resets in 3d 4h" / "3h 20m" / "45m"; nothing for a null, past or unreadable time; the
  prefix goes when the labels are shortened.
- **[S-633]** `GET api.anthropic.com/api/oauth/usage` through the Claude account (the account service
  renews the token, retries one 401, sends the CLI user agent and `anthropic-beta`). The body is
  parsed strictly for the four windows; unknown buckets are ignored; a non-object body is a loud
  error, never all zeros. (S-07, U-02)
- **[S-634]** A 401 the service could not mend is "sign in again" (the account is marked, never
  removed); a 403 is "lacks a permission: {Anthropic's reason}"; anything else names the status and
  Anthropic's words. (classic `map_usage_error`)
- **[S-635]** The widget never scrolls: rows draw at the loosest density that fits (label and % over
  bar over reset; without the reset line; one line; one line sharing the leftover height), and the
  labels shorten with the width ("Weekly (all models)" → "Weekly" → "7d"), estimated on the type
  ramp. One-line rows are centred on a wide, short tile. (S-17, S-18)
- **[S-636]** Settings: the Claude account (the first by default), the Opus and Sonnet toggles,
  refresh. (S-09..S-12)
- **[S-637]** Debug gallery fixtures (`DeveloperFixture`): the plain page of each widget is the
  no-account state, `Kind+fixture` is populated (two GitHub accounts, one Enterprise; a Claude plan),
  and `THEMIA_DEV_FIXTURE` = `signin`, `gone`, `loading`, `offline`, `ratelimited`, `empty`, `stale`
  or `norepo` draws that state. Never installed over the running app's real services.
- **[S-638]** Content stays through a failed refresh; the header shows a caution glyph whose tooltip
  says what failed (and opens Accounts when the fix is there). (S-36)
- **[S-639]** Lists show whole rows only: the scroll viewer is given the height of the rows that fit
  (never ending on a section heading); the rest is a scroll away.

Not ported: the legacy `config.token` migration (S-38; nothing is imported from the classic app, [S-523]),
the per-widget `label` and `hideHeader` (the card's own title and header placement cover both), demo
mode (S-32, S-15; the gallery fixtures replace it), and the single-click open (double-click, as every
list on this desktop). The cache across restarts and anonymous public access are ported in
[S-746]/[S-747] below.

## Responsive layout (shared foundation)

- **[S-660]** A widget's content area (the card less its header) is classified per axis into
  `SizeClass` Compact / Regular / Expanded / Huge (width breakpoints 180 / 400 / 720, height
  128 / 280 / 480 DIPs) plus a shape (Wide ≥ 2:1, Tall ≥ 1:1.6). The seven gallery sizes land in
  seven distinct (width, height) pairs. An unmeasured area is Compact. (`WidgetArea`)
- **[S-661]** Optional parts are disclosed in priority order from one height budget: a part is shown
  only when it fits whole; once a part misses, no later part is shown (growing a tile only ever adds
  parts); a part too narrow is skipped without closing the ladder. (`Disclosure`)
- **[S-662]** Lists and line budgets count whole rows only, one arithmetic for all of them
  (`Disclosure.WholeRows` / `TakeRows`, 0.5 DIP rounding tolerance).
- **[S-663]** The card tells a view that implements `IWidgetSizeAware` its slot, once per layout pass
  that changed it, never unmeasured; views that do not implement it are unaffected.
- **[S-664]** Meters: reading → caption → sparkline (or bar) → min / avg / max (a line at Regular
  height; three stat tiles at Expanded width, beside the reading on a strip, under it on a panel, one
  under another in a tall column; a non-moving meter's tiles carry its breakdown, e.g. a drive's
  used / free / total) → a scale line naming the chart's span at Expanded × Expanded. A wider chart
  spans more history (one sample per ~6 DIPs, 30..180 samples). With a chart, the reading stops at
  Display and the chart takes the rest; without one, the reading fills the tile.
- **[S-665]** Clock: time → date (short "Sun 4 Oct" when narrow; with the ISO week and day of the
  year appended on a wide strip) → "Week 40 · Day 277" at Expanded height → time zone ("UTC+02:00 ·
  name") at Expanded × Expanded. A tall column stacks hours over minutes when that draws them larger.
  Analog face unchanged.

## Shell skeleton and shared settings

Prep for the parallel shell and widget packages (S-700..S-719). Mostly file layout; the behaviour
rules are these.

- **[S-700]** Calculator, Clock, Colour swatch and Web search start with their header **hidden**, as
  in the original (extends [S-485]; `WidgetInstance.DefaultHeader`). Only creation reads the default;
  a loaded widget keeps the placement it was saved with.
- **[S-701]** The app pages of the settings window come from one table, `ConfigPages.AppPages`
  (page, title, glyph, main or footer slot, builder). The navigation and the body both read it, so a
  page is added with one line plus its own `ConfigPages.<Page>.cs`. `AppPage` reserves Animations,
  License and Logs; a reserved page with no table entry is not in the menu and, if asked for, logs a
  warning and shows an empty page. About stays the last footer entry ([S-493]).
- **[S-702]** Behaviour switches live in `AppState.Preferences` (`AppPreferences`), apart from the
  look (`DesignState`): language (`AppLanguage`, default follow Windows), hide in full-screen apps
  (off), desktop icons (on), local network access (off), developer mode (off), verbose logging (off).
  Nothing reads them yet; each is wired by its package. Import brings them over and lists changes.
- **[S-703]** A view may carry an icon (`ViewState.Icon`, one glyph, null = initials). Anything but
  a single glyph is repaired to null at load.
- **[S-704]** Design gains the header fill (`HeaderBackgroundColor`, empty = the widget's fill;
  `HeaderOpacity` 0..1), the view-change and unlock animations with their own durations (0..3000 ms),
  and the opt-in startup sound. `IntroAnimation` gains Sides, TopBottom, Flip, Blur, Hyperspace and
  Hacker. Out-of-range values are clamped, unknown animations reset to Scale, at load.
- **[S-705]** Per-display settings on `AppState`, keyed like `MonitorViews`: `DisabledMonitors`
  (blank and repeated ids dropped), `MonitorScales` (clamped to 0.5..2), and
  `DefaultViewForNewDisplays` (null = the active view; a deleted view is repaired to null).
- Not added: the updater's auto-check switch and an Updates page, since the updater is out of scope.

## Gallery

Pictures from a folder, one picture file, or Bing's photo of the day (S-760..S-779; the earlier
[S-300]..[S-303] still hold for folders). Ported from the original's `widgets/others/gallery` SPEC.

- **[S-760]** `Source` is a closed set: `Folder` (default), `File`, `Bing`. Folder uses `Path` +
  `Recursive`; File uses `FilePath`; Bing needs no setup. The settings page shows only the rows the
  chosen source (and, for the slide time and picture size, the layout) uses.
- **[S-761]** File: one picture full-bleed. An empty box is "No picture chosen", a name that is not
  a picture is refused in settings and reported by the widget, and a missing file says so. The
  picker offers only the extensions the widget can draw.
- **[S-762]** Bing: the `HPImageArchive` JSON (today, en-US) through the shared HTTP client and a
  source-generated contract. Only a path on `www.bing.com` is accepted (protocol-relative, absolute,
  backslashed and non-rooted paths are refused). Offline and "could not load" are separate states.
  The picture is decoded at the size shown and cached by the system's HTTP cache; a failed load is
  dropped from the decoder cache so the next refresh asks again.
- **[S-763]** Bing shows its title and credit under the picture while `ShowCaption` (default on);
  the credit is shed before the title, and both below Regular width. File and folder never caption
  a single picture.
- **[S-764]** Scatter: square prints, one per cell of the grid shape for the count (spare cells left
  empty at random), centre in the middle half of its cell, side 1.05–1.4× the cell's short side
  (capped at 90% of the card's), tilt ±12° as in the original, stacking order shuffled. The
  original's "within 28% of the centre" heaped every print in the middle of a big card. About one
  print per 12,000 DIP².
- **[S-765]** Mosaic: the card cut recursively, the first half of the pictures getting a 35–65%
  share, across the longer side (the original alternated; same on a square, no slivers on a strip).
  Cells tile the card exactly with no gaps; always cropped to fill. About one cell per 9,000 DIP².
- **[S-766]** Grid: about one 120-DIP tile per slot (max 24), shaped so tiles are closest to square
  with a small penalty per empty cell.
- **[S-767]** The ladder (`GalleryPlan`): any layout with room for only one picture is that picture
  full-bleed with nothing on it (XS). Slideshow: counter in the header and the hover bar from
  Regular width; "up next" thumbnails beside it on a Wide strip (squares, the picture keeping ≥1.5:1)
  or in a row under it from Expanded height (3–8 thumbnails); the file name under it at L, and
  "N of M · folder" at XL. Grid / scatter / mosaic take more pictures as the card grows. Scatter and
  mosaic are dealt from one seed per scan, so a resize moves pictures rather than reshuffling them.
- **[S-768]** A tap on a thumbnail jumps the slideshow there and restarts its hold.
- **[S-769]** The hover bar (previous / pause-resume / next) shows only while the pointer is over
  the widget, in the slideshow with more than one picture. Pause is not persisted.
- **[S-770]** The slideshow's own timer runs only while it is a folder slideshow with more than one
  picture and not paused.
- **[S-771]** Double-click a picture opens it; right-click a picture: Open, Show in Explorer, the
  layout switch (folder only, current checked), Pause / Resume, Refresh images.
- **[S-772]** `RefreshMinutes` is one of 5 / 15 / 30 / 60 (default 60, the original's); a corrupt
  value snaps to the nearest.
- **[S-773]** The card's menu adds Pause / Resume, Refresh images (re-reads now and deals a new
  scatter / mosaic) and "Show as …" for the other layouts (folder only).
- Not ported: Left / Right / Space keys ([S-29] of the original) — the surface takes no keyboard
  outside edit mode; the commands are ready for the shell's keyboard lease. A file or folder caption
  of date taken. The slide minimum stays 2 s (original 1 s).

## GitHub widgets: filling the tile, detail, saved copies

Round 3 of the four GitHub widgets (`GitHub`, `GitHubIssues`, `GitHubNotifications`,
`GitHubReviews`). Rules in `Themia.Core/Developer` (`DevLayout`, `DevSavedCopy`, `GitHubRules`,
`DevScreens`, `GitHubPolling`), tests in `DevLayoutTests`, `DevSavedCopyTests`, `GitHubDetailTests`,
`DeveloperConfigTests` (`S74x_`).

- **[S-740]** "Fill the tile" (`FillTile`, on by default) on every GitHub widget: the feed asks for
  the most a list may hold (search 25, notifications 20, each repository list 10; a repository list
  at 0 stays hidden) and the view shows the whole rows that fit, so a bigger tile shows more. Off,
  the counts are exact. Settings: a toggle over the count, which hides while filling.
- **[S-741]** The repository widget's lists share their height by quota (`DevLayout.Quota`): every
  list that has something gets its heading and one row first (priority commits, pull requests, runs,
  issues), then rows are dealt one at a time; a heading over the only list shown gives its room to a
  row. A wide strip (Wide shape, at least 400 wide) puts the numbers in a 170 column and the newest of
  each list beside them, without headings.
- **[S-742]** From 520 wide the repository's lists sit in two columns (commits and runs | pull
  requests and issues), and grouped notifications put their repositories in two columns, a group
  starting the second column when it would not fit whole under the first (`DevColumns`, each column
  a whole-rows host).
- **[S-743]** Rows grow sideways: a two-line row adds its detail at the right of its second line from
  400 wide and label chips from 480 (one-line rows: 640 / 720). Issues and reviews: "author · N
  comments" and labels; notifications ungrouped: the subject type; repository rows at a Huge height
  are two-line with the commit author, the pull request's author and state word, the issue's author,
  comments and labels, the run's branch and "trigger · actor". Search lists go two-line from 168 of
  body (other lists 200). The tooltip carries everything a row trims.
- **[S-744]** New fields from payloads already fetched: commit author (name, else login), pull request
  author, issue author / comment count / labels (named only, at most five, colour kept only as six hex
  digits; chips draw a dot in that colour on the theme's subtle fill, two at most, then "+N"), run
  event ("pull request") and actor, search items' comments and labels.
- **[S-745]** The repository's description (first line) shows from an Expanded height (one or two
  lines by estimate), or fills the room a tile too short for any list has left. From 400 wide the
  release tag sits in the header beside the verdict (double-click opens it); at Huge × Huge the
  summary adds "tag · release name · released 3d".
- **[S-746]** Anonymous public access (classic S-13): with no GitHub account signed in and none
  chosen, the repository widget reads as `github:anonymous` — no token, github.com, its own
  rate-limit gate and cache — never faster than every 15 minutes (60 requests an hour per address;
  a repository costs six, a `304` none). A 401 there is a refusal, not a broken sign-in; a 404 says a
  private repository needs an account. A chosen account that left still says so. Settings call the
  account optional. List widgets still need an account.
- **[S-747]** Saved copies (classic S-34, A-22, R-24, N-26): each feed's last good answer per
  account is written atomically to `%LOCALAPPDATA%\Themia.WinUI\Cache\GitHub\<hash>.json` (an
  envelope: `schemaVersion`, key, account, fetch time, value; the name a hash of key and account; no
  token). On start it is shown at once, flagged by the header caution "Showing what was saved 3 h ago;
  refreshing." until the first answer lands (a failed refresh's own warning outranks it). A file of
  another version, feed or account, older than a week, or that does not parse is ignored with a log
  line. A signed-out account's copies are deleted; copies untouched for a week are pruned.
- **[S-748]** Notifications: an accent dot before an unread title wherever read and unread are both
  listed (not colour alone); the widget menu's "Mark all as read" sends `PUT notifications` per
  account read, optimistic with rollback as [S-621].
- **[S-749]** Row menus: Open in browser, Copy link, Open repository (from the item's own page,
  `GitHubRules.RepoPageOf`); reviews add "Open changed files"; notifications keep their mark-read
  items. The repository widget's menu adds "Open repository".

Not ported: keyboard navigation of rows — there is no shell keyboard lease yet; rows already open
through `DevRows.Open` and build their menu through a factory, so Enter and the Menu key attach there
later. The search total ("8 of 23") and grouping issues by repository (extras, not classic).

## Weather: current conditions and forecast

Round 3 of `WeatherNow` and `WeatherForecast` (originals `weather-current`, `weather-forecast`).
Rules in `Themia.Core/Widgets` (`WeatherLayout`, `WeatherFacts`, `WeatherReport`, `WeatherData`) and
`Themia.Core/Storage/WeatherCacheStore`; tests in `WeatherLayoutTests`, `WeatherCacheStoreTests`,
`WeatherReportTests` (`S72x_`).

- **[S-720]** One open-meteo request per place carries the current block (temperature, feels-like,
  humidity, wind + direction + gusts, precipitation, pressure, cloud cover, UV), today and the next
  five days (high/low, sky, precipitation sum + max chance, max wind, max UV, sunrise, sunset) and
  the next 24 hours (temperature, precipitation chance), `timezone=auto`. The forecast asks the same
  daily fields for 1..14 days. Every value is range-checked; a missing extra block still yields a
  reading. Hours carry the place's UTC offset; a reply without one has no hourly curve.
- **[S-721]** Day labels: "Today", then the culture's own abbreviated weekday (English while the app
  runs globalization-invariant).
- **[S-722]** The current readings, in order: Feels like, Humidity, Wind ("11 km/h NW"),
  Precipitation (the last hour's amount while it rains, else today's chance), Pressure, UV index,
  Cloud cover, Gusts, Sunrise, Sunset. A reading the service did not send is left out, not "--".
- **[S-723]** Current-conditions ladder (`WeatherNowLayout.Plan`): the temperature always (icon
  beside once it fits, condition beside or under, [S-656]); then — a strip: the readings as
  label/value lines in a column beside the reading; a tall column (H ≥ Expanded): today's high/low,
  then up to 8 edge-to-edge rows; otherwise a flat row of icon + label + value cells (no boxes,
  original S-07) once H ≥ Regular and three cells fit, else a single "Feels 17°C" token; today's
  "H 19°  L 11°" under the condition (Least ≥ Regular, or beside on a strip); a second row of cells
  and a five-day strip at Huge both ways; and the next 24 hours as a full-bleed area chart with
  precipitation-chance bars and six-hourly labels once H ≥ Expanded (not on strips or columns). With
  a chart the reading stops at Display and the chart takes the rest. "Show detail" off = the reading
  alone at every size.
- **[S-724]** Forecast layout setting `Layout` = Auto | Rows | Columns | Grid (default Auto: columns
  on a strip of width ≥ Regular, rows elsewhere); the user's choice always wins. Widget menu: the
  two layouts not on screen, and "Automatic layout" when one is pinned.
- **[S-725]** Rows: stretched to fill the tile when every day fits at the row floor (40, or 32
  compact under a 200px list), capped at 80, else scrolling (hidden bar). Columns by width: icon +
  low ≥ 160, range bar ≥ 300, sky ≥ 380, droplet + amount ≥ 420, chance and max wind ≥ 500. A row
  ≥ 46 tall without a sky column puts the sky on a second line and the precipitation under the
  high. Huge both ways: a Today panel (sky, high/low, precipitation + chance, wind, UV, sunrise,
  sunset) beside the rows; a tall column with room for it and every row: the panel above them.
- **[S-726]** Columns: day + high always, then (by height) icon, low, a vertical range bar that takes
  the leftover, precipitation, sky (column ≥ 72), and at H ≥ Expanded the chance and the wind;
  scroll sideways below 56px a column. Grid: 1 card across ≤ 220, else 2..4; cards stretch to fill or
  scroll at 64; each card: day + icon, high/low, then bar, sky, precipitation "mm", chance · wind, sun.
- **[S-727]** Today is marked by an accent, semibold label and a quiet fill under its row, column or
  card. Precipitation is a droplet and the amount ("0.3"; "0.3 mm" in the grid), blank on a dry day.
- **[S-728]** `HideLocation` ("Location name" switch, both widgets): off leaves the header to the
  catalogue label; on, the title is the chosen place's city (first part of `LocationName`), the typed
  query only before a pick. A user label still wins.
- **[S-729]** The last good answers persist in `weather_cache.json` beside the state
  (`schemaVersion` 1, atomic temp + rename, one writer chained after each fetch that brought a new
  answer). At launch they are restored marked `Restored`: drawn at once with their real age in the
  caution colour ("Saved reading from 3 h ago; refreshing."), never treated as fresh, fetched again.
  A file of another schema or not JSON is set aside as `.corrupt`; entries older than 3 days, from
  the future, or without a temperature are dropped. The gallery never reads or writes it.
- **[S-730]** "Refresh" in both widgets' menus resets the back-off and bypasses cache freshness
  (`WebCache.GetAsync(force)`), still joining a request already in flight.
- **[S-731]** Gallery fixture: a fixed reading, 24 hours and 14 days for Zurich seeded into the
  caches as fresh answers, so `+fixture` renders offline and the same every run
  (`THEMIA_WEATHER_LAYOUT=rows|columns|grid` picks the forecast layout).
- **[S-732]** City search is one reusable piece, `PlaceSearch` + `PlaceSearchBinding` (Sun & Moon can
  bind its own config): search 400 ms after typing stops once 2+ characters, Enter at once; results
  open as a drop-down over the rows below; "Searching…", "No locations found." (not an error),
  "Search failed." (error, logged); malformed results dropped. A chosen place replaces the search with
  one "Location" row and a Change button, which clears the choice and brings the search back.
- **[S-733]** Reading glyphs, checked against the font: droplet EB42, thermometer E9CA, wind E7BC;
  layouts: list E8FD, columns E8C0, grid E8A9.

Not ported: wind in mph for °F (the original had none either); a gradient fill on the range bar
(solid accent, as every other bar in the app); the refresh stepper (fixed choices app-wide).

## Tasks and Claude usage: using the whole tile, and the rest of the original

- **[S-780]** Tasks ladder (`TaskLayout.Plan`, from the card's `WidgetArea`): one task always; the add
  row from Regular height; the summary strip on an Expanded × Expanded panel; the list takes the rest.
  Compact rows below Expanded height; one-line titles at Compact height, at most two below Expanded.
  Due text by width: none on an XS tile (both axes Compact), only Overdue/Today in a Compact-wide
  column, the short label otherwise, label plus date ("Overdue · 2 Oct", "Thu 8 Oct") on a panel;
  beside the title on a strip or panel (Expanded width) or in Compact layout, never in a column under
  240px. An open overdue task's marker rings in the critical colour at every size. On a Compact-wide
  tile the rows' side inset halves.
- **[S-781]** The list shows whole lines only (the viewport ends on the last line that fits, never on
  a section heading) and the lines near its bottom fade while more is below: row opacity, not a
  painted gradient, because the card may be transparent. Without an add row the list's foot padding
  goes to rows.
- **[S-782]** Two columns, filled top to bottom then left to right, the two cells of a line sharing
  its height — only on a Huge-wide tile and only when one column would not hold the list.
- **[S-783]** The summary strip: "7 open · 2 overdue · 1 due today · 3 done", open always, the rest
  when non-zero.
- **[S-784]** Completed tasks (when shown) follow the open ones, each part in its own order, under a
  "Completed · n" heading from Expanded height.
- **[S-785]** An open important task leads its title with a "!" in the critical colour (classic
  S-11) where its column is at least 160px wide; the weight and the tooltip say it everywhere.
- **[S-786]** Drag a row to reorder it (classic S-54): a press arms a drag after 5px (never on the
  marker, never while an editor is open); the lifted row follows the pointer and the rows it passes
  make way (into a slot over its middle half, within its own section); the list scrolls a little when
  the pointer nears an edge; letting go commits once through the Move up / Move down path; a lost
  capture puts the rows back. In edit mode the card captures the press and moves the widget instead.
  Redraws wait for the drop.
- **[S-787]** Keyboard (classic S-33..S-37, S-50 F2): a click on a row focuses it and asks the
  surface for the keyboard (`RequestTextInput`, the one place, `UpdateKeyboard`, so a shared keyboard
  lease can replace it); Up/Down/Home/End move without wrapping, Enter ticks, Delete deletes, F2
  renames, Escape gives the keyboard back; leaving the list gives it back; a redraw keeps the focus
  on the same task, or the row now in its place.
- **[S-788]** Tab in a rename box commits and opens the next task's box (none past the last); in the
  add box it commits and keeps it open; Shift+Tab commits and closes (classic S-56).
- **[S-789]** Under Today on a panel each row names its list (the account's lists, cached).
- **[S-790]** Claude usage spends the height stacked rows leave: thicker bar (6 → 12), then the
  percentage at Subtitle, then gaps up to 24; what is left centres the rows.
- **[S-791]** One-line rows on a strip at least 360 wide carry a reset column ("2h 9m"); labels
  shorten to make room for it.
- **[S-792]** An XS tile with one or two bars shows the session percentage as a hero in its severity
  colour over its bar, and "7d 73%" under it.
- **[S-793]** On an Expanded-wide tile each stacked row adds the reset's clock time and the pace
  ("resets in 3d 4h · Wed 16:00 · ahead of pace") and a tick on the bar at the share of the window
  gone; windows are 5 h and 7 d; within 5 points is "on pace".
- **[S-794]** A session gauge (270° arc, percentage and countdown inside, pace tick across the ring,
  label and pace under it): beside the weekly bars on a Huge-wide panel, or on any Expanded panel with
  one or two bars; above the week in a tall column with one or two bars.
- **[S-795]** "Updated 3m ago" under the rows from Regular height, with the account's name when the
  gauge is beside the bars.
- **[S-796]** Refresh is a 5–60 minute slider in steps of five (the classic stepper); a stored 10 or
  45 is kept, not snapped to a preset.

## No backwards compatibility

The app is unreleased, so the schema changes in place and no code reads an older shape. Validation of
corrupt, hand-edited or wrong-typed data at trust boundaries stays: that is robustness, rejected or
repaired to defaults and logged, never an upgrade. S-1050..S-1059.

- **[S-1050]** No migration, legacy-key reader or "old shape" repair exists, and none may be added.
  `WidgetConfig.Repair()` only clamps ranges and drops values a hand edit made unusable.
- **[S-1051]** Only `AppState.CurrentSchemaVersion` is read. A newer state file is refused
  (`NewerSchema`), an older one is refused as not supported (`NotState`); the file is set aside as
  `.corrupt`, an empty desktop starts, and the user is told. The same holds for the settings import.
- **[S-1052]** *(Retired.)* [S-540]/[S-563]'s hearth-era Mail, Calendar and Contacts config fold-in
  (`collectionId`, `unreadOnly`, `showPreview`, `maxCount`, `daysAhead`, `showAllDay`, `showLocation`,
  `showDetail`, `showAvatar`) and the `Legacy*` properties: removed. Those fields are unknown JSON now
  and are ignored like any other.
- **[S-1053]** *(Retired.)* [S-555]'s read of bare-`uid` IMAP handles: removed.
- **[S-1054]** *(Retired.)* `TokenExpiry.FromUnixSeconds` (the classic `expires_at`) and the
  "imported account" wording in [S-501], [S-505], [S-506], [S-509], [S-525]: removed. An untagged
  secret in `accounts.json` is still refused ([S-506]); that is validation, not a format to accept.

## Settings window: Windows 11 Settings

The settings window is drawn as Windows 11's Settings app (S-1540..S-1559). Owning files:
`Shell/FluentLayout.cs`, `Shell/SettingRow.cs`, `Shell/ColorChoice.cs`, `Shell/Config/ConfigWindow*.cs`,
`Shell/Config/SettingsPage.cs`, `Shell/Config/SettingsSearchIndex.cs`, `Shell/Config/ConfigPages.Uninstall.cs`,
`Services/DataPurge.cs`, `Core/Settings/*` (tests: `SettingsWindowTests`).

- **[S-1540]** Every row is the Community Toolkit `SettingsCard` (groups: `SettingsExpander`), built
  only through `FluentLayout` so every page — and every page added later — gets the look at once:
  header icon, title, secondary-text description, control on the right, card fill and stroke from
  the theme, 4 DIPs between cards, BodyStrong section headings 30 above / 6 below, switches with
  "On"/"Off" to their left, drop-downs at least 200 wide. A card that drills into another page
  (`NavRow`) carries the chevron; one that leaves the window (`ClickRow`) the open-elsewhere mark.
  Mica behind the whole window; the page sits on the same surface as the pane. There is **no status
  bar** (the old "Themia 1.0.0 · N widgets" line is gone). Brushes on settings surfaces come from
  App.xaml styles with ThemeResource, so they follow the window's own light/dark choice.
- **[S-1541]** The page title is a breadcrumb in the Title style above the scrolling cards, still
  while they scroll: a widget's page reads "View › Widget" and the view crumb opens the view. A page
  is a `SettingsPage` (title + optional description + rows); the window draws the title.
- **[S-1542]** "Find a setting" sits centred in the 48-DIP title bar. Its index is read off the pages
  themselves (`SettingRow` on every row's frame; section = the heading above it): every app page,
  view and display page, and one page per widget kind in use (built over a throwaway widget,
  offered for every widget of that kind as "View › Widget"). Built on first use, refreshed with the
  live page on every visit, dropped when the navigation changes shape. Pages are results too.
- **[S-1543]** Matching: lower-cased words, British spellings folded to American on both sides
  ("color" finds "Border colour"); every query word must land in the title, keywords (including a
  drop-down's options: "dark" finds Theme), section, page path or description.
- **[S-1544]** Ranking: field weight × match quality (exact, word, prefix, word prefix, substring,
  small typo — none for ≤3 letters, 1 up to 6, 2 beyond; a short word with a wrong first letter is
  no typo, typos only in title and keywords), plus a bonus when the whole query starts or equals the
  title; a page beats its own rows on its name.
- **[S-1545]** At most 8 suggestions; the same row reached twice is one suggestion.
- **[S-1546]** Choosing a suggestion (or Enter, which takes the best) opens its page; a row is
  brought a third of the way down the page (its expander opened first) and ringed in the accent for
  a moment. "No results" when nothing matches.
- **[S-1547]** Back in the title bar, Alt+Left/Alt+Right and the mouse's back/forward buttons step
  through the user's navigations (`NavHistory`): pushing from an older entry drops the forward
  tail, repeats coalesce, pages that disappear (deleted widget/view) leave the history.
- **[S-1548]** The debug render draws the real window: `--settings-png <Page|Widget:Kind|View> <out.png>
  [waitMs] [--theme light|dark] [--search text] [--land text]`; the window is as tall as the page,
  Mica is painted as its fallback colour; `--search` also writes the open suggestion list to
  `<out>-search.png`.
- **[S-1549]** Colours are never typed. One control, `ColorChoice` / `FluentLayout.ColorPickerRow`:
  a swatch button naming the colour, opening a picker with "use the default" (where empty means
  follow the theme), the preset swatches in rows of 8 (Windows' 48 accent colours by default; a
  neutral row first for surfaces; a setting may give its own set), and "Custom colour" opening
  WinUI's `ColorPicker` in a dialog with no text entry. The chosen preset carries a check.
- **[S-1550]** Stored format stays `#RRGGBB` (empty = default). Replaced: Appearance's accent,
  widget background and border colour; the calendar colours (Automatic + the six + custom); the
  palette editor's entries (rgb()/hsl() entries open on their colour, are stored as hex once
  changed); the scratchpad ink (the six inks + custom).
- **[S-1551]** The navigation pane is open at 1008 DIPs and wider; narrower it folds away (no
  icon-only rail, as in Settings) behind a button in the title bar next to back. About stays the
  last footer entry ([S-493]).
- **[S-1552]** About ends with an "Uninstall" heading and a danger card. Packaged (has a package
  identity): Themia removes its own package with `PackageManager.RemovePackageAsync` — no capability
  is needed (full trust, medium integrity, own publisher); if that fails, Settings' page for the app
  (`ms-settings:appsfeatures-app`) opens. Not packaged: Installed apps (`ms-settings:appsfeatures`)
  opens and Themia quits.
- **[S-1553]** The confirmation offers, unticked, "Also delete my widgets, settings and accounts";
  ticked, it names the folders: `%APPDATA%\Themia` and `%LOCALAPPDATA%\Themia` (`AppStateStore.FolderName`),
  and nothing else — a missing/relative root or a name that is not a plain folder name deletes
  nothing. They go only after Themia has closed and saved: packaged, in-process just before the
  package is removed; loose, by `Themia.exe --purge-data <pid>`, which waits on the app's and its
  children's process handles, works the folders out again itself, and does nothing unless the id
  is a running copy of the same executable.
- **[S-1554]** The goodbye survey `https://themia.app/goodbye?v=<version>` (escaped; "unknown"
  without one) opens on a confirmed uninstall, as the original installer did on an interactive one.
- **[S-1555]** Nothing is removed or deleted without the dialog's Uninstall; no dialog, no
  uninstall; a debug settings render never uninstalls.
- Not ported / different: karpo's status bar (removed by user request). The SDK's `TitleBar` and
  `BreadcrumbBar` controls are not used (the first crashed hearth; the second needs a WinRT
  collection) — equivalents are built from a grid and text blocks.

## Meter framework and memory

The six meters (CPU, memory, disk, battery, network, ping) share one view, `MeterView`, and differ
only in a `MeterPart` per kind (`Widgets/Meters/<Kind>Meter.cs`).

- **[S-1120]** Every meter has a refresh interval (`GaugeConfig.IntervalSeconds`, 1–60 s; ping
  1–300 s). Defaults: CPU, memory, network 1 s; disk 5 s; battery 15 s; ping 10 s; GPU 2 s. Load
  clamps an out-of-range value and logs it. A new cadence restarts the history, so the fixed-slot
  x-axis never mixes two spacings.
- **[S-1121]** Every meter has a graph span (`GraphSpanSeconds`, one of 30 s, 1, 2, 3, 5, 10, 20,
  30 min, 1 h; load snaps anything else to the nearest). The chart holds span / interval samples,
  2–600 (`MeterTiming.Capacity`), and a chart too narrow for that many 2-DIP slots draws the
  newest it has room for (`MeterTiming.Window`). Defaults: CPU, memory, network, GPU 1 min; disk
  3 min; battery 5 min; ping 20 min (the original's).
- **[S-1122]** A meter's settings page ends with the shared rows: Show history, Chart span, Refresh.
  The kind's own rows come first.
- **[S-1123]** Pressure colours, as the original: usage accent < 70 %, caution 70–89 %, critical
  ≥ 90 % (CPU, memory); a drive 80/90; a battery success > 50 %, caution 21–50 %, critical ≤ 20 %,
  accent while charging. Compared in whole percent. They colour the chart, the bars and the
  composition — never the reading, which stays primary text.
- **[S-1124]** A share is charted against 100 %. A rate (network) is charted against the peak of
  the samples on screen, floor 1 KB/s — not the session's all-time peak, which let one burst
  flatten the trace for the rest of the session. A large tile's scale line names that top.
- **[S-1125]** Performance counters go through one AOT-safe `PdhQuery` (English counter paths,
  `SafeHandle`-owned query, every counter closed with it). The GPU sampler uses it too.
- **[S-1126]** The chart (`SparkBand`) draws one or two series: the first filled, the second a
  thinner line (upload, writes). The battery draws its history when Show history is on (the first
  port ignored the setting).
- **[S-1127]** A kind adds detail lines (priority order) and a breakdown list to its reading. The
  layout shows detail lines from Regular height (two) and all of them from Expanded height; the
  list in whole compact rows from Expanded height, up to the kind's ceiling. A part is told what
  fits (`MeterDemand`) so it only does work that will be seen; when the room grows it is asked for
  a fresh reading at once rather than leaving the space empty for an interval.
- **[S-1128]** A kind may draw a usage bar beside its chart (from Regular height), which on a tile
  Expanded in height and wide enough for its legend becomes a composition bar.
- **[S-1129]** Arrangements: stacked (one column, chart full-bleed along the bottom, guaranteed 22 %
  of the height from Regular); side on a wide strip with a chart (reading and details left, chart
  the full height on the right); panels on a Huge-wide, Expanded-tall tile with a list (meter left,
  list right, each row with a share bar).
- **[S-1130]** Memory: percent used, caption "used of total". Data from `GlobalMemoryStatusEx`;
  the commit charge is `TotalPageFile − AvailPageFile` of `TotalPageFile`.
- **[S-1131]** Memory composition: in use / cached (standby + modified) / free, from the PDH
  standby and modified-list counters, always summing to the total; absent if a counter is missing.
- **[S-1132]** Memory detail lines: Available; Swap (page file used of total · %, total = commit
  limit − physical, used from `\Paging File(_Total)\% Usage`; hidden with no page file);
  Committed. The PDH query is opened only while a memory widget can show them, and closed when the
  last one detaches.
- **[S-1133]** Memory top processes: by working set, a ceiling of 1–10 (default 10; the tile shows
  what fits), name and size, pid on hover. The process walk is shared and refreshed at most every
  2 s whatever the meter's cadence.
- **[S-1134]** Memory settings: Usage bar, Available, Swap, Committed, Top processes (+ ceiling),
  each on by default — a toggle says whether a figure may appear; the tile decides if it fits.
- Not ported / different: the original's `visibleMetrics` multi-select (typed toggles instead) and
  per-metric ordering (fixed ladder order); its "Usage" toggle (the reading is the meter); friendly
  process descriptions in the memory list (image names — a description needs a handle per process
  per refresh). The audit's `MeterLadder` was not built: `Disclosure` is the one ladder.

## Power and Bluetooth

Port of `main`'s Power (`widgets/system/power`, [S-01]..[S-11]) and Bluetooth (`widgets/system/bluetooth`,
[S-01]..[S-16]) plus the round-3 ladder. Core: `PowerActionRules`, `PowerArm`, `PowerTileLayout`,
`PowerStrip`, `BluetoothRules`, `BluetoothLayout`; tests `PowerActionTests`, `BluetoothLadderTests`.

- **[S-1240]** Power is the original's **actions tile** (shut down, restart, sleep, hibernate, lock,
  sign out, display off), not a status readout. The former status facts live on as a strip
  ([S-1250]). One widget, no flag.
- **[S-1241]** `PowerAction` is a closed enum at every boundary, stored by name. On load
  `PowerConfig.Repair` drops undefined numbers and duplicates and says so; an unknown name fails
  the state load like every other enum.
- **[S-1242]** `Actions` is the ordered list of buttons; default shut down, restart, sleep, lock
  (orig. [S-02]). Settings list every action once — chosen ones first in tile order, each with a
  switch and move up/down.
- **[S-1243]** Win32 only, no `shutdown.exe` (orig. [S-03]/[S-09]): `ExitWindowsEx`
  (`EWX_POWEROFF`/`EWX_REBOOT`/`EWX_LOGOFF` | `EWX_FORCEIFHUNG`, planned reason), `SetSuspendState(h, 0, 0)`,
  `LockWorkStation`, `SC_MONITORPOWER` broadcast via `SendMessageTimeout`. `SeShutdownPrivilege` is
  enabled per call for shut down, restart, sleep and hibernate; `ERROR_NOT_ALL_ASSIGNED` after a
  "successful" `AdjustTokenPrivileges` is "not allowed". The token is a `SafeHandle`. The plan per
  action is decided in Core (`PowerActionRules.Plan`); `Win32PowerActions` only executes it, on a
  worker thread (sleep blocks until resume, orig. [S-04]); one action at a time.
- **[S-1244]** `ShowLabels` (default on) puts names under icons where the tile has room; the
  widget's context menu toggles it (orig. context action).
- **[S-1245]** With `Confirm` on (default), shut down, restart and sign out arm on the first press
  ("Confirm" in the critical colour) and run on the second; sleep, hibernate, lock and display-off
  never arm (orig. [S-05]). `Confirm` off runs everything on one press.
- **[S-1246]** A failed action shows "Could not <verb>: <reason>" as the tile's first status line in
  the critical colour and logs a warning (WinUI has no toast pipeline; orig. [S-07]).
- **[S-1247]** One armed tile at a time; it disarms after 5 s, on pointer exit, on a config change,
  or when another arms (orig. [S-06]). The 5 s window is checked by `PowerArm` against the press
  time, so safety does not depend on the repaint timer. **New:** a second press within 400 ms of
  arming is ignored — an accidental double-click cannot end the session.
- **[S-1248]** Ladder (`PowerTileLayout`): every button labelled (cell ≥ 64×56), else every button as
  an icon (≥ 32×28), else the first ones that fit; the grid picks the squarest cells (cell height
  capped at 132). The status strip only takes room that keeps the buttons on the same rung. XS/S:
  icons only (2×2 for four). M/Tall/L: strip under the buttons. Wide: strip beside.
- **[S-1249]** Huge×Expanded (XL): buttons left, a side panel right with every status fact as a row
  and the machine's power plans (`PowerEnumerate`), the active one marked. Read-only.
- **[S-1250]** Status strip facts, in drop order: source (with charge on a laptop), plan
  (`ShowPlan`), time left while discharging (`ShowTimeRemaining`), battery saver (`ShowBatterySaver`,
  battery only). Unreadable power state is one line saying so.
- **[S-1251]** Gallery cards are previews (`IWidgetSurface.IsPreview`, from the gallery's card
  host): the power view's backend is `InertPowerActions` there. The view starts inert and switches to
  the real backend only when bound to a non-preview surface (orig. [S-08] demo mode).
- **[S-1252]** No actions chosen shows "No actions selected" (orig. [S-11]).
- **[S-1253]** Gallery `Power+fixture`: a laptop on battery (64%, saver on, 2h 10m left), all seven
  actions, three plans. Read only by preview cards.
- **[S-1254]** Bluetooth ladder (`BluetoothLayout`): the list takes every whole row the tile has
  (snapped, the rest scrolls); `MaxCount` is a ceiling (1–30, default 20), not a target. Status word
  from a 260 px column; battery bar from a 400 px column (critical at ≤ 20%); "2 connected" in the
  header from Expanded width; two columns at Huge width when one cannot hold the list.
- **[S-1255]** Regular×Expanded and up: two-line rows — name, then kind · connection · battery ·
  signal (each only when enabled and reported; a wide row keeps battery and signal in columns).
- **[S-1256]** `ShowSignal` (default off): RSSI from the association endpoint's
  `System.Devices.Aep.SignalStrength`, plus a passive 3 s LE advertisement scan per refresh matched by
  device address. Shown as "-67 dBm"; no reading or an implausible one (≥ 0, < -127) is no column
  (orig. [S-16]).
- **[S-1257]** `ShowIcon` and `ShowConnection` (default on) are the original's `type` and
  `connection` metrics; the name is always shown (orig. [S-14]/[S-15]).
- **[S-1258]** Gallery `Bluetooth+fixture`: fourteen paired devices, three connected with battery
  and signal. Read only by preview cards.
- **[S-1259]** Context menu "Refresh" enumerates now (orig. [S-09]).

Not ported: Bluetooth's 1–30 s refresh choices (kept 5–300 s: enumeration and GATT reads take
seconds); per-metric `visibleMetrics` ordering (fixed ladder order).

## Habits

Code: `Widgets/HabitTrackerView*.cs`, `Shell/Config/WidgetSettings.HabitTracker.cs`,
`Services/Habits/HabitPool.cs`, Core `Habits/HabitLists.cs`, `Habits/HabitListStore.cs`,
`Habits/HabitLayout.cs`, `Widgets/HabitPeriods.cs`, `Widgets/HabitRules.cs`. Tests:
`HabitRulesTests`, `HabitListsTests`, `HabitLayoutTests`, `TrackerPersistenceTests` (S1420/S1422).
Ports the original `widgets/others/habit-tracker/SPEC.md`.

- **[S-1420]** Habits are device-wide **lists** (original S-18), not per-widget data. The pool lives
  in `habits.json` beside the state file: `schemaVersion` envelope, every list/habit/key validated
  (malformed entries dropped and counted, duplicate ids keep the first), a corrupt or newer file set
  aside and never overwritten, an unreadable one leaves the pool read-only. Saves are atomic (temp +
  move), single-writer (one gate) and coalesced (one more write of the latest snapshot); the UI
  thread is the only mutator and every change goes through `HabitPool.Update`. A habit owns its
  log, so deleting it takes its history (original S-05/S-20 by construction). The widget config
  holds only `ListId` plus its own settings; with none chosen it takes the first list, creating
  one only when there is none; a deleted list is not replaced ("pick a list"). Two widgets on one
  list see each other's ticks at once (`HabitPool.Changed`). The header title is the list's name.
- **[S-1421]** Only the box ticks a habit (original S-02); name and run never do. The box is drawn
  (not a stock `CheckBox`, whose template clipped its glyph to a dash in both states): empty when
  not done, accent-filled tick when done. A tap reads the clock once and toggles that period;
  un-ticking removes every copy of the key.
- **[S-1422]** Reset period per widget (original S-07/S-08): hourly, daily, weekly (ISO), monthly,
  custom 1–1440 min in epoch-anchored windows. Keys: `YYYY-MM-DD`, `YYYY-MM-DD-Hhh`, `YYYY-Www`,
  `YYYY-MM`, `C{n}m-{window}`. Steps back are calendar steps for day/week/month; an hour steps from
  its own first instant, so a skipped spring hour is never visited and a repeated autumn hour is
  one period. The widget wakes every 15 s (hourly/custom) or 60 s and redraws only on a new period.
  The custom interval is typed and refused (not coerced) when out of range; the config is repaired
  to its ranges on load.
- **[S-1423]** Streak = consecutive done periods ending now; the current period not yet done does
  not break it; a gap does. Future, malformed and other-cadence keys are never counted.
- **[S-1424]** Best run = longest consecutive run anywhere in the log under the cadence, using the
  same step as the streak.
- **[S-1425]** Habits are added, renamed, moved and deleted **in the widget** (original S-13/14/16/22/23):
  an "Add a habit" row at the foot (always shown for an empty list; Enter keeps it open for the
  next habit); rename by double-clicking the name, F2 or the row menu (Enter/leaving commits, Escape
  cancels, blank or unchanged commits nothing, the id and log never change); the row menu (shared
  `ItemMenu`) has Done/Not done, Rename, Move up/Move down (disabled at the ends), Delete (danger,
  no dialog). Drag-to-reorder is not ported: the card owns pointer capture; Move up/down replace it.
  The settings page holds no habit editor (original S-23).
- **[S-1426]** History dots (0–31, default 7): oldest left, the current period right, from the same
  step as the streak; each dot's tooltip names its period in the cadence's units ("Week of Monday
  28 Sep 2026", "14:00, …", "September 2026"). A narrow tile shows fewer, dropping the oldest; the
  name never yields to them.
- **[S-1427]** Ladder (`HabitLayout.Plan`): XS box + name, two rows → S + run (number) → M + dots
  and add row → Wide + run in words and a 7-period rate per row → Tall + summary strip (done this
  period, rate, bar) and a completion chart (one bar per past period, done/total) in the height the
  rows leave → L all of that → XL + best run per row. Rows are 40px (32 on a Compact side); the
  viewport holds whole rows and the rest scroll (no chart then). The header shows `done/total`.
- **[S-1428]** Keyboard (original S-17): a clicked row takes the keyboard (`RequestTextInput`, through
  the one `UpdateKeyboard` method that the shell's keyboard lease will replace — not built yet):
  Up/Down clamp, Home/End, Tab/Shift+Tab wrap, Enter ticks, F2 renames, Delete deletes, Escape gives
  the keyboard back. In an editor Tab commits and opens the next row's editor, falling into the add
  row past the last. Not verified live: needs a manual pass on the desktop.
- **[S-1429]** Settings: the list picker/editor (pick, rename as typed, habit count, add, delete
  — always confirmed, empty or not, original S-24), Resets, Interval (custom only), Show streaks,
  History. Renaming a list retitles every widget showing it.
- Not ported: original S-10 compact header default (no compact header mode here), S-12 equal-slice
  rows (Fluent fixed rows, S-213), S-13 drag reorder (above), S-21 legacy migration (no backwards
  compatibility).

## Calendar and contacts: the whole tile

The Calendar and Contacts ladders ([S-660] API), the Outlook hand-off and the key maps. Rules in
`Themia.Core/Office/CalendarFit.cs`, `CalendarLinks.cs`, `ContactRules.cs` (tests `CalendarFitTests`,
`ContactsFitTests`, `S12xx_`); views `CalendarView.Fit.cs` (new), `CalendarView*.cs`, `ContactsView.cs`.

- **[S-1260]** The calendar's layout is chosen from the configured view and the slot
  (`CalendarFit.Layout`): a slot under 150 tall and at least 360 wide (and 56 tall) is a **strip**,
  whatever the view; else under 160 wide or 88 tall the **next** tile; a month under 154 × 112, a
  week under 280 × 96 and a day under 140 × 112 the **agenda** (S-574); a month at least 300 tall
  and 1.6× as tall as wide puts the **agenda below**; a month at least 720 × 400 the **agenda
  beside**. Unmeasured: the configured view. Gallery: XS next, S agenda, M month (dots), Wide strip,
  Tall month over agenda, L month (chips), XL month beside agenda. Next and agenda do not page.
- **[S-1261]** Next (XS): the first agenda day's event under way or next (a timed one before an
  all-day one or a birthday), its time ("Now" in the accent, "All day", "Birthday"; prefixed with the
  day when not today), its title (a second line, or two caption lines, before anything else), then
  "+N more today". Nothing coming up: the weekday, the date and "Nothing coming up". Click opens it.
- **[S-1262]** Strip: 3–5 day columns of ≥112 px from the anchor (today while following it), or the
  configured week's 7; each has a heading ("Today" in the accent, "Tue 6") and one-line entries
  ("09:00 Standup", the time stacked above in columns under 100 px) as many as fit, then "+N more".
  It pages by its own width; title "4 Oct – 7 Oct".
- **[S-1263]** Month with agenda: below, the month's rows are square to the column (24–32 px, dots)
  and the list takes the rest; beside, the list is 280 px with a hairline. The list is the month's
  fetched events from today (from the 1st when paged away), so no extra fetch.
- **[S-1264]** Large month: weekday names ("Mon") from 64 px columns; month chips lead with their
  start time from 100 px cells (classic S-44); chips get a 20 px line in cells ≥90 tall; at height
  class Expanded or more only the weeks holding a day of the month are drawn (4–6).
- **[S-1265]** Week columns ≥64 px spell the weekday; ≥100 px a timed chip shows start–end and its
  location under the title.
- **[S-1266]** Day: 32 px hours when that shows 07–22 unscrolled (480–600 px), else 40; a block of
  ≥48 px adds a "location · organizer" line.
- **[S-1267]** Agenda: under 128 tall the first group's "Today" heading is dropped (any other day keeps
  its heading); at ≥480 px a row puts location and organizer in columns beside the title; at ≥720 px
  the days flow into two columns (whole days, balanced by rows).
- **[S-1268]** Opening an event: a Microsoft account's event opens `ms-outlook://events/{id}` when
  Outlook's protocol is registered (one cached check shared with Mail, `ShellLauncher.OutlookInstalled`),
  else the provider's https page; the provider comes from the event's account, never the link's
  domain. Only https (no user info) and `ms-outlook://events/<percent-encoded>` pass
  `CalendarLinks.IsLaunchable`, re-checked by `ShellLauncher.OpenCalendarLink`.
- **[S-1269]** Keys (classic S-31..34), `CalendarView.HandleKey(WidgetKey)`: Left/Right page, Up/Down
  move the cursor day a week (month and week layouts; the month outlines it), Home today, Enter opens
  the cursor day's first event (next event where nothing pages). Not yet reachable: the shell's
  keyboard lease does not exist; this is what it will call.
- **[S-1270]** Contacts list rows say what the list's area allows (`ContactRules.ListDetail`, on the
  tile less its search box): width Compact → the name only (32 px rows, no avatar); height Compact →
  the role, else the address/number; Regular → the role plus the address (else number) when three
  such rows fit, else the role; Expanded+ → role, address and number, each address/number with its
  glyph. Every row of a render is one height. Rows flow into columns of 240 px (up to 4), row-major;
  avatars from 180 px columns; the viewport stops at a whole row.
- **[S-1271]** Compact rows are one line: a 24 px avatar, the name, and the one field right-aligned
  (columns ≥240 px; the name keeps the larger share).
- **[S-1272]** Details: the Name cell carries a 20 px avatar when the table is ≥360 px wide.
- **[S-1273]** Grid cards are one height (88 px; whole rows only); at width and height class Expanded+
  they are 140 px wide with a 56 px avatar and the address as a second line.
- **[S-1274]** XL (width Huge, height Expanded+) with someone selected: a 300 px detail pane beside
  the list (not the grid) — 64 px avatar, name, role, then Send email (the address), Copy email, Call
  (the number), Message, Copy phone, Copy name. Selecting re-lays the list around it.
- **[S-1275]** Search box: always from 220 px tall; from 128 a header search button opens it (it folds
  away again when left empty); below 128 none. A typed query keeps the box open.
- **[S-1276]** The People count is a number box (`FluentLayout.Stepper`): at least 5, steps of 5, no
  upper bound (S-583); text that is not a number is overwritten with the last good value.
- **[S-1277]** Keys (classic S-31/32), `ContactsView.HandleKey`: Up/Down move the selection through the
  people on screen (from none, Down picks the first, Up the last), Home/End jump, Escape clears, Enter
  writes (else calls). A press anywhere else on the desktop surface clears the selection (classic
  S-38); a click in another app needs the shell's mouse hook (not built).
- **[S-1278]** vCard 2.1 quoted-printable: `ENCODING=QUOTED-PRINTABLE` (or the bare parameter) values
  are decoded in UTF-8 or Latin-1 (`CHARSET`), across `=` soft line breaks; bare 2.1 parameters join
  `TYPE` (so `TEL;CELL;PREF` is preferred). Lenient: a stray `=` stays.
- **[S-1279]** The gallery fixture holds 42 people, some with both address and number, so XL scrolls.
