import { createSignal, createMemo, createEffect, untrack } from 'solid-js';
import { Show } from 'solid-js/web';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoPingLatencyHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { createPolling } from '../../../src/createPolling.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { Settings } from 'lucide-solid';
import './Ping.css';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import {
  buildSpark, fmtMs, pingStats, pingTitle, statusColor, statusText,
  type PingHost,
} from './pingDisplay.ts';
import { t } from '../../../src/i18n.ts';

interface PingResult {
  ok: boolean;
  latency_ms: number | null;
}

interface PingProps {
  host?: PingHost;
  interval?: number;
  label?: string;
  showGraph?: boolean;
  headerMode?: HeaderMode;
  _refresh?: number;
  visibleMetrics?: unknown;
  graphSpanSec?: number;
}

export default function Ping(props: PingProps) {
  const [result, setResult] = createSignal<PingResult | null>(null);
  const [history, setHistory] = createSignal<(number | null)[]>([]);
  const [_loading, setLoading] = createSignal(false);

  const capacity = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.ping),
                   refreshInterval(props.interval, 'ping')));

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
    const host = props.host?.address ? props.host : (isDemo() ? { label: 'Google DNS', address: '8.8.8.8' } : null);
    if (!host) { setResult(null); return; }
    setLoading(true);
    try {
      const res = await invoke('ping_hosts', {
        hosts: [{ label: host.label || '', address: host.address }],
      });
      const r = res[0] ?? null;
      setResult(r);
      const val = r?.latency_ms ?? null;
      setHistory((prev) => [...prev, val].slice(-capacity()));
    } catch (e) { logger.error('ping', `ping_hosts failed: ${e}`); }
    setLoading(false);
  }

  if (isDemo()) setHistory(demoPingLatencyHistory(capacity()));

  // Background refresh: pauses while the app is hibernating + staggers wakeups.
  // Re-polls when the target host changes. (createPolling, B4/2.2)
  createPolling(refresh, () => {
    void props.host;
    return Math.max(1, refreshInterval(props.interval, 'ping')) * 1000;
  }, 'ping');

  // Instant refresh on mount + whenever the manual `_refresh` nonce bumps:
  // createPolling's first tick is staggered, so this keeps first paint snappy.
  createEffect(() => {
    void props._refresh;
    untrack(() => refresh());
  });

  // Aggregates + sparkline both read the visible window, so "Min/Avg/Max" match
  // the trace and the graph shows the configured span ([S-28]).
  const windowed = createMemo(() => history().slice(-capacity()));
  const spark = createMemo(() => buildSpark(windowed(), capacity()));
  const ok = createMemo(() => result()?.ok ?? null);
  const latency = createMemo(() => result()?.latency_ms ?? null);
  const color = createMemo(() => statusColor(ok()));
  const gradId = 'ping-grad';
  const displayLabel = createMemo(() => pingTitle(props.label, props.host));

  const stats = createMemo(() => pingStats(windowed()));

  return (
    <div class="ping-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={displayLabel()}
      />

      <div class="ping-widget__body widget-body">
      <Show
        when={props.host?.address}
        fallback={
          <div class="ping-widget__empty">
            <div class="ping-widget__empty-title">{t('ping.selectHost')}</div>
            <div class="ping-widget__empty-hint">
              {t('widgetUi.clickToConfigure')} <Settings size={13} strokeWidth={1.5} class="ping-widget__empty-gear" /> {t('widgetUi.toConfigure')}
            </div>
          </div>
        }
      >
        <div class="ping-widget__center">
          <Show when={shows('status')}>
            <div class="ping-widget__dot-row">
              <div
                class="ping-widget__dot"
                style={{ background: color(), "box-shadow": `0 0 10px ${color()}66` }}
              />
              <span class="ping-widget__status-text" style={{ color: color() }}>
                {statusText(ok())}
              </span>
            </div>
          </Show>

          <Show when={shows('latency')}>
            <span class="ping-widget__latency" style={{ color: color() }}>{fmtMs(latency())}</span>
          </Show>

          <Show when={shows('address') && props.host?.label && props.host?.address}>
            <span class="ping-widget__address">{props.host!.address}</span>
          </Show>
        </div>

        <Show when={shows('graph')}>
          <div class="ping-widget__spark-wrap">
            <svg class="ping-widget__spark" viewBox="0 0 100 100" preserveAspectRatio="none">
              <defs>
                <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stop-color={color()} stop-opacity="0.25" />
                  <stop offset="100%" stop-color={color()} stop-opacity="0" />
                </linearGradient>
              </defs>
              <path d={spark().area} fill={`url(#${gradId})`} />
              <path
                d={spark().line}
                fill="none"
                stroke={color()}
                stroke-width="1.5"
                stroke-linecap="round"
                stroke-linejoin="round"
                vector-effect="non-scaling-stroke"
              />
            </svg>
          </div>
        </Show>

        <Show when={shows('jitter') && stats().jitter != null}>
          <div class="ping-widget__aux"><span>{t('widgetUi.jitter')}</span><span>{fmtMs(Math.round(stats().jitter!))}</span></div>
        </Show>
        <Show when={shows('loss')}>
          <div class="ping-widget__aux"><span>{t('widgetUi.loss')}</span><span>{stats().loss.toFixed(0)}%</span></div>
        </Show>
        <Show when={shows('minAvgMax') && stats().min != null}>
          <div class="ping-widget__aux"><span>{t('widgetUi.minAvgMax')}</span><span>{fmtMs(stats().min!)} / {fmtMs(Math.round(stats().avg!))} / {fmtMs(stats().max!)}</span></div>
        </Show>
      </Show>
      </div>
    </div>
  );
}
