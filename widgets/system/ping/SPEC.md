# Ping: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header title is the first non-empty of: the widget's header override, the configured host label, the host address, `"Ping"`. Header mode is `props.headerMode ?? 'compact'`.
- [S-02] With no host address configured, the body renders only an empty state ("Select a host to see your latency." plus a "Click ⚙ to configure." hint) — no status, latency, graph or aux rows. The gate is the *configured* address, so the demo fallback host [S-20] never opens the body.
- [S-03] The status row shows a dot and a word: "Online" once a ping succeeds, "Offline" once one fails, "Checking…" until the first result arrives.
- [S-04] Status color is `--success` when online, `--destructive` when offline, and `--subtle-foreground` before the first result. It tints the dot, the status text, the latency value, and the sparkline's stroke and gradient together.
- [S-05] Latency formatting: `—` when there is no measurement, `{n}ms` below 1000 ms, `{n.n}s` (one decimal) at 1000 ms and above.
- [S-06] The address row renders only when the host has *both* a label and an address, so the address is not repeated when the header [S-01] has already fallen back to showing it.
- [S-07] The graph is a latency sparkline (area gradient under a line). It needs at least 2 samples and at least one successful one, else it renders empty paths. Values scale against `max(successful samples, 1)` over a 94% band; a failed sample plots on the baseline rather than breaking the line.
- [S-08] History keeps the 40 most recent samples. Every poll appends one entry: the latency, or `null` for a failed ping.
- [S-09] The jitter row is the mean absolute difference between consecutive *successful* samples, rounded and formatted per [S-05]. Failed samples are dropped rather than breaking the series, so jitter can compare two samples that were not adjacent in time. The row is hidden until at least one ping succeeds, and reads `0ms` when exactly one has.
- [S-10] The loss row is the share of `null` samples across the whole history, to 0 decimals. **Known defect:** a history with no successful sample short-circuits to `0`, so a host that has *never* answered reads "Loss 0%" instead of 100%.
- [S-11] The min / avg / max row is computed over successful samples only (avg rounded), and is hidden until the first success.

## Configuration
- [S-12] The Source section's two text inputs write `host.label` and `host.address`, trimmed on every keystroke.
- [S-13] Every field renders from `props.config` directly; the panel keeps no local mirror of config state, so an edit to one field can never write a stale value back over another. (Regression: `host` was mirrored into local state at mount, and editing the interval rewrote `host` from that mount-time snapshot — a lost update. `ConfigWindow`'s `keyed` remount was covering for it.)
- [S-14] The Display section's metric multi-select writes `visibleMetrics`; reordering is disabled.
- [S-15] The header title is the shared `ConfigTabs` header field, which writes the *widget-level* label, not `config.label`. The registry's `defaultConfig.label` is vestigial: the grid spreads config and then overrides `label` with the widget-level value, so `config.label` never reaches the widget.
- [S-16] The Refresh section's stepper spans 1..60 s in steps of 1. The value resolves through the shared `refreshInterval(config.interval, 'ping')` against `REFRESH_DEFAULTS.ping` = **30 s** — absent or corrupt yields 30, and a stored value passes through unclamped (see `widgets/system/refreshInterval.ts`).

## Data / backend
- [S-17] Polls the Tauri command `ping_hosts` with a single-element `hosts` array every `max(1, refreshInterval(interval, 'ping'))` seconds via `createPolling` with key "ping". Changing the host re-polls immediately.
- [S-18] Also refreshes on mount and whenever `_refresh` changes (the registry's "Refresh" context action), because `createPolling` staggers its first tick.
- [S-19] Reads the first element of the result array. A failed `invoke` is logged via `logger.error('ping', …)` and leaves the last result and the history untouched — no toast, and no data cleared on error.
- [S-20] In demo mode, an unconfigured host falls back to Google DNS (8.8.8.8) for polling, and the history is seeded with 39 fixture samples.
- [S-21] The backend validates the address before spawning `ping.exe`, rejecting empty strings, a leading `/` or `-`, whitespace, and anything outside `[A-Za-z0-9.:_-]`; a rejected host reports `ok: false` rather than running the process (argument injection / `/t` hang). On Windows it runs `ping /n 1 /w 2000` with `CREATE_NO_WINDOW` and parses "Average = Xms", falling back to a `<number>ms` scan for non-English locales; other platforms always report unreachable. The command runs on `spawn_blocking`, off the async runtime, because a down host blocks ~2 s.

## Per-metric visibility
- [S-22] `visibleMetrics` config selects which rows render. Defaults to `['status','latency','address','graph']`. Legacy `showGraph: false` is honoured only when `visibleMetrics` is absent or null.
- [S-23] Available metrics: `status`, `latency`, `address`, `graph` (all default on), `jitter`, `loss`, `minAvgMax` (default off).
