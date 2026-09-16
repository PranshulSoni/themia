# Battery: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] When the backend reports no battery (`has_battery` false), renders a "No battery detected" message instead of the main view.
- [S-02] Main view shows a battery-shaped SVG icon with a fill rectangle proportional to the current percent.
- [S-03] When `is_charging` is true, a lightning bolt icon is overlaid on the battery icon.
- [S-04] Shows the current percent as an integer and a status line below it.
- [S-05] Status text:
  - "Charging" when charging.
  - "Fully charged" when on AC and percent ≥ 99.
  - Otherwise `{h}h {m}m remaining` when `seconds_remaining` is a positive number.
  - "Plugged in" when on AC with no time estimate.
  - "On battery" when discharging with no time estimate.
- [S-06] Battery fill / graph color: `--primary` when charging; otherwise green (`#22c55e`) above 50%, amber (`#f59e0b`) 21–50%, red (`--destructive`) at 20% or below.
- [S-07] When `showGraph` is true and at least 2 samples exist, renders a percent-over-time sparkline below the main view; history retains the most recent 40 samples and is only appended while `has_battery` is true.

## Configuration
- [S-08] `showGraph` toggle (Display tab) shows/hides the sparkline; defaults to on.
- [S-09] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the widget title bar.
- [S-10] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; default config value is 10s.

## Data / backend
- [S-11] Polls Tauri command `get_battery_stats` every `max(1, interval)` seconds via `createPolling` with key "battery".
- [S-12] Also refreshes when `_refresh` prop changes (used for externally-triggered reloads).
- [S-13] Backend returns `percent`, `is_charging`, `is_on_ac`, `seconds_remaining` (nullable), `has_battery`.

## Per-metric visibility
- [S-14] `visibleMetrics` config selects which rows render. Defaults to `['percent','status','eta','graph']`. Legacy `showGraph` honoured when absent.
- [S-15] Available metrics: `percent`, `status`, `eta`, `graph`, `health`, `cycles`, `voltage`, `temperature`.
- [S-16] Hiding `eta` removes the "{h}h {m}m remaining" status fallback (status shows only Charging / Fully charged / Plugged in / On battery).
- [S-17] `health`, `cycles`, `voltage`, `temperature` read backend fields `health_pct` / `cycle_count` / `voltage_mv` / `temp_c`, sourced from WMI `root\WMI` namespace (`BatteryStaticData`, `BatteryFullChargedCapacity`, `BatteryCycleCount`, `BatteryStatus`, `BatteryTemperature`). Rows hide when the value is `null` (firmware doesn't expose it).
- [S-18] `health_pct` = `full_charge_capacity_mwh / design_capacity_mwh * 100`; both source values must be > 0.
