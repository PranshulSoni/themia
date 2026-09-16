# Bluetooth: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header row shows a Bluetooth icon and the configured `label` (defaulting to "Bluetooth").
- [S-02] Header is hidden when `hideHeader` is true.
- [S-03] Shows "Loading..." before the first response returns.
- [S-04] When the device list is empty after loading, shows "No paired devices found".
- [S-05] Otherwise renders a list of paired devices; each item shows a type-specific icon, the device name (or "Unknown device" if empty), and a connection status indicator with a `connected` class when `is_connected` is true.
- [S-06] Device icon is selected by `device_type`: `audio` → Headphones, `mouse` → Mouse, `keyboard` → Keyboard, `phone` → Smartphone, `gamepad` → Gamepad2, otherwise a generic Bluetooth icon.

## Configuration
- [S-07] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the header row.
- [S-08] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; default config value is 10s.

## Context menu
- [S-09] Widget-level "Refresh" action forces an immediate device re-query by updating `_refresh` with the current timestamp.

## Data / backend
- [S-10] On mount, and whenever `interval` or `_refresh` changes, calls Tauri command `get_bluetooth_devices` and then polls it every `max(1, interval)` seconds via `setInterval`.
- [S-11] Backend returns an array of `{ name, id, is_connected, device_type, battery_pct, rssi }`; `device_type` is inferred from the device name (Windows enumeration path).
- [S-12] Backend enumerates both Classic (`BluetoothDevice`) and LE (`BluetoothLEDevice`) paired devices and merges by name so a single device shows once.
- [S-13] For each connected device, backend best-effort reads GATT Battery Service (UUID 0x180F) → Battery Level characteristic (0x2A19) and reports as `battery_pct`. Disconnected devices skipped (would block on radio scan timeout).

## Per-metric visibility
- [S-14] `visibleMetrics` config selects which per-device elements render. Defaults to `['deviceName','type','connection','battery']`.
- [S-15] Available metrics: `deviceName`, `type`, `connection`, `battery`, `rssi`.
- [S-16] `battery` and `rssi` rows hide automatically when the device doesn't report the value (e.g. non-LE devices, devices without Battery Service).
