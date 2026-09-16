# RAM Monitor: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header row shows the configured `label` (defaulting to "Memory") and used/total RAM in GB (one decimal).
- [S-02] Header is hidden when `hideHeader` is true.
- [S-03] When `showGraph` is true, renders a usage sparkline (area + line) of RAM used percent over time.
- [S-04] Sparkline retains the most recent 40 samples.
- [S-05] A horizontal bar track shows current RAM used percent as fill.
- [S-06] Usage color is cyan (`--primary`) below 70%, amber (`#f59e0b`) at 70–89%, red (`--destructive`) at 90%+; applied to the used/total value, sparkline stroke/gradient, and bar fill.

## Configuration
- [S-07] `showGraph` toggle (Display tab) shows/hides the sparkline; defaults to on.
- [S-08] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the header row.
- [S-09] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; defaults to 2s.

## Data / backend
- [S-10] Polls Tauri command `get_system_stats` every `max(1, interval)` seconds via `createPolling` with key "ram".
- [S-11] Uses `ram_used` and `ram_total` (bytes) from the shared system stats backend; percent is `ram_used / ram_total * 100`.

## Per-metric visibility
- [S-12] `visibleMetrics` config (string[]) selects which metric rows render. Defaults to `['usage','graph','bar']`. Legacy `showGraph` honoured when `visibleMetrics` is absent.
- [S-13] Available metrics: `usage`, `graph`, `bar`, `available`, `swap`, `topProcs`.
- [S-14] `available` shows `ram_available` (bytes) in GB. `swap` shows `swap_used / swap_total` in GB plus percent, hidden when `swap_total = 0`.
- [S-15] `topProcs` shows the `topProcsCount` ([S-17]) highest-memory processes (name + bytes) from backend `ram_top`.
- [S-16] The widget passes `includeProcs: true` to `get_system_stats` only when `topProcs` is visible; otherwise the backend skips the process-table refresh and `ram_top` comes back empty (see cpu [S-21]).
- [S-17] `topProcsCount` config (Display tab, visible only while `topProcs` is enabled) selects how many memory process rows render: 1–5, default 3. The value is clamped and non-numeric persisted junk falls back to the default (`clampRamProcCount`, `procsDisplay.ts`); old configs without the field load with the default. The backend returns up to 5 rows (`top_mem_procs` `.take(5)`) so the frontend slice never starves; the count is a display concern only and does not change the IPC args ([S-16]). Mirrors CPU [S-26] but capped at 5 rather than 10.
