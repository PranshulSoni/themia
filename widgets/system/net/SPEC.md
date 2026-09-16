# Network Monitor: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header row shows the configured `label` (defaulting to "Network"), a down-arrow with current RX rate, and an up-arrow with current TX rate.
- [S-02] Header is hidden when `hideHeader` is true.
- [S-03] Rates are formatted with adaptive units: B/s, KB/s (int), MB/s (one decimal), or GB/s (two decimals) based on magnitude.
- [S-04] When `showGraph` is true, renders two overlaid sparklines: RX in `--success` color, TX in `--primary` color, each with its own area gradient.
- [S-05] Both sparklines are scaled against a shared max of `max(1024, ...rxHist, ...txHist)` so the two rates are directly comparable.
- [S-06] Sparklines retain the most recent 40 samples each.

## Configuration
- [S-07] `showGraph` toggle (Display tab) shows/hides the sparklines; defaults to on.
- [S-08] `hideHeader` toggle (config Header field, shared by all widgets) shows/hides the header row.
- [S-09] `interval` segmented control (Refresh tab) selects polling period from {1, 2, 5, 10, 30} seconds; defaults to 2s.

## Data / backend
- [S-10] Polls Tauri command `get_system_stats` every `max(1, interval)` seconds via `createPolling` with key "net".
- [S-11] Uses `net_rx_per_sec` and `net_tx_per_sec` (bytes/sec) aggregated across all interfaces (excluding capture mirrors, [S-17]) via `sysinfo::Networks` in the shared backend.

## Per-metric visibility
- [S-12] `visibleMetrics` config selects which rows render. Defaults to `['rx','tx','graph']`. Legacy `showGraph` honoured when absent.
- [S-13] Available metrics: `rx`, `tx`, `graph`, `perInterface`, `session`, `publicIp`.
- [S-14] `perInterface` shows up to 3 active interfaces (rx + tx > 0) sorted by total activity, sourced from backend `net_interfaces[]`.
- [S-15] `session` shows cumulative `net_session_rx` / `net_session_tx` since the host process started.
- [S-16] `publicIp` shows the public IP from backend `public_ip`. Fetched from `api.ipify.org` once per 10 minutes in a background thread; first poll after startup shows nothing until the fetch completes.
- [S-17] Interfaces whose name marks them as a packet-capture filter mirror (case-insensitive contains "pcap packet driver", e.g. `Ethernet-Npcap Packet Driver (NPCAP)-0000`) are excluded from the aggregate rates, session totals, and the per-interface list, since they re-report the underlying NIC's counters and would double every value. The standalone `Npcap Loopback Adapter` is NOT excluded (distinct loopback traffic). (Regression: themia-releases#33.)
