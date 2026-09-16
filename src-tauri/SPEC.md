# Display Watcher & Work-Area Pinning: SPEC

Scope: the Rust backend's overlay-window geometry pipeline in `src/lib.rs`,
covering how each `monitor-{i}` overlay gets (and keeps) its bounds. Z-order behavior
is documented inline at `reconcile_overlay_zorder` / `ensure_widget_zorder_tick`
and is NOT covered here. Frontend behaviors live in `../src/SPEC.md` ([S-NN]).
The logging pipeline ([LOG-NN]) is specced at the bottom of this file.

Tags are `[DW-NN]`; tests in `lib.rs` mirror them as `dwNN_*`.

- **[DW-01] Exact work-area bounds.** Every overlay window covers its
  monitor's **work area** (`rcWork`, full monitor minus taskbar/appbars) in
  physical pixels, exactly. The overlay must never overlap the taskbar band.
  Single writer: `pin_to_desktop` via raw `SetWindowPos`; the Tauri builder
  intentionally sets no position/size.

- **[DW-02] Debounced re-pin pass.** `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`
  (includes `SPI_SETWORKAREA`) and the shell's `TaskbarCreated` broadcast all
  request one debounced pass (`request_display_pass` → quiet period →
  `on_display_change`), which re-enumerates monitors and re-pins / builds /
  closes every overlay. This is the ONLY positioning path: every healing
  mechanism funnels into it; nothing else sizes overlay windows.

- **[DW-03] Monotonic debounce, never wall clock.** The debounce state is an
  event **sequence counter** (`DISPLAY_CHANGE_SEQ` / `DISPLAY_HANDLED_SEQ`).
  Wall-clock timestamps are forbidden here: a backwards clock step (NTP)
  once stamped fresh events before the last-handled time, so every later
  display change was dropped and a taskbar-overlapping overlay persisted
  until app restart. Any seq≠handled state, in either direction, fires a
  pass.

- **[DW-04] Work-area self-heal sweep.** A ~1s background sweep (divided off
  the 80ms reconciliation tick) compares each overlay's `GetWindowRect`
  against the live `rcWork`. Divergence on **two consecutive sweeps**
  requests the same [DW-02] pass (never repositions directly). The sweep
  stands down while polling is paused (fullscreen) and while a pass is
  already pending, and never acts on an empty monitor topology. This is the
  backstop for every missed/re-ordered broadcast (login races, RDP,
  dock/undock, stale rcWork at debounce time).

- **[DW-05] Watcher starts before the first build.** `start_display_watcher`
  runs before the first `build_widget_window` in setup, so taskbar/work-area
  broadcasts arriving during or right after startup (autostart-at-login
  before explorer docks the taskbar) are recorded, not missed.

- **[DW-06] Dead or missing overlays self-rebuild.** A `monitor-{i}` window
  whose HWND is gone (webview crash / teardown race), or a missing overlay
  for an enabled monitor, counts as divergence: the sweep requests a pass and
  `on_display_change` destroys the husk and rebuilds the window.

- **[DW-07] Empty enumeration builds NOTHING — no phantom fallback (#70).**
  When `enumerate_monitors()` returns empty, the startup pass logs an error and
  builds no overlay at all, then defers to [DW-02]/[DW-04]. It must never
  invent one: the pre-fix code built an unconditional 1920x1080 window at
  `(0,0)` (the primary's origin) under a fake `"FALLBACK"` device name and
  never consulted the disabled list — a fail-open onto the primary that also
  poisoned the frontend's per-monitor maps with a phantom key. It was reachable
  in practice: the autostart LogonTrigger has no delay ([AS-01]/[AS-02]), so the
  first enumeration lands at the least-settled moment of the boot. Recovery is
  event-driven, never a wall-clock retry loop: `WM_DISPLAYCHANGE` fires a pass
  once displays appear, and the [DW-04] sweep independently sees "enabled
  monitor with no overlay" as divergence ([DW-06]) and requests one. The
  watcher is already running by then ([DW-05]). Startup and every later pass
  share ONE decision function (`plan_reconcile`), so an empty topology
  trivially yields an empty plan.

## Manual / live verification (not reachable by CI)

1. Autostart at login: overlay must not cover the taskbar once the desktop
   settles (no restart needed).
2. Move taskbar edge / toggle auto-hide / change resolution: overlay re-fits
   within ~2s.
3. Lock → dock/undock monitor → unlock: overlay re-fits without restart.
4. [DW-07] Autostart at login on a machine whose panels wake slowly (or with a
   monitor powered off at logon): overlays must appear on the right displays
   once the topology settles, with no window on the primary in the meantime.

# Monitor identity & per-monitor disable: SPEC

Scope: `MonitorInfo` / `enumerate_monitors` / `stable_monitor_id`, the
`get_monitor_id` + `get_active_monitors` commands, and the
`disabled_monitors.json` persistence (`read_/write_/migrate_disabled_monitors*`,
`plan_reconcile`, `sync_disabled_monitors`) in `src/lib.rs`. Tags are
`[MI-NN]`; the #70 regression tests mirror the issue tag as `s70_*`.

- **[MI-01] `\\.\DISPLAYn` is an ordinal, NOT an identity.** Windows re-derives
  the GDI device name on every boot from the order the panels come up in
  (DisplayPort link-training order, a panel asleep at logon, GPU driver init
  order), so the same string designates a DIFFERENT physical panel from one boot
  to the next. This was issue #70: widgets pinned to display 3 reappeared on
  display 2 after every reboot — the saved bytes survived perfectly, their
  meaning did not. The persistence key is `MonitorInfo::id`, the monitor's
  device *interface* path
  (`\\?\DISPLAY#DEL4098#5&1a2b3c&0&UID4353#{e6f07b5f-…}`), which carries the
  panel's hardware ids and survives reboots. `device_name` stays in the model
  for exactly two uses, both correct only because they re-resolve from the live
  topology every time: the human "Display N" label, and the
  `EnumDisplayDevicesW` / `tauri::Monitor::name()` lookups. It must never again
  be written to disk as a key, in Rust or in frontend storage.
  That id also has to survive every channel it is carried on, not just disk. It
  is full of `#` and `&`, so `config_window_url` percent-encodes the config
  window's `target` query value: concatenated raw, the first `#` started the URL
  fragment and the Display page opened on the truncated key `\\?\DISPLAY`,
  reading and writing a phantom monitor's settings while the display it was
  opened for never changed. (The already-open path carries the target as a
  `config-navigate` event payload, so only a freshly-built window was hit.)

- **[MI-02] Identity resolution degrades, never panics.** `id` comes from
  `EnumDisplayDevicesW(<szDevice>, 0, &dd, EDD_GET_DEVICE_INTERFACE_NAME)`
  taking `dd.DeviceID`; `dd.cb` MUST be set first or the call is rejected, and
  the fixed UTF-16 buffer is trimmed at the first NUL (the units past it are
  undefined padding). A failed call or an empty `DeviceID` (indirect/virtual/RDP
  adapters) falls back to the ordinal with a WARN — a usable-if-unstable key,
  never an empty one, which would collide across every such monitor and merge
  their settings. The lookup runs OUTSIDE the `EnumDisplayMonitors` FFI callback:
  an `extern "system"` callback must never unwind into Windows.

- **[MI-03] `disabled_monitors.json` holds stable ids; legacy files are re-keyed
  once.** The file is `{"schemaVersion":2,"disabled":[<stable id>,…]}`; the stamp
  is what makes migration run-once and idempotent. A pre-#70 file (schemaVersion
  1: a bare JSON array of ordinals) is re-keyed through the live
  `device_name → id` map on the first enumeration that can resolve it (startup,
  or `on_display_change_impl` if startup enumerated nothing, [DW-07]). Rules:
  * **Empty enumeration ⇒ do not migrate, do not touch the file.** Nothing
    resolves, so a rewrite would destroy the config. Reachable, not theoretical
    ([AS-01] LogonTrigger has no delay).
  * Entries resolving via the map → replaced by the stable id.
  * Entries that are not ordinal-shaped (already stable ids) → passed through
    untouched, **including for panels unplugged right now**: an id still names
    that exact panel, so its state is worth keeping while it is away.
  * Ordinals resolving to nothing → **DROPPED**, with a loud WARN naming them.
    Deliberate: a bare ordinal cannot be resolved back to a panel once that
    panel is gone, and keeping it is not neutral — it IS the bug, it would go on
    disabling whichever unrelated panel inherits that ordinal next boot. Cost:
    a monitor disconnected at migration time forgets its disabled state once.
  * A file that is **corrupt** is left byte-for-byte alone (read as "nothing
    disabled", never rewritten); "absent" (first run) and "corrupt" are distinct
    states, never one `unwrap_or_default()`.
  * Writes are atomic (temp file + `sync_all` + rename) and single-writer
    (`DISABLED_MONITORS_LOCK`): every window pushes its map at startup and the
    migration is a read-modify-write, so the writes really do race. A truncated
    list would silently re-enable every overlay the user disabled.

- **[MI-04] `sync_disabled_monitors` normalizes at the trust boundary.** The
  frontend is expected to send stable ids, but the command re-keys the incoming
  list with the same [MI-03] rules anyway, so an un-migrated `localStorage` from
  an older build can never re-persist ordinals and let #70 back in through the
  front door. Stable ids pass through untouched, making the steady-state call a
  no-op re-key.

- **[MI-05] One build gate, keyed on the id.** `monitor_should_have_window(id,
  disabled)` is the only predicate deciding whether a monitor gets an overlay,
  and `plan_reconcile` is the only build/close plan. Startup, the runtime toggle
  reconcile, `on_display_change_impl`, and the [DW-04] divergence sweep all key
  on `m.id` — never `m.device_name`.

- **[MI-06] The `monitor-{i}` slot follows the PANEL, not the enumeration
  index (#93).** `assign_monitor_slots(monitors, bound)` is the single
  assignment: a monitor whose stable id already owns a slot KEEPS it, and only
  monitors with none take the lowest free slot, in enumeration order. `bound`
  comes from `bound_monitor_slots`, the live `label → id` map filtered to
  labels that still have a window, so a stale entry cannot reserve a slot
  forever. This is [MI-01] applied to the window: the label is an enumeration
  ordinal and is exactly as unstable as `\\.\DISPLAYn`.
  * The bug: powering monitors off overnight drops them from the topology
    (DisplayPort HPD) and powering them back on re-adds them in link-training
    order, which is not the order they left in. Index-keyed slots then re-pinned
    the overlay that was covering the secondary onto the PRIMARY's work area, so
    the user's widgets moved screens and stopped taking right clicks (the
    click-through region belongs to the layout that moved with them). Only a
    restart put it back. The reporter's bisect is the signature: correct with
    only the secondary awake, wrong the instant the primary comes back.
  * Consequence: **slots are not `0..monitors.len()`.** They already weren't (a
    disabled monitor leaves a hole), so no caller may count up from the monitor
    count. `on_display_change_impl`'s orphan sweep enumerates the live window
    set and closes every `monitor-*` label the pass did not handle.
  * The [DW-04] sweep MUST use the same assignment, or it compares an overlay
    against another monitor's work area, reports permanent divergence and fires
    a re-pin pass every ~2s forever. Assignment is a fixed point on a settled
    topology (`s93_slot_assignment_is_idempotent`).
  * Startup binds nothing, so slots fall out as enumeration order — a cold
    launch is unchanged.

- **[MI-07] The newest monitor adoption wins (#93, frontend).**
  `initForMonitor` (`src/views.ts`) suspends twice — the [MI-03] re-key, then
  `get_active_monitors` — before it writes anything, and display changes arrive
  in storms (a waking panel fires several `WM_DISPLAYCHANGE` while DisplayPort
  link-trains). A monotonic `adoptSeq` is taken on entry and re-checked after
  the last await: whichever adoption STARTED last wins, not whichever resolved
  last. Without it a window could settle on a superseded monitor id and go on
  showing another screen's view, laid out for another screen's width, until the
  app was restarted — [MI-06] removes the trigger, this removes the failure
  mode. A stale adoption returns before any write and logs a WARN.

- **[MI-08] An identity we could not resolve is never a key (#123).**
  [MI-02]'s ordinal fallback answers two very different questions with the same
  string: "this adapter has no device path" (indirect / virtual / RDP, permanent
  and legitimate) and "this panel has not finished link-training yet"
  (transient). `stable_monitor_id` therefore returns `(id, id_stable)`, and the
  flag travels to the frontend on `MonitorDescriptor.idStable` because the
  string alone cannot tell the two apart. `initForMonitor` refuses to ADOPT an
  unresolved id that has no stored binding: it writes nothing and keeps the view
  it is showing, and the next display change, by which time the panel has
  resolved, adopts the real id. Pre-fix it interned a fresh binding under the
  transient ordinal and switched that screen to the DEFAULT view, so the widgets
  read as "moved to another screen's layout" and recovered only on a restart,
  which is exactly #123's symptom and its restart workaround. An unresolved id
  that ALREADY has a binding is still adopted: that is the permanently id-less
  adapter, whose ordinal is the only key it will ever have. An absent flag means
  "resolved", so demo fixtures and e2e stubs keep their old meaning. Pinned by
  `mi08_descriptor_carries_the_degraded_identity_flag` (Rust) and the `mi08_*`
  cases in `src/views.test.ts`.
  * NOT covered by this: an overlay re-pinned onto another panel's work area.
    [MI-06] covers the known path there; if #123 recurs with logs showing NO
    "falling back to the ordinal" line, the remaining fault is in that half,
    not this one.

## Manual / live verification (not reachable by CI)

CI has one virtual display and cannot reshuffle ordinals, so the whole point of
#70 is unreachable by test. Required on a real multi-monitor machine:

1. **The bug itself.** Disable Themia on one monitor of a 3-display setup,
   reboot (ideally several times / with one panel powered off at logon so
   Windows reshuffles `\\.\DISPLAYn`), and confirm the overlays stay on the same
   *physical* panels every time.
2. **The migration.** With a pre-#70 `disabled_monitors.json` (bare array of
   ordinals) in `%APPDATA%\com.themia.desktop`, launch and confirm the file
   becomes `{"schemaVersion":2,…}` with device-interface paths, that the same
   physical monitor stays disabled, and that a second launch does not rewrite it.
3. **The drop rule.** Unplug a disabled monitor, launch once with a legacy file,
   and confirm the WARN names the dropped ordinal and the app does not blank an
   unrelated panel.
4. **[MI-02] fallback.** On a machine with a virtual/indirect display (RDP, a
   DisplayLink dock, a tablet-as-second-screen), confirm the WARN fires at most
   for that display and the others still get real interface-path ids.
5. **[MI-06] the #93 power-cycle.** Two monitors, widgets on the secondary.
   Power BOTH panels off, wait, power them back on — and separately, power the
   secondary on first, then the primary (the reporter's order, which is the
   reliable trigger). Widgets must stay on the panel they were placed on, the
   overlay must still take right clicks, and no restart may be needed. Repeat
   with the primary re-appearing first. CI has one virtual display and cannot
   drop a panel out of the topology, so only the pure assignment is unit-tested.

# Autostart (launch on startup): SPEC

Scope: `mod autostart` in `src/lib.rs`, the `autostart_enable` / `autostart_disable`
/ `autostart_is_enabled` commands, and the `--autostart` flag handling in `run()`
(setup + single-instance callback). Tags are `[AS-NN]`; Rust tests mirror them
(`build_task_xml_*`, `schtasks_accepts_our_generated_xml`, `s69_*`).

- **[AS-01] Scheduled Task, not Run key.** Autostart is a per-user Windows
  Scheduled Task with a `LogonTrigger`, created via `schtasks /Create /XML`
  (`enable()`). This avoids both the ~10s `StartupDelayInMSec` throttle on
  `HKCU\...\Run` and the elevation that `/SC ONLOGON` requires even for the
  current user. `disable()` deletes it; `is_enabled()` queries it. Any legacy
  `HKCU\...\Run\Themia` value is removed on every enable/disable.

- **[AS-02] Login launches pass `--autostart`; the task starts widgets only.**
  `build_task_xml` emits `<Arguments>--autostart</Arguments>`. At startup the
  flag's presence suppresses the settings window (`run()` setup + the
  single-instance callback both call `show_config_window` only when the flag is
  ABSENT). So the app's own login launch is silent (widgets only).

- **[AS-03] A manual startup entry opens Settings — by design (#69).** A
  Windows startup entry a user adds by hand (Startup-folder shortcut or a Run
  value) launches the BARE exe with no flag, which is treated as a manual launch
  and opens Settings. This is intentional, not a bug: users wanting widgets-only
  at login should use the in-app "Launch on startup" toggle (installs the [AS-01]
  task), or append the `--autostart` argument to a hand-made entry. Do NOT
  "fix" this by making bare launches skip the settings window — that would break
  Start-menu / double-click launches.

- **[AS-04] Enable/disable failures surface, never swallowed (#69).**
  `enable()` / `disable()` return `Result<(), String>`, and the `schtasks`
  invocation is captured with `.output()` so its exit code + stderr are mapped by
  `map_schtasks_result` into a descriptive `Err` (e.g. Task Scheduler blocked by
  policy → "ERROR: Access is denied."). The commands return that failure to the
  frontend (typed by [AS-05]) instead of flipping the toggle ON over a silent
  `false`. The first-run / migration enable in `run()` setup is best-effort: a
  failure is logged, not fatal. On success the persisted UI toggle reflects real
  state; on failure the caller must not treat the setting as applied.

- **[AS-05] A failed toggle says what to DO about it (#69/#79).** The commands
  reject with a typed `AutostartError { kind, message }`, `kind` being the closed
  set `elevationRequired | other`. The kind is decided in Rust by
  `classify_autostart_failure(message, autostart::is_elevated())`: NOT elevated →
  `elevationRequired`, already elevated → `other`. The decider is the process
  token, never a substring of schtasks' stderr, which is localized. A token query
  failure reports not-elevated, so the advice is offered rather than withheld.
  BOTH kinds tell the user to try running Themia as administrator — that is the
  step that unblocked #69 and #79 — and the kind decides how it is phrased:
  `elevationRequired` states it plainly, `other` hedges it ("if you already ran
  it as administrator…") and names group policy as the likelier cause, since
  that user's token says they are already elevated. `message` always carries the
  raw diagnostic, is logged on every failure, and is rendered as a quieter
  `.cfg-callout-detail` line UNDER the advice — never as the whole message, which
  reads as "something broke, good luck".
  The frontend parses the payload through `parseAutostartError` (`autostartError.ts`)
  — an unrecognised shape degrades to `other`, never up to a claim we can't back —
  and renders the matching text in a `.cfg-callout` INLINE under the toggle row,
  not a toast: the toggle stays off, and the remedy is a step taken outside the
  app, so a message that auto-dismisses while the user reads the row it belongs
  to is what made the control read as dead. The callout clears on the next
  successful flip and is not persisted across Settings sessions.

- **[AS-06] The warning is up front, and the toggle stays clickable.**
  `autostart_can_configure` dry-runs the create — `autostart::can_configure()`
  creates and immediately deletes an INERT probe task (`PROBE_TASK_NAME`, no
  `<Triggers>`, `<Enabled>false</Enabled>`, action `cmd /c exit`) — and the
  Settings body calls it on mount, so a machine that cannot configure autostart
  shows the [AS-05] callout before the user clicks anything. Rules:
  * The probe is a real create, NOT an elevation check. A standard user can
    normally create a per-user task (the reason [AS-01] uses `/Create /XML` over
    `/SC ONLOGON`), so gating on `is_elevated()` would warn the majority of users
    who have no problem.
  * `PROBE_TASK_NAME` must never equal `TASK_NAME`: the probe deletes its task,
    so sharing the name would turn the user's autostart off on every Settings
    open. Guarded by a test.
  * The probe task is inert by construction so a failed cleanup leaks a disabled
    do-nothing entry, never a second logon launch. A failed delete is logged and
    does NOT turn the probe's result into a failure — creating worked, which is
    what was being measured.
  * The toggle is NOT disabled on a failing probe. A dead control is exactly what
    #69 ("the startup button is actually locked disabled") and #79 ("the checkbox
    appears to be unresponsive") reported; and since the remedy is relaunching
    elevated, the user must be able to retry in place rather than reopen Settings.
  * A click's own outcome always overrides the probe: success clears the warning,
    a real failure replaces it. So a stale probe self-corrects.

- **[AS-07] "Enabled" means it will actually run.** `is_enabled()` reads the
  task's XML and requires BOTH switches Windows consults: the task-level
  `<Settings><Enabled>` and an enabled `<LogonTrigger>`. A task that exists but
  was switched off in Task Scheduler, or whose trigger is disabled, or that has
  no logon trigger at all, reads as OFF. The old check was `schtasks /Query` exit
  status — "a task with our name exists" — which reported Enabled for every one
  of those, and that is #69 as filed ("boot computer, no start up" with the
  setting looking on). A missing `<Settings>` block reads as enabled (the
  scheduler default); the reader round-trips `build_task_xml`'s own output, so
  writer and reader cannot drift.
  `is_enabled()` returns `Result<bool, String>`: `Err` = could not ask at all
  (schtasks unspawnable), which the frontend logs rather than rendering as a
  confident Disabled. A non-zero exit is `Ok(false)` — almost always "no such
  task", and schtasks offers no reliable way to tell that from an unreadable one.
  `task_exists()` is the SEPARATE, weaker question ("is there a task"), and is
  what the first-run/migration enable in `run()` uses: a task the user switched
  off deliberately still exists, so asking `is_enabled()` there would recreate it
  and override that choice on every launch.

## Manual / live verification (not reachable by CI)

1. On a machine where Task Scheduler is restricted by group policy, open Settings
   and confirm the inline warning is present [AS-06] BEFORE clicking; then toggle
   "Launch on startup" ON and confirm the toggle does not falsely show ON.
1b. [AS-05] On a NON-elevated normal install, force the failure path and confirm
   the warning names administrator rights; repeat elevated ("Run as
   administrator") and confirm the same failure instead shows the generic text
   with the raw schtasks diagnostic — the admin advice must NOT appear there.
2. Enable the toggle on a normal machine, reboot, and confirm widgets appear at
   login with NO settings window.
2b. [AS-07] Enable the toggle, then open Task Scheduler and switch the `Themia`
   task OFF by hand. Reopen Settings: the toggle must read Disabled (not
   Enabled), and restarting Themia must NOT silently re-create the task.
3. Add a manual Startup-folder shortcut to the bare exe, reboot, and confirm it
   opens Settings (documents the #69 user misunderstanding); repeat with the
   `--autostart` argument appended and confirm it launches widgets only.

# Window activation: SPEC

Scope: which windows are allowed to take the Windows foreground, and when.
Covers `build_widget_window`'s overlay builder, `build_detached_window` /
`open_web_element_picker` for the user-invoked windows, and the
`present_window` / `refocus_window` helpers they share. Tags are `[WA-NN]`;
tests in `lib.rs` mirror them as `waNN_*`.

- **[WA-01] Overlay creation never takes the foreground.** `build_widget_window`
  passes `.focused(false)`. `.visible(false)` alone does not prevent activation:
  tauri's `WebviewWindowBuilder::focused` feeds the WEBVIEW attribute as well
  (`tauri-runtime-wry` -> `.with_focused(webview_attributes.focus)`), and wry
  then calls `controller.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC)`
  inside the WebView2 controller build. The resulting `SetFocus` activates the
  overlay even though it is still hidden, and it happens *before*
  `pin_to_desktop` applies `WS_EX_NOACTIVATE`, so nothing downstream can stop
  it. Symptom: every launch stole the foreground once per monitor (measured with
  an `EVENT_SYSTEM_FOREGROUND` hook: `FG -> themia ... vis=False ex=0x00040118`)
  invisibly, since no window ever appeared - the user's keystrokes just went
  somewhere else. Worst under e2e, which respawns the app once per spec, but it
  hit autostart at login too. The un-hide path stays `SW_SHOWNOACTIVATE`
  (`show_overlay_once`); `.focused(false)` closes the creation-time hole.

- **[WA-02] `--no-activate`: user-invoked windows stay passive under the e2e
  harness.** Settings, the connect wizard, the picker and widget popouts SHOULD
  take focus when a user asks for them, so [WA-01] does not apply to them. But
  the e2e suite opens them from automation, once or more per spec across ~90
  specs, and each one stole the foreground twice - once at creation (the same
  wry `MoveFocus` as [WA-01]) and once at `show()` + `set_focus()`. The harness
  therefore launches with `--no-activate` (`NO_ACTIVATE_FLAG`, set in
  `tests/e2e/wdio.conf.ts` alongside `--autostart`), which makes
  `build_detached_window` build `.focused(false)` and routes presentation
  through `present_window` / `refocus_window` -> `show_without_activating`
  (`SW_SHOWNOACTIVATE`, which also restores a minimized window without
  activating it - `unminimize()` is `SW_RESTORE` and does activate). It is a
  presentation switch only: the same windows are created at the same moments
  with the same content, and WebDriver addresses them by handle, never by OS
  focus.

  Two things were tried and rejected. `WS_EX_NOACTIVATE` on these windows (the
  style the overlays use) makes the guarantee structural instead of per-call-
  site, but WebView2 never comes up behind it - the picker window stops
  existing as far as the driver is concerned (`config window kind="add" did not
  appear within 5000ms`). Suppressing only `set_focus()` and leaving creation
  alone fixes half of it, since creation activates on its own ([WA-01]).

  Residual, and NOT fixable from the app: WebDriver's own "switch to window"
  activates its target, so a spec that drives a second window still raises it
  once. Measured over the same 7 specs: 39 foreground steals before, 6 after,
  all 6 at `switchToWindow` (attributed with a probe spec that split "app opens
  the window" from "driver switches to it" by 4s and watched an
  `EVENT_SYSTEM_FOREGROUND` hook). Killing those too would mean running the
  suite on its own Windows desktop (`CreateDesktop` + `STARTUPINFO.lpDesktop`).

- **[WA-03] A detached window is CREATED on its target monitor, never moved
  there.** `build_detached_window` (settings, connect, layout, layout picker,
  shortcut-add, widget popout) and `open_web_element_picker_window` resolve
  `detached_window_placement(parent_label)` BEFORE building and pass the result
  to `WebviewWindowBuilder::position`. Only the degraded case — no resolvable
  parent, or no monitor under it — still touches a built window, and there it
  is `.center()`, which keeps the window on the display it was created on.

  The rejected shape is the one this replaced: build at wry's default position
  (primary monitor), then `set_position` onto the parent's monitor. Two things
  are wrong with it. The window is sized from `inner_size` using the CREATION
  monitor's scale factor and then moved to a monitor that may have another one,
  so on a mixed-DPI desktop it arrives at the wrong size and off-center (the old
  code compounded this by computing the move with the PARENT WINDOW's scale
  factor rather than the target monitor's). And the move happens after
  `.build()`, i.e. after the WebView2 controller exists, so it drags a live
  webview across a per-monitor-DPI boundary and takes a `WM_DPICHANGED` resize
  through tao + the WebView2 controller while that controller is still coming
  up. That is the suspected mechanism behind themia-releases#111 ("app closes
  when entering configure on a dual display": silent process death, no log line,
  no Event Viewer entry, only on the fresh-window path — a settings window that
  is already open takes the idempotent `refocus_window` branch and never
  crashes, which is what made the report read as "random").

  Passing a LOGICAL position is not a compromise, it is the whole mechanism:
  tao converts `attributes.position` with each connected monitor's own DPI and
  takes the first monitor whose physical rect contains the result, then sizes
  the window with THAT monitor's scale factor
  (`tao::platform_impl::windows::window::init`). So a logical position derived
  from the target monitor's own scale round-trips back to the exact physical
  point on the exact monitor, and both the placement and the size are settled
  before any webview exists. `detached_window_logical_position` is pure and
  covered by `wa03_*`; the builders are held to the shape by a source scan,
  since the difference is only observable on a live mixed-DPI multi-monitor
  desktop.

  NOT verified: themia-releases#111 itself is unreproduced. It did not fire on a
  two-monitor, same-DPI (2×100%) Windows 11 machine driving the real
  `show_config_window` path. This spec fixes a real defect on that path; whether
  it is the reporter's defect needs a mixed-DPI dual-monitor machine, or their
  log with logging enabled ([LOG-04] means a release build persists only
  ERROR until the toggle is on).

# Logging pipeline: SPEC

Scope: the tauri-plugin-log configuration in `src/lib.rs` (`run()` plus the
`set_log_stream` / `set_logging_enabled` commands) and the frontend console
bridge in `../src/debugLog.ts`. While logging is enabled ([LOG-04]), every
record (Rust `log::*!`, JS `logger.*`, ad-hoc `console.*`) flows through
the plugin, which fans out to LogDir (file), Stdout (debug builds) and the
Webview target (`log://log` → per-window in-memory buffer for
Settings > Logs + DevTools echo), down to DEBUG for our own code ([LOG-06]).
While logging is off (the release default), only ERROR flows: the [LOG-04]
error floor, which is what makes a crash on an un-opted-in machine
diagnosable.

Tags are `[LOG-NN]`; Rust tests mirror them as `logNN_*`, TS tests cite the
tag in the test/describe name.

- **[LOG-01] Bounded log dir.** Rotation is `KeepSome(LOG_KEEP_FILES)` with a
  5 MB `max_file_size`, a hard ~50 MB ceiling. Never `KeepAll`: rotated
  segments were never deleted and a real machine accumulated 628 files /
  2.8 GB in `%LOCALAPPDATA%\com.themia.desktop\logs`.

- **[LOG-02] Gated webview forwarding.** The Webview target re-broadcasts
  every record to ALL windows via eval, and each window buffers it. In
  release builds WARN+ always forwards (rare, and the Logs page must show
  errors from before it was opened); INFO and below forward only while at
  least one Settings > Logs page is open. The page reports open/close via
  the `set_log_stream` command into a **saturating** viewer counter: an
  unbalanced close (window killed) must never underflow and pin forwarding
  on. Debug builds forward everything (DevTools echo + the e2e logs-panel
  spec rely on it). Opening a viewer emits one INFO record of its own
  (`[logs] log viewer attached`, after the counter rises so the gate passes
  it) so a page opened on a quiet app proves the pipeline instead of showing
  an empty list.

- **[LOG-03] Console-noise filter (frontend).** The `console.warn` →
  persisted-WARN promotion in `debugLog.ts` drops known-noise records,
  currently the orphaned fs-watch Channel spam
  `"[TAURI] Couldn't find callback id N…"`, instead of persisting them.
  One orphaned watcher otherwise amplifies every fs event into a WARN that
  hits the log file and every window's buffer. Filtered records still echo
  to DevTools via the saved console originals. See also folder `[S-63]`,
  which fixes the orphaning itself.

- **[LOG-04] Verbose logging is opt-in (master switch, default OFF) above an
  always-on ERROR floor.** In release builds ERROR always reaches the file
  (and, per [LOG-02], the Logs page); WARN and below reach nothing until the
  user enables logging (the Stdout target exists only in debug builds, which
  bypass the gate). The floor exists because the opt-in shape made every
  crash report unfalsifiable: the panic hook logs at ERROR, a panic does not
  reproduce on demand, and "enable logging and hit it again" is not a
  diagnosis. It stays a floor rather than a raised default because ERROR is
  rare — the [LOG-01] 50 MB budget and the [LOG-06] DEBUG verbosity are only
  affordable for records the user asked for. Two gates, both fed by the
  persisted `globalDesign.loggingEnabled` (default `false`):
  - Rust: a dispatch-wide `.filter()` on the plugin builder reading the
    record's level and `LOGGING_ENABLED` (`logging_allowed`, a pure truth
    table: `debug_build || user_enabled || level == Error`). The gate is
    **seeded from a marker file**
    (`%LOCALAPPDATA%\com.themia.desktop\logging-enabled.marker`, presence =
    opted in) at the very top of `run()`, so an opted-in user's startup
    records, including setup panics, are captured from the first record;
    `set_logging_enabled` keeps both the atomic and the marker current.
    Fresh install / absent marker = closed.
  - Frontend: the bridge in `debugLog.ts` (`setLogForwarding`), which while
    off sends only ERROR over IPC (`logger.error` and `console.error`, the
    same floor): the rest of `console.*` falls through to the original
    console, `logger.warn` echoes to DevTools so warnings stay visible,
    `logger.info/debug` are dropped. `main.tsx` seeds EVERY window type once
    from the persisted blob (`seedLogForwardingFromStorage`) so aux windows
    (connect/layout/picker) honor the toggle too; `App.tsx`/
    `ConfigWindow.tsx` re-push reactively on change.

  Debug builds bypass both gates (`cfg!(debug_assertions)`; the frontend
  bridge initializer is `import.meta.env.DEV`), so `npm run dev` and the
  e2e suite always log; e2e specs that read the Logs page seed the toggle
  via the `enableLogging()` helper because the e2e build pairs a production
  frontend with debug Rust. Consequence by design: while logging is off,
  WARN and below are NOT persisted, so diagnosing anything short of an
  outright failure still starts with flipping the toggle (records flow from
  that moment, no restart) — but the ERROR that preceded the report is
  already on disk.

- **[LOG-05] Dev-only toggle.** The switch lives in Settings > General >
  Developer (visible only while Developer Mode is on) and takes effect
  immediately, no restart; the effect in `App.tsx`/`ConfigWindow.tsx`
  re-pushes on every change and forces the frontend bridge on under dev.
  Turning Developer Mode OFF also turns logging off (same contract as
  DevTools: a disabled toggle fully hides the feature). The Logs page shows
  a hint pointing at the switch while logging is disabled in release.

- **[LOG-06] One level policy, with a dependency cap.** The dispatch level is
  `Debug` in **every** build; the release/debug split that capped release at
  `Info` is gone. That split was issue #85 ("logging does not show any entry
  even when enabled"): practically all of the app's diagnostics are DEBUG
  (55 frontend `debugLog()` call sites against 3 `logger.info`, plus every
  `log::debug!`), so an opted-in user on a release build saw an empty Logs
  page. Lifting the cap wholesale is not an option either — one session
  produced 8.5k `rustls` DEBUG lines against 2.5k of ours — so
  `log_target_allowed` caps **dependencies** at INFO instead: DEBUG survives
  only for our own targets, meaning any `webview*` target (the frontend's
  stack-derived string), any flat custom target (`drag_drop`, `folder:icons`,
  `app`), and module paths rooted at `themia` / `themia_lib` / `widget_*`
  (a bare `log::debug!` is tagged with its module path). Dependency
  INFO/WARN/ERROR is never capped. The cap runs before the [LOG-04] master
  gate, so a suppressed record costs one string check.

- **[LOG-07] Startup sweep of stale segments.** `KeepSome` prunes only what the
  CURRENT run rotates, so a machine upgraded from a pre-[LOG-01] `KeepAll`
  build keeps its entire history forever (1.54 GB / 319 files measured on a dev
  machine; the report behind [LOG-01] had 2.8 GB / 628). `sweep_stale_logs`
  runs at the top of `run()`, BEFORE the log plugin is built, so this session's
  own file is not yet a candidate. It deletes only files
  `is_themia_log_segment` recognises (`themia*` + `.log` / `.log.bak`, matching
  the current name, the pre-0.13 `Themia_*` casing and the plugin's `.bak`
  segments), keeping the newest `LOG_KEEP_FILES` by mtime with the file name as
  a stable tiebreak. Best-effort throughout: a log dir that cannot be read or
  pruned is not worth failing a boot over, and `NotFound` is the fresh-install
  case, not a fault.

- **[LOG-08] The Logs page links to the files, it does not re-export them.**
  The page's actions are: copy the (filtered) buffer to the clipboard, open the
  log folder, clear the buffer. `open_log_folder` reveals
  `%LOCALAPPDATA%\com.themia.desktop\logs` in Explorer, creating the directory
  first so a user who has just enabled logging is not dropped in Documents by
  Explorer's missing-path behavior. This replaced an "export to file" action
  that wrote a **worse** copy of what was already on disk: the buffer holds
  only the current session, only what the [LOG-02] viewer gate forwarded, and
  only since the page's window opened, whereas the files carry every session
  and every level. Copy-to-clipboard stays because a filtered excerpt pasted
  into an issue is a different job from attaching the files.

- **[LOG-09] ERROR is mirrored into the Windows Application event log.** A
  `TargetKind::Dispatch` target with its own `LevelFilter::Error` (so it stays
  quiet even with the toggle on) hands each ERROR to `eventlog::report_error`,
  which writes it under source `Themia`, event id 1000. Rationale: a user who
  reports "it just closed" will not find `%LOCALAPPDATA%\…\logs`, and Windows
  already files the WER `Application Error` entry for a hard crash in that same
  log — the mirror puts our message next to it instead of leaving a bare module
  offset. Constraints, all enforced in `eventlog.rs`:
  - **Capped at 25 records per session**, then ONE notice saying the rest are
    suppressed (a reader must not read the silence as "the errors stopped").
    The Application log is machine-wide and bounded; an error inside a retry
    loop must not evict other software's events. The file log stays uncapped.
  - **Truncated at 2,000 chars on a char boundary** (`char_indices`, never a
    byte slice: a panic payload can carry any user text, and splitting a
    multi-byte char would panic inside the logging pipeline).
  - **Best-effort, never fatal**: registration happens once via `OnceLock`
    (`None` = Event Log service unavailable → silently skip); the handle is an
    RAII `SourceHandle` held for the process lifetime; no path panics, logs, or
    returns an error, because the caller IS the logging pipeline.
  - The installer registers the source
    (`windows/installer-hooks.nsh` → `EventMessageFile` = `EventCreate.exe`,
    `TypesSupported` = 7) so entries render as plain text. That needs HKLM, so
    a per-user install leaves them wrapped in Windows' "description ... cannot
    be found" preamble — degraded, still readable, never depended on.
  - **Privacy:** the Application log is readable by any local administrator,
    whereas the log files are user-scoped, and error text can carry paths or
    mail subjects. That is the deliberate trade for a diagnosable crash; it is
    also why only ERROR is mirrored and why the cap is small.

# Process lifetime: SPEC

Scope: the `RunEvent` handler in `run()`, `quit_app`, `APP_QUITTING`.

- **[EX-01] Only a deliberate quit may exit.** `tauri-runtime-wry` raises
  `RunEvent::ExitRequested` the moment its window map becomes empty
  (`TaoWindowEvent::Destroyed`), and if nothing calls `api.prevent_exit()` the
  default is `ControlFlow::Exit`. Themia keeps no permanently-open window and
  has no tray icon, so its window set legitimately passes through zero during a
  reconcile — the [DW-06] destroy-and-rebuild, disabling every monitor,
  hot-unplug, dock, wake. With no handler the process therefore died with no
  panic, no log line and no Event Viewer entry: themia-releases #87,
  "application exits without a trace on some machines". The handler refuses the
  request unless `quit_app` armed `APP_QUITTING`, logs the refusal at ERROR, and
  requests a display pass so the overlays are rebuilt (there being no tray icon,
  a prevented exit must not leave an invisible process behind).

# Input hooks: SPEC

Scope: `kb_hook_proc` / `mouse_hook_proc`, `point_over_widget_window`,
`is_widget_hwnd`, `publish_overlay_snapshot`, `request_hook_emit` /
`start_hook_emitter`, `install_input_hooks`, `start_input_hook_thread`,
`rearm_input_hooks_if_dead`, `rearm_backoff_checks`.

- **[HK-01] The hook hot path takes no lock the main thread can hold.** A
  WH_MOUSE_LL / WH_KEYBOARD_LL callback runs for EVERY input event in the
  session, and Windows silently and permanently removes a hook whose callback
  overruns `LowLevelHooksTimeout` (300 ms by default). Reading Tauri's window
  map (`app.webview_windows()`) or emitting an event (`Emitter::emit`) from the
  callback both take the manager lock, which the main thread holds across window
  creation and teardown — i.e. across exactly the display-change storms during
  which input keeps flowing. Losing the hook is invisible and unrecoverable:
  clicks stop dismissing Start, `themia-blur` never fires and widget selection
  sticks until restart (themia-releases #58, "widgets stop working after a
  while"). So the overlay HWNDs are mirrored into a fixed array of atomics
  (`OVERLAY_HWNDS`, republished by every `wndproc_map` mutation) that the
  callback scans without any lock. `is_widget_hwnd` reads the same snapshot,
  since it runs on the same thread from the foreground WinEvent.

  **No callback emits.** Both hooks hand every delivery to one parked worker
  (`start_hook_emitter`): the mouse hook via a wait-free counter, the keyboard
  hook via `request_hook_emit` and a bounded queue whose lock neither side ever
  holds across an emit. One worker, not one per hook — it is the same job. The
  keyboard path is why this matters twice over: it used to call
  `app.webview_windows()` and `Emitter::emit` inline, so a Ctrl+Z or a routed
  Ctrl+C landing during a window rebuild could block the callback past the
  timeout and cost the app both hooks. Consuming the keystroke must still be
  decided synchronously, so the "is there a live window to route to" test reads
  the overlay snapshot (`WIDGET_KB_HWND` + `overlay_snapshot_contains`) rather
  than `Manager::get_webview_window`; `decide_widget_kb_routing` has never let
  the emit result change that decision, which is what makes the delivery safe to
  defer. Blur requests coalesce: the event has no payload, so N clicks away from
  the app mean the same thing as one.

- **[HK-02] Evicted hooks are detected and re-armed.** No API reports that a
  low-level hook has been removed — the handle stays valid and the callback
  simply stops being called — so liveness is inferred: every callback bumps
  `HOOK_HEARTBEAT`, and the watchdog compares it against `GetLastInputInfo`,
  which is independent of our hooks. Input that the OS saw within the interval
  we just watched, with no heartbeat movement, means the hooks are dead
  (`hooks_look_dead`), and both are reinstalled. The handles are retained so a
  re-arm unhooks the old registration first and can never leak one. Never
  re-arm without evidence: no `GetLastInputInfo` reading means no decision.
  A re-arm that does not restore the heartbeat backs off — `rearm_backoff_checks`
  doubles the wait to a 16-check cap — because a registration the OS cannot
  service costs every input event in the session a `LowLevelHooksTimeout` wait,
  so retrying it at the health-check cadence forever is worse than the eviction
  it is trying to repair.

- **[HK-03] The hooks live on a thread that does nothing but pump.** Windows
  delivers a WH_KEYBOARD_LL / WH_MOUSE_LL callback through the message queue of
  the thread that called `SetWindowsHookEx`, and `SetWindowsHookEx` succeeds
  whether or not that thread will ever dispatch. Installing from a thread with
  no pump therefore produces a registration that can never fire AND makes the
  OS wait out `LowLevelHooksTimeout` on every keystroke and mouse move in the
  session — system-wide input stutter, not just a dead feature. The hooks were
  first armed on the watcher thread, whose pump also services `wnd_proc`
  (`Emitter::emit` on session unlock, display-change bookkeeping), and re-armed
  from the z-order poll thread, which only sleeps. Both are now wrong by
  construction: `start_input_hook_thread` owns the hooks on a thread whose loop
  is `GetMessage`/`Dispatch` and nothing else, `install_input_hooks` refuses and
  logs if called from any other thread, and a re-arm is a
  `WM_REARM_INPUT_HOOKS` posted to that thread.

# Z-order poll pacing: SPEC

- **[ZP-01] The fast cadence is budgeted, not permanent.** The z-order
  reconciliation poll needs ~80 ms (one frame at 60 Hz) to beat a secondary
  monitor's WorkerW back into place after a Show Desktop, but running at that
  rate for the app's whole uptime is 12.5 timer wakeups a second that keep the
  CPU package out of its deep C-states for nothing. `next_fast_ticks` refills a
  `ZORDER_FAST_TICKS` budget on any activity signal and spends one per idle
  tick; `zorder_poll_interval_ms` maps a non-empty budget to
  `ZORDER_POLL_FAST_MS` and an empty one to `ZORDER_POLL_IDLE_MS`. Signals are
  `SHELL_ACTIVITY_SEQ` (bumped by the foreground hook, the DWM cloak hook and
  `request_display_pass`) and `zorder_race_possible()` — the shell owning the
  foreground, or us currently holding the topmost pin. Both helpers are pure, so
  the pacing is testable without a shell.

# Shell surfaces vs. the topmost pin: SPEC

Scope: `SHELL_SURFACE_CLASSES` / `SHELL_SURFACE_PROCESSES` / `is_shell_surface`
/ `hwnd_is_shell_surface` / `shell_surface_is_foreground` / `decide_zaction` /
`ZAction::HoldTopmost` and the `foreground_change_proc_impl` arm that returns on
a shell surface, plus `overlay_activation_demotes` /
`anchor_widget_above_desktop` and the `WM_ACTIVATE` arm of
`no_minimize_wnd_proc_impl`. Tags are `[ZS-NN]`; Rust tests mirror them
(`zs01_*`, `zs02_*`).

- **[ZS-01] Start / Search / Task View / the taskbar are not "an app came
  back".** During a Show Desktop the shell elevates its desktop layer past the
  normal z-order, so `SetWindowPos(widget, Progman, …)` cannot beat it and the
  `WS_EX_TOPMOST` pin is the ONLY thing keeping widgets visible. The foreground
  hook and the reconcile used to drop that pin for any foreground window that
  wasn't the desktop or one of our own overlays — which the Start menu is. The
  result was themia-releases#104: press the Windows key on an otherwise empty
  desktop and every widget vanishes, and stays gone, because dismissing Start
  hands the foreground to the taskbar (another shell surface), never to the
  desktop, so nothing re-armed the pin. A window whose class is in
  `SHELL_SURFACE_CLASSES` **and** whose owning process is in
  `SHELL_SURFACE_PROCESSES` therefore maps to `ZAction::HoldTopmost`: keep the
  latch, restack nothing. Both halves are load-bearing — `CoreWindow` alone is
  every UWP app's host class, and `explorer.exe` alone owns File Explorer
  windows; either on its own would hold the pin over a real window. The
  process list covers the out-of-process Start/Search hosts across Win10 and
  Win11 plus `explorer.exe`, which is where in-process Start replacements
  (StartIsBack, Open-Shell — the reporter ran StartIsBack) live.
  `HoldTopmost` deliberately does NOT re-raise: the surface is topmost too and
  was activated after us, so leaving the z-order alone is exactly what keeps it
  painted above the widgets instead of fighting it at 12.5 Hz. An unresolvable
  owning process falls back to "app", the conservative side (a wrongly-dropped
  pin costs one reconcile tick; a wrongly-held one paints widgets over a real
  window). `reconcile_overlay_zorder` probes the shell surface before
  `show_desktop_is_active()` because the former is a class read plus a process
  lookup and the latter is two `EnumWindows` sweeps; a shell surface owning the
  foreground implies the shell desktop does not, so the short-circuit cannot
  change the outcome.

- **[ZS-02] Themia never raises its own windows to repair z-order; it demotes
  the overlay.** The settings window (and every other detached window) must keep
  whatever z-position the user's window switching gave it. It used to be
  re-raised whenever a widget's z-order could have moved — on every
  `set_interactive` flip, on overlay self-activation, and on `WM_ACTIVATE` — via
  `SetWindowPos(HWND_TOPMOST)` followed by `SetWindowPos(HWND_NOTOPMOST)`. That
  second call does not restore the previous position: `HWND_NOTOPMOST`
  re-inserts at the **top of the entire non-topmost band**, above every other
  application. Because merely hovering a focusable widget element flips
  interactivity, the settings window climbed in front of whatever app the user
  had switched to, with no user action and no obvious trigger — the reported
  "settings puts itself over the window after some time". The relative order the
  raise was protecting (settings above our overlays) is a property of the
  overlay's own anchor, so the repair belongs there: `WM_ACTIVATE` on an overlay
  re-anchors *that overlay* to `z_above_desktop()` (`anchor_widget_above_desktop`),
  matching what `foreground_change_proc` already does on self-activation and
  what the `WM_WINDOWPOSCHANGING` rewrite enforces for every other restack.
  `overlay_activation_demotes` gates it on the Show Desktop pin: while widgets
  are deliberately topmost, demoting would drop them under the shell's elevated
  desktop layer ([ZS-01]'s failure), so the pin wins. No HWND registry of
  detached windows is kept, because nothing needs to move them.

# Always-on-top pin layer: SPEC

Scope: `OverlayLayer` / `overlay_label` / `label_is_pin_layer` /
`base_label_of` / `PIN_LAYER_ENABLED` / `pin_layer_hwnds` / `hwnd_is_pin_layer`
/ `z_anchor_for` / `set_pin_layer` / `sync_pin_overlays` and the `layer`
parameter on `build_widget_window` / `pin_to_desktop`. Tags are `[PL-NN]`;
tests mirror them as `plNN_*`. The frontend half is src/SPEC.md [S-45].

- **[PL-01] The pin is a second WINDOW, not a flag on a widget.** Z-order is a
  property of a top-level window, and every widget on a monitor shares one
  overlay, so "keep THIS widget above other applications" is not expressible on
  the base overlay: anything that lifted it would lift every widget on that
  display. `monitor-{i}-pin` is that second window — same bounds, same content
  pipeline, anchored `HWND_TOPMOST` instead of just-above-Progman. Its anchor
  is decided in ONE place, `z_anchor_for(hwnd)`, so there is no correct call to
  `z_above_desktop()` on an overlay HWND that does not go through it; a new
  restack site therefore cannot silently demote the pin layer. Four existing
  restack paths that exist purely to settle the DESKTOP band skip pin overlays
  outright — `set_widgets_topmost` (its `HWND_NOTOPMOST` half is exactly what
  would sink the window behind the app that just came back),
  `self_heal_widgets_above_desktop` ("below Progman" is not a state a topmost
  window can reach), `raise_widgets_above_desktop`, and the `diag_reset_zorder`
  helper. The `WM_WINDOWPOSCHANGING` rewrite is the pin overlay's own repair
  and is exempt from the Show-Desktop latch guard: its anchor is `HWND_TOPMOST`
  in every state, so the latch has nothing to protect there. The HWND is
  registered in `pin_layer_hwnds` BEFORE the first restack in `pin_to_desktop`
  (the subclass starts rewriting immediately) and pruned in the `WM_NCDESTROY`
  arm with the other HWND-keyed maps, so a recycled handle can never inherit
  the topmost anchor.

- **[PL-02] Built by the display pass, mirrored off the live base overlays.**
  `set_pin_layer` only flips an atomic and requests the debounced display pass
  ([DW-02]); it never builds a window itself. That keeps overlay creation and
  geometry on the single writer [DW-01]/[DW-02] already establish, and it makes
  the command idempotent, so every monitor's base overlay can report the same
  global answer without churn. `sync_pin_overlays` then holds the invariant
  "one pin overlay per LIVE base overlay while enabled, none while not", and
  reads each pin overlay's bounds back from its base overlay's window rect
  rather than recomputing them from the monitor list — two computations of the
  same work area is exactly how the two layers would drift apart. Layers are
  told apart by `parse::<usize>()` alone: a pin label never parses as a slot,
  which is what stops `bound_monitor_slots`, the disabled reconcile and the
  sync's own loop from mistaking a pin overlay for a base one (`pl02_*` pins
  this). The orphan sweep in `on_display_change` skips pin labels for the same
  reason: it runs while the bases are still being rebuilt, so the sync that
  follows it owns them. A pin overlay with a dead HWND is destroyed and rebuilt,
  the same [DW-06] treatment a base overlay gets.

## Manual / live verification (not reachable by CI)

1. Pin a widget, then open a maximized app over it: the widget stays visible and
   clickable; every unpinned widget is covered as before.
2. Enter edit mode: the pinned widget drops back into the normal layer and can
   be dragged / resized; empty grid cells are still drop targets and nothing
   traps input over other applications. Leave edit mode: it floats again.
3. Show Desktop, then Win key, then click back into an app: neither layer
   flickers and the pinned widget never sinks behind the app ([ZS-01] latch vs.
   the [PL-01] exemption).
4. Unpin the last pinned widget: `monitor-{i}-pin` closes on every monitor (one
   fewer WebView2 renderer per display).
5. Multi-monitor: pin, then unplug / disable a display and plug it back in; the
   pin overlay follows its base overlay onto the right panel with the right
   work-area bounds, and no orphan window is left behind.
6. Start a fullscreen game: the pinned widget disappears with the rest
   (`gameModeSuspended`), it must not be the one thing left on top.

# Overlay backdrop denial: SPEC

Scope: `backdrop_denial_attrs` / `deny_dwm_backdrop` and their two call sites
(`pin_to_desktop`, the overlay WndProc). Tags are `[BD-NN]`; tests in `lib.rs`
mirror them as `bdNN_*`.

- **[BD-01] The denial table holds OFF values, not defaults.**
  `backdrop_denial_attrs` is the single source of the `(DWM attribute, value)`
  pairs: `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_NONE (1)`, `DWMWA_MICA (1029) = 0`,
  `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND (1)`. `DWMSBT_AUTO` is 0
  and means "DWM decides", which is NOT the same as denial — the table is
  unit-pinned because that one-character difference is invisible in every
  environment CI can reach. All three go out unconditionally: which spelling a
  build honours is a runtime property (`DWMWA_MICA` is the undocumented 21H2
  form, `DWMWA_SYSTEMBACKDROP_TYPE` its 22523+ replacement), and DWM answers
  `E_INVALIDARG` for the rest.

- **[BD-02] An overlay never carries a backdrop.** `pin_to_desktop` calls
  `deny_dwm_backdrop` on every overlay it pins: the [BD-01] table, plus
  `DwmExtendFrameIntoClientArea` with all-zero `MARGINS` and the [BD-04]
  blur-behind re-assert. Those five knobs are exactly
  what a third-party backdrop injector (MicaForEveryone and its kin) pushes at
  a window. The overlay spans the whole work area and is transparent, so a
  backdrop composited into it is indistinguishable from "my wallpaper is
  permanently blurred" (themia-releases#74). This is prophylaxis, not a fix for
  a reproduced application: Themia's overlays already fail MicaForEveryone's
  own eligibility filter twice (it rejects `WS_EX_NOACTIVATE`, then
  `WS_EX_TOOLWINDOW` without a title bar, and our overlays carry both). The
  invariant is stated on our own window rather than left to depend on another
  process's filter continuing to exclude us.

- **[BD-03] Re-asserted on DWM composition change, never polled.** A DWM
  restart resets every window attribute, which silently re-opens the overlay to
  a backdrop. The overlay WndProc re-asserts on `WM_DWMCOMPOSITIONCHANGED`
  (0x031E) and falls through to the original proc. That is the only event that
  can undo [BD-02] behind our back — a third-party sweep re-applies only to
  windows that pass its own filter, which ours do not — so there is no timer
  and no poll behind this.

- **[BD-04] Blur-behind is re-asserted, never disabled — it IS the
  transparency.** tao implements `transparent: true` with
  `DwmEnableBlurBehindWindow(DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = TRUE,
  hRgnBlur = CreateRectRgn(0, 0, -1, -1))`: an EMPTY blur region, which asks DWM
  for per-pixel alpha and has no pixels left to blur. `deny_dwm_backdrop` pushes
  that same call (and frees the HRGN, which DWM copies). Sending
  `fEnable = FALSE` instead makes the overlay opaque; the region clip hides that
  everywhere except where the shape covers the whole window (a context-menu
  backdrop, edit mode, a modal), so the symptom is "right-click blanks my
  wallpaper", not "widgets look wrong". If `CreateRectRgn` fails the call is
  skipped entirely: with `DWM_BB_BLURREGION` set, a NULL region means "the whole
  window", the wallpaper-wide blur this function exists to prevent.

## Manual / live verification (not reachable by CI)

1. With MicaForEveryone (or any backdrop injector) running with a Global rule
   set to Mica/Acrylic + Blur Behind + Extend Frame: the wallpaper must stay
   sharp behind the widgets, and widget corners must not be rounded/clipped by
   DWM.
2. `taskkill /f /im dwm.exe` (DWM auto-restarts) → wallpaper still sharp,
   overlays still frameless: [BD-03] re-assert fired.
3. [BD-04] Right-click a widget, enter edit mode, open a modal: each expands the
   window region to the full screen, and the wallpaper must stay visible behind
   the menu / grid / modal. Then repeat after `taskkill /f /im dwm.exe`.

# Widget cloaking: SPEC

Scope: `uncloak_widget` / `cloaked_event_proc` / `ensure_widget_cloak_tick` /
`CLOAK_CHECK_INTERVAL_MS` and the `diag_cloak_widget` command. Tags are
`[ZC-NN]`.

- **[ZC-01] The uncloaked invariant has a backstop, not just an event.** Show
  Desktop and fullscreen exclusive apps hide windows by DWM-cloaking them, and
  the recovery was a single `EVENT_OBJECT_CLOAKED` `WINEVENT_OUTOFCONTEXT`
  callback. Windows drops those when the callback can't be delivered inside the
  hook timeout, which is precisely what a fullscreen game does to the desktop —
  and a dropped uncloak was permanent: the widget stays `IsWindowVisible`,
  non-iconic and above Progman, it is simply never composited, so no amount of
  z-order work recovers it (themia-releases#104, "widgets completely disappear
  with a game open"). `ensure_widget_cloak_tick` re-reads `DWMWA_CLOAKED` per
  widget on a `CLOAK_CHECK_INTERVAL_MS` cadence from the z-order poll thread and
  routes any mismatch through the same `uncloak_widget` the hook uses (one path,
  two triggers). The read is one `DwmGetWindowAttribute` per overlay and the
  write only happens on a real mismatch, so the steady state costs nothing; it
  honours `POLLING_PAUSED` so specs can still observe a deliberate cloak.

# WebView2 process failure: SPEC

- **[WV-01] A dead renderer is recovered, not left on screen.** An overlay is a
  long-lived window nobody ever closes or reopens, so anything that kills its
  content is permanent from the user's point of view. WebView2 renderers do die
  in the field: GPU driver resets, the 192 MB V8 old-space cap in
  `WEBVIEW_BROWSER_ARGS`, and a WebView2 Runtime that auto-updates underneath a
  running process. The [DW-06] heal only fires on a MISSING HWND, which a
  renderer crash does not produce — the window survives, blank or frozen. Every
  overlay therefore arms `add_ProcessFailed` at build time.
  `process_failure_action` (pure, testable) maps the kind: render / frame /
  unresponsive → reload in place; browser-process-exited → destroy the window
  and request the [DW-06] display pass, since the webview cannot be revived
  through its own API; helper processes (GPU, utility, sandbox) → log only,
  because WebView2 restarts those itself and acting would be worse than not.
  Missing args default to reload, the conservative choice: a no-op on a healthy
  page and the cure for the failures that actually strand a widget. The rebuild
  goes through the existing display pass rather than adding a second
  window-construction path.

- **[WV-02] The browser args never declare the machine low-end.**
  `--enable-low-end-device-mode` sets `base::SysInfo::IsLowEndDevice()`, and
  Blink derives its raster budgets from that flag
  (`third_party/blink/renderer/platform/widget/compositing/layer_tree_settings.cc`):
  the GPU memory policy drops to `bytes_limit_when_visible = 96 MB` and
  `max_staging_buffer_usage_in_bytes` is divided by 4. Themia is the worst
  possible shape for that cap - one FULL-WORK-AREA transparent overlay per
  monitor, and every widget surface (`.widget-header` / `.widget-body`) carries
  a `backdrop-filter`, so each one needs a backdrop snapshot on top of the
  layer's own tiles. Past the budget cc simply stops filling: the widget fill
  and its blur land only in the raster tiles that fit and the wallpaper shows
  straight through the rest, as a hard tile-aligned rectangle that survives
  resizing the widget. The same cap hits the detached settings window, which
  paints its page background in one 512-device-px tile column and leaves the
  bare window brush everywhere else - "the background doesn't fill the window"
  (themia-releases#124, the reopened half of #43, and #108). It is
  device-dependent purely because the cap is fixed while the layer size is not:
  a 1x 1080p dev box stays under it, a 4K or 150%-scaled display does not.
  Renderer memory is bounded by the [WV-01] V8 cap instead; never trade the
  raster budget for it. Pinned by `wv02_browser_args_never_declare_a_low_end_device`.

# Desktop icon visibility: SPEC

Scope: `find_desktop_shell_view` / `resolve_desktop_shell_view` /
`desktop_icons_need_toggle` / `desktop_icons_visible` /
`set_desktop_icons_visible`, the `set_desktop_icons` + `get_desktop_icons`
commands in `src/lib.rs`, and the Settings > General "Show desktop icons"
toggle. Tags are `[DI-NN]`; Rust tests mirror them (`di_02_*`).

- **[DI-01] Toggle through the shell's own command, never `ShowWindow`.** The
  apply is `PostMessageW(SHELLDLL_DefView, WM_COMMAND, 0x7402, 0)` — the exact
  command the desktop context menu's View > Show desktop icons item posts.
  Explorer performs the hide AND writes its own folder-view flag
  (`HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop`, `FFlags` bit
  `0x4000` = `FWF_NOICONS`), which is what makes [DI-03] work and what keeps
  the context-menu checkmark honest. Pre-0.15 this was
  `ShowWindow(SysListView32, SW_HIDE)`: it hid the window behind Explorer's
  back, so the state was invisible to the shell, un-persisted, and lost on
  every explorer.exe restart. The command id is undocumented (no MSDN
  constant) but unchanged since Vista and identical on Windows 10 and 11 —
  11's rewritten context menu still routes here. `PostMessage`, never
  `SendMessage`: the window lives in explorer.exe and a synchronous
  cross-process call blocks us for the shell's whole handling of the command.

- **[DI-02] The command is a TOGGLE, so the apply reads state first.** There is
  no "set" form. `set_desktop_icons_visible` reads the live state and posts
  only on a mismatch (`desktop_icons_need_toggle`), which is what makes a
  re-apply idempotent rather than a flip-flop. State is read as
  `IsWindowVisible(SysListView32)`: Explorer hides the list-view window itself,
  so visibility IS the state and it updates the instant the shell handles the
  command, whereas the `FFlags` value it also persists is written lazily and
  would read stale. An unreadable state (`None`) must post NOTHING — with no
  set form, a guess has a 50% chance of hiding the icons of a user who just
  asked to show them.

- **[DI-03] The hide persists; Themia never re-asserts or restores it.** The
  setting belongs to the shell, exactly as if the user had used the desktop
  context menu, so it must survive Themia exiting, explorer.exe restarting and
  reboot. There is deliberately no force-visible at startup and no restore on
  `RunEvent::Exit` (both existed pre-0.15 as safety nets for the
  non-persistent `SW_HIDE`, and both would now silently undo a hide the user
  asked for). The command is posted only when the user moves the Settings
  toggle. Consequence by design: uninstalling Themia while icons are hidden
  leaves them hidden — the user restores them from the desktop context menu.

- **[DI-04] Windows owns the state; the stored setting is a first-paint cache.**
  Because the state now lives in the shell, `globalDesign.desktopIconsVisible`
  must not be treated as a second source of truth: it is not pushed by
  `applyGlobalDesign` (which runs on every unrelated design change and would
  re-assert a stale value over a change the user made from the desktop menu).
  Settings adopts reality via `get_desktop_icons` and pushes via
  `set_desktop_icons` only on user change. The adopt runs on window focus, on
  visibility return, and on a 1s poll while the General page is up
  (`watchDesktopIcons` in `src/desktopIcons.ts`) — not once at mount, which is
  what it used to be: the config window stays open while the user flips the
  setting from the desktop context menu, so the toggle sat showing a state it
  does not control. The poll is this codebase's one sanctioned wall-clock timer.
  It is justified because the shell broadcasts NOTHING on an icon toggle (the
  state IS the list-view window's visibility, [DI-02]), so there is no event to
  listen for, and it is display-freshness only: nothing waits on a tick, and a
  dropped tick just leaves the cached value on screen. It is bounded on all
  sides — only while the one page rendering the toggle is up, only while the
  window is visible, single-flight so a slow read never stacks, and stopped on
  dispose. An unreadable state (`null`) or one that agrees with the cache writes
  nothing, so a steady state costs one Win32 read per second and no store write.

- **[DI-05] Cached shell HWNDs are revalidated, not trusted.** Both handles
  (`SHELLDLL_DefView` for the command, `SysListView32` for the state read) are
  cached and cleared on the `TaskbarCreated` broadcast, but
  `resolve_desktop_shell_view` also revalidates with `IsWindow` before use: the
  broadcast can be missed, and a stale handle must degrade to a re-resolve, not
  to a command posted at a dead window. `FindWindowEx` ignores visibility, so
  the walk resolves in both icon states. The Windows 10/11 WorkerW fallback
  stays: with a wallpaper host in between, the view is not a direct child of
  Progman.

- **[DI-06] The pre-0.15 hide is repaired once, then never touched again.**
  `ShowWindow(SW_HIDE)` hid the list-view behind Explorer's back, so every
  machine upgrading with the setting ON is in a divergent state: window hidden,
  shell still believing icons are shown (observed in the field:
  `IsWindowVisible` false with `HideIcons=0` and `FFlags` NOICONS clear). From
  there the [DI-02] apply is correct but useless — asked to SHOW, it reads
  "hidden", posts the toggle, and the shell flips its own "shown" belief to
  "hidden", leaving the icons exactly where they were. So
  `repair_legacy_desktop_icon_hide` undoes the legacy hide (`SW_SHOW`) and
  immediately re-hides through the shell, converging both views with no visible
  state change. Two guards make this safe, and both are load-bearing:
  * It runs only when the STORED setting is `false` — only then did Themia hide
    them. A hide the user made from the desktop menu is already the shell's own.
  * It is claimed exactly once per profile (`widget-design-migration-v10`), and
    the marker is written even when it does not claim. A legitimate
    shell-owned hide is indistinguishable from the legacy one by inspection, so
    a repair that could fire twice would eventually "repair" a hide the user
    actually wanted, showing the icons they just hid.

## Manual / live verification (not reachable by CI)

1. Toggle "Show desktop icons" OFF in Settings, then right-click the desktop:
   View > Show desktop icons must be UNCHECKED (proves the shell owns the
   state, which the pre-0.15 `SW_HIDE` did not).
2. With icons hidden, quit Themia — icons stay hidden ([DI-03]). Reboot —
   still hidden.
3. With icons hidden, restart explorer.exe from Task Manager — icons stay
   hidden, and the Settings toggle still applies afterwards ([DI-05]).
4. Hide icons from the DESKTOP context menu while Themia runs, then open
   Settings: the toggle must read "Hidden" ([DI-04]). Repeat with Settings
   ALREADY open on the General page: click the desktop, hide icons from its
   context menu, click back into the config window — the toggle must flip to
   "Hidden" on focus without reopening the window. On a second monitor, with the
   config window left visible and focused on General, toggle icons from the
   desktop menu: the toggle must follow within ~1s ([DI-04] poll).
5. Repeat 1–4 on Windows 10 as well as 11 ([DI-01] command-id parity).
6. [DI-06] Upgrade path: on a profile that hid icons with the pre-0.15 build
   (icons hidden, desktop menu still showing "Show desktop icons" CHECKED),
   install this build. Icons must stay hidden, the desktop menu must now read
   UNCHECKED, and the repair must not re-fire on the next launch (hide icons,
   restart Themia, confirm they stay hidden).

# Drag & drop target registration: SPEC

WebView2 owns the OLE drop target of its own window tree, so the app revokes
every descendant's target and registers ONE `IDropTarget` of its own. Which
descendant it lands on decides whether the whole window can receive drags at
all.

- [DD-01] The host HWND is chosen by CLASS PREFERENCE, never by enumeration
  order: `Chrome_RenderWidgetHostHWND` > `Chrome_WidgetWin_0` >
  `Chrome_WidgetWin_1` > `WRY_WEBVIEW` > anything else, restricted to children
  whose `RevokeDragDrop` succeeded (i.e. that actually carried a target), ties
  going to the FIRST match. `Intermediate D3D Window` is excluded outright: it
  is a DirectComposition surface that can carry a registered `IDropTarget` but
  never receives the drag. When no child qualifies, the parent window is used.
  Pinned by `drop_target_choice_tests` in `src-tauri/src/drag_drop/mod.rs`,
  which encodes the two real window trees below.
  (The bug: the chooser kept the LAST child that had a target. On a two-display
  machine monitor-0 enumerated as `WRY_WEBVIEW(no) / Chrome_WidgetWin_0(YES) /
  Chrome_WidgetWin_1(no) / Chrome_RenderWidgetHostHWND(no)` and worked, while
  monitor-1 enumerated the same PLUS `Intermediate D3D Window(YES)` last — so
  that monitor registered its drop target on the composition surface. Every drop
  on that display silently missed the app: no file/URL drops, and a shortcut
  dragged inside its own widget fell through to the desktop, which took it as a
  move, so the item disappeared instead of reordering.)

## Manual / live verification (not reachable by CI)

The window tree depends on the machine's compositor, so the failing shape only
appears on some displays. On a multi-monitor machine, per display: drop a file
onto a Folder or Shortcut widget, and drag a Shortcut item onto another item in
the same widget. Both must work on EVERY display. The chosen HWND is logged at
`INFO`/`DEBUG` under `drag_drop` ("chosen target: …") for each monitor window.
