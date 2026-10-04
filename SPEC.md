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
- **[S-13]** The per-widget settings dialog edits a **clone** and writes back only on confirm, so
  Cancel really cancels. It does not mirror the live config into local state and sync it back.
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
- **[S-83]** Cadence follows what the number actually does: CPU and memory once a second, disk and
  battery once a minute. Battery draws no history - at a one-minute cadence a flat line reads as a
  broken graph rather than a stable charge.
- **[S-84]** Byte sizes use **binary** units, because that is what Windows reports; a widget
  disagreeing with File Explorer about the size of the same drive reads as a bug. A percentage never
  rounds a non-zero load down to 0% nor an incomplete one up to 100%, because both read as "nothing
  is happening" when something is.

## The hearth hub

- **[S-90]** The mail widget holds **no account, token, provider or sync code**. The hearth hub owns
  all of it; this widget asks for the `email` scope, lists what it is granted, and refreshes when the
  hub says something changed.
- **[S-91]** Every scope asked for is a **reading** scope; `email.send` is never among them, so the
  worst the app can do with a granted mailbox is show it.
- **[S-98]** One handshake asks for `email`, `contact`, `event` and `task` together, and `subscribe`
  listens to the same four. A scope requested after the fact is a second consent prompt for the same
  process, which is the thing [S-92] exists to prevent.
- **[S-99]** The handshake keeps **every** collection the hub grants, of every type. A connection
  that filtered to mail at `hello` would leave nothing to draw a calendar from and no second
  handshake to ask again with; the mail list is a filter over the whole set, not a separate fetch.
- **[S-92]** There is **one** connection for the whole app, shared by every widget that reads from
  hearth. Hearth identifies an app by its process and grants consent to that process, so a second
  connection would mean a second consent prompt for the same app.
- **[S-93]** The wire types are declared in Themia with a **source-generated** JSON context rather
  than by referencing `Hearth.Client`. The SDK serialises reflectively
  (`JsonSerializer.Serialize(value, value.GetType(), ...)`), which is neither trim- nor AOT-safe, so
  referencing it would fail this build or throw once trimmed. Requests stay exactly on schema because
  hearth rejects unknown members inbound; replies may carry more than is declared.
- **[S-94]** The server process is checked (`GetNamedPipeServerProcessId`) before `hello` is sent.
  The hub is what draws the consent prompt, so consenting to an impostor would mean consenting to
  nothing.
- **[S-95]** `hello` waits at **human speed** (5 minutes), not the 30 seconds an ordinary call gets:
  the hub does not answer until the user has answered its consent prompt, and a prompt sitting on
  screen is not a hung hub.
- **[S-96]** The hub is **never started** by this widget. Launching another application's executable
  is not a widget's business, so an absent hub is an empty state that says so.
- **[S-97]** A change event carries no data - it is a nudge to re-query, so a missed one costs
  nothing. The empty state names which of "not running", "not granted" and "nothing to show" is
  true, because each needs a different action from the user.

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

## Calendar, tasks and contacts, via the hearth hub

- **[S-180]** `HearthDataService` owns no pipe. It is constructed over the one connection and exposed
  as `AppServices.Data` beside `AppServices.Hub` — two views of one object, never two connections.
- **[S-181]** Every hearth widget names which of "hearth not running", "access not granted", "no
  collections granted", "loading", "nothing to show" and "error" is true. A blank tile with no
  explanation is a defect, because each of those needs a different action from the user.
- **[S-182]** Hearth widgets draw Fluent rows at a fixed height and re-lay what is already drawn
  on a resize rather than re-querying the hub. What varies with the tile is how many rows fit and
  whether they are the standard or the compact height — never the type size.
- **[S-183]** A widget re-queries only when a field that changes the *request* changed;
  presentation-only settings — avatar, detail line, location, due date, all-day — re-render the
  records already in hand. The ticker subscription is remade only when the cadence actually changed.
- **[S-184]** A config change arriving during an in-flight query is remembered, not dropped: the
  widget re-queries once the reply lands, so the list can never settle on the collection that was
  selected a moment ago.
- **[S-185]** Cancelled occurrences are never listed — a cancelled event is a record like any other
  and would otherwise read as a meeting that is happening. An event that began before the window and
  has not ended is filed under today, because "on now" is what a glanceable calendar must not lose,
  and a heading that has already gone by is where nobody looks.
- **[S-186]** A completed task is struck through, and an overdue one says "Overdue" **in words** as
  well as in the critical colour: neither state may depend on colour alone. "Today" and "overdue" are
  decided once per render from one clock reading, so a list drawn across midnight cannot disagree
  with itself. These rules live in `Themia.Core` so they can be tested against a fixed clock rather
  than against whatever today happens to be.
- **[S-187]** `InvariantGlobalization` leaves no IANA zone table, so an event carrying an
  unresolvable zone is rendered at the wall-clock time the provider gave, on the local clock: right
  for an event in the user's own zone, **wrong by exactly the zone difference for one abroad**, and
  never an invented offset. It cannot throw on a render path (`GetUtcOffset`, not
  `ConvertTimeToUtc`, so an event inside a DST gap still draws a row). Times are shown on a 24-hour
  clock for the same reason: there is no culture to read a 12-hour preference from. **Known
  limitation, not a fix**: making it visible per row would need the shared time helper to report
  whether the zone resolved.

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
