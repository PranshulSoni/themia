import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import { Monitor, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { resolveVisibleWithLegacy } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, spanOptions, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface NetConfigData {
  visibleMetrics?: string[];
  interval?: number;
  graphSpanSec?: number;
  /** Legacy: pre-`visibleMetrics` graph toggle; honoured by the widget when
   *  `visibleMetrics` is absent, so the panel must honour it too. */
  showGraph?: boolean;
}

export interface NetConfigProps {
  config: NetConfigData;
  onChange: (config: NetConfigData) => void;
  globals?: Record<string, unknown>;
}

export default function NetConfig(props: NetConfigProps): JSX.Element {
  const config = (): NetConfigData => props.config ?? {};
  function update<K extends keyof NetConfigData>(k: K, v: NetConfigData[K]) {
    props.onChange({ ...config(), [k]: v });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('net.visibleMetrics.label')}</span>
              <span class="design-desc">{t('net.visibleMetrics.desc')}</span>
            </div>
            <MetricMultiSelect
              defs={METRIC_DEFS}
              value={resolveVisibleWithLegacy(config().visibleMetrics, METRIC_DEFS, { key: 'graph', enabled: config().showGraph })}
              onChange={(v) => update('visibleMetrics', v)}
              reorder={false}
            />
          </div>
        </Show>
        <Show when={tab === 'refresh'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('net.refreshInterval.label')}</span>
              <span class="design-desc">{t('net.refreshInterval.desc')}</span>
            </div>
            <NumberStepper
              value={refreshInterval(config().interval, 'net')}
              min={1}
              max={60}
              step={1}
              unit="s"
              onChange={(v) => update('interval', v)}
            />
          </div>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.graphSpanLabel')}</span>
              <span class="design-desc">{t('widgetUi.graphSpanDesc')}</span>
            </div>
            <Dropdown
              value={String(graphSpanSec(config().graphSpanSec, GRAPH_SPAN_DEFAULTS.net))}
              options={spanOptions()}
              onChange={(v) => update('graphSpanSec', Number(v))}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
