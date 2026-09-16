# GPU Monitor: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] In single-GPU mode the header row shows the configured `label`, falling back to the GPU's reported `name`, then "GPU"; and the current 3D-engine utilization percent to one decimal. In multi-GPU mode ([S-26]) the header shows `label` or "GPU" and no utilization (each panel carries its own).
- [S-02] Header is hidden when `hideHeader` is true.
- [S-03] When `showGraph` is true, renders a utilization sparkline (area + line) over time.
- [S-04] Sparkline retains the most recent 40 samples.
- [S-05] Utilization color is cyan (`--primary`) below 70%, amber (`#f59e0b`) at 70–89%, red (`--destructive`) at 90%+.
- [S-06] When the GPU reports dedicated VRAM usage that is consistent (used ≤ total, or total unknown but used > 0), renders a "Dedicated" row with used (and total when known) formatted in MB or GB plus a bar track.
- [S-07] When the reported dedicated usage exceeds the registry-reported total (common on integrated GPUs), the Dedicated row is suppressed.
- [S-08] When shared-memory `shared_used_mb` and `shared_total_mb` are both > 0, renders a "Shared" row with used/total and a bar track.
- [S-09] VRAM bar color is cyan below 80%, amber at 80–89%, red at 90%+.
- [S-10] Switching a panel's `gpuIndex` clears that panel's stats and history so the new GPU's sparkline starts fresh.

## Configuration
- [S-11] `gpuIndexes` multi-select (Source tab, `MultiSelectDropdown`) selects which GPUs to monitor; only shown when more than one GPU is detected. Options are populated from the `list_gpus` command. Legacy single `gpuIndex` is migrated to `[gpuIndex]` ([S-24]).
- [S-12] `showGraph` toggle (Display tab) shows/hides the sparkline; defaults to on.
- [S-13] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the header row.
- [S-14] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; defaults to 2s.

## Data / backend
- [S-15] Each monitored GPU polls Tauri command `get_gpu_stats` with `{ gpuIndex }` (its own index) every `max(1, interval)` seconds via `createPolling` with key "gpu".
- [S-16] On config mount, calls Tauri command `list_gpus` once to populate the GPU dropdown.
- [S-17] Backend returns `name`, `utilization` (0–100 %), `vram_used_mb`, `vram_total_mb`, `shared_used_mb`, `shared_total_mb`; sourced from Windows registry (VRAM total) and PDH counters (VRAM/shared usage) on Windows.
- [S-22] `utilization` prefers NVML `utilization_rates().gpu` on NVIDIA cards (true GPU-Util, matches nvidia-smi); falls back to the PDH `\GPU Engine(*engtype_3D)\Utilization Percentage` sum on non-NVIDIA / NVML-missing hardware. The result is clamped to 0–100. (NVML avoids the borderless-fullscreen present-mode quirk where the PDH 3D-engine counter under-reports a foreground game; see themia-releases#32.)

## Per-metric visibility
- [S-18] `visibleMetrics` config selects which rows render. Defaults to `['utilization','graph','vram','shared']`. Legacy `showGraph` honoured when absent.
- [S-19] Available metrics: `utilization`, `graph`, `vram`, `shared`, `temperature`, `power`, `fan`, `clocks`, `encoder`.
- [S-20] `temperature`, `power`, `fan`, `clocks`, `encoder` are populated via `nvml-wrapper` on NVIDIA cards only; rows hide automatically when the value is `null` (non-NVIDIA hardware or driver missing).
- [S-21] NVML init is attempted once per process; failure leaves all NVIDIA-only fields `None`.
- [S-23] `get_gpu_stats` is an async command executed via `spawn_blocking`, so PDH/NVML sampling never runs on the main event-loop thread.

## Multi-GPU selection
- [S-24] The widget monitors one or several GPUs. `gpuIndexes` (array of registry indexes) is the current selection field; the legacy single `gpuIndex` is migrated to `[gpuIndex]` when `gpuIndexes` is absent. `resolveGpuIndexes` (`gpuSelection.ts`) owns the migration.
- [S-25] The persisted selection is validated at load (JSON trust boundary): non-integer and negative entries are dropped and duplicates removed (order preserved); an absent or wholly-corrupt selection defaults to `[0]` (the primary GPU). "Absent" and "corrupt" are distinguished — a corrupt `gpuIndexes` falls through to the legacy field, then the default, never crashing or blanking the widget.
- [S-26] Layout rule (`itemLayout`): one selected GPU stretches to fill the widget (unchanged single-GPU rendering; name + utilization in the header, [S-01]). Two or more selected GPUs render as stacked panels — each with its own name + utilization heading and a divider between them — and the body scrolls (`overflow-y: auto`, scrollbar hidden per the widget-body convention) when the panels exceed the widget height.
- [S-27] Each selected GPU is an independent panel that owns its own poll loop, stats, and 40-sample utilization history; panels do not share state, so two GPUs never mirror each other's readings.
- [S-28] The Source-tab picker reflects the RAW persisted selection (`selectedGpuIndexes`, `gpuSelection.ts`): migrated + validated but WITHOUT the `[0]` runtime default. Unticking the last GPU writes `gpuIndexes: []` and leaves the picker empty — the `[0]` primary-GPU default ([S-25]) is a display concern only and must never re-tick a GPU in the picker.
