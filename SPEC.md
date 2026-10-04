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
  not corrected. "The screen" is the **whole monitor**, taskbar included, never the work area
  ([S-840]).
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
- **[S-09]** Widgets are clamped fully on-screen (`0 ≤ x`, `0 ≤ y`, `x + w ≤ 1`, `y + h ≤ 1`) at
  load, and into the display's **work area** for drag, resize, placement, paste and drop ([S-841]).
  Clamping is **total**: a non-finite or inverted rect collapses to
  a valid one rather than propagating NaN into storage.
- **[S-31]** Edit mode draws **eight** handles: four corners and four sides. A corner drags one edge
  on each axis; a side handle drags **only its own edge**, leaving the opposite edge and the whole
  other axis untouched. An edge dragged past its opposite stops at the [S-08] floor instead of
  inverting the rect.
- **[S-31a]** Settling a resize snaps and clamps **only the edges the handle moves**: a moved edge
  rounds to the grid and stops at the work-area edge; an anchored edge never moves. Snapping the whole
  rect rounded the anchored edge too, and clamping the whole rect pushed a widget whose bottom edge
  was dragged into the taskbar upwards instead of stopping the edge at it.

## Defaults & placement

- **[S-10]** A widget definition declares its default size in **pixels**. At placement that is
  converted to a fraction of the current screen, so a newly dropped widget has a consistent physical
  size on any monitor, and scales per [S-02] thereafter.
- **[S-11]** Placement centres the widget on the drop point, then snaps ([S-06]) and clamps ([S-09])
  into the work area, so a widget dropped on the taskbar lands just above it.

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
- **[S-18]** `monitorViews` maps a display's **stable id** ([S-842]) to a view id; a display with no entry shows
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
  it traces. It is always cut to the work area ([S-851]).
- **[S-22]** Only a genuine style change passes `SWP_FRAMECHANGED`. Sending it on an ordinary
  z-order reassert forces a non-client recalculation, which hands the borderless window its frame
  back.
- **[S-23]** The window is placed with `AppWindow.MoveAndResize` over the monitor's **physical**
  bounds — the whole monitor, not the work area ([S-840]). Display scale only applies inside the window, where XAML lays out in DIPs ([S-05]).
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
  to its own screen ([S-09]) and so can never reach across the display boundary. A point over the
  destination's taskbar counts as that display; the widget lands just above the taskbar ([S-841]).
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
- **[S-128]** The note is handed to the note pool on a 400 ms debounce and flushed on blur, on
  switching note and on detach; the debounce is a write-rate limit, not a correctness path. A change
  from elsewhere never overwrites an unflushed local edit ([S-1348]).
- **[S-129]** The editor's chrome is removed through the text control's own resource keys (the
  `RichEditBox` shares the `TextBox`'s), not its `Background`: the stock template's visual states
  assign the border element directly and outrank a local value, so the field would light up solid
  on hover.
- **[S-130]** "Clear note" confirms in a dialog. The text is unrecoverable, so one click is not
  enough, and failing to show the dialog leaves the note alone. It empties the note's body (for every
  widget showing it); the note and its title stay.
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
  focus (was [S-10]). How it is sized and cropped is [S-1316].
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
  off-screen WebView2 at a 1280×900 desktop viewport and the display's device scale, captured at the size it is drawn ([S-1317]), lets it settle
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
the developer-mode gate ([S-33]) — this app has no developer mode, so the widget is listed normally;
`cssPath` unit tests — the selector code runs only inside the page and there is no DOM in the Core
tests; the `demoVideoFrame`/`demoWebElementRender` demo fixtures — the debug gallery's `Kind+fixture`
pages (a loopback MJPEG test card, a loopback status page) take their place. The SSRF guard
([S-08b] `check_url`) and the cached render across restarts ([S-20]) are now ported: [S-1314] and
[S-1319]. The video stream stays unguarded, as in the original. RTSP stays unsupported ([U-01]);
credentials in the address are refused ([U-03]).

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
([S-47]) — one form with auto-fill instead; STARTTLS on port 143 — the classic app spoke implicit TLS
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
- **[S-439]** Driver rows (`ShowTemperature`, `ShowPower`, `ShowFan`, `ShowClocks`)
  hide themselves on a card whose driver does not answer; their defaults and the video engines are
  now [S-1204], [S-1208].

Not ported: the widget's own `label` (the card's label is the instance's). The `graphSpanSec`
setting and the one-decimal percentage are ported since ([S-1200], [S-1206]).

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
  Durations read `3h 24m`, `12m`, `<1m`. How many apps the legend and the drawing name is
  [S-1211]/[S-1212] (`MaxApps` is a ceiling over what fits, `ShowList` hides the legend). Each app's colour is a stable hash of its key; a legend row
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
the bottom edge as any scrolling list does.

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

Not ported, deliberately: the per-widget header hide and label [S-03, S-19] (the card's own header
placement and label do this for every widget), the "unread only" filter, and the composite
`account:id` message ids (the account travels separately). Keyboard navigation, the header sort
button, the scroll fade and the envelope icons are ported in [S-1281]..[S-1289].

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
ported in [S-781]..[S-788]. OneDrive's scroll fade and single-click open are ported in [S-1281] and
[S-1291].

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
(the classic app had none either). Keyboard navigation goes through the shell's keyboard lease: see
[S-1269] / [S-1277] and [S-806] / [S-807].

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
- Left / Right / Space keys ([S-29] of the original) are ported through the keyboard lease
  ([S-811]). Not ported: a file or folder caption
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

Keyboard navigation of rows is ported through the keyboard lease ([S-810]). Not ported: the search total ("8 of 23") and grouping issues by repository (extras, not classic).

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
- **[S-787]** Keyboard (classic S-33..S-37, S-50 F2): a click in the widget takes the shell's
  keyboard lease ([S-808]) and a click on a row focuses it; Up/Down/Home/End move without wrapping,
  Enter ticks, Delete deletes, F2 renames, Escape gives the keyboard back; leaving the list drops
  the row focus; a redraw keeps the focus
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
- **[S-1428]** Keyboard (original S-17): a click in the widget takes the shell's keyboard lease
  ([S-809]) and a clicked row takes the focus:
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
  writes (else calls). A click anywhere else — another widget, the desktop, another app — ends the
  keyboard lease the widget holds from the selecting click, which arrives as Escape and clears the
  selection (classic S-38; [S-807]).
- **[S-1278]** vCard 2.1 quoted-printable: `ENCODING=QUOTED-PRINTABLE` (or the bare parameter) values
  are decoded in UTF-8 or Latin-1 (`CHARSET`), across `=` soft line breaks; bare 2.1 parameters join
  `TYPE` (so `TEL;CELL;PREF` is preferred). Lenient: a stray `=` stays.
- **[S-1279]** The gallery fixture holds 42 people, some with both address and number, so XL scrolls.

## Mail and OneDrive: the whole tile

- **[S-1280]** Mail's ladder (`EmailView.Lay`, rules in `MailLayout`): the first row always; the row
  view's column titles from 140px of height; then as many whole rows as fit. Stacked rows shed account
  tag, preview, extra preview lines and time as before ([S-556]); a wide strip and a big tile go one
  line per message ([S-1283]); the envelope joins from 300px ([S-1282]); the largest tile adds the
  provider, quick actions and the folder summary ([S-1284], [S-1285]).
- **[S-1281]** Bottom scroll fade, shared (`Widgets/ScrollFade` + Core `ListFade`): while the list
  continues below the viewport, rows within 48px of its bottom edge fade to 0.35 opacity (the Tasks
  depth and floor, [S-781]); scrolled to the end every row is whole. Opacity per row, never a
  gradient over the list (the card can be transparent). Event-driven: scroll, size, one layout pass
  after a rebuild. Used by Mail and OneDrive.
- **[S-1282]** Envelope: a closed envelope (accent) in front of unread mail, an open one (tertiary)
  in front of read mail, from 300px wide, in every view; the unread accent bar stays.
- **[S-1283]** One-line rows (list and compact views) from 640px wide, or from 420px on a strip under
  220px tall: sender column (24% of the line, 96-200px), subject with the preview after an en dash
  (preview from 440px), account from 560px, time. The row view is unchanged. A one-line row shows
  one preview line whatever `PreviewLines` says (that setting shapes the stacked rows).
- **[S-1284]** From 720px the account tag names its provider ("Work (Microsoft)"); the tooltip always
  does. On one-line rows at that width the pointer swaps a row's time for quick actions: mark read
  or unread, archive (not in Junk), delete — only where the account allows changes ([S-544]).
- **[S-1285]** Folder summary under the list on a tile ≥ 480px of content height: "4 unread of 1,661
  in Inbox" (or "… in Inbox, all read"), summed over the accounts from the counts each probe reports
  (`MailAccountOutcome.Folder`). Hidden while any answering account has not reported its counts — a
  total missing one account would be wrong, not smaller. Not a ladder rung: it never moves the
  fetch count.
- **[S-1286]** The count setting is a floor: the feed asks for `max(MaxCount, rows that fit)`, at most
  50, so a big tile is never half empty. Resizing refetches only when that number changes.
- **[S-1287]** Row view from 700px: when the user's columns leave Subject as the only flexible column
  and it would take more than 40% of the line, Preview joins after Subject. A chosen Preview keeps
  the user's order; without Subject nothing is added.
- **[S-1288]** List keyboard navigation, shared (Core `ListKeys`, App `RowFocus`): a click focuses a
  row (an inset 1.5px accent ring every row reserves, so nothing moves); Up/Down step, Page Up/Down
  jump 10, Home/End go to the ends, Enter opens, Delete deletes (Mail, where the account allows),
  F2 renames (lists that rename), Escape lets go. With no row focused a movement key lands on an end.
  The focus follows its item across redraws and moves to the row that took its place when the item
  left. Keys reach a view only once the shell's keyboard lease (WP11) makes the surface take the
  keyboard; the views' `KeyDown` handlers are in place for it. Clearing the focus on a click outside
  or on app blur is the lease's job.
- **[S-1289]** Mail's header carries a sort button (from 200px wide) that turns the list round and
  saves `SortOrder`, like the menu's toggle; its tooltip says the current order.
- **[S-1290]** OneDrive's ladder (`OneDriveView.Lay`, rules in `DriveLayout`): the back row inside a
  folder; then, where the text has 216px, one line per item with detail columns beside the name —
  Modified, then Size (a folder's item count), then Modified by, then Location (recent files only) —
  each added while the name keeps 120px, a column that misses closing the ladder, and shown in
  Explorer's order with Size last at the right edge; column titles from
  280px of height; a narrow tile with height keeps the details as a second line; a narrow short one
  shows names only. Then as many whole rows as fit.
- **[S-1291]** `ClickMode` (`ItemClickMode`, shared with the file explorer): files open on a double
  click (default) or a single click; folders always open on a single click. In double-click mode a
  single click focuses the row. Item menu: Open / Open in browser, Copy link.
- **[S-1292]** OneDrive's item count is a floor too: `max(FileCount, rows that fit)`, 5 to 50.
- **[S-1293]** Graph `$select` adds `lastModifiedBy` and `parentReference`: who changed an item, the
  folder it is in (`/drive/root:/Work/Q%203` reads "Work › Q 3", the top "My files", any other path
  nothing) and a folder's child count ("1 item", "12 items").
- **[S-1294]** On a tile ≥ 480px of content height the drive's quota sits under the list ("12 GB of
  1.0 TB used", `GET /me/drive?$select=quota`), cached 30 min per account and single-flight in
  `DriveService`; a failure is logged and means no footer, never an error on the widget.
- **[S-1295]** Gallery: `Email+fixture` holds 20 messages and folder counts, `OneDrive+fixture` 18
  items with modifier, location, child counts and a quota, so L and XL fill.

Not ported, deliberately: OneDrive image thumbnails (an XL-only extra, not in the original; every
row would cost a download). Main's low-level keyboard hook for list focus (the lease replaces it).

## Clipboard, Countdown and Mood: the whole tile

Code: `Widgets/ClipboardView.cs`, `Widgets/CountdownView.cs`, `Widgets/MoodTrackerView*.cs`,
`Widgets/ItemMenu.cs` (submenus), `Shell/Config/WidgetSettings.{Clipboard,Countdown}.cs`,
`Shell/Gallery/ClipboardFixture.cs`, Core `Widgets/ClipboardLayout.cs`, `ClipboardHistory.cs`,
`CountdownLayout.cs`, `MoodLayout.cs`, `MoodInsights.cs`, `ItemCursor.cs`,
`Model/Configs/CountdownConfig.cs`. Tests: `TrackersLayoutTests` (S1440..S1457).

- **[S-1440]** Clipboard keeps 1–200 entries, default 50 (original `maxItems`); the slider steps by 1.
- **[S-1441]** A row's preview is up to the plan's lines: the entry's own line breaks kept, blank
  lines dropped, whitespace collapsed inside a line, " …" when more lines follow; wrapped and
  trimmed by the row. One line is exactly the old single-line preview.
- **[S-1442]** Each row is marked by what it looks like (`ClipboardLayout.Classify`): link (http/https/
  ftp), address, number (leading digit/sign), colour (`#hex`, `rgb()`, `hsl()` → a swatch of it),
  several lines, else text; the current entry keeps the accent tick. Display only, never opened.
- **[S-1443]** Clipboard ladder (`ClipboardLayout.Plan`): XS/S one line, compact rows → M + time
  column → Wide 2-line previews + time → Tall 2 lines + detail line ("4m · 32 characters")
  → L/XL 3 lines + detail + "N of capacity" footer. A gallery preview never subscribes to the
  real clipboard; `Clipboard+fixture` seeds a made-up history.
- **[S-1444]** Clipboard row menu (shared `ItemMenu`): Copy, Remove from history, Clear history…
  (confirmed through `FluentLayout.ConfirmAsync`, like the header bin). `HandleKey`: Up/Down/Home/End
  cursor, Enter copies, Escape drops it — for the shell's keyboard lease, which is not built yet
  (nothing calls it; needs a live pass once it is).
- **[S-1445]** Countdown timer menu on every row and on the hero (original C-60): a "Display" header,
  the four formats with the current one checked, Remove… (confirmed). A pick writes the timer and
  saves (`RequestSave`) and the view rebuilds itself. Original C-61 (menu stays open while
  previewing formats) is not ported: a WinUI menu closes on a pick.
- **[S-1446]** The featured timer is the soonest still ahead, else the most recently passed; the
  "Next: <label> in <auto>" line names the soonest other upcoming one. The featured timer is not
  listed again; when it passes, the next tick re-lays the tile.
- **[S-1447]** Countdown ladder (`CountdownLayout.Plan`): XS/S the hero alone (+ next line); one timer
  is the hero at any size; Wide hero left, list right; M/Tall/L/XL hero on top, the others as rows
  under it. The hero readout takes the height the rows leave (`FluentType.Hero`, sized for the
  format's widest text); when every row fits they sit at the foot and the hero is centred above.
  Short or narrow tiles use compact rows; a compact list ≥260 wide shows the date as a column.
- **[S-1448]** `CountdownTimer.Created` (set when added in settings): L/XL draw a progress bar under
  the hero, XL under each row too. Unknown (unset) or not before the target: no bar.
- **[S-1449]** Mood faces (original S-01): five drawn faces (circle, eyes, mouth from frown to smile)
  on the heatmap's wash; the number and name are the tooltip and accessible name.
- **[S-1450]** A day's tooltip is its sentence plus one line per entry, oldest first, with the
  thought ("21:40 · Good: long walk") — the original S-12 hover.
- **[S-1451]** Right-click a logged day: its date, then each entry, with "Delete this entry" one
  submenu down (original S-12); an empty day opens no menu (the card's opens).
- **[S-1452]** Mood stacked ladder (`MoodLayout.Plan`, width < Expanded): faces always → one map row
  → thought box (H ≥ Regular) → footer (H ≥ Regular) → the map takes the rest, preferring three weeks
  to bigger squares (fixes M never showing the thought box); labels and digits from 18px squares.
- **[S-1453]** Wide tiles (W ≥ Expanded) split: a left pane (faces, thought, footer, insights) and the
  map beside it with weeks as columns, weekdays as rows, weekday names down the left and month names
  on top; weeks = what fits, capped by the setting; the left pane takes the rest of the width. A
  month span is always shown whole (squares shrink; [S-1573]).
- **[S-1454]** Insights (split, H ≥ Regular): "How it went" (count per level) and "By weekday"
  (average per weekday), both over the map's window, then (when the pane still has room, XL)
  "In this window": days logged of days elapsed, best run, thoughts written; L/XL list recent
  thoughts under the map
  ("Tue 29 Sep · Good: …"), as many whole rows as fit.
- **[S-1455]** Mood `HandleKey`: Left/Right/Home/End move a cursor over the faces (focus stroke),
  Enter logs that face, Escape drops it. For the keyboard lease; not live-verified.
- **[S-1456]** `ItemMenuEntry.Children` makes an entry a submenu (`MenuFlyoutSubItem`), separators
  collapse inside it as at the top level.
- **[S-1457]** `ItemCursor.Move`: the shared list cursor (Up/Left back, Down/Right forward, clamped;
  Home/End; Escape clears; no cursor + forward = first, + back = last).

Not ported, deliberately: clipboard pin (an optional extra the audit listed, not in the original);
Countdown keyboard (the original had none); Countdown C-61 keep-open (above). Mood `storageMode` stays
gone (one source). A Month span on an XS/S tile can need more height than it has (six rows at the
minimum square); it then clips at the bottom.

## CPU and processes

- **[S-1140]** CPU: percent of all cores (`GetSystemTimes` delta, shared per tick [S-643]); caption the
  clock ("4.72 GHz", [S-1141]) or, with Speed off or unknown, "N logical". The dead "all cores" caption is gone.
- **[S-1141]** Speed = base clock (`CallNtPowerInformation(ProcessorInformation).MaxMhz`) × PDH
  `\Processor Information(_Total)\% Processor Performance` / 100 (Task Manager's figure, turbo included);
  without the counter, the reported `CurrentMhz`. GHz to two decimals. Counter held only while a tile shows Speed.
- **[S-1142]** Per-core load: `NtQuerySystemInformation(SystemProcessorPerformanceInformation)` idle/kernel/user
  deltas per logical processor (kernel includes idle); a backwards counter reads 0, a changed processor
  count gives no answer for that tick. First processor group only (≤ 64).
- **[S-1143]** Uptime from `GetTickCount64`, two largest units: `3d 4h`, `2h 5m`, `7m`.
- **[S-1144]** Totals from the same process snapshot as the list: processes (pid 0 excluded), threads
  (`NumberOfThreads`), handles (`HandleCount`). Detail lines "Processes 312 · 4,210 threads", "Handles 128,402".
- **[S-1145]** Top processes by CPU: friendly name (version-info description, resolved off the UI thread
  once per pid+image, image name until then / when none), exe + pid tooltip, share bar on panels. Ceiling
  1–20 (default 20, above the original 10 so an XL list panel fills); the tile decides how many fit. One process walk per 2 s shared by every process
  list (`ProcessList.Recent`).
- **[S-1146]** Temperature (default off): LibreHardwareMonitor/OpenHardwareMonitor web server
  `http://localhost:8085/data.json` first (package/Tctl/Tdie inside the CPU node, else its hottest; outside a
  CPU node only a probe named "CPU"), then the ACPI thermal zone via PDH `\Thermal Zone Information(*)\Temperature`
  (Kelvin, hottest, 0–150 °C kept). Once on it never vanishes: "N/A" when nothing answers, nothing before the
  first poll (no N/A flash). WMI is not used (AOT).
- **[S-1147]** Fan (default off): fastest plausible (1–60000) RPM sensor named for the CPU in the monitor's
  tree; "N/A" otherwise — Windows has no fan API.
- **[S-1148]** Sensor poll: only while a tile with Temperature or Fan holds a lease; every 5 s while a source
  answers, 60 s otherwise; single-flight; a poll cut short by the last release does not count.
- **[S-1149]** CPU config: ShowFrequency, ShowCores, ShowUptime, ShowProcessCount, ShowTopProcesses
  (+ TopProcessCount) default on; ShowTemperature, ShowFan default off. Detail order: Temperature, Fan,
  Uptime, Processes, Handles. Settings page "Show" + "Sensors" + the shared history/refresh rows.
- **[S-1150]** Meter framework rung "cells" (`MeterReading.Cells`, `MeterPart.CellsWanted`,
  `MeterDemand.Cells`): a strip of per-part bars after the stats, at H ≥ Regular or on a strip's left
  column; bars ≥ 4 px with 2 px gaps in balanced rows (`MeterCellGrid.Fit`), taller and with each figure
  under it when the tile is Expanded in height and every bar is ≥ the figure's width; each bar in its own
  pressure colour.
- **[S-1151]** Processes: whole rows (`Disclosure.TakeRows`) up to a ceiling 1–30 (default 20), so a bigger
  tile shows more. Laid out from `IWidgetSizeAware`, no private `SizeChanged`.
- **[S-1152]** Processes columns (`ProcessTable.Columns`): the ranked figure always, then the other figure,
  threads, pid, each while the name keeps 96 px; a step that does not fit ends the ladder. Keeps [S-648]'s
  232 px boundary for the second figure; under 160 px figures drop their reserved width and the "by CPU" label goes.
- **[S-1153]** Processes at H ≥ Expanded: column headers (ranked one stronger) and a footer
  "312 processes · 4,210 threads" (ShowTotals); at Expanded × Expanded a hairline share bar per row
  (CPU share, or working set over physical memory).
- **[S-1154]** Re-rank in place: clicking the CPU / Memory heading, or the card menu's
  "Rank by memory / processor"; persisted through `RequestSave`.
- **[S-1155]** Processes settings: Rank by, Most processes (1–30), Columns: Memory, Threads, Process ID, Totals.
- Not ported: per-core sparklines in each cell (the original's per-core grid) — a bar per core carries the
  same comparison at a fraction of the drawing cost; per-core history would need 64 more series per tick.

## Displays (WP2)

The display watcher, stable monitor identity, the per-display switch and scale, and the geometry
model that keeps widgets still when the taskbar changes (S-840..S-859).

- **[S-840]** The grid and every stored fraction are laid out on the **whole monitor**, taskbar
  included. The surface window covers the monitor's bounds exactly ([S-23]); `Metrics` is its client
  rect in DIPs; the snap grid overlay spans it. Resizing, moving or auto-hiding the taskbar therefore
  never shifts or rescales a widget: rendering ignores the work area entirely. Was: the window and
  the fractions followed the work area, so every taskbar change moved every widget.
- **[S-841]** The **work area** is the region *new* geometry may use: `ScreenMetrics.Allowed`, the
  work area as a fraction of the monitor (`GridGeometry.AllowedArea`). Placement, drag, resize, paste,
  drop and duplicate settle into it, so nothing is put under the taskbar; a rect that does not fit
  shrinks to it, one outside slides in keeping its size, and one dragged against the taskbar stops
  flush with it. A widget the taskbar later grows over **stays where it is**: load validation clamps
  to the whole screen only, and nothing re-clamps on a display change. An unusable work area
  (non-finite, empty, off the monitor) allows the whole screen. An auto-hidden taskbar reserves
  nothing, so the whole screen is allowed.
- **[S-842]** A display's id is its monitor **device interface path** (from
  `DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath`, lower-cased), never `\\.\DISPLAYn`, which is an
  ordinal Windows re-deals on every topology change. Every per-display setting (`MonitorViews`,
  `DisabledMonitors`, `MonitorScales`) is keyed by it. Old ordinal keys are **not** migrated (no
  backwards compatibility, by decision); they simply match no display.
- **[S-843]** A monitor with no device path (a virtual or remote display, or a panel still waking)
  falls back to its GDI name, flagged unresolved and logged. An unresolved id is never *given* a new
  view: it shows the active view until it resolves, so a waking panel does not bind to a fresh empty
  view the next display change abandons. One that already has a view (a permanently path-less
  display) keeps it. When the path appears, the next pass replaces its surface with one under the
  real id.
- **[S-844]** One display pass (`AppHost.ReconcileDisplays`, plan in `DisplayPlan.Reconcile`) builds,
  closes and updates surfaces, matched by stable id, so a surface follows its panel whatever order
  Windows enumerates them in, and a surviving surface keeps its cards (a taskbar change restarts no
  widget). A surface whose window died is closed and rebuilt. Startup, display messages, the
  per-display switch and an import all go through it.
- **[S-845]** Per-display switch: a display in `DisabledMonitors` gets no surface, so its whole screen
  passes through to the desktop. The last display Themia is on cannot be switched off (the toggle is
  disabled and the host refuses). If every connected display is off anyway (the only one on was
  unplugged), the primary is shown without changing the setting.
- **[S-846]** An empty monitor enumeration builds nothing and closes nothing: it is a driver
  mid-change, and the next display message runs the pass again. No phantom fallback surface.
- **[S-847]** `DefaultViewForNewDisplays` (General page, "New displays show"): a display without a
  view shows that view, shared rather than claimed. Unset, a new display gets a view of its own.
- **[S-848]** The watcher is the command window: `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE` with
  `SPI_SETWORKAREA`, `WM_DPICHANGED` and Explorer's `TaskbarCreated` each request a pass. Requests are
  **coalesced, not timed**: the first queues one pass at low dispatcher priority, behind the rest of
  the burst; a later message queues the next. It is subscribed before the first surface is built, so
  a change during startup is heard.
- **[S-849]** Each pass also checks every surface's window rect against its monitor and re-places one
  that moved. A display-scale change resizes windows to a rect Windows scales from the old one; the
  surface's rasterization-scale change requests a pass so it is put back over its monitor.
- **[S-850]** Per-display **widget scale** (`MonitorScales`, 50–200 %, 100 % stored as no entry)
  magnifies widget content only: the card's cell stays where it is and the same size, the content is
  laid out at cell ÷ scale and render-scaled back up, so a view discloses less at a bigger scale
  ([S-663]). The app-wide widget scale (`Design.UiScale`) multiplies in through the same path.
- **[S-851]** The hit region never covers the taskbar: it is cut to the work area in normal mode,
  and is the work area itself (not the whole window) in edit mode. A widget the taskbar has grown
  over is clipped there (`SetWindowRgn` clips painting as well as input) rather than moved. This
  matters while Show Desktop lifts the surface above the topmost band ([S-33]), when the region is
  all that keeps the taskbar clickable.
- **[S-852]** The edit toolbar and the placement hint sit inside the work area (margins from
  `GridGeometry.AllowedPixels`), so a taskbar on the top edge does not cover them.
- **[S-853]** Settings lists **every connected display**, switched off or not, and each display's
  page has: Show Themia on this display, Shows (view), Widget scale, and Resolution (with the work
  area the taskbar leaves). The page title is the monitor's name; the id is never shown.

Not ported, deliberately: the original's legacy ordinal-key migration ([MI-03]/[MI-04]; no
compatibility, by decision); its ~1 s work-area self-heal sweep ([DW-04]), which polls, replaced by
[S-848]'s messages plus [S-849]'s check on every pass; its adoption sequence counter ([MI-07]): the
pass runs synchronously on the UI thread, so there is no await for an older adoption to win across.

## Notes: note store and markdown editor

Code: Core `Notes/NoteStore.cs`, `Notes/NoteMarkdown.cs`, `Notes/NoteEditing.cs`,
`Notes/NoteHistory.cs`, `Notes/NoteText.cs`, `Notes/NotesLayout.cs`; App `Services/Notes/NotePool.cs`,
`Widgets/MarkdownEditor.cs`, `Widgets/NotesView.cs` (+ `.List.cs`), `Shell/Config/WidgetSettings.Notes.cs`,
`Shell/Gallery/NotesFixture.cs`. Tests: `NoteStoreTests`, `NoteMarkdownTests`, `NoteEditingTests`.
Ports the original `widgets/others/notes` (SPEC S-01..S-38, `src/lib.rs`, `markdown.ts`, `editing.ts`,
`history.ts` and their tests). Replaces the old one-string-in-config note directly (no migration).

- **[S-1340]** A note is a document the app owns: `notes\<id>.md` (the markdown, byte for byte) plus
  `notes\index.json` (`schemaVersion` + `{id, title, updatedAt}` rows) beside the state file
  (original data layer). The widget config holds only `NoteId`, so a note outlives its widget, view
  and layout, and two widgets can show one note.
- **[S-1341]** Atomic and single-writer: every file is written to a unique temp sibling and moved
  over the target; every index read-modify-write runs under one lock; the index is read once and kept
  in memory (one process, one writer). A body is written before the index stamp, so a failed index
  write never advertises a save that did not land (original S-30).
- **[S-1342]** Ids are exactly 32 lowercase hex digits (`Guid "n"`), checked by shape before any path
  is built, so a hand-edited config can never name a file outside the folder. A write, rename or
  delete of a note not in the index does nothing (`NotFound`): no resurrection (original S-31).
- **[S-1343]** Index trust boundary (`NoteStore.ParseIndex`): absent → bodies adopted silently (first
  run); not JSON / no or another `schemaVersion` → set aside as `index.json.corrupt-<stamp>` and every
  `<id>.md` adopted with title "Untitled", ids (so every widget's link) and text kept; malformed rows
  dropped and counted; rows whose body is gone pruned (original S-33).
- **[S-1344]** Titles are one line (control characters → spaces), trimmed, at most 200 characters,
  blank → "Untitled". List order is title (case-insensitive) then id, never date. A new note is named
  uniquely ("Note", "Note 2", …).
- **[S-1345]** Reading tells "missing" from "empty" (`NoteReadStatus`), so emptiness is never saved
  over a note that is gone.
- **[S-1346]** A desktop widget with no `NoteId` creates a note of its own on first show (named after
  the widget's label, else "Note") and saves the id. A preview card (the gallery) creates nothing: it
  shows an empty draft that is never saved (original S-32).
- **[S-1347]** A widget whose note was deleted (anywhere) says "This note was deleted" and where to
  pick another (settings, or the list at XL); it is never handed another note, and a pending edit to
  it is dropped (original S-36).
- **[S-1348]** One `NotePool` per process: a change lands in memory at once and raises `Changed(id)`
  for a body, `Changed(null)` for the list; widgets re-read only for their id or null. Disk work runs
  off the UI thread, one operation at a time, in order; a queued body write is coalesced, never
  stacked. An unflushed local edit outranks an incoming body (original S-09/S-34). The pool is flushed
  on exit after the surfaces close.
- **[S-1349]** Settings: one card per note (pick dot; title committed on Enter or blur, not per
  keystroke; last-written age; delete behind a confirm), "New note" (creates and picks), and a line
  when the shown note was deleted. The cards follow the pool while the page is on screen (original
  S-38).
- **[S-1350]** Block syntax, one line each: `#`..`###` (a space after the hashes), `- ` `* ` `+ `
  bullets, `1. ` / `1) `, `> `, `---` `***` `___` rules, and fences (every line between two fence
  lines is code). Indent depth: two spaces or a tab a level, capped at 6 (original S-15).
- **[S-1351]** Inline: bold `**`, italic `*` and `_` (word boundary only, so `snake_case` stays
  plain), strike `~~`, code (backticks, never nests) (original S-16).
- **[S-1352]** The spans of a line concatenate to exactly the source line; an unpartnered delimiter is
  literal text. The editor formats spans at their offsets and never rewrites text (original S-14/S-18).
- **[S-1353]** Every intercepted key and command is a pure `(text, selection) → (text, selection)` in
  `NoteEditing`; the control only applies the smallest replacement and places the caret.
- **[S-1354]** Enter continues `- `, `- [ ] ` (unchecked) and `1. ` (numbered on) when the caret is in
  the item's content; on an item holding only its marker it clears the marker. Shift+Enter is a plain
  newline (original S-19).
- **[S-1355]** Tab / Shift+Tab indent / outdent every touched line by two spaces; a tab outdents as one
  unit (original S-20).
- **[S-1356]** Ctrl+B / Ctrl+I wrap in `**` / `*`, unwrap when already wrapped (inside or just outside
  the selection), or insert an empty pair with the caret between (original S-21).
- **[S-1357]** Paste is plain text only, line endings normalised to `\n`. Native drag-and-drop is kept
  (RichEdit moves text correctly); whatever formatting a drop brings is overwritten when the touched
  lines are redrawn (original S-23, adapted).
- **[S-1358]** Tasks: `- [ ] ` / `- [x] ` (`X` too); a done item is struck and dimmed; a click that
  leaves the caret on the box toggles it, length-preserving. The box is drawn in the accent colour
  rather than as an icon over the text — a `RichEditBox` cannot host one (original S-17, simplified).
- **[S-1359]** Toolbar commands: heading cycles H1 → H2 → H3 → body (level from the first touched
  line); bullet / numbered / task / quote apply to every touched line, replace any other block marker,
  and strip when every line already has them; buttons light for the marks at the caret (a checked
  task's strike does not light Strike) (original S-27).
- **[S-1360]** Undo/redo is the editor's own (`NoteHistory`: Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y, the menu):
  runs of single-character typing coalesce, Enter and pastes are steps of their own, no timers, 200
  steps. The control's own stack is off — it would also hold every formatting pass (original S-22).
- **[S-1361]** Only the lines an edit touched, plus lines whose parse changed (a fence opened or
  closed), are redrawn (`NoteText.LinesToRedraw`); replacements never split a surrogate pair.
- **[S-1362]** Status line: "N words" (runs holding a letter or digit, so markers are not words); at
  wide sizes also "N characters · Saved / Saving… / Not saved", driven by the pool's save events.
- **[S-1363]** Ladder (`NotesLayout.Plan`): the text always (at least two lines; headings at body size
  on a compact width) → formatting bar from Regular × Regular or on a Wide strip, buttons shed by rank
  as it narrows (bold, italic, task, bullet, heading, strike, code, numbered, quote) → status line at
  height Expanded (with detail at width Expanded) → note list beside the text at width Huge.
- **[S-1364]** The header title is the note's title (the user's own widget label still wins). A
  chevron in the header opens the note picker (every note, New note, Rename…, Delete note…), except at
  XS (it would push the title out) and XL (the list does its job).
- **[S-1365]** The bar's buttons never take focus, so a command runs on the note's selection. Bold,
  italic, strikethrough, bulleted list and checklist are Segoe Fluent glyphs checked against the font;
  heading and numbered list are lettered ("H", "1.") because the font has neither.
- **[S-1366]** The editor admits no formatting the markdown does not hold: rich-text accelerators off,
  every Ctrl chord other than clipboard, selection and caret keys swallowed (Ctrl+Alt passes: AltGr),
  no selection flyout, its own context menu (undo, redo, cut, copy, paste, select all), copy as plain
  text. Keys are not intercepted while an IME composition is open.
- **[S-1367]** XL note list: title and last-written age per row, the shown note lit, "New note" at the
  bottom; click opens, double-click / F2 / menu renames in place, the menu or Delete deletes after a
  confirm; Up/Down/Home/End/Page keys, Enter and Escape via `ListKeys`.
- **[S-1368]** The gallery uses a throwaway note store in the temp folder, fresh per page (plain page
  empty, `+fixture` four notes with a markdown sampler); it never touches the user's notes.

Not ported, deliberately: the pop-out window (original S-11..S-13, user decision); the selection
bubble (S-26: the bar and Ctrl+B/I cover it); header-mode placement of the bar (S-29: the card owns the
header and a view cannot know its mode, so the bar is a row the ladder sheds instead); the "On this
device" source row (S-35: there is no other source); a comfortable-measure cap at L (a 560-wide card
is about 80 characters already, and a cap would leave the empty band the ladder exists to avoid).

## Disk and ping

Package P2 of the system audit. Both stay `MeterPart`s on the shared `MeterView` ([S-1129]).

- **[S-1160]** Disk reports on `Drives` (roots `C:\`, chosen order, each once; empty = the system
  drive). The first is the reading; load repairs the list (drive letters only, deduped, at most 26;
  anything else dropped and logged). Changing the first drive clears the chart (original [S-22]).
  The settings page lists every drive with a switch (a removed drive stays listed as "not
  connected" so it can be switched off). `Drive` (single) is gone, no migration.
- **[S-1161]** The header title names the drive as Explorer does ("System (C:)", "Local Disk (D:)",
  "USB Drive (E:)"; "Disks" with several chosen). Space is the reading (% used), a used / free bar
  beside the chart (the bar alone with the history off), the "Space 412 GB free of 931 GB" detail
  line, and used / free / total as the stat tiles. Colours 80 / 90 ([S-1123]).
- **[S-1162]** Read and write rates are per drive from one shared PDH query
  (`\LogicalDisk(*)\Disk Read|Write Bytes/sec`, matched by letter ignoring case; `_Total` and folder
  mounts left out). A drive the counters do not list has no rate, never the system total (original
  themia-releases#49). The query is opened by the first disk widget that can show a rate and
  closed when the last lets go. Volumes and labels are re-walked at most every 30 s (original
  [S-21]); free space is read on every sample (microseconds, and the figure that should move while
  a large copy runs — the audit's "once a minute" was the old space-only cadence).
- **[S-1163]** **The chart is disk I/O, never space** (user requirement): reads (filled) and writes
  (line) against the peak of what is on screen, floor 1 KB/s; no space-over-time history exists. Its
  caption is the current rates "R 12 MB/s · W 0 B/s" (the meter caption, placed for its widest; with
  the history off the caption is the space instead). Whose I/O: `IoScope` = the reading's drive
  (default), all drives (sum of the letter instances, never `_Total` too) or one named drive
  (`IoDrive`); a detail line says which when it is not the reading's. Load repairs an unknown scope
  or a non-letter drive to the reading's, loudly. Changing what is charted clears the chart. No rate
  yet is a gap, not a 0. The history is on by default, as in the original.
- **[S-1164]** Detail line "IOPS R 120 · W 34 /s", system-wide from `\PhysicalDisk(_Total)\Disk
  Reads|Writes/sec` (`ShowIops`).
- **[S-1165]** Detail line "Volume System · NTFS · fixed" (`ShowVolume`).
- **[S-1166]** The drives list (where the tile has room, whole rows; a share bar per row on panels):
  the chosen drives, then with `ShowAllDrives` every other volume; one drive alone is no list. A
  row is the drive's name and "42% · 1.2 MB/s"; its tooltip has free / total and read / write.
  Clicking a row, or the card's "Open C: in Explorer", opens the drive through `ShellLauncher`.
  List rows hover only when they do something.
- **[S-1167]** Framework hooks for a kind (`MeterView.Part.cs`): a header title
  (`IMeterHost.SetTitle`), right-click commands (`MeterPart.ContextActions`), a header trailing
  element, and `IMeterHost.SampleNow`. A reading that carries a fraction is plotted even when it is
  not available (a missed ping); every other unavailable reading carries none.
- **[S-1168]** Disk settings: Drives, then Chart (same drive as the reading / all drives / each
  drive), Operations, Volume, All drives (all on), then the shared meter rows.
- **[S-1169]** Gallery samples (Debug only, preview tiles only): `Disk+fixture` is three made-up
  drives (System, a nearly full Data, a removable stick) with rising and falling rates;
  `Ping+fixture` is a labelled router answering in 3–40 ms that misses one probe in nine, with no
  packet sent. The plain pages read this machine and probe the default host.
- **[S-1170]** Ping status: Checking… until the first probe returns, then Online / Offline from the
  last probe. A dot at the right of the header carries it in the success / critical / tertiary
  colour; the caption says it in words (placed for "Checking…").
- **[S-1171]** Ping statistics over the probes the chart spans (`PingHistory`, span / interval):
  jitter = mean absolute difference of consecutive answers (gaps dropped; 0 after exactly one;
  hidden before any); loss = unanswered share (hidden before any probe; **100 % when none was ever
  answered** — the original's [S-10] defect, fixed); fastest / average / slowest over answers only,
  as the stat tiles (the view's own min / avg / max would count a miss as 0 ms). Detail lines:
  Jitter, Loss, Address (only beside a label), Probes "38 of 40 answered". A new cadence restarts
  them with the chart.
- **[S-1172]** The ping chart is latency against the window's slowest answer (floor 1 ms); an
  unanswered probe is a dimmed "--" that plots on the baseline. Online colours the chart success,
  offline critical. The bar (history off) keeps [S-110]'s 300 ms ceiling.
- **[S-1173]** `HostLabel` (≤ 64 characters, stored as typed, trimmed when shown): the header title is
  the label, else the host; the address becomes a detail line when a label is set.
- **[S-1174]** Ping "Refresh" on the card's menu probes now; a probe already out is the answer
  coming, so it is not doubled ([S-106]).
- **[S-1175]** Ping settings: Host, Name, then Jitter, Packet loss, Fastest / average / slowest (all
  on), then the shared meter rows.

Not ported / different: several ping hosts (the original had one); the original's per-drive stacked
blocks at every size (the first drive is the reading and the others are the drives list, which needs
a tile Expanded in height — the shared ladder has one reading); a last-answer age line (it would
re-lay the tile every second); keyboard navigation of the drives list (waits for the shell's
keyboard lease, WP11); a coloured latency number (S-432: pressure never colours the reading).

## Keyboard lease (WP11)

The desktop surface is `WS_EX_NOACTIVATE`; a widget that is typed into or navigated with keys gets
the keyboard through one lease. Rules in Core `KeyboardLease<T>` / `WidgetKeys` (tests
`KeyboardLeaseTests`), carried out by `AppHost.Keyboard.cs` and `DesktopWindow.Input.cs`.

- **[S-800]** One lease, app-wide, one holder. Outside edit mode a press anywhere in a card (handled
  presses too; the innermost card of a container) asks for it: a view implementing `IWidgetKeys`
  takes it, any other press ends the current holder's lease, on any display. The holder's surface
  drops `WS_EX_NOACTIVATE` and becomes the foreground window; the z-order controller keeps it on the
  desktop. If Windows refuses the foreground the lease is dropped at once (logged), so the surface is
  never left activatable for nothing. Gallery previews never take it.
- **[S-801]** Text boxes ride the same lease: `IWidgetSurface.RequestTextInput(true)` on focus grants
  it to the box's widget, `false` on blur ends it — except for a widget with keys, which keeps the
  arrows after its editor closes. Focus moving between two widgets' boxes hands over in either event
  order; a late blur from a previous holder changes nothing.
- **[S-802]** The lease ends on Escape (which also hands the keyboard back to the window that was
  foreground when the lease began, if the surface is still foreground), on a press on any other card,
  on the surface losing activation (a click on the desktop or another app, Alt+Tab — the "click
  outside" signal, no global mouse hook), when the holder's card leaves its surface, on entering edit
  mode and when the surface closes. Only Escape gives the keyboard back: after a click elsewhere the
  click already put it where the user wants, and a launched app must not be raced. Every end sends
  the old holder's view `WidgetKey.Escape`, the one place a view drops its cursor or selection, and
  moves XAML focus out of its card so no caret is left blinking.
- **[S-803]** Keys: arrows, Page Up/Down, Home/End, Tab / Shift+Tab, Enter, Space, Delete, F2
  (Rename), the Menu key / Shift+F10 (Menu), Escape. Ctrl and Alt chords are never widget keys;
  Shift+Delete, Shift+Space and Shift+F2 are not either. A view returns whether it used the key; an
  unused key carries on (Tab still moves focus).
- **[S-804]** While a text box on the surface has focus, every key but Escape is the box's. Escape
  reaches the lease only when the box did not handle it (a search box that clears on Escape keeps it).
- **[S-805]** The keyboard is never taken without the user asking: only a press in a card or a text
  box getting focus can grant the lease.
- **[S-806]** Calendar (`HandleKey`, [S-1269]): Escape hides the cursor day.
- **[S-807]** Contacts ([S-1277]): the surface-wide press hook is gone; a click outside ends the
  lease and its Escape clears the selection, which now also covers a click in another app.
- **[S-808]** Tasks ([S-787]) and **[S-809]** Habits ([S-1428]): keys arrive through `HandleKey`
  (the per-row `KeyDown` / `PreviewKeyDown` handlers are gone); `UpdateKeyboard` asks only for an
  open editor. The lease ending while an editor is open forgets the row; the box's blur commits it.
- **[S-810]** GitHub (repository, issues, reviews, notifications; `RowCursor`): a click on a row
  focuses it (XAML focus, system focus rect: no layout cost), Up/Down/Home/End move (clamped,
  `ItemCursor`), Enter opens the row's page, the Menu key opens its menu, Escape lets go; the focus
  follows its row's link across redraws and is dropped when the row leaves.
- **[S-811]** Gallery slideshow: Left / Right step, Space pauses (the original's S-29). Other
  layouts take no keys.
- **[S-812]** Tasks and Habits with the lease but no row focused: a movement key lands on an end
  (Up/Down/Home/Tab the first, End/Shift+Tab the last); Enter, Delete and F2 do nothing.

Not wired here (lanes still running; see the WP11 report): Mail, OneDrive (`ListKey` handlers),
Clipboard and Mood (`HandleKey(WidgetKey)` ready), Notes' list, CPU/Processes and Network drives.
Not live-verified: focus taking, Alt+Tab, deactivation timing, IME (see the report).

## Battery and network

Package SYS-P3. Both meters live in their own files (`Widgets/Meters/BatteryMeter.cs`,
`NetworkMeter.cs`, `WidgetSettings.Battery.cs` / `.Network.cs`); the rules are Core
(`BatteryRules`, `NetworkRules`, tests in `BatteryNetworkTests`).

- **[S-1180]** Battery status line, the original's rules in order: "Charging" (the `BatteryFlag`
  charging bit, not the plug); "Fully charged" on mains at ≥ 99 %; "{h}h {m}m remaining" from
  Windows' estimate (Time remaining on); "Plugged in"; "On battery". The no-battery decision is
  `PowerRules.Read`'s, so the meter and the power tile agree.
- **[S-1181]** Device figures come from the battery class driver's IOCTLs (`SetupDi` →
  `IOCTL_BATTERY_QUERY_TAG` / `_INFORMATION` / `_STATUS`), not WMI (not AOT-safe; the WMI classes
  wrap these IOCTLs). The structures are parsed from bytes, bounds-checked, with every sentinel
  (0xFFFFFFFF capacity/voltage, 0x80000000 rate, 0 cycles) as "not reported"; a temperature outside
  −40..120 °C is not reported. Several batteries sum (capacities, rate); the rest is the first
  battery's that reports it. Read at most every 2 s, the device list re-enumerated each minute.
- **[S-1182]** Health = full-charge ÷ design capacity (both > 0; may pass 100 %); time to full =
  (full − remaining) ÷ charge rate, time to empty = remaining ÷ drain, only in mWh terms and ≤ 48 h;
  rate in W; voltage in V.
- **[S-1183]** Battery detail lines, ladder order: Health ("91% · 51.9 of 57.0 Wh"), Full in /
  Empty in (own estimate only when Windows has none), Charging at / Draining at, Cycles, Change,
  Voltage, Temperature, Power (source), Power plan, Battery saver. A figure the battery does not
  report hides; Temperature (off by default) says "N/A" once turned on. Toggles: Time remaining,
  Health, Charge rate, Cycle count, Voltage, Temperature, Power source and plan — all on except
  Temperature. The plan is read only while a detail line can show it.
- **[S-1184]** On a tile with room for a list (Tall/L stacked, XL right panel) the battery lists its
  capacity: full charge (bar = share of design), remaining (bar = share of full), design,
  chemistry, maker, battery count.
- **[S-1185]** "Change" line: the charge's move over the chart's span ("−12% in 5m", "No change in
  2m"); hidden under a minute of history.
- **[S-1186]** The gallery's `Battery+fixture` is a discharging laptop battery reporting every figure
  (`Shell/Gallery/BatteryFixture.cs`, Debug only); a meter reads it only when its card is a preview
  (`MeterPart.Bind(preview)` via `IWidgetSurfaceAware`), never on the desktop.
- **[S-1187]** The battery reading leads with the Segoe Fluent battery glyph filled to the nearest
  tenth, with a bolt while charging, in the pressure colour (`MeterPart.Badge`).
- **[S-1190]** Network throughput is per adapter (`ThroughputTracker`): a counter that falls is that
  adapter's reset (reads 0 once, re-baselines) and no longer zeroes every other adapter's traffic;
  a new adapter starts from a baseline. Session totals (since Themia started) add positive deltas
  only. One shared reader; a second tile in the same tick reuses the sample.
- **[S-1191]** Download is the reading, "↑ upload" the caption, and the chart's two series (upload
  the thin line) share one top: the window's peak of both, floor 1 KB/s. The graph-off bar uses the
  same window (`RateWindow`), so it is no longer scaled to the session's all-time peak. The scale
  line's top counts the second series too.
- **[S-1192]** Counted adapters: up, not loopback/tunnel, not a packet-capture mirror (name or
  description contains "pcap packet driver"; the standalone Npcap Loopback Adapter is kept), and not
  an NDIS filter binding listed as its own adapter ("WiFi-QoS Packet Scheduler-0000": another
  adapter's name, "-", a filter, "-NNNN"), each of which re-reports its adapter's counters — on the
  dev machine seven of them multiplied the reading sevenfold, a bug the first port had too.
- **[S-1193]** Network detail lines, ladder order: the connection (the busiest adapter with a
  gateway: its name, then Wi-Fi network · signal · link speed — the SSID from `WlanQueryInterface`,
  absent without location permission), IP address (IPv4, else global IPv6), Public IP, Session
  (↓ ↑ totals), Peak (window's ↓ ↑). The list ("Adapters"): one row per counted adapter, busiest
  first, total rate (a Tall row has no room for ↓ ↑ and a name), bar = share of traffic; the ↓ ↑
  split, description, type, speed, address and session on hover.
  Adapters are described only when a line or row can show it.
- **[S-1194]** Public IP: opt-in; `https://api.ipify.org`, one request per 10 min, single-flight,
  shared, asked only while a tile shows its details; the body must parse as one IP address
  ("Checking…" until the first answer, "Unavailable" after a failure). The settings row names the
  service.
- **[S-1195]** Network toggles: Connection, IP address, Public IP address (off), Session totals,
  Adapters (ceiling 8; the tile shows what fits).
- Not ported / different: the original's per-metric order; its "top 3 active" interface cap (a
  ceiling of 8, idle adapters listed after busy ones); RX green / TX accent colours (the chart's
  two-series styling is the framework's: filled first series in the meter colour, thin second line).


## GPU and screen time: the whole tile (SYS-P4)

Code: `Widgets/GpuView.cs` + `GpuView.Panel.cs`, `Services/GpuSampler.cs`, Core `GpuEngines`, `GpuLayout`,
`GpuRules`; `Widgets/ScreenTimeView*.cs` (+ `.Footer.cs`), Core `ScreenTimeRules`; settings
`WidgetSettings.Gpu.cs`, `WidgetSettings.SystemWidgets.cs`. Tests `GpuEnginesTests`, `ScreenTimeRulesTests` (`S12xx_`).

- **[S-1200]** The GPU chart is the meters' `SparkBand` holding `GraphSpanSeconds / IntervalSeconds`
  samples (`GaugeConfig.GraphCapacity`, default 60 s at 2 s); the page ends with the shared history
  rows (show history, chart span, refresh). Supersedes the fixed 60-sample band of [S-430].
- **[S-1201]** Each adapter panel climbs one ladder (`Disclosure`): the load (always) → the history
  (min 24 px, 72 px on a large tile) → the busiest-engine caption (beside the number when it fits,
  else under it from Regular height) → Dedicated, Shared, Temperature, Power, Clocks, Fan, each only
  whole → the engine list and the process list (whole rows, at least two each) → the scale line
  ("last 60 s · 100%") on a tile large both ways. What is left goes to the number (up to Display
  when there is a chart) and then to the chart. Lines are Body from Expanded height, else Caption.
- **[S-1202]** Arrangement (`GpuLayout.Arrange`): a Wide strip (width ≥ Expanded, height < Expanded)
  puts the reading and its lines left and the chart right at full height; a tile Expanded both ways
  keeps reading, lines and chart on the left and puts Engines and Top processes down the right half;
  everything else is one column with the lists under the lines from Expanded height.
- **[S-1203]** Engines: `\GPU Engine(*)` instances map onto `GpuEngineKind` (3D/Graphics_N/High
  Priority 3D → 3D; Compute_N/Cuda → Compute; Copy; VideoDecode; VideoEncode; VideoProcessing;
  others such as Security are not drawn). A kind's load is its busiest engine summed across
  processes; a kind is listed (at 0%) once any instance of it exists. One row each with a share bar.
- **[S-1204]** On NVIDIA cards the driver's encoder and decoder figures replace (or join) the
  counters' Video encode / Video decode rows. This replaces the old "Enc / Dec" line.
- **[S-1205]** Top processes: per pid (from `pid_N_...`), the busiest engine it uses, busiest first,
  idle and pid 0 left out, at most 8 per adapter. Names from `ProcessNames` (description, else the
  file name, else "Process N"), resolved on the sampler's pool thread, cached per pid and dropped
  when the pid leaves the counters.
- **[S-1206]** Caption: the busiest engine kind ("3D"); on a tile large both ways the load to one
  decimal first ("12.4% · 3D"). Empty while every engine is idle (the precise figure alone on a
  large tile). Laid out by its widest form so it does not hop.
- **[S-1207]** Several adapters share the tile with no scrolling (`GpuLayout.Grid`): the column count
  whose panels come closest to 200 × 120 in their tighter direction (ties: fewer columns), so a
  strip puts two side by side and a column stacks them. Each panel runs its own ladder.
- **[S-1208]** `GpuConfig`: Temperature, Power, Fan and Clocks default **on** (each hides itself when
  the driver says nothing); `ShowEngines` and `ShowProcesses` (on) replace `ShowVideoEngines`.
- **[S-1210]** The ring's total takes the largest step that fits its hole; when even Caption cannot
  hold `4h 37m` it shows the compact form (`4.6h`, `13h` from ten hours, `37m`, `<1m`). Never clipped.
- **[S-1211]** `MaxApps` is a ceiling over what fits: 0 (the default) lists as many apps as the tile has
  whole rows for, 1–50 at most that many; an "Other" row joins when apps are left over. `ShowList`
  (on) switches the legend off. Supersedes the fixed default of 7 and "0 lists none" in [S-452].
- **[S-1212]** The drawing names the apps the legend lists: all of them when they fit (no "Other"),
  else all but the last row, which is "Other" — but never fewer than 7 unless `MaxApps` says fewer.
- **[S-1213]** On a tile Expanded both ways, with the drawing keeping at least 200 px, a footer
  appears from the bottom: the stats line, the daily limit as a bar ("Daily limit · 2h 10m of 4h",
  critical past the limit; a window judges its daily average), and for a day the week strip.
- **[S-1214]** Week strip (Day period, not style None): the seven days ending on the shown day, one
  bar each scaled to the busiest, the shown day in the accent, weekday under each; a click shows that
  day (today unpins) within the navigation's reach.
- **[S-1215]** Stats line: under a day "7-day average 1h 34m · busiest Tue 5h 0m"; under a window
  "busiest Tue 29 Sep 5h 0m · 9 apps". Hidden apps are left out of every figure.
- **[S-1216]** Screen time lays out from the card's `WidgetArea` (`IWidgetSizeAware`), not its own
  `SizeChanged`; the gallery still installs the in-memory fixture and never hooks or writes ([S-459]).
- Not ported / different: the original's GPU `visibleMetrics` order (fixed ladder order); per-engine
  history charts (one chart, the load).

## Feed, stocks and the web widgets: parity and the whole tile (OFF-E)

Ported from `widgets/online/{rss,stocks,video-stream,web-element}` on main, renumbered; the rules
live in Core (`FeedAggregate`, `FeedLayout`, `NetworkGuard`, `MarketSearch`, `MarketQuote`,
`ExchangeTime`, `StockLayout`, `NetworkCacheStore`), tests in `FeedWidgetTests`/`StockWidgetTests`.

- **[S-1300]** Feeds and quotes survive a restart (classic S-23/S-29): the last good answers are
  saved to `network_cache.json` beside the state (own file, never in an export; schema-versioned;
  atomic temp + rename; one writer; saved after each fetch that brings a new answer). At launch they
  are read back before the windows are built and marked restored: drawn at once, with their real age
  in the header in the caution colour ("Saved 3h ago; refreshing"), never treated as fresh, so a
  fetch follows at once. Entries older than 7 days, from the future, or not a usable feed/quote are
  dropped; feed links are re-checked; a corrupt file is set aside as `.corrupt`. Only the app wires
  it; the gallery never reads or writes the user's file. This reverses the earlier "in memory, so a
  cold start never shows old data as current" note: the age and colour say it is old.
- **[S-1301]** Network guard (classic `netguard::check_url`, S-08b): an address that comes from a
  user or a stranger and is then fetched by the app — every feed, every feed item's site icon, a
  pinned web page in the renderer and the picker — is refused when its host is, or resolves to
  (every address, not the first), loopback, private (10/8, 172.16/12, 192.168/16), link-local
  (incl. 169.254.169.254), CGNAT 100.64/10, 0/8, 240/4, a documentation range, ::1, ::, fe80::/10,
  fc00::/7 or 2001:db8::/32 (IPv4-mapped judged as IPv4). Feed requests follow redirects themselves
  and check every hop; https→http is refused. An unresolvable name is refused unless a proxy will
  resolve it (classic NG-07), but an IP literal is checked even then. One switch for the whole app,
  Settings › General › **Local network access** (`AppPreferences.AllowPrivateNetwork`, off), read on
  every check. A refused feed says so in the widget ("x is on the local network. Turn on local
  network access…"). The camera stream stays unguarded, as in the original; every other request,
  the fixed public services included, now connects through the guard ([S-960]), and the loopback
  hardware monitor is reached as "this machine only" ([S-967]).
- **[S-1302]** Rss reads several feeds (`RssConfig.Feeds`, up to 12, a list editor in settings, each
  address checked on commit), fetched in parallel through the shared cache and merged
  (`FeedAggregate.Merge`, classic S-30): newest first, undated last in arrival order, capped at
  `MaxItems` across all feeds. Repeated and blank addresses fold; refused ones are not fetched. A
  feed that fails while others answer is a partial refresh: "Name: reason" in the header tooltip and
  the caution colour (classic S-34/S-35); all failing shows every reason. Header title (S-04): the
  user's, else the single feed's `<title>`, else the feeds' names (title or host) joined with ", ",
  else the catalogue name. Item age (S-06): "Just now"/"5m ago"/"3h ago"/"6d ago", then "Mar 4"
  ("Aug 30, 2025" in another year). `FeedUrl` is gone (no compatibility).
- **[S-1303]** Rss **Refresh** (card menu and item menu, classic S-15): the network now, past the
  cache's freshness and the back-off. While any fetch is out, a small spinner sits beside the age in
  the header (classic S-33), so Refresh is never a button with no visible effect.
- **[S-1304]** Site icons (classic S-26/S-27, the Google s2 service replaced): fetched from the site
  itself, `scheme://host[:port]/favicon.ico` of the item's link — no third-party service. Guarded
  ([S-1301]), 64 KB cap, the bytes must be ICO/PNG/GIF/JPEG/BMP (not HTML, not SVG), one request per
  origin per session shared by every row and widget, a missing icon retried after 6 h; decoded at
  2× the 16px drawn size. A globe stands in until it arrives or when there is none.
- **[S-1305]** Rss ladder (`FeedLayout.Plan`, rows shown whole per [S-639], measured since rows are
  as tall as their headlines, the rest a scroll away with the shared fade [S-1281]): rung 0 the
  newest headline, two lines (one if even that does not fit); its age; its site icon from 180px of
  row width (inline at the start of the age line); the site's name on its own line from 240px; two
  columns from 400px wide (three from 720px on a short strip), read left to right; the summary where
  it costs no row, or where ≥3 rows still fit on a tile ≥280px high and not a tall column (1–2
  lines). Gallery: XS one headline; S headline + icon + age; M two rows with names; Wide two
  columns; Tall 5+ headlines; L two columns with summaries; XL the reading pane.
- **[S-1306]** From 720px wide and 128px high a reading pane sits beside the list (list 40%,
  240–360px; pane ≥240px): the selected item's icon and site, whole headline, date ("Sat 4 Oct
  2026, 14:30 · 2h ago"), whole summary (the parser now keeps 600 characters, was 300) and **Open
  article**. A click on a row selects (and focuses) it; a double-click opens it everywhere, as every
  list on this desktop. Item menu (`ItemMenu`): Open, Copy link, Refresh. Keys through the shell's
  keyboard lease (`IWidgetKeys`, [S-800]; a click in the widget takes it): Up/Down/Left/Right step, Home/End, Enter opens, Escape lets go;
  in the reading layout the cursor is the selection.
- **[S-1307]** Stocks symbol search in settings (classic `SymbolSearch.tsx`, S-38): an
  `AutoSuggestBox` asks Yahoo `v1/finance/search` and CoinGecko `search` together after a 300 ms
  typing pause (the previous search abandoned), keeps only chartable kinds (EQUITY, ETF, MUTUALFUND,
  INDEX, CURRENCY, FUTURE), at most 8 per service, alternating, 12 in all; each line says name,
  symbol, exchange and class. A pick sets symbol, market and name; typed text committed with Enter
  is a raw symbol under the current market, through the old check. The chosen symbol's class
  ("Index on SNP") shows under the box. A failing service contributes nothing (logged).
- **[S-1308]** Extended hours (classic S-34/S-35, `ShowExtendedHours`, off): listed symbols on the
  1-day and 5-day charts ask `includePrePost=true` (part of the cache key). The last pre-market /
  after-hours bar against the regular close is shown under the price ("After hours 412.10 −0.36% ·
  16:42", in its direction's colour): pre-market before the bell, after hours after it, the latest of
  the two while closed. The badge turns accent in an extended session with data; bars outside the
  regular session draw dashed at 55% opacity. Timestamps are kept paired with closes (nulls drop
  both halves; out-of-order series are sorted).
- **[S-1309]** Times on the chart are the exchange's (classic S-36): Yahoo's `gmtoffset` and
  `timezone` ("EDT") — not an IANA lookup, which needs ICU the app does not ship — "09:30" for 1 day,
  "Tue 15:00" for 5 days, "Mar 4" beyond, with the year from 2 years; local time when the reply has
  no offset. The badge and the time labels say which zone in their tooltip.
- **[S-1310]** Chart options: **High and low** markers (dot and price at the period's first highest
  and lowest bar, kept inside the chart); **Fill under the line** (`ShowAreaFill`, on — WinUI's
  look; the original defaulted off); **Rising/Falling colour** through the shared colour picker
  (`ColorPickerRow`, never hex; empty = the theme's green/red; a hand-edited non-colour is repaired
  to empty at load).
- **[S-1311]** Stocks ladder (`StockLayout.Plan`, replacing the 34%/120px chart band): rung 0 price +
  signed change (change under the price where it does not fit beside); currency and session glyph
  from 180px, the session's words from 300px; the extended line; the chart (≥24px, full-bleed) with
  start/end times from 128px high and the middle one from 400px wide, a price axis (high/mid/low) from
  400px wide and 120px of chart; facts (`MarketQuote.Facts`: previous close, day range, 52-week
  range, volume "18.7M", exchange — only what the reply carried): a stack under the chart on a tall
  column, a row of tiles on a large tile, a column beside the chart on the largest; range chips
  (1D…5Y, saving `Range`) on the largest. A strip (≥400px, 2:1) puts the readout beside a full-height
  chart. The chart takes what is left; the price grows only to Title / TitleLarge / Display by the
  tile's smaller class. Pointer over the chart on L/XL reads the bar under it (time · price). The
  session badge and age re-evaluate every minute on the shared ticker (render only), so the badge
  flips at the bell whatever the refresh. **Refresh** in the card menu. Header title: name, else the
  service's own name (listed), else the symbol. Gallery `Stocks+fixture` / `Rss+fixture` are fixed
  answers seeded into the caches (offline; only site icons are fetched).

Not ported, on purpose: RSS feeds as shared *connections* and the classic "Manage connections" page
(S-08/S-29 — feeds stay a widget setting, recorded earlier); per-widget `label`/`hideHeader` (card
title and header placement); demo mode (gallery fixtures); single-click open of a feed item
(double-click, recorded); the original's IANA time-zone names in labels (offset + short name
instead, see [S-1309]).

## Web widgets: size ladder, overlay, guard and saved renders

Round 3 parity and layout for the video stream and web element widgets (package OFF-E). Owning
files: `Widgets/VideoStreamView.cs` + `VideoStreamView.Overlay.cs`, `Widgets/WebElementView.cs`,
`Services/Web/StreamSession.cs` (`FrameFacts`), `Services/Web/PageSnapshot.cs`,
`Services/Web/NavigationGate.cs`, `Services/Web/ElementRenderCache.cs`,
`Shell/Config/ElementPickerWindow.cs`, `Shell/Config/WidgetSettings.Web.cs`,
`Shell/Gallery/WebElementFixture.cs`, `Themia.Core/Widgets/VideoStreamRules.cs`
(`StreamChrome`, `StreamChromeRules`, `FrameRateMeter`), `Themia.Core/Widgets/WebElementRules.cs`
(`ElementPlacement`, `ElementFitRules`), `Themia.Core/Storage/ElementRenderStore.cs`,
`Themia.Core/Model/Configs/WebWidgetConfigs.cs`. Tests: `StreamChromeTests`,
`ElementFitRulesTests`, `ElementRenderStoreTests`.

- **[S-1312]** Video stream chrome ladder (`StreamChromeRules.Chrome`, `IWidgetSizeAware`). The
  picture is the widget at every size; nothing else ever shows on XS, S or a Wide strip (height
  under Regular). From M (Regular × Regular, so Tall too), with the pointer on the picture, a
  bottom strip shows a state dot, the state and the frame's age. From L (Expanded × Expanded) the
  strip adds Pause/Resume, Rotate and Refresh (the context menu's three actions, same handlers) and
  the camera's resolution and measured frame rate. At XL (Huge × Huge) a persistent top-left chip
  shows the state and transport. Two settings: `HoverControls` (default **on**: invisible until
  hovered, and never on the small sizes) gates every hover part; `ShowStatus` (default **off**,
  because the original had no chrome) gates the chip. The view's own `SizeChanged` is gone: the
  decode size, the message's shedding and the chrome all follow the card's area.
- **[S-1313]** What the chrome says (`StreamChromeRules`): "LIVE" for an open stream, "every 10 s"
  in interval mode, "connecting" before the first frame, "reconnecting n" after the n-th failure in
  a row (reset by a frame), "paused"; frame age "now" / "4 s ago" / "2 min ago" / "3 h ago";
  resolution "1280×720" from the frame's own size before the decode scaled it (`FrameFacts`
  through `IStreamSink.OnFrame`; for MP4/HLS the player's natural video size); frame rate over the
  last 5 s of arrivals (`FrameRateMeter`, monotonic timestamps, "12 fps" / "2.5 fps"), shown only
  for an open MJPEG stream; transport MJPEG / Snapshot / Video / HLS. The age ticks through the
  shared ticker at 1 s only while the strip is on screen and the stream is live.
- **[S-1314]** Network guard on the web element, ported as the original had it (`check_url` on
  render and picker; [S-08b]). Every render first asks `NetGuard.CheckAsync`: a page on loopback,
  a private, link-local, CGNAT, reserved or documentation range is refused with its reason
  ("… is on the local network. Turn on local network access in Settings › General …") unless the
  app-wide "local network access" switch is on, and no browser starts. Inside the render every
  main-frame navigation (redirects, script navigations) goes through a `NavigationGate`: a host
  not yet cleared is cancelled, checked, and loaded again only if allowed (at most 5 hops); a
  refused hop fails the render with the reason. The picker does the same on `NavigationStarting`
  and says a refusal in its toolbar (caution colour) while the page stays where it was; a form
  post re-sent after the check becomes a plain load. Only http/https documents load in the main
  frame. Frames inside a page are not checked, as in the original. A refused render shows its
  reason in place of the picture (`SnapshotStatus.Refused`). The video stream stays unguarded, as
  the original (a LAN camera is its point).
- **[S-1315]** Web element settings: the pick button reads "Pick element…" with nothing pinned and
  "Pick again…" once something is; a Clear button beside "Pinned" empties the selector and label
  (was the original's trash button).
- **[S-1316]** Web element fit (`ElementFit` setting: **Whole** default, **Fill width**;
  `ElementFitRules.Place`). Whole scales the element to fit both ways, up to 3×; Fill width scales
  it to the width and cuts the bottom. Either way a scale under 0.45 (illegible) becomes 1:1 and
  shows the top-left part instead. A cropped picture is anchored top-left with a soft fade on each
  cut edge (24 DIPs, 12 copies of the one bitmap at falling opacity, so it works over any card fill, the
  wallpaper included); a whole one is centred. Replaces [S-462]'s "never scaled up".
- **[S-1317]** Captured at the size it is drawn (`ElementFitRules.CaptureZoom`): `PageSnapshot`
  takes the widget's box, fit and the display scale, and captures with the DevTools clip `scale`
  = the drawn scale clamped to 1..3 (× the display scale as before), never past 8192 px on a side.
  When the widget grows past what the picture was captured for (drawn scale > 1.25 × captured
  zoom, and the box larger than the last capture's box) a new render starts — from the card's size
  event or the end of a render, never a timer. The first render waits until the area is measured.
- **[S-1318]** Web element age: the header's trailing text is the render's age ("4m ago"), in the
  caution colour while the picture is a saved one not yet re-rendered, after a failed refresh, or
  past three intervals; its tooltip says which. At XL a footer line shows host · "Rendered 4m ago"
  · a caution "Last refresh failed" badge (tooltip: the reason) when the last refresh failed.
- **[S-1319]** Saved renders across restarts (`ElementRenderStore`, `ElementRenderCache`): the last
  good PNG per page + selector, with its CSS size, capture zoom and time, in an `element_renders`
  folder beside the app state (`index.json` + one `<hash>.png` each). Wired by the app only
  (`AppHost`); the gallery never reads or writes it. Index schema-versioned and validated on load
  (bad envelope → set aside as `.corrupt`, loud log, empty cache; bad entry → dropped; picture
  missing or not the recorded size/PNG → not drawn). File names are derived from the page and
  selector (SHA-256), never read from the index, so a hand-edited index cannot point outside the
  folder. Every write is temp + rename, picture before index, one writer chain. Bounded: 48
  pictures, 32 MB, 8 MB each, 30 days, newest kept, dropped files deleted. At launch the saved
  picture is shown at once with its age (stale, caution colour) and a render follows; a render
  that finds the element gone forgets the saved one. The gallery fixture is now a page served on
  loopback (`WebElementFixture`), so the populated render is offline and deterministic; installing
  it sets `NetGuard.AllowPrivate` to true for the rest of the (debug-only) gallery run.

Not ported, on purpose: a compact header for the video stream — the original's
`defaultHeaderMode: 'compact'` has no counterpart (this app has Top/Bottom/Hidden only; see the
S-10 note), so the camera keeps the Top default; camera credentials ([U-03]) — the original had
none, so basic-auth streams stay unsupported and an address with a user name or password is
refused (the settings note says so); a save-current-frame button at XL (an audit suggestion, not
the original's); frame rate for MP4/HLS (the player exposes no per-frame arrivals); the hover strip
in the gallery PNGs (the gallery cannot hover; the XL status chip is shown by the fixture instead).

## Appearance engine (WP1)

Every Appearance setting takes effect live, on every widget and, where it applies, on the settings
window. One apply path: `DesignApplier.Apply(design)` (app-wide: theme, accent, font, text size)
runs first on every state change (`AppHost` subscribes it before any surface), then each card's
`WidgetCard.ApplyDesign` (fill, border, header fill, radius, font scope). Nothing is rebuilt: no
card is re-created, no tick restarts. Pure rules live in Core (`DesignLook`, `DesignPresets`,
`FluentType.StepsAt`), tested in `DesignEngineTests`.

- **[S-820]** Accent override. Empty = the Windows accent (`UISettings` Accent, AccentDark1..3,
  AccentLight1..3). A colour of the user's own is walked into seven shades (HSL lightness, hue and
  saturation kept; `DesignLook.Shades`). The framework's accent brushes are found once, by colour,
  before anything is recoloured (every `AccentFill…`/`AccentTextFill…`/`SystemControl…Accent…`
  brush is one of the seven shades at some alpha) and recoloured in place, alpha kept; the
  `SystemAccentColor*` resources are set for later lookups. So template `{ThemeResource}` uses
  follow, in widgets and in the settings window.
- **[S-821]** Text size (`FontScale`, 0.5..2) scales the whole `FluentType` ramp (size and line
  height together), so `Fit`/`Hero` still pick real steps and a layout sheds rather than
  overflows. Live: before a card's view re-lays out on the state change, text already on the card
  whose size was set on the element is rescaled by the ratio; text built afterwards comes from the
  scaled ramp. Icons keep their size (widget scale, [S-850], scales everything).
- **[S-822]** Theme. "Use system setting" is resolved by the engine (Windows background colour) and
  every window root gets the explicit Light/Dark. `FluentLayout.Themed` hands out one engine-owned
  brush per key, coloured from the effective theme's dictionary and recoloured in place on a theme
  or accent change — a brush looked up from code used to be the app's start-up theme, so code-made
  text stayed white in a light card. A Windows theme or accent change (`ColorValuesChanged`) re-runs
  the apply path.
- **[S-823]** Widget background and opacity. A colour of the user's own replaces the Fluent base and
  its tint (exactly that colour); empty keeps the Fluent base + card tint. Opacity 0.2..1; a
  translucent widget is 60% of it (min 0.15), a transparent one has no fill.
- **[S-824]** Border: style None/Solid/Dashed (3-on-2 stroke widths)/Dotted (round dots two widths
  apart), width 0..8, colour (empty = the theme's card stroke), opacity 0..1. Drawn as a shape over
  the content and inset with the gap; none on a transparent widget.
- **[S-825]** Corner radius 0..32 on the fill, tint, border, content clip and edit outline.
- **[S-826]** Font: `system` (Segoe UI Variable), `sans` (Arial), `serif` (Georgia), `mono` (Cascadia
  Mono, Consolas), or any installed family name; a value that is not a plausible family name is the
  system font. Applied to the card content through a presenter, so text inherits it; icon glyphs
  name their own font and are untouched. Text boxes keep the control font.
- **[S-827]** The font picker lists every installed family (GDI `EnumFontFamiliesExW`, off the UI
  thread, once per process): vertical `@` faces dropped, duplicates folded, sorted.
- **[S-828]** Header background (empty = shares the widget's fill) and header opacity (0..1, applies to
  a header colour of its own); the strip reaches the card's edges and follows its corners.
- **[S-829]** Debug: the gallery renders with `THEMIA_GALLERY_DESIGN` (a `DesignState` as JSON, the
  state file's shape); its backdrop follows the theme. Opened from the app, it shows the app's own
  design and applies nothing.
- **[S-830]** Design presets (the original's six): Default, Midnight, Daylight, Glass, Terminal, Paper.
  Each owns 13 fields (theme, accent, font, widget fill + opacity, header fill + opacity, border
  style/width/colour/opacity, corner radius, widget gap) and never widget scale, text size or
  motion.
- **[S-831]** Default is read from a fresh `DesignState`, so a fresh install reads as Default.
- **[S-832]** Applying a preset writes its fields in one mutation and leaves every other field alone.
- **[S-833]** The picker shows the matching preset, or Custom; only owned fields are compared, colours
  case-insensitively, numbers within 0.001.
- **[S-834]** Appearance page: Style (presets + Custom), theme, accent, widget background + opacity,
  header background + opacity, corner radius, border group, font (presets + installed), text size,
  widget scale. Picking a preset rebuilds the page's rows in place; no other row rebuilds anything.

Not ported, on purpose: the original's preset preview tiles (a drop-down lists them, through the
shared `FluentLayout` row like every setting); the blur that tracked opacity (there is no blur layer
behind a card here); the eyedropper (optional in the audit). Motion settings are WP9's.

## Logging and developer mode (WP7)

Ported from the original's logging pipeline (`src-tauri/SPEC.md` [LOG-01..09]) and its Developer
section. Pure rules in Core (`Diagnostics/LogFileRules`, `LogPolicy`, `LogRedactor`, `LogFormat`,
`LogBuffer`), tested in `LoggingTests`; the sinks in `Services/LogFiles.cs` and
`Services/EventLogSink.cs`; the page in `Shell/Config/ConfigPages.Logs.cs`.

- **[S-980]** Bounded, rotating log folder: `%LOCALAPPDATA%\Themia\logs` (machine-local, not
  roaming; covered by the uninstall's data purge). The current file is `themia.log`; before a write
  would take it past 2 MB it is renamed to `themia-<UTC yyyyMMdd-HHmmss-fff>[-n].log` and a fresh
  one starts. An empty file is never rotated (one oversized record cannot chain empty archives).
  One writer: a lock around one open stream (shared for read and delete), flushed per record. A
  write that fails with an I/O error turns the file sink off for the session instead of throwing
  on every record. Only the real app writes files: the gallery and the settings render get the
  debugger and session-buffer sinks only, and a second copy that hands its request to the running
  one never opens the file.
- **[S-981]** Level policy: warnings and errors are always recorded; info and debug only with
  verbose logging on, or in a debug build. A record under the floor is dropped in `Log` before it
  is redacted or formatted. The original kept only errors by default; warnings are kept here too
  because they are rare in this app and are what explains most "it stopped updating" reports.
- **[S-982]** Verbose logging is a developer setting: it counts only while developer mode is on, it
  sits on the Logs page, and turning developer mode off also clears it. It takes effect on the next
  record, no restart (`AppHost.StateChanged` → `LogFiles.Apply`). Switching it writes one WARN line
  ("verbose logging on/off") so a reader of the file can see where the detail starts and stops.
- **[S-983]** Redaction, central: `Log` passes every message and every exception's full text
  through `LogRedactor` before any sink sees it, so no sink — file, event log, Logs page, clipboard
  — can carry a secret. Removed: `Bearer`/`Basic` credentials; secret-named parameters
  (`access_token`, `refresh_token`, `id_token`, `client_secret`, `password`, `api_key`, `cookie`,
  `authorization`, …) in `=`, `:` and JSON form; OAuth query parameters (`code`, `state`, `token`,
  `code_verifier`, `sig`, `key`, …); JWTs; GitHub/Anthropic/Slack/Google token prefixes; runs of 40+
  base64 characters mixing upper case, lower case and digits; URL user info. Email addresses keep
  their first character and domain (`j***@example.com`). Audit of the existing call sites: none
  logs a token or password directly; account ids (`imap:<user>@<host>`, `caldav:…`, `saved
  <account>`) and exception messages carrying addresses were the leaks, and the central pass covers
  them.
- **[S-984]** Record format: `yyyy-MM-dd HH:mm:ss.fff zzz LEVEL scope: message`, the exception
  (inner exceptions and stack) indented four spaces under it. Line breaks inside a message become
  " ⏎ " so a message cannot forge another record. A record is cut at 32 KiB.
- **[S-985]** Retention: at most 9 archives beside the current file (≤ 20 MB in all), and none older
  than 14 days. Swept at start (before the first write) and after every rotation. Only names
  `[S-980]` produces are ever deleted; anything else in the folder is left alone. Best-effort: a
  folder that cannot be listed or a file that cannot be deleted is skipped, never fatal.
- **[S-986]** Errors are mirrored into the Windows Application event log, source `Themia`, event
  id 1000 (`RegisterEventSourceW`/`ReportEventW`, handle held in a `SafeHandle` for the process).
  25 per session, then one notice that the rest are suppressed, then nothing. Text cut at 2,000
  characters without splitting a surrogate pair. The source is registered by the installer (HKLM,
  later package); without it Windows still files the event, wrapped in its "description cannot be
  found" preamble. A failure to register or report is silent: the caller is the logging pipeline.
- **[S-987]** Session buffer: the last 500 records of this session (the same floor as the file),
  in memory, what the Logs page lists. Older sessions are in the files.
- **[S-988]** Logs page list: newest first, filtered by minimum level (all / info+ / warnings and
  errors / errors only) and by source (all, or one scope seen this session), at most 200 rows drawn
  with "Newest 200 of N". Live while on screen (subscribes on Loaded, unsubscribes on Unloaded;
  bursts coalesce into one render; a record logged by its own render does not trigger another).
  Copy puts the rows shown on the clipboard in the file format; Clear empties the list, not the
  files. Empty states say whether only warnings and errors are being recorded.
- **[S-989]** Logs page rows: verbose logging; log files (count, size, folder, limits) with Open
  folder (creates the folder first, so Explorer is not sent elsewhere); a note on the event log
  mirror and on redaction.
- **[S-990]** Developer mode: a switch at the end of General › Developer. It adds the Logs page to
  the settings menu (footer, above About) and to search; turning it off removes the page, and the
  window leaves it if it was open (`AppPageEntry.Available`).

Not ported, on purpose: DevTools (no web view), demo mode and dev views (the widget gallery plays
that role), the per-window webview log forwarding gate and console-noise filter ([LOG-02],
[LOG-03]; no webview), the dependency DEBUG cap ([LOG-06]; .NET libraries do not log into `Log`).
Not done here, pending other lanes: the widget gallery in release behind developer mode (its entry
is in `DesktopWindow.Toolbar.cs` and its fixtures are debug-only), the Web element widget hidden
from the picker outside developer mode (`DesktopWindow.Picker.cs`), the developer-mode banner over
every settings page (`ConfigWindow.Chrome.cs`), and the original's pre-release gate on the Google
and GitHub sign-in providers (a launch decision, not a developer feature).

## Mixer and Volume (SYS-P5)

`Themia.App/Services/CoreAudio{,.Worker,.Native}.cs` (replaces `AudioSessions.cs` and the polled
`AudioEndpoint.cs`), `Themia.App/Widgets/{MixerView,MixerView.Row,MixerView.Panel,VolumeView,LevelRow,AudioFeed}.cs`,
`Themia.Core/Widgets/{AudioRules,MixerRules,VolumeRules}.cs`, tests `AudioRulesTests`.

- **[S-1220]** One Core Audio worker serves both widgets (the [S-440] MTA thread, now named
  `CoreAudio`). The volume tile no longer polls: it is a subscriber that does not ask for sessions,
  and moves on the endpoint's `IAudioEndpointVolumeCallback`. `VolumeConfig.IntervalSeconds` and its
  settings row are gone. Each subscriber states what it wants (`AudioWant`: device, sessions, meters,
  microphone); the worker binds exactly the devices wanted and releases the rest, and publishes one
  snapshot per subscriber per wake.
- **[S-1221]** `DeviceId` (+ `DeviceName`, remembered for display) on both configs; null follows
  Windows' default output (console role). A chosen device that is not connected is "*name* is not
  connected", never silently the default. The worker re-enumerates active render endpoints (friendly
  name from `PKEY_Device_FriendlyName`) on device added/removed/state/rename notifications. A blank
  id is repaired to null at load.
- **[S-1222]** Device picker: settings ComboBox ("Default output" + connected devices, enumerated off
  the UI thread); in the widget, the mixer's device line (from a Regular-height card with room) and
  the volume tile's caption on large tiles open a radio menu; the XL mixer panel lists the devices.
  Picking binds *the widget*; Windows' default device is not changed (that needs the undocumented
  `IPolicyConfig`, deliberately not used). Context action "Follow the default output" when a device is
  chosen. Inert in previews.
- **[S-1223]** Level meters via `IAudioMeterInformation` (endpoint, microphone, per session; a group
  shows its loudest session). The only sampled thing: at most 10 Hz, only while some widget has a
  meter on screen (loaded, ladder rung shown, `ShowPeaks`), silence sent once. Meters are a scale
  transform on a fixed bar (no layout per sample); a muted row's meter is empty.
- **[S-1224]** Microphone: the default capture endpoint's level and mute (`MicrophoneKey` write key),
  shown in the XL mixer panel and as the volume tile's row on large tiles (`ShowMicrophone`).
- **[S-1225]** Mixer keys (keyboard lease): Up/Down/Home/End move the row cursor (system focus rect on
  the row), Left/Right and +/− step the row 2 points, Page Up/Down 10, M or Space mute, Menu key opens
  the row menu, Escape lets go. With no row focused a row key only focuses the first row. `WidgetKey`
  gains `Plus` (VK_ADD, VK_OEM_PLUS), `Minus` (VK_SUBTRACT, VK_OEM_MINUS), `Mute` (M without Shift).
- **[S-1226]** Volume keys: arrows and +/− step 2 points, Page Up/Down 10, M or Space mute. Ignored when
  `AllowControl` is off.
- **[S-1227]** Mixer rows: compact (< 170 px: icon, percent, mute; 32 px), two lines (48 px), one line
  (≥ 360 px; 40 px) — fixed heights so the list takes whole rows (`Disclosure.TakeRows`); the rest
  scroll, never half a row. Supersedes [S-449]'s thresholds' wiring (values unchanged).
- **[S-1228]** Mixer extras by room: the device line (name, "N playing", chevron) always on an
  Expanded-height card, else only when every row still fits; apps holding a session but silent are
  drawn dimmed; with `OnlyActive`, the silent apps it hid follow the list dimmed when all playing
  rows fit.
- **[S-1229]** XL mixer (Huge width, Expanded+ height): a side panel (≤ 320 px) with the device name,
  its master level (the master row leaves the list), the microphone and every output device.
- **[S-1230]** Volume ladder: number + slider band at every size (a short tile keeps an 8 px foot so XS
  keeps its slider); meter from Regular×Regular (or along a wide strip); microphone row and the
  device switcher on large tiles (Expanded×Regular or Regular×Expanded); the rest goes to the number
  (≤ Display).
- **[S-1231]** Volume apps rung on large tiles with room: top 3 (6 at Huge height) apps on the
  device as read-only level bars, playing ones first in name order; silent ones in the quiet fill.
- **[S-1232]** The caption beside the number is the device's name (was "Output level"), still "Muted" /
  "Turned down" when those apply.
- **[S-1233]** Previews never write (sliders, buttons, keys and device picks are inert); the plain
  gallery page reads the machine, `+fixture` shows `AudioFixture` (3 devices, a microphone, 7 apps,
  meters) without touching Core Audio. Debug only.
- **[S-1234]** Stepping lands on the whole-percent grid and clamps (`AudioRules.Step`).

Not ported / not done: switching Windows' default output device (undocumented `IPolicyConfig`, per
the audit); app icons for fixture rows (fixture apps have no executables; the plain page shows real
icons). The original Volume had no peak meter, device name or microphone — these are audit additions.

## Widget polish 1 (POLISH-1)

Keyboard lease in the remaining lists, Accounts links, and four layout gaps.

- **[S-1560]** One key vocabulary: `ListKeys.Decide` takes the lease's `WidgetKey` (the separate
  `ListKey` enum and `RowFocus.Key(VirtualKey)` are gone). Keys a list has no use for (Left/Right,
  Tab, Space) decide `None` and go on to the surface; the Menu key (or Shift+F10) is
  `ListCommand.Menu`, the focused row's menu. Mail and OneDrive implement `IWidgetKeys` (no view
  `KeyDown`): Up/Down, Page Up/Down, Home/End, Enter opens, Delete deletes (Mail, where the account
  allows), Menu, Escape (also every lease end) clears the row focus.
- **[S-1561]** Clipboard takes the lease: Up/Down/PgUp/PgDn/Home/End move a cursor, Enter copies the
  entry back, Delete removes it from the history (cursor stays on the row that moved up), Menu opens
  its menu, Escape drops the cursor.
- **[S-1562]** Mood takes the lease: arrows/Home/End over the five faces, Enter logs the face.
- **[S-1563]** Notes: the XL note list answers the lease (Up/Down/PgUp/PgDn/Home/End, Enter opens,
  F2 renames, Delete deletes after asking, Menu, Escape). Only the editor and a rename box request
  the keyboard for typing; a click on a list row parks focus on the list host (outside the
  scroller, which would take the arrows) so the keys reach the list.
- **[S-1564]** `IWidgetKeys.WantsKeys` (default true): a view whose keys belong to a part shown only
  at some sizes says no while it is off screen, so the click does not take the keyboard. The card's
  `TakesKeys` honours it.
- **[S-1565]** `KeyedCursor`: a cursor over a list that re-sorts under it remembers its item (a
  pid), not its row; Up/Down clamp, Home/End jump, Escape clears, Left/Right/Enter/PgUp/PgDn are
  not its keys. An item that left the list marks no row.
- **[S-1566]** Processes: Up/Down/Home/End move a ring over the processes shown; it follows its
  process through re-rankings. Rows carry the inset ring; the column headings step in by as much.
- **[S-1567]** Meter breakdown lists (CPU and memory top processes, drives): same cursor; Enter does
  what a click on the row does (a drive opens in Explorer). Only a meter whose list is on screen
  takes the keyboard (`WantsKeys`).
- **[S-1568]** RSS: the Menu key opens the focused item's menu.
- **[S-1569]** Bluetooth detail panel: on a tile Regular+ wide and Expanded+ tall with one column of
  rows that leaves at least 128 DIPs (`BluetoothLayout.DetailMinHeight`), the height under the rows
  shows one device: icon and name large, kind and connection, battery (with a level bar, or
  "Not reported"), signal while its scan is on ("Not heard" when none), address
  (`BluetoothRules.AddressText`, "A4:C3:…"). The device is the last clicked row while it is listed,
  else the first (`BluetoothRules.Selected`); its row stays lit. Narrow columns stack the facts and
  let the name wrap to two lines.
- **[S-1570]** (superseded by S-1573) Mood Month on a short tile used to shed weeks of the month.
- **[S-1573]** Mood Month is whole or it is the recent weeks, never a partial month and never clipped.
  Invariant (`MoodLayout.Plan`, test `S1453_a_month_view_is_whole_or_...`): `plan.Recent == false`
  means every week of the month is drawn; `Recent == true` means the month was replaced by a
  rolling map of fewer weeks. Stacked, the month's floor (all its weeks at the smallest legible
  square, 9 DIPs) is reserved right after the faces, before the thought box and the footer; so on a
  short tile the box, the footer and the weekday labels (squares < 18) give way first, and squares
  shrink toward 9. If even the floor does not fit (XS, S: 4 to 6 weeks need 49 to 74 DIPs under
  the faces), the map shows the weeks that fit at 9 DIPs ending with this one (`MoodGrid.Rolling`,
  1 to month-weeks minus 1) and the header says so (`MoodLayout.Caption`): "This week" / "Last N
  weeks"; a whole month is captioned with its name. The caption is also the map's tooltip and
  accessible name, because a header too narrow for a label (XS, `HeaderFit`) draws none. The footer's
  average covers the weeks shown. Split tiles (width at Expanded) always hold the whole month
  (squares shrink to 9). `MoodGrid.Window` is gone.
- **[S-1571]** Habits one height class down (Regular, e.g. M): the height no whole row can use
  carries the summary's line ("3 of 5 done today · 71%") without its bar; the full strip with bar
  stays at Expanded height.
- **[S-1572]** Meters, Stacked at Expanded height with a list (CPU at Tall/L): the list's first 3
  rows are promised right after the chart's floor, before details, stats and the per-core strip;
  the list grows into what is left at its own rung. Trade-off: at Tall and L the per-core strip
  sheds (it is shown at M, Wide and XL); the original's L put cores and processes side by side,
  which the stacked arrangement cannot.

Settings: the "Accounts" link on the Developer (GitHub/Claude) and Tasks pages is a `NavRow`
(chevron, in-window), like OneDrive's.

## Shortcuts A: display and items (PC)

Code: `Widgets/ShortcutsView.cs` (lifecycle, ladder, widget menu) + `.Cells.cs` (tiles, rows,
icons, painting) + `.Input.cs` (selection, band, keys, item menu, rename, search),
`Shell/Config/WidgetSettings.Shortcuts.cs`, `Services/AppIcons.cs`, `Services/RecycleBinStatus.cs`,
Core `Model/Configs/ShortcutsConfig.cs`, `Widgets/LaunchTarget.cs`, `Widgets/ShortcutRules.cs`,
`Widgets/ShortcutLayout.cs`, `Widgets/ShortcutSelection.cs`. Tests: `ShortcutsTests` (S1380..S1393),
`LaunchTargetTests`, `LauncherPersistenceTests`. Ports the original `widgets/others/shortcut/SPEC.md`
except drop, cross-widget drag and app layouts (package PD).

- **[S-1380]** Config (original S-21..S-23, S-33, S-46..S-48, S-71): `View` Grid (default) / List /
  Details, `GridSize` and `ListSize` Small / Medium / Large, `GridStretchHeight`, `GridIconOnly`,
  `ShowFileExtension`, `ClickMode` (shared `ItemClickMode`, Double by default). Enums are written as
  names; `Repair` brings unknown values back, drops null items and mends blank or repeated ids.
  `ShowTarget` and `OpenOnSingleClick` are gone (the details view and `ClickMode` replace them).
- **[S-1381]** Display name: label, else host without `www.`, else the Windows location's name, else
  the file name without its extension unless `ShowFileExtension` (original S-05, S-18, S-48).
- **[S-1382]** Grid ladder (`ShortcutLayout.Grid`): tiles keep the chosen size and the columns share
  out the width; L and XL step the size up once; XL adds a caption line under the name (host, the
  folder it is in, or "Windows location"). Names go, then tiles shrink (down to a 16 px icon), only
  when keeping them would leave fewer than four items (or all of them) on screen: XS 3×2 icon-only,
  S icons, M 3 columns with names, Wide one row of five named tiles, Tall 2 columns. Stretch shares
  the height out when every row fits. A grid whose items all fit sits centred as a group.
- **[S-1383]** Real icons: a file, folder, executable or `shell:` location gets Explorer's icon
  through `IShellItemImageFactory` (icon only, never a thumbnail) at twice the drawn size, one
  extraction at a time (concurrent first requests return E_PENDING; one immediate re-ask, no wait),
  cached per path, size and variant; a web address gets the site's own `/favicon.ico` through the
  shared guarded `FaviconCache` (no third-party service). The kind glyph shows until the icon
  arrives and stays when there is none. List and details rows: 32 / 40 / 48 high (one step smaller
  on a short tile), the target as a caption line on L/XL, two columns on XL and on a Wide strip.
- **[S-1384]** Details view: name; target column from Wide up; kind column (Web address / Windows
  location / File / Folder / Missing / Not allowed) on XL; column header when it has the target
  column and room for two rows under it. XL (all views) adds a footer: "N shortcuts", "N of M shown"
  under a search, "N selected", "N missing".
- **[S-1385]** Search (original S-43): the header's search button (or the card menu) opens a box
  above the items; it narrows to items whose shown name or target holds the text, ignoring case.
  Escape closes it and clears the query; Enter opens the first match. Typing rides the keyboard lease.
- **[S-1386]** Broken targets (original S-44): a local path is checked on attach and every minute on
  the thread pool; a missing one is dimmed, struck through and its tooltip says "(target missing)".
  Web and shell items are never marked. A UNC path is never probed (no icon, no existence check):
  touching it connects to that server with the user's credentials, and the path came from a file
  anything can rewrite. It still opens when clicked. A refused target is dimmed with a warning glyph
  and its refusal in the tooltip, and cannot be opened.
- **[S-1387]** Item menu (original S-26): Open (disabled when refused) or "Open N" on a right-click
  inside a multi-selection; web: Copy URL; file: Open file location, Copy path; shell: Copy path;
  Move to start / up / down; Rename (off for several); Remove / Remove N (danger). Remove keeps one
  step of "Undo remove" on the card menu. Rename edits in place (Enter / losing focus commits,
  Escape cancels); a name equal to the derived one clears the label.
- **[S-1388]** Settings (original S-16, S-45, S-61): Display first, then Add (file / folder pickers
  and one switch per Windows location with its real icon; on adds it, off removes every item
  pointing there; the switches follow the list), the list editor last.
- **[S-1389]** Recycle Bin (original S-75): an item pointing at `shell:RecycleBinFolder` shows a
  count badge ("99+" cap; a dot under a 24 px icon), none when empty or unknown; tooltip
  "name — N items (size)" / "name — empty". `RecycleBinStatus` is process-wide, single-flight, reuses
  an answer younger than 5 s, is asked every 15 s on the shared ticker only by widgets holding the
  bin, keeps the last answer on failure, and the icon is fetched per state (empty / full).
- **[S-1390]** Selection (original S-40..S-42, S-62, S-63): click selects one, Ctrl toggles, Shift
  selects the range from the anchor (Ctrl+Shift adds it); right-click outside the selection moves it;
  a background press past 5 px draws a rubber band (accent fill at 20 %) that selects live, Ctrl adds
  to the selection; a background click clears it. Keys while the widget holds the lease (`IWidgetKeys`,
  [S-800]): arrows (Up/Down by a row of the grid), Home/End, PageUp/PageDown, Space toggles,
  Enter opens the selection, Delete removes it, F2 renames, the Menu key opens the item menu,
  Escape cancels rename, then closes search, then clears the selection. Ctrl+A is "Select all" on the
  card menu (the lease never maps Ctrl chords, [S-803]).
- **[S-1391]** Card menu (original S-27, S-49): Search, the two other views, Larger / Smaller tiles
  or rows, Icons only / Show names and Stretch (grid), Show / Hide file extensions, Open on single /
  double click, Select all, Undo remove.
- **[S-1392]** Items enter the widget's own surface through one method (`AddItems`); every
  target-specific choice (icon, menu, open, health) switches on `LaunchKind`. The seams PD builds on.
- **[S-1393]** A new Shortcuts widget starts with the Recycle Bin and This PC (original S-20); the
  "under the clock on a new screen" placement belongs to the default layout and is not ported here.

Not ported: Ctrl+Z op log (S-36: one-step "Undo remove" instead); "Change file…" from the widget
(the picker needs an owner window the desktop layer cannot lend; the target is edited in settings);
the dedicated "Add shortcut" window (S-66: the settings page carries the same affordances);
drop, drag-to-reorder, cross-widget drag and app layouts (S-28..S-38, S-50..S-74: package PD);
favicons are cached per run in memory, not on disk.

## Network safety (WP6)

Classic `widgets/netguard` (NG-01..07) and `widgets/netproxy` (PX-01..10), built on the guard of
[S-1301]. Owning files: `src/Themia.Core/Widgets/NetworkGuard.cs` (address classes, verdicts),
`src/Themia.Core/Net/ConnectRules.cs` (proxy hop, routes, CONNECT), `src/Themia.App/Services/Web/NetGuard.cs`
(resolve, judge, connect, the switch), `src/Themia.App/Services/Net/GuardedHttp.cs` (the one HTTP
client factory, connect callback, per-request check), `src/Themia.App/Services/Net/GuardedTcp.cs`
(raw TLS for IMAP, tunnel). Tests: `NetworkSafetyTests` (`S96x_`).

- **[S-960]** One guard, one connect path. Every HTTP client is built by `GuardedHttp.Create` — the
  shared `WebFetch` client (feeds, favicons, weather, quotes and symbol search, the photo of the day,
  the public address) and the `AccountHttp` client (sign-in, token refresh, Microsoft Graph incl.
  OneDrive, Google, GitHub incl. a user-typed server, Claude usage, CalDAV/CardDAV) — and every
  connection it opens goes through one `ConnectCallback`. IMAP (account check and every sync) opens
  through `GuardedTcp.OpenTlsAsync`. Both end in `NetGuard.OpenSocketAsync`. A refusal is a
  `NetRefusedException` carrying the sentence ("x is on the local network. Turn on local network
  access in Settings › General to reach it"): `FetchStatus.Refused` in a widget,
  `AccountError.Refused` for an account. Not guarded, as in the original: the camera stream (a LAN
  camera is its point). WebView2 (web element) cannot be connected for; it keeps the per-navigation
  `NetGuard.CheckAsync` of [S-1314].
- **[S-961]** Address classes (NG-01), one table for the whole app (`NetworkGuard.IsBlocked`):
  [S-1301]'s list plus site-local fec0::/10, and an IPv6 address carrying an IPv4 one — mapped
  ::ffff:a.b.c.d, IPv4-compatible ::a.b.c.d, NAT64 64:ff9b::/96 — is judged as that IPv4 address.
- **[S-962]** DNS-rebinding resistance: a direct connection resolves the name once, judges every
  address (one bad address refuses the connection), and connects only to the addresses it judged,
  so the checked address is the connected address — on every new connection, not when a feed or an
  account was added (the classic app's "re-validate the stored host before reconnecting"). A name
  that resolves to nothing fails as HostNotFound. An IP literal is judged as written.
- **[S-963]** Proxy as the original had it: Windows is the only source (`HttpClient.DefaultProxy`:
  manual box, automatic WPAD / setup script, the bypass list incl. `<local>`); a failure to read it
  is direct (PX-05). The proxy's own address is never judged (PX-07, proxies live on 10.x). The
  destination behind a proxy is checked with NG-07: a literal or a name that resolves here is
  judged, a name only the proxy can resolve is allowed. Checked per request (`GuardedHandler`,
  because a proxied plain-http request rides the proxy's pooled connection) and again in the connect
  callback for a tunnel. A connection to another endpoint than the request's own authority is the
  proxy's (`ConnectRules.IsProxyHop`).
- **[S-964]** IMAP through a proxy (PX-08): the service is put to Windows as `https://host:port/`; an
  HTTP proxy gets `CONNECT host:port HTTP/1.1` with Host and the app's user agent, the reply is read
  one byte at a time to the blank line (16 KB cap), and only a 2xx opens the tunnel. A 407 says the
  proxy requires authentication; another status is shown with the proxy's own status line; a SOCKS
  proxy is refused out loud rather than bypassed. Bounded by the IMAP job's deadline.
- **[S-965]** Account servers follow the switch. A user-typed CalDAV/CardDAV, IMAP or GitHub server
  on this computer or the local network (a NAS) is refused while **Local network access** is off and
  reached when it is on, as in the classic app (NG-04). This reverses the earlier "a NAS is always
  legitimate" exemption. The settings row says so.
- **[S-966]** Redirects: both clients follow them by hand, never the stack, so every hop is a new
  request through the guard. One rule for which hops to follow (`RedirectRules.MayFollow`, [S-519]):
  http(s) only, never https→http, never from a public host into local space; at most 5 hops.
  `RedirectRules.IsLocal` uses the guard's address classes (CGNAT, documentation and embedded IPv4
  now count as local).
- **[S-967]** "This machine only" (`NetReach.ThisMachine`): the loopback hardware monitor's
  `localhost:8085` is fetched by a second client from the same factory whose connections admit
  loopback addresses only, whatever the switch says, and are never proxied.
- **[S-968]** One user agent for every HTTP and CONNECT request (PX-10); TLS 1.2 or newer for HTTP
  and IMAP; timeouts and caps: web 8 s connect / 20 s request / 4 MB body, accounts 10 s connect /
  30 s per request / 16 MB body, IMAP 30 s per job, 1 MB line, literal cap; favicons 64 KB.
- **[S-969]** The switch is read on every connection (`NetGuard.AllowPrivate`, wired from
  `AppPreferences.AllowPrivateNetwork`), default off, so nothing reaches the LAN before the app has
  read its settings (NG-04). The gallery turns it on for its loopback fixtures only.

Not ported: proxy authentication (classic known limit; Windows stores no proxy credentials) and
proxy failover; the classic per-scheme+host proxy cache and its invalidation (PX-04) — .NET's
`DefaultProxy` owns caching and notices setting changes itself; the updater path (PX-09; no updater).

## Widget picker: search and adding into containers (WP5)

Search is `Core/Widgets/WidgetSearch.cs` + `WidgetKeywords.cs` (ported from `widgetSearch.ts`,
`searchKeywords.ts`, WidgetPicker.SPEC S-01..S-11; tests `WidgetSearchTests`, the original's cases);
the picker body is `Shell/WidgetPicker.cs`, opened from `DesktopWindow.Picker.cs`.

- **[S-940]** A query matches six weighted fields: label 1.0, curated keywords 0.9, the kind's name
  split on camel case 0.8, category title 0.55, account providers 0.55, description 0.45 (S-01).
- **[S-941]** Both sides are lower-cased, accents folded (`Grüße` ≡ `grusse`, ß → ss) and split on
  anything that is not a letter or digit; unsegmented scripts stay one token (S-02). Accents fold
  through a Latin table, not `string.Normalize`: the app runs with invariant globalization, where
  decomposition is a no-op and the accent would never come off.
- **[S-942]** A multi-word query is an AND: every word must land in some field (S-03).
- **[S-943]** Every kind has curated English synonyms, plus providers for account widgets (S-04);
  a test keeps the list complete as kinds are added.
- **[S-944]** Typo budget 0 / 1 / 2 edits for ≤3 / 4–6 / ≥7 characters, Damerau-Levenshtein, a
  mistyped prefix matches, short tokens must keep their first letter (S-07, S-08).
- **[S-945]** Descriptions match literally only, never fuzzily (S-11).
- **[S-946]** Results are best first, ties by label; a score of 0 drops the widget (S-05, S-10).
- **[S-947]** Exact > word > prefix > word prefix > substring > typo, plus a bonus when the whole
  query is a prefix of the label (S-09).
- **[S-948]** The picker opens on a search box above the category columns. Typing replaces the columns
  with one flat ranked list (name, description, category) in the columns' place and size, so the
  flyout does not jump; clearing the box brings the columns back (S-05). No match says so.
- **[S-949]** Keyboard: the box has focus when the picker opens, so typing searches; Up / Down move a
  highlight through what is shown (the columns in reading order, or the results); a search starts on
  the best match so Enter takes it; Enter picks; Escape closes. The highlight is drawn, not focused,
  so the caret stays in the box.
- **[S-950]** The Web element widget is listed only in developer mode (web-element S-33). Discovery
  only: existing web elements keep rendering, and copy, paste and duplicate still work.
- **[S-951]** A widget can be created directly inside a container: the target must exist on this
  display's view, be a container, and leave room under `WidgetTree.MaxDepth`. The rule is the same
  for every kind, containers included (the original allowed no nesting; this app allows it to the
  depth limit, as "Move into" does).
- **[S-952]** In a group it gets its default size (converted against this display), inset 3% from the
  area's edges below a 40 DIP allowance for the group's header, stepped 6% down and right per member
  already there (restarting after five) and always held inside the area (`areaChildRect`, S-36).
- **[S-953]** In a stack or a tab strip its stored rect is the container's own (where it returns to if
  taken out, S-33) and it goes last. One write with the parent already set, never add-then-reparent.
- **[S-955]** The picker is rebuilt on every open. When this display has containers that can take a
  widget, an "Add to" choice sits beside the search: Desktop, then each container by its title
  (duplicates numbered). A container that is selected when the picker opens is preselected.
- **[S-956]** Picking with Desktop starts click-to-place ([S-421]); picking with a container adds the
  widget at once and selects it. The settings window is not opened (the original opened it; [S-421]
  keeps adding and configuring as two decisions).
- **[S-957]** The widget gallery stays debug-only rather than "release behind developer mode": its
  sample data and PNG command line are debug-only and share its files, and a release gallery without
  them would show only empty states. Debug builds also render the open picker:
  `Themia.exe --picker-png <out.png> [--search text] [--into] [--dev]`.

Not ported: picker row previews (`WidgetRowPreview`) and the group-by choice (`pickerGroups.ts`) — the
columns are the grouping; the pro lock (licensing is out of scope). Pending other lanes: an "Add
widget inside…" entry on a container's own menu and empty state, which needs a hook in
`WidgetCard`'s menu or the container views (PA/WP1 files) to open this picker preset to that
container; drop-to-join, child reorder by drag, group header side and members in the clipboard
(B13/B14) live in `WidgetCard`/`DesktopWindow` drag code and the group files (PA).

## Desktop integration and suspend (WP3)

Game mode, Windows' desktop icons, the click clean-up a non-activating surface needs, and start with
Windows for both installs. Rules in `Themia.Core/Shell/{GameMode,DesktopIconRules,AutostartRules,
ShellClickRules}.cs` (tests `DesktopIntegrationTests`); Win32 in `Interop/{UserNotificationState,
FullscreenWatch,DesktopIcons,DesktopFolderView,ShellClicks}.cs`; host in `Services/AppHost.Suspend.cs`,
`Services/Autostart.cs`; surface in `Shell/DesktopWindow.Integration.cs`; rows in
`Shell/Config/ConfigPages.General.Desktop.cs`.

- **[S-920]** Game mode (General › "Pause widgets during full-screen apps", off by default, original
  `game_mode.rs`/`gameMode.ts`): the widgets are suspended while `SHQueryUserNotificationState`
  reports an exclusive Direct3D app, a full-screen window (`QUNS_BUSY`: borderless games, full-screen
  video) or presentation settings, and resumed when it no longer does. One pure decision
  (`GameMode.Decide(enabled, state, foreground, suspended)`) from a fresh query on every check. The
  row shows "Paused now" while it holds the widgets.
- **[S-921]** Switching it off resumes at once; off, no hook, window or check exists at all.
- **[S-922]** Never suspended while a window of this process has the foreground: an edit-mode surface
  spans its monitor and Windows calls that "busy".
- **[S-923]** Fail open: a failed query or an unknown state never suspends.
- **[S-924]** Hold, don't churn: Start/Search/taskbar in front, no foreground window, or the lock
  screen keep whatever is in effect, so opening Start over a game does not rebuild every widget.
- **[S-925]** Presentation settings suspend whatever is in front.
- **[S-926]** Otherwise only an application in front counts; the desktop in front (Show Desktop over
  a game) resumes.
- **[S-927]** Event-driven, no polling (the original polled every 2 s): a hidden top-level window
  hears the shell hook (`HSHELL_WINDOWACTIVATED`/`RUDEAPPACTIVATED`/`MONITORCHANGED`/`WINDOWDESTROYED`,
  which is how a window going full-screen in place is heard) and `WM_DISPLAYCHANGE`, plus an
  out-of-context `EVENT_SYSTEM_FOREGROUND` hook. Each event only queues one coalesced low-priority
  check. The first check runs synchronously before the first surface is built, so an autostart under
  a running game never flashes the widgets.
- **[S-928]** Suspending = leave edit mode, then every surface detaches all its widgets (each widget
  already stops its timers, samplers, feeds and watchers on detach, and the shared services go idle
  with their last subscriber), leaves the z-order controller and hides; the controller's backstop
  timer stops and the shared ticker pauses. A surface the display pass builds while suspended starts
  suspended. Screen time keeps recording on purpose ([S-459]: the game is what it counts).
- **[S-929]** Resuming re-tracks and shows each surface, rebuilds its widgets from the state (each
  fires at once, so nothing shows pre-game data), unpauses the ticker and requests a display pass for
  any mode or monitor change the game made.
- **[S-930]** "Show desktop icons" (original DI-01..05): posted as the shell's own command
  (`WM_COMMAND 0x7402` to `SHELLDLL_DefView`), so Explorer persists it and the desktop menu's check
  mark agrees; Themia never re-asserts or restores it (it survives quitting Themia and reboots). The
  shell windows are looked up on every use (Progman, or a WorkerW in between), never cached.
- **[S-931]** The command is a toggle: posted only when the live state (`IsWindowVisible` of the icon
  list) is known and differs; an unreadable shell posts nothing.
- **[S-932]** Windows owns the state; `Preferences.DesktopIconsVisible` is a first-paint cache. The
  switch adopts the live state on every visit and, while on screen, on the icon list's own
  `EVENT_OBJECT_SHOW/HIDE` (a WinEvent hook scoped to that one Explorer thread) — event-driven,
  replacing the original's 1 s poll. Adopting writes only on a known disagreement.
- **[S-933]** Start with Windows, picked by package identity: unpackaged → `HKCU\…\Run\Themia`;
  packaged (MSIX) → the manifest's startup task, id `ThemiaStartup` (`Package.appxmanifest` declares
  `desktop:StartupTask TaskId="ThemiaStartup" Enabled="false"`). One switch, same states and advice.
- **[S-934]** The Run value is `"<exe>" --autostart`; a startup-task activation counts the same.
  `Autostart.LaunchSource` tells a sign-in launch from a manual one (read once in `App.OnLaunched`).
- **[S-935]** A change that did not take says what to do (original AS-04/05): an inline note under
  the row (Windows switched it off → Settings › Apps › Startup; policy; access denied; other), with the
  raw error in small print under the advice, never alone. The switch stays clickable and shows what
  was reached.
- **[S-936]** "On" means it will actually run (original AS-07): the Run value must launch this
  executable (a stale path is off) and Windows' own switch (`Explorer\StartupApproved\Run`, low bit
  of the first byte = disabled) must allow it. Turning it on from Themia removes that record, the
  user's newest choice winning. Read fresh on every visit; `Design.RunAtStartup` is a first-paint cache.
- **[S-937]** Packaged states map from `StartupTaskState`; "disabled by user" can only be undone in
  Windows (advice says where), policy states are reported as such, unknown values read as unknown.
- **[S-938]** Click clean-up (original `dismiss_start_menu_if_open`, `dismiss_context_menu_if_open`,
  `clear_desktop_selection`): every press on a surface (handled or not), outside edit mode, sends one
  Escape when Start/Search (a `CoreWindow` of StartMenuExperienceHost/SearchHost/SearchApp) has the
  foreground or a shell context menu (`#32768`, or the Windows 11 menu's popup owned by
  explorer.exe) is open; in edit mode the press activates the surface and Windows closes them itself.
  Every press also clears the desktop's icon selection, off the UI thread and single-flight: a
  bounded `LVM_GETSELECTEDCOUNT` first, then — only if something is selected — the desktop's
  `IShellView::SelectItem(null, SVSI_DESELECTOTHERS)` through `IShellWindows` (not the original's
  `WriteProcessMemory` into Explorer, which security software flags as injection).

Not ported: the original's corner "paused" pill (the surfaces are hidden while suspended; the
General row shows "Paused now"); the Scheduled-Task autostart and its probe (AS-01/06: by decision
the unpackaged build keeps the Run entry, the packaged build uses its startup task); the pre-0.15
desktop-icon repair (DI-06, no backwards compatibility); suspending on session lock (not game mode).

## Settings navigation icons (NAV-ICONS)

The settings window's navigation pane draws its entries with full-colour icons, the way Windows 11
Settings does (System, Bluetooth, Personalization, Accounts...). Code: `Shell/Config/NavIcons.cs`,
`ConfigWindow.Navigation.cs`, the `Icon` column of `ConfigPages.AppPages`; tests `NavIconAssetTests`.

- **[S-1600]** The icons are Microsoft's Fluent UI System Icons "Color" set (the family Windows uses),
  bundled unmodified as SVG in `src/Themia.App/Assets/NavIcons/` (content files, no package). One
  per entry, echoing the Windows Settings equivalent where there is one: General = gear (`settings`),
  Appearance = Personalization's brush (`paint_brush`), Layout = tiles (`apps`), Accounts =
  Accounts' person (`person`), Logs = `document_text`, About = `question_circle` (the set has no
  info icon), every view = `content_view`, every display = System's screen (`laptop`; the set has
  no desktop monitor). A new app page names its icon in its `AppPages` line.
- **[S-1601]** Each icon is drawn by `SvgImageSource` in the pane's 16-DIP icon box, rasterised at
  exactly the pixels the box covers on the window's display (16 px at 100 %, 32 px at 200 %) and
  re-rasterised when the window moves to a display with another scale. The 16-DIP design of each
  icon is bundled. Colours are the icon's own in light and dark, as in Windows Settings.
- **[S-1602]** Only the pane's pages, views and displays change. A widget nested under its view keeps
  its monochrome kind glyph (the one its header and the picker show); settings-card row icons and
  "Find a setting" suggestions stay monochrome glyphs, as in Windows Settings.
- **[S-1603]** The MIT notice for the icons is in `THIRD-PARTY-NOTICES.md` at the repository root,
  listing every bundled file, and ships beside `Themia.exe` in every build.
- **[S-1604]** A bundled icon uses only shapes, groups and gradients (what Direct2D's SVG renderer
  draws) and the 16-DIP view box; a test rejects one with filters, masks or other features that
  would silently drop out.

Not done: a view's own icon ([S-703], a monochrome Segoe glyph) is not used in the pane, so views
share one colour icon there; the view bar and switchers keep showing it.
