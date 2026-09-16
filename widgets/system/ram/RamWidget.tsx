import { createSignal, createMemo, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoRamPctHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { ramStatsArgs } from '../../../src/widgets/systemStatsArgs.ts';
import { METRIC_DEFS } from './metrics.ts';
import { clampRamProcCount } from './procsDisplay.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './RamWidget.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface RamWidgetProps {
  headerMode?: HeaderMode;
  interval?: number;
  showGraph?: boolean;
  label?: string;
  visibleMetrics?: unknown;
  topProcsCount?: number;
  graphSpanSec?: number;
}

interface ProcMem { name: string; pid: number; mem_bytes: number }

interface SystemStats {
  ram_used: number;
  ram_total: number;
  ram_available?: number;
  swap_used?: number;
  swap_total?: number;
  ram_top?: ProcMem[];
}

interface SparkPaths {
  line: string;
  area: string;
}

const uid = Math.random().toString(36).slice(2, 8);

function usageColor(p: number): string {
  if (p >= 90) return 'var(--destructive)';
  if (p >= 70) return '#f59e0b';
  return 'var(--primary)';
}

function gb(bytes: number): string {
  return (bytes / 1_073_741_824).toFixed(1);
}

function fmtMem(b: number): string {
  if (b >= 1_073_741_824) return (b / 1_073_741_824).toFixed(1) + ' GB';
  return (b / 1_048_576).toFixed(0) + ' MB';
}

function buildSpark(pts: number[], capacity: number): SparkPaths {
  if (pts.length < 2) return { line: '', area: '' };
  const n = pts.length;
  const coords = pts.map((v, i) => [
    sampleX(i, n, capacity),
    +(100 - Math.max(0, Math.min(100, v))).toFixed(2),
  ]);
  const line = 'M ' + coords.map(([x, y]) => `${x} ${y}`).join(' L ');
  const area = `M ${coords[0][0]} 100 ` +
               coords.map(([x, y]) => `L ${x} ${y}`).join(' ') +
               ` L ${coords.at(-1)![0]} 100 Z`;
  return { line, area };
}

export default function RamWidget(props: RamWidgetProps): JSX.Element {
  const interval  = () => refreshInterval(props.interval, 'ram');
  const label     = () => props.label ?? '';
  const capacity  = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.ram), interval()));

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
  const [history, setHistory] = createSignal<number[]>([]);

  async function refresh() {
    try {
      const s = await invoke('get_system_stats', ramStatsArgs(visible()));
      setStats(s);
      const pct = s.ram_total ? (s.ram_used / s.ram_total) * 100 : 0;
      setHistory(h => [...h, pct].slice(-capacity()));
    } catch (e) { logger.error('ram', `get_system_stats failed: ${e}`); }
  }

  if (isDemo()) setHistory(demoRamPctHistory(capacity()));

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'ram');

  const spark  = createMemo(() => buildSpark(history().slice(-capacity()), capacity()));
  const ramPct = createMemo(() => {
    const s = stats();
    return s && s.ram_total ? (s.ram_used / s.ram_total) * 100 : 0;
  });
  const color  = createMemo(() => usageColor(ramPct()));
  const gradId = `ram-grad-${uid}`;

  const swapPct = createMemo(() => {
    const s = stats();
    return s && s.swap_total ? (s.swap_used! / s.swap_total) * 100 : 0;
  });

  return (
    <div class="ramw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || 'Memory'}
        secondary={
          <Show when={stats() && shows('usage')}>
            <span class="ramw-info-val" style={{ color: color() }}>
              {gb(stats()!.ram_used)}<span class="ramw-info-sep"> / </span>{gb(stats()!.ram_total)} GB
            </span>
          </Show>
        }
      />

      <div class="ramw-body widget-body">
      <Show when={shows('graph')}>
        <div class="ramw-spark-wrap">
          <Show when={stats()}>
            <svg class="ramw-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
              <defs>
                <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stop-color={color()} stop-opacity="0.30"/>
                  <stop offset="100%" stop-color={color()} stop-opacity="0"/>
                </linearGradient>
              </defs>
              <path d={spark().area} fill={`url(#${gradId})`} />
              <path d={spark().line} fill="none" stroke={color()} stroke-width="1.5"
                    stroke-linecap="round" stroke-linejoin="round"
                    vector-effect="non-scaling-stroke" />
            </svg>
          </Show>
        </div>
      </Show>

      <Show when={stats() && shows('bar')}>
        <div class="ramw-bar-track">
          <div class="ramw-bar-fill" style={{ width: `${ramPct()}%`, background: color() }} />
        </div>
      </Show>

      <Show when={shows('available') && stats()?.ram_available != null}>
        <div class="ramw-aux-row">
          <span class="ramw-aux-label">{t('widgetUi.available')}</span>
          <span class="ramw-aux-val">{gb(stats()!.ram_available!)} GB</span>
        </div>
      </Show>

      <Show when={shows('swap') && (stats()?.swap_total ?? 0) > 0}>
        <div class="ramw-aux-row">
          <span class="ramw-aux-label">{t('widgetUi.swap')}</span>
          <span class="ramw-aux-val">{gb(stats()!.swap_used!)} / {gb(stats()!.swap_total!)} GB ({swapPct().toFixed(0)}%)</span>
        </div>
      </Show>

      <Show when={shows('topProcs') && (stats()?.ram_top?.length ?? 0) > 0}>
        <div class="ramw-procs">
          {/* [S-17] row count follows the topProcsCount config (1..5). */}
          <For each={stats()!.ram_top!.slice(0, clampRamProcCount(props.topProcsCount))}>
            {(p) => (
              <div class="ramw-proc-row">
                <span class="ramw-proc-name">{p.name}</span>
                <span class="ramw-proc-val">{fmtMem(p.mem_bytes)}</span>
              </div>
            )}
          </For>
        </div>
      </Show>
      </div>
    </div>
  );
}
