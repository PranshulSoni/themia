# Disk Monitor: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header row shows the configured `label`, falling back to the active disk's mount, then "Disk"; and current read rate ("R ...") and write rate ("W ...") for the **selected disk** (per-mount, not system-wide; themia-releases#49).
- [S-02] Header is hidden when `hideHeader` is true.
- [S-03] Rates are formatted with adaptive units: B/s, KB/s (int), MB/s (one decimal), or GB/s (two decimals).
- [S-04] When `showGraph` is true, renders two overlaid sparklines: read in `--success`, write in `--primary`, each with its own area gradient.
- [S-05] Both sparklines are scaled against a shared max of `max(1024, ...readHist, ...writeHist)`.
- [S-06] Sparklines retain the most recent 40 samples each.
- [S-07] When the selected disk is available, a space row shows used, total (both formatted in MB or GB), and used-percent; below it a bar track shows used-percent fill.
- [S-08] Space color is cyan (`--primary`) below 80%, amber (`#f59e0b`) at 80–89%, red (`--destructive`) at 90%+; applied to percent text and bar fill.
- [S-09] Active disk resolution (single mode): match the first selected mount against `stats.disks[].mount`; if no match (or no selection), fall back to the first disk.
- [S-24] Render layout follows the selected-drive count (`diskLayout`, #67): 0 or 1 selected → single-drive view (one drive stretched to fill, exactly as before #67); 2+ → each selected drive's space bar/percent stacked in a container that overflow-scrolls (hidden scrollbar, `scrollbar-width: none` + `::-webkit-scrollbar { display:none }`) when the drives exceed the widget height, each block stretching to share the height. In multi mode the header shows "Disks" (or `label`), and the per-disk R/W secondary, graph and IOPS rows are single-mode only.
- [S-25] Clicking a drive opens it in Explorer (#64) via the shared `open_in_explorer` command with the drive's mount path (e.g. `C:\`). The click target is the drive's space row/bar in single mode and each stacked drive block in multi mode, with a `var(--card-hover)` + `cursor: pointer` hover affordance. Errors are logged, never swallowed.

## Configuration
- [S-10] Disk selection (Source tab) is a **multi-select** (`MultiSelectDropdown`) over all disks with `total_space > 0` reported by the backend; the chosen mounts persist as `mounts: string[]`. Selecting none shows the primary/active disk ([S-09]).
- [S-23] Config migration (#67): `mounts?: string[]` is the source of truth. `resolveMounts` prefers an explicit `mounts` (even an empty array) and migrates a legacy `mount?: string` to `[mount]` only when `mounts` is absent — an empty `mounts` is a real "no selection", distinct from absent. Non-string entries in a corrupt `mounts` array are dropped.
- [S-11] `showGraph` toggle (Display tab) shows/hides the sparklines; defaults to on.
- [S-12] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the header row.
- [S-13] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; default config value is 5s.

## Data / backend
- [S-14] Polls Tauri command `get_system_stats` every `max(1, interval)` seconds via `createPolling` with key "disk".
- [S-15] On config mount, calls `get_system_stats` once to populate the disk list.
- [S-16] The header read/write rate and the graph history both track the **selected disk's** per-mount `read_per_sec` / `write_per_sec` from `disks[]` (PDH `\LogicalDisk(*)`), resolved via [S-09]. Missing per-mount rates report 0 rather than falling back to the system-wide total. (System-wide `disk_read_per_sec` / `disk_write_per_sec` from `NtQuerySystemInformation` deltas are no longer surfaced by this widget; IOPS below is the only remaining system-wide figure.)

## Per-metric visibility
- [S-17] `visibleMetrics` config selects which rows render. Defaults to `['space','ioBytes','graph']`. Legacy `showGraph` honoured when absent.
- [S-18] Available metrics: `space`, `ioBytes`, `graph`, `iops`, `perDisk`.
- [S-19] `iops` shows `disk_read_ops_per_sec` / `disk_write_ops_per_sec` (system-wide, from `NtQuerySystemInformation`).
- [S-20] `perDisk` lists each disk's `read_per_sec` / `write_per_sec` from backend `disks[]`. Per-mount rates populated by PDH `\LogicalDisk(*)\Disk Read|Write Bytes/sec` matched by mount letter.
- [S-21] Backend caches the volume list: a tick refreshes only free space (`GetDiskFreeSpaceExW`) on the cached list; the full volume re-enumeration (`FindFirstVolumeW` walk, which can spin up removable drives) runs at most every 30 s, so newly mounted/removed drives appear within 30 s.
- [S-22] Changing the selected `mount` ([S-10]) clears the graph history so the sparkline never carries the previously monitored partition's samples.
