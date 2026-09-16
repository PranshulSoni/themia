import { createSignal, createMemo, createEffect, untrack, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoGpuUtilHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { resolveGpuIndexes, itemLayout } from './gpuSelection.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './GpuWidget.css';
import type { JSX, Accessor } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface GpuWidgetProps {
  headerMode?: HeaderMode;
  interval?: number;
  showGraph?: boolean;
  label?: string;
  /** Legacy single-select GPU index (migrated to `gpuIndexes`, [S-24]). */
  gpuIndex?: number;
  /** Multi-select GPU registry indexes ([S-24]). */
  gpuIndexes?: number[];
  visibleMetrics?: unknown;
  graphSpanSec?: number;
}

interface GpuStats {
  name?: string;
  utilization: number;
  vram_used_mb: number;
  vram_total_mb: number;
  shared_used_mb?: number;
  shared_total_mb?: number;
  temp_c?: number | null;
  power_w?: number | null;
  fan_pct?: number | null;
  encoder_pct?: number | null;
  decoder_pct?: number | null;
  core_clock_mhz?: number | null;
  mem_clock_mhz?: number | null;
}

interface SparkPaths {
  line: string;
  area: string;
}

function usageColor(p: number): string {
  if (p >= 90) return 'var(--destructive)';
  if (p >= 70) return '#f59e0b';
  return 'var(--primary)';
}

function fmtMb(mb: number): string {
  if (mb >= 1024) return (mb / 1024).toFixed(1) + ' GB';
  return mb + ' MB';
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

function vramColor(p: number): string {
  if (p >= 90) return 'var(--destructive)';
  if (p >= 80) return '#f59e0b';
  return 'var(--primary)';
}

interface GpuPanelProps {
  index: number;
  interval: () => number;
  /** Samples this panel's sparkline holds = span / interval, bounded ([S-28]). */
  capacity: Accessor<number>;
  shows: (k: string) => boolean;
  /** In multi-GPU mode each panel carries its own name + utilization heading. */
  showHeading: boolean;
  /** Single-GPU mode lifts the one panel's stats up to drive the widget header. */
  onStats?: (s: GpuStats | null) => void;
}

/**
 * One monitored GPU: owns its own poll loop, stats, and windowed utilization
 * history ([S-27], [S-28]), and renders the graph + metric rows. Reused for both
 * the single-GPU view and each stacked card in the multi-GPU view — one
 * rendering path, no fork.
 */
function GpuPanel(props: GpuPanelProps): JSX.Element {
  const uid = Math.random().toString(36).slice(2, 8);
  const [stats, setStats]     = createSignal<GpuStats | null>(null);
  const [history, setHistory] = createSignal<number[]>([]);

  async function refresh() {
    try {
      const s = await invoke('get_gpu_stats', { gpuIndex: props.index });
      setStats(s);
      setHistory(h => [...h, s.utilization].slice(-props.capacity()));
      props.onStats?.(s);
    } catch (e) { logger.error('gpu', `get_gpu_stats failed: ${e}`); }
  }

  // [S-10] switching this panel's GPU clears its stats + history so the new
  // card's sparkline starts fresh.
  //
  // The demo seed lives INSIDE this effect on purpose. Solid runs an effect
  // after the component body, so seeding the history on the line below this
  // call was immediately wiped on mount and the picker preview drew an empty
  // graph area. Clearing and re-seeding have to be the same operation.
  // `capacity` is untracked so the effect still depends only on `props.index`.
  createEffect(() => {
    props.index;
    setStats(null);
    setHistory(untrack(() => (isDemo() ? demoGpuUtilHistory(props.capacity()) : [])));
    props.onStats?.(null);
  });

  createPolling(refresh, () => Math.max(1, props.interval()) * 1000, 'gpu');

  const spark   = createMemo(() => buildSpark(history().slice(-props.capacity()), props.capacity()));
  const current = createMemo(() => stats()?.utilization ?? 0);
  const color   = createMemo(() => usageColor(current()));
  const gradId  = `gpu-grad-${uid}`;

  const vramTotal = createMemo(() => {
    const s = stats();
    return s && s.vram_total_mb > 0 ? s.vram_total_mb : Math.max(1, s?.vram_used_mb ?? 1);
  });
  const vramPct = createMemo(() => {
    const s = stats();
    return s ? Math.min(100, (s.vram_used_mb / vramTotal()) * 100) : 0;
  });

  const sharedUsed  = createMemo(() => stats()?.shared_used_mb  ?? 0);
  const sharedTotal = createMemo(() => stats()?.shared_total_mb ?? 0);
  const sharedPct   = createMemo(() => {
    const t = sharedTotal();
    return t > 0 ? Math.min(100, (sharedUsed() / t) * 100) : 0;
  });

  const showDedicated = createMemo(() => {
    const s = stats();
    if (!s || s.vram_used_mb <= 0) return false;
    if (s.vram_total_mb === 0) return true;
    return s.vram_used_mb <= s.vram_total_mb;
  });

  return (
    <div class="gpuw-panel">
      <Show when={props.showHeading}>
        <div class="gpuw-panel-head">
          <span class="gpuw-panel-name">{stats()?.name || `GPU ${props.index}`}</span>
          <Show when={stats() && props.shows('utilization')}>
            <span class="gpuw-info-val" style={{ color: color() }}>{current().toFixed(1)}%</span>
          </Show>
        </div>
      </Show>

      <Show when={props.shows('graph')}>
        <div class="gpuw-spark-wrap">
          <Show when={stats()}>
            <svg class="gpuw-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
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

      <Show when={props.shows('vram') && showDedicated()}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">{t('widgetUi.dedicated')}</span>
          <span class="gpuw-vram-val">
            {fmtMb(stats()!.vram_used_mb)}
            <Show when={stats()!.vram_total_mb > 0}>
              <span class="gpuw-sep"> / </span>{fmtMb(stats()!.vram_total_mb)}
            </Show>
          </span>
        </div>
        <Show when={stats()!.vram_total_mb > 0}>
          <div class="gpuw-bar-track">
            <div class="gpuw-bar-fill" style={{ width: `${vramPct()}%`, background: vramColor(vramPct()) }} />
          </div>
        </Show>
      </Show>

      <Show when={props.shows('shared') && sharedUsed() > 0 && sharedTotal() > 0}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">{t('widgetUi.shared')}</span>
          <span class="gpuw-vram-val">
            {fmtMb(sharedUsed())}
            <span class="gpuw-sep"> / </span>{fmtMb(sharedTotal())}
          </span>
        </div>
        <div class="gpuw-bar-track">
          <div class="gpuw-bar-fill" style={{ width: `${sharedPct()}%`, background: vramColor(sharedPct()) }} />
        </div>
      </Show>

      <Show when={props.shows('temperature') && stats()?.temp_c != null}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">{t('widgetUi.temp')}</span>
          <span class="gpuw-vram-val">{stats()!.temp_c!.toFixed(0)}°C</span>
        </div>
      </Show>

      <Show when={props.shows('power') && stats()?.power_w != null}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">{t('widgetUi.power')}</span>
          <span class="gpuw-vram-val">{stats()!.power_w!.toFixed(1)} W</span>
        </div>
      </Show>

      <Show when={props.shows('fan') && stats()?.fan_pct != null}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">Fan</span>
          <span class="gpuw-vram-val">{stats()!.fan_pct}%</span>
        </div>
      </Show>

      <Show when={props.shows('clocks') && (stats()?.core_clock_mhz != null || stats()?.mem_clock_mhz != null)}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">{t('widgetUi.clocks')}</span>
          <span class="gpuw-vram-val">
            <Show when={stats()!.core_clock_mhz != null}>{stats()!.core_clock_mhz} MHz</Show>
            <Show when={stats()!.core_clock_mhz != null && stats()!.mem_clock_mhz != null}><span class="gpuw-sep"> / </span></Show>
            <Show when={stats()!.mem_clock_mhz != null}>{stats()!.mem_clock_mhz} MHz mem</Show>
          </span>
        </div>
      </Show>

      <Show when={props.shows('encoder') && (stats()?.encoder_pct != null || stats()?.decoder_pct != null)}>
        <div class="gpuw-vram-row">
          <span class="gpuw-vram-label">Enc/Dec</span>
          <span class="gpuw-vram-val">
            <Show when={stats()!.encoder_pct != null}>{stats()!.encoder_pct!.toFixed(0)}%</Show>
            <Show when={stats()!.encoder_pct != null && stats()!.decoder_pct != null}><span class="gpuw-sep"> / </span></Show>
            <Show when={stats()!.decoder_pct != null}>{stats()!.decoder_pct!.toFixed(0)}%</Show>
          </span>
        </div>
      </Show>
    </div>
  );
}

export default function GpuWidget(props: GpuWidgetProps): JSX.Element {
  const interval  = () => refreshInterval(props.interval, 'gpu');
  const label     = () => props.label ?? '';
  const capacity  = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.gpu), interval()));

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

  // [S-24]/[S-25] migrate + validate the selection; [S-26] pick the layout.
  const selected = createMemo<number[]>(() =>
    resolveGpuIndexes({ gpuIndexes: props.gpuIndexes, gpuIndex: props.gpuIndex }));
  const multi = () => itemLayout(selected().length) === 'overflow';

  // Single-GPU mode: the sole panel reports its stats up so the widget header can
  // show that GPU's name + utilization (unchanged single-GPU behaviour, [S-01]).
  const [headStats, setHeadStats] = createSignal<GpuStats | null>(null);
  const headUtil  = createMemo(() => headStats()?.utilization ?? 0);
  const headColor = createMemo(() => usageColor(headUtil()));

  return (
    <div class="gpuw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || (!multi() ? (headStats()?.name || 'GPU') : 'GPU')}
        secondary={
          <Show when={!multi() && headStats() && shows('utilization')}>
            <span class="gpuw-info-val" style={{ color: headColor() }}>{headUtil().toFixed(1)}%</span>
          </Show>
        }
      />

      <div class="gpuw-body widget-body" classList={{ 'gpuw-multi': multi() }}>
        <For each={selected()}>
          {(idx, i) => (
            <GpuPanel
              index={idx}
              interval={interval}
              capacity={capacity}
              shows={shows}
              showHeading={multi()}
              onStats={!multi() && i() === 0 ? setHeadStats : undefined}
            />
          )}
        </For>
      </div>
    </div>
  );
}
