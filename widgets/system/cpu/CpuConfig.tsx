import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import { Monitor, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { TOP_PROCS_DEFAULT, TOP_PROCS_MAX } from './procsDisplay.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, spanOptions, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface CpuConfigData {
  visibleMetrics?: string[];
  interval?: number;
  topProcsCount?: number;
  graphSpanSec?: number;
}

export interface CpuConfigProps {
  config: CpuConfigData;
  onChange: (config: CpuConfigData) => void;
  globals?: Record<string, unknown>;
}

export default function CpuConfig(props: CpuConfigProps): JSX.Element {
  const config = (): CpuConfigData => props.config ?? {};
  const metricsOn = () => config().visibleMetrics ?? canonicalVisible(METRIC_DEFS);
  function update<K extends keyof CpuConfigData>(k: K, v: CpuConfigData[K]) {
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
              <span class="design-label">{t('widgetUi.metricsLabel')}</span>
              <span class="design-desc">{t('widgetUi.metricsDesc')}</span>
            </div>
            <MetricMultiSelect
              defs={METRIC_DEFS}
              value={config().visibleMetrics ?? canonicalVisible(METRIC_DEFS)}
              onChange={(v) => update('visibleMetrics', v)}
              reorder={false}
            />
          </div>
          {/* [S-26] only meaningful while the topProcs metric is on */}
          <Show when={metricsOn().includes('topProcs')}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.topProcsLabel')}</span>
                <span class="design-desc">{t('widgetUi.topProcsDesc')}</span>
              </div>
              <NumberStepper
                value={config().topProcsCount ?? TOP_PROCS_DEFAULT}
                min={1}
                max={TOP_PROCS_MAX}
                step={1}
                onChange={(v) => update('topProcsCount', v)}
              />
            </div>
          </Show>
        </Show>
        <Show when={tab === 'refresh'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.refreshLabel')}</span>
              <span class="design-desc">{t('widgetUi.refreshDescPoll')}</span>
            </div>
            <NumberStepper
              value={refreshInterval(config().interval, 'cpu')}
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
              value={String(graphSpanSec(config().graphSpanSec, GRAPH_SPAN_DEFAULTS.cpu))}
              options={spanOptions()}
              onChange={(v) => update('graphSpanSec', Number(v))}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
