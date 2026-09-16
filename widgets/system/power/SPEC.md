# Power: SPEC

A grid of system power controls — shut down, restart, sleep, hibernate, lock,
sign out, turn off the display. Windows-only. Mirror `[S-NN]` tags in tests.

- **[S-01]** Backend command: `power_run(action)` where `action` is one of
  `shutdown | restart | sleep | hibernate | lock | signOut | screenOff`. It
  resolves on success and rejects with the Win32 reason on failure.
- **[S-02]** The action set is a closed union at every boundary: a TS union in
  `actions.ts` (referenced by `Commands['power_run']`) and a serde enum in
  `widget_power::PowerAction`. An unknown value is a compile error on the
  frontend and a deserialization error at the IPC edge, never a silent no-op.
  Persisted `visibleActions` is filtered back through `POWER_ACTIONS` on read,
  so a key from an older build cannot reach `invoke`.
- **[S-03]** Win32 only, no `shutdown.exe` spawn: `ExitWindowsEx` (shut down,
  restart, sign out), `SetSuspendState` (sleep, hibernate), `LockWorkStation`
  (lock), and a `WM_SYSCOMMAND`/`SC_MONITORPOWER` broadcast (display off).
  `ExitWindowsEx` passes `EWX_FORCEIFHUNG`, so a hung app is overridden but a
  responsive one with unsaved work can still block, matching the Start menu.
- **[S-04]** `power_run` is `async` + `spawn_blocking`. A sync command runs on
  the main event-loop thread, and all three of `ExitWindowsEx`, the monitor-off
  broadcast and `SetSuspendState` block — `SetSuspendState` until the machine
  RESUMES, so it owns its worker thread for the whole sleep.
- **[S-05]** With `confirm` on (the default), the three session-ending actions —
  shut down, restart, sign out — arm on the first click and run on the second;
  the tile shows "Confirm" in `--destructive` while armed. Sleep, hibernate,
  lock and display-off never arm: they return to the same desktop, so a
  mis-click costs a keypress. `confirm: false` runs everything on one click.
- **[S-06]** An armed tile disarms after 5 s, when the pointer leaves the widget,
  or when another tile is armed (one armed tile at a time). The timer is cleared
  on cleanup.
- **[S-07]** A rejected `power_run` is logged AND surfaced as an error toast
  naming the action and the reason. The machine is still up, so silence would
  read as "the click did nothing".
- **[S-08]** Demo mode and the Add-Widget preview intercept `power_run` and
  return `null` without touching the backend: demo mode exists so clicking
  around is safe.
- **[S-09]** `SeShutdownPrivilege` is enabled per call for shut down, restart,
  sleep and hibernate. `AdjustTokenPrivileges` reports success even when it
  assigned nothing, so `ERROR_NOT_ALL_ASSIGNED` is checked and turned into a
  "not allowed to shut down" error rather than a shutdown that does nothing.
  Lock and display-off need no privilege. The token handle is owned by a guard
  that closes it on every path.
- **[S-10]** Layout is a CSS `auto-fit` grid (`minmax(58px, 1fr)`, or 40px with
  labels hidden), so the same instance holds one row of four at the default
  260×120 and reflows on resize with no JS measuring. Layout is scoped
  `.power-widget .power-body` (specificity 0,2,0) so it beats App.css's
  later-loading `.widget-body`.
- **[S-11]** With every action deselected the body shows the "No actions
  selected" placeholder rather than an empty card.
