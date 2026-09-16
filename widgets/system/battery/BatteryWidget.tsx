import { createSignal, createEffect, createMemo } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show } from 'solid-js/web';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoBatteryHistory } from '../../../src/demo/fixtures.ts';
import { Zap, BatteryMedium } from 'lucide-solid';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { METRIC_DEFS } from './metrics.ts';
import './BatteryWidget.css';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

interface BatteryStats {
  percent: number;
  is_charging: boolean;
  is_on_ac: boolean;
  seconds_remaining: number | null;
  has_battery: boolean;
  design_capacity_mwh?: number | null;
  full_charge_capacity_mwh?: number | null;
  health_pct?: number | null;
  cycle_count?: number | null;
  voltage_mv?: number | null;
  temp_c?: number | null;
}

interface BatteryWidgetProps {
  interval?: number;
  showGraph?: boolean;
  _refresh?: number;
  visibleMetrics?: unknown;
  label?: string;
  headerMode?: HeaderMode;
  graphSpanSec?: number;
}

function buildSparkPath(data: number[], w: number, h: number, capacity: number): string {
  if (data.length < 2) return '';
  const n = data.length;
  return data.map((v, i) => {
    const x = (sampleX(i, n, capacity) / 100) * w;
    const y = h - (v / 100) * h;
    return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');
}

export default function BatteryWidget(props: BatteryWidgetProps) {
  const [stats, setStats] = createSignal<BatteryStats | null>(null);
  const [history, setHistory] = createSignal<number[]>([]);

  const interval = () => refreshInterval(props.interval, 'battery');
  const capacity = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.battery), interval()));

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

  async function refresh() {
    try {
      const s = await invoke('get_battery_stats');
      setStats(s);
      if (s.has_battery) {
        setHistory(h => [...h, s.percent].slice(-capacity()));
      }
    } catch (e) { logger.error('battery', `get_battery_stats failed: ${e}`); }
  }

  createEffect(() => { void props._refresh; refresh(); });
  if (isDemo()) setHistory(demoBatteryHistory(capacity()));

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'battery');

  const pct = () => stats()?.percent ?? 0;
  const barColor = createMemo(() => {
    if (stats()?.is_charging) return 'var(--primary)';
    const p = pct();
    if (p > 50) return '#22c55e';
    if (p > 20) return '#f59e0b';
    return 'var(--destructive)';
  });

  function statusText(): string {
    const s = stats();
    if (!s) return '';
    if (!s.has_battery) return 'No battery detected';
    if (s.is_charging) return 'Charging';
    if (s.is_on_ac && s.percent >= 99) return 'Fully charged';
    if (shows('eta') && s.seconds_remaining != null && s.seconds_remaining > 0) {
      const h = Math.floor(s.seconds_remaining / 3600);
      const m = Math.floor((s.seconds_remaining % 3600) / 60);
      return `${h}h ${m}m remaining`;
    }
    return s.is_on_ac ? 'Plugged in' : 'On battery';
  }

  return (
    <div class="battery-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        icon={BatteryMedium}
        title={props.label || 'Battery'}
      />
      <div class="widget-body battery-widget__body">
      <Show when={stats()}>
        <Show when={stats()!.has_battery} fallback={
          <div class="battery-widget__no-battery">{t('net.noBattery')}</div>
        }>
          <div class="battery-widget__main">
            <div class="battery-widget__icon-area">
              <svg class="battery-widget__icon" viewBox="0 0 44 24" fill="none">
                <rect x="0.5" y="0.5" width="38" height="23" rx="3" stroke="currentColor" stroke-width="1" fill="none" />
                <rect x="39" y="7" width="4" height="10" rx="1.5" fill="currentColor" opacity="0.4" />
                <rect x="2" y="2" width={Math.max(0, (pct() / 100) * 35)} height="20" rx="2" fill={barColor()} />
              </svg>
              <Show when={stats()!.is_charging}>
                <span class="battery-widget__bolt"><Zap size={14} strokeWidth={2} /></span>
              </Show>
            </div>
            <Show when={shows('percent')}>
              <span class="battery-widget__pct">{pct()}%</span>
            </Show>
            <Show when={shows('status') || shows('eta')}>
              <span class="battery-widget__status">{statusText()}</span>
            </Show>
          </div>

          <Show when={shows('graph') && history().length >= 2}>
            <div class="battery-widget__graph">
              <svg viewBox="0 0 120 30" preserveAspectRatio="none">
                <path
                  d={buildSparkPath(history().slice(-capacity()), 120, 30, capacity())}
                  fill="none"
                  stroke={barColor()}
                  stroke-width="1.5"
                />
              </svg>
            </div>
          </Show>

          <Show when={shows('health') && stats()!.health_pct != null}>
            <div class="battery-widget__aux">Health {stats()!.health_pct!.toFixed(0)}%</div>
          </Show>
          <Show when={shows('cycles') && stats()!.cycle_count != null}>
            <div class="battery-widget__aux">Cycles {stats()!.cycle_count}</div>
          </Show>
          <Show when={shows('voltage') && stats()!.voltage_mv != null}>
            <div class="battery-widget__aux">{(stats()!.voltage_mv! / 1000).toFixed(2)} V</div>
          </Show>
          <Show when={shows('temperature') && stats()!.temp_c != null}>
            <div class="battery-widget__aux">{stats()!.temp_c!.toFixed(0)}°C</div>
          </Show>
        </Show>
      </Show>
      </div>
    </div>
  );
}
