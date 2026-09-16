import { createSignal, createMemo, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoNetHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { ArrowDown, ArrowUp } from 'lucide-solid';
import './NetWidget.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface NetWidgetProps {
  headerMode?: HeaderMode;
  interval?: number;
  showGraph?: boolean;
  label?: string;
  visibleMetrics?: unknown;
  graphSpanSec?: number;
}

interface NetIface {
  name: string;
  rx_per_sec: number;
  tx_per_sec: number;
  is_up?: boolean;
}

interface SystemStats {
  net_rx_per_sec: number;
  net_tx_per_sec: number;
  net_interfaces?: NetIface[];
  net_session_rx?: number;
  net_session_tx?: number;
  public_ip?: string | null;
}

interface SparkPaths {
  line: string;
  area: string;
}

const uid = Math.random().toString(36).slice(2, 8);

function fmtSpeed(bytes: number): string {
  if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824).toFixed(2) + ' GB/s';
  if (bytes >= 1_048_576)     return (bytes / 1_048_576).toFixed(1) + ' MB/s';
  if (bytes >= 1_024)         return (bytes / 1_024).toFixed(0) + ' KB/s';
  return bytes + ' B/s';
}

function fmtBytes(bytes: number): string {
  if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824).toFixed(2) + ' GB';
  if (bytes >= 1_048_576)     return (bytes / 1_048_576).toFixed(1) + ' MB';
  if (bytes >= 1_024)         return (bytes / 1_024).toFixed(0) + ' KB';
  return bytes + ' B';
}

function buildSpark(pts: number[], maxVal: number, capacity: number): SparkPaths {
  if (pts.length < 2 || maxVal === 0) return { line: '', area: '' };
  const n = pts.length;
  const coords = pts.map((v, i) => [
    sampleX(i, n, capacity),
    +(100 - (v / maxVal) * 100).toFixed(2),
  ]);
  const line = 'M ' + coords.map(([x, y]) => `${x} ${y}`).join(' L ');
  const area = `M ${coords[0][0]} 100 ` +
               coords.map(([x, y]) => `L ${x} ${y}`).join(' ') +
               ` L ${coords.at(-1)![0]} 100 Z`;
  return { line, area };
}

export default function NetWidget(props: NetWidgetProps): JSX.Element {
  const interval  = () => refreshInterval(props.interval, 'net');
  const label     = () => props.label ?? '';
  const capacity  = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.net), interval()));

  const visible = createMemo<string[]>(() => {
    const raw = props.visibleMetrics;
    if (raw === undefined || raw === null) {
      const out = canonicalVisible(METRIC_DEFS).slice();
      if (props.showGraph === false) {
        const i = out.indexOf('graph');
        if (i >= 0) out.splice(i, 1);
      }
      return out;
    }
    return resolveMetrics(raw, METRIC_DEFS).visible;
  });
  const shows = (k: string) => visible().includes(k);

  const [stats, setStats]     = createSignal<SystemStats | null>(null);
  const [rxHist, setRxHist]   = createSignal<number[]>([]);
  const [txHist, setTxHist]   = createSignal<number[]>([]);

  async function refresh() {
    try {
      const s = await invoke('get_system_stats', {});
      setStats(s);
      setRxHist(h => [...h, s.net_rx_per_sec].slice(-capacity()));
      setTxHist(h => [...h, s.net_tx_per_sec].slice(-capacity()));
    } catch (e) { logger.error('net', `get_system_stats failed: ${e}`); }
  }

  if (isDemo()) {
    const seed = demoNetHistory(capacity());
    setRxHist(seed.rx);
    setTxHist(seed.tx);
  }

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'net');

  // Draw at most `capacity` samples; scale both traces to the max within the
  // visible window so the y-axis matches what is drawn.
  const rxWin    = createMemo(() => rxHist().slice(-capacity()));
  const txWin    = createMemo(() => txHist().slice(-capacity()));
  const scaleMax = createMemo(() => Math.max(1024, ...rxWin(), ...txWin()));
  const rxSpark  = createMemo(() => buildSpark(rxWin(), scaleMax(), capacity()));
  const txSpark  = createMemo(() => buildSpark(txWin(), scaleMax(), capacity()));

  const gradRxId = `net-rx-${uid}`;
  const gradTxId = `net-tx-${uid}`;

  return (
    <div class="netw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || 'Network'}
        secondary={
          <>
            <Show when={stats() && shows('rx')}>
              <span class="netw-info-rx"><ArrowDown class="netw-arrow" size={11} strokeWidth={2} /> {fmtSpeed(stats()!.net_rx_per_sec)}</span>
            </Show>
            <Show when={stats() && shows('tx')}>
              <span class="netw-info-tx"><ArrowUp class="netw-arrow" size={11} strokeWidth={2} /> {fmtSpeed(stats()!.net_tx_per_sec)}</span>
            </Show>
          </>
        }
      />

      <div class="netw-body widget-body">
      <Show when={shows('graph')}>
        <div class="netw-spark-wrap">
          <Show when={stats()}>
            <svg class="netw-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
              <defs>
                <linearGradient id={gradRxId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stop-color="var(--success)" stop-opacity="0.25"/>
                  <stop offset="100%" stop-color="var(--success)" stop-opacity="0"/>
                </linearGradient>
                <linearGradient id={gradTxId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stop-color="var(--primary)" stop-opacity="0.25"/>
                  <stop offset="100%" stop-color="var(--primary)" stop-opacity="0"/>
                </linearGradient>
              </defs>
              <path d={rxSpark().area} fill={`url(#${gradRxId})`} />
              <path d={rxSpark().line} fill="none" stroke="var(--success)" stroke-width="1.5"
                    stroke-linecap="round" stroke-linejoin="round"
                    vector-effect="non-scaling-stroke" />
              <path d={txSpark().area} fill={`url(#${gradTxId})`} />
              <path d={txSpark().line} fill="none" stroke="var(--primary)" stroke-width="1.5"
                    stroke-linecap="round" stroke-linejoin="round"
                    vector-effect="non-scaling-stroke" />
            </svg>
          </Show>
        </div>
      </Show>

      <Show when={shows('perInterface') && (stats()?.net_interfaces?.length ?? 0) > 0}>
        <div class="netw-procs">
          <For each={stats()!.net_interfaces!.filter(i => i.rx_per_sec > 0 || i.tx_per_sec > 0).slice(0, 3)}>
            {(iface) => (
              <div class="netw-proc-row">
                <span class="netw-proc-name">{iface.name}</span>
                <span class="netw-proc-val"><ArrowDown class="netw-arrow" size={10} strokeWidth={2} /> {fmtSpeed(iface.rx_per_sec)} <ArrowUp class="netw-arrow" size={10} strokeWidth={2} /> {fmtSpeed(iface.tx_per_sec)}</span>
              </div>
            )}
          </For>
        </div>
      </Show>

      <Show when={shows('session') && stats()?.net_session_rx != null && stats()?.net_session_tx != null}>
        <div class="netw-aux-row">
          <span class="netw-aux-label">{t('widgetUi.session')}</span>
          <span class="netw-aux-val"><ArrowDown class="netw-arrow" size={10} strokeWidth={2} /> {fmtBytes(stats()!.net_session_rx!)} <ArrowUp class="netw-arrow" size={10} strokeWidth={2} /> {fmtBytes(stats()!.net_session_tx!)}</span>
        </div>
      </Show>

      <Show when={shows('publicIp') && stats()?.public_ip}>
        <div class="netw-aux-row">
          <span class="netw-aux-label">{t('net.publicIp')}</span>
          <span class="netw-aux-val">{stats()!.public_ip}</span>
        </div>
      </Show>
      </div>
    </div>
  );
}
