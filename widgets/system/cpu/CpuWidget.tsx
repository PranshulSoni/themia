import { createSignal, createMemo } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show, For } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoCpuHistory, demoCpuCoresHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { cpuStatsArgs } from '../../../src/widgets/systemStatsArgs.ts';
import { METRIC_DEFS } from './metrics.ts';
import { fanCell } from './fanDisplay.ts';
import { tempCell } from './tempDisplay.ts';
import { displayProcName, clampProcCount } from './procsDisplay.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './CpuWidget.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface CpuWidgetProps {
  headerMode?: HeaderMode;
  interval?: number;
  showGraph?: boolean;
  showCores?: boolean;
  label?: string;
  visibleMetrics?: unknown;
  topProcsCount?: number;
  graphSpanSec?: number;
}

interface ProcInfo { name: string; pid: number; cpu_pct: number; desc?: string | null }

interface SystemStats {
  cpu_usage: number;
  cpu_cores?: number[];
  cpu_freq_mhz?: number;
  cpu_temp_c?: number | null;
  cpu_fan_rpm?: number | null;
  cpu_processes_total?: number;
  cpu_threads_total?: number;
  cpu_top?: ProcInfo[];
  cpu_uptime_s?: number;
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

function fmtUptime(s: number): string {
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

export default function CpuWidget(props: CpuWidgetProps): JSX.Element {
  const interval  = () => refreshInterval(props.interval, 'cpu');
  const label     = () => props.label ?? '';
  // Samples the graph holds = span / interval, bounded. Newest sample is pinned
  // to the right edge and the trace grows in from the right ([S-28]).
  const capacity  = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.cpu), interval()));

  const visible = createMemo<string[]>(() => {
    const raw = props.visibleMetrics;
    if (raw === undefined || raw === null) {
      const out = canonicalVisible(METRIC_DEFS).slice();
      if (props.showGraph === false) {
        const i = out.indexOf('graph');
        if (i >= 0) out.splice(i, 1);
      }
      if (props.showCores === true && !out.includes('cores')) out.push('cores');
      return out;
    }
    return resolveMetrics(raw, METRIC_DEFS).visible;
  });
  const shows = (k: string) => visible().includes(k);

  const [stats, setStats]               = createSignal<SystemStats | null>(null);
  const [history, setHistory]           = createSignal<number[]>([]);
  const [coreHistories, setCoreHistories] = createSignal<number[][]>([]);

  async function refresh() {
    try {
      const s = await invoke('get_system_stats', cpuStatsArgs(visible()));
      setStats(s);
      setHistory(h => [...h, s.cpu_usage].slice(-capacity()));
      if (s.cpu_cores?.length) {
        setCoreHistories(prev =>
          s.cpu_cores!.map((usage, i) =>
            [...(prev[i] ?? []), usage].slice(-capacity())
          )
        );
      }
    } catch (e) { logger.error('cpu', `get_system_stats failed: ${e}`); }
  }

  if (isDemo()) {
    setHistory(demoCpuHistory(capacity()));
    setCoreHistories(demoCpuCoresHistory(capacity()));
  }

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'cpu');

  // Draw at most `capacity` samples; if the span shrank, older samples beyond
  // the window are dropped from the trace (the next poll trims the buffer too).
  const spark   = createMemo(() => buildSpark(history().slice(-capacity()), capacity()));
  const current = createMemo(() => stats()?.cpu_usage ?? 0);
  const color   = createMemo(() => usageColor(current()));
  const gradId  = `cpu-grad-${uid}`;

  const coreSparks = createMemo(() => coreHistories().map(h => buildSpark(h.slice(-capacity()), capacity())));

  return (
    <div class="cpuw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || 'CPU'}
        secondary={
          <>
            <Show when={stats() && shows('usage')}>
              <span class="cpuw-info-val" style={{ color: color() }}>{current().toFixed(1)}%</span>
            </Show>
            <Show when={stats() && shows('frequency') && stats()!.cpu_freq_mhz}>
              <span class="cpuw-info-sub">{(stats()!.cpu_freq_mhz! / 1000).toFixed(2)} GHz</span>
            </Show>
            {/* [S-20] issue #60: once stats load an enabled `temperature` metric
                always shows a chip: the °C reading when a sensor exposes one, else
                an "N/A" placeholder (no driverless temp source exists without
                LHM/OHM or an ACPI thermal zone). Mirrors the fan metric. */}
            <Show when={tempCell(shows('temperature'), stats() != null, stats()?.cpu_temp_c)}>
              {(cell) => cell().kind === 'value'
                ? <span class="cpuw-info-sub">{(cell() as { c: number }).c.toFixed(0)}°C</span>
                : <span class="cpuw-info-sub cpuw-info-na"
                        title={t('net.noCpuTemp')}>Temp N/A</span>}
            </Show>
            {/* [S-23] issue #40: once stats load an enabled `fan` metric always
                shows a chip: the RPM when a sensor exposes one, else an "N/A"
                placeholder (no driverless fan source exists without LHM/OHM). */}
            <Show when={fanCell(shows('fan'), stats() != null, stats()?.cpu_fan_rpm)}>
              {(cell) => (
                // #82: re-select the value/N-A branch reactively as the sensor
                // appears or drops. A bare ternary in this child callback runs
                // once (the outer <Show> stays truthy once stats load) and
                // freezes the branch; the nested <Show>'s `when` is a tracked
                // memo, so it switches on every poll.
                <Show when={cell().kind === 'value'}
                      fallback={
                        <span class="cpuw-info-sub cpuw-info-na"
                              title={t('net.noCpuFan')}>Fan N/A</span>
                      }>
                  <span class="cpuw-info-sub">{(cell() as { rpm: number }).rpm} RPM</span>
                </Show>
              )}
            </Show>
          </>
        }
      />

      <div class="cpuw-body widget-body">
      <Show when={shows('cores') && (stats()?.cpu_cores?.length ?? 0) > 0}
            fallback={
              <>
                <Show when={shows('graph')}>
                  <div class="cpuw-spark-wrap">
                    <Show when={stats()}>
                      <svg class="cpuw-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
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
                <Show when={stats() && shows('usage')}>
                  <div class="cpuw-bar-track">
                    <div class="cpuw-bar-fill" style={{ width: `${current()}%`, background: color() }} />
                  </div>
                </Show>
              </>
            }>
        <div class="cpuw-cores-grid">
          <For each={stats()!.cpu_cores!}>
            {(coreUsage, i) => {
              const coreSpark = () => coreSparks()[i()] ?? { line: '', area: '' };
              const coreColor = () => usageColor(coreUsage);
              const gradCoreId = `cpu-core-${uid}-${i()}`;
              return (
                <div class="cpuw-core-cell">
                  <span class="cpuw-core-idx">{i()}</span>
                  <svg class="cpuw-core-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
                    <defs>
                      <linearGradient id={gradCoreId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                        <stop offset="0%" stop-color={coreColor()} stop-opacity="0.28"/>
                        <stop offset="100%" stop-color={coreColor()} stop-opacity="0"/>
                      </linearGradient>
                    </defs>
                    <path d={coreSpark().area} fill={`url(#${gradCoreId})`} />
                    <path d={coreSpark().line} fill="none" stroke={coreColor()} stroke-width="1.5"
                          stroke-linecap="round" stroke-linejoin="round"
                          vector-effect="non-scaling-stroke" />
                  </svg>
                  <span class="cpuw-core-pct" style={{ color: coreColor() }}>{coreUsage.toFixed(0)}%</span>
                </div>
              );
            }}
          </For>
        </div>
      </Show>

      <Show when={shows('topProcs') && (stats()?.cpu_top?.length ?? 0) > 0}>
        <div class="cpuw-procs">
          {/* [S-25] friendly name when resolved, exe name in the tooltip;
              [S-26] row count follows the topProcsCount config. */}
          <For each={stats()!.cpu_top!.slice(0, clampProcCount(props.topProcsCount))}>
            {(p) => (
              <div class="cpuw-proc-row">
                <span class="cpuw-proc-name" title={p.name}>{displayProcName(p)}</span>
                <span class="cpuw-proc-pct">{p.cpu_pct.toFixed(1)}%</span>
              </div>
            )}
          </For>
        </div>
      </Show>

      <Show when={shows('processes') && stats()?.cpu_processes_total != null}>
        <div class="cpuw-aux-row">
          <span class="cpuw-aux-label">{t('widgetUi.processes')}</span>
          <span class="cpuw-aux-val">
            {stats()!.cpu_processes_total}
            <Show when={stats()!.cpu_threads_total != null}>
              <span class="cpuw-info-sub"> / {stats()!.cpu_threads_total} threads</span>
            </Show>
          </span>
        </div>
      </Show>

      <Show when={shows('uptime') && stats()?.cpu_uptime_s != null}>
        <div class="cpuw-aux-row">
          <span class="cpuw-aux-label">{t('widgetUi.uptime')}</span>
          <span class="cpuw-aux-val">{fmtUptime(stats()!.cpu_uptime_s!)}</span>
        </div>
      </Show>
      </div>
    </div>
  );
}
