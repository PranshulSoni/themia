import { createSignal } from 'solid-js';
import { Show } from 'solid-js';
import { invoke } from '../../../src/ipc.ts';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import MultiSelectDropdown from '../../../src/components/MultiSelectDropdown.tsx';
import { Database, Monitor, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { selectedGpuIndexes } from './gpuSelection.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, spanOptions, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface GpuConfigData {
  /** Legacy single-select GPU index (migrated to `gpuIndexes`, [S-24]). */
  gpuIndex?: number;
  /** Multi-select GPU registry indexes ([S-24]). */
  gpuIndexes?: number[];
  visibleMetrics?: string[];
  interval?: number;
  graphSpanSec?: number;
}

export interface GpuConfigProps {
  config: GpuConfigData;
  onChange: (config: GpuConfigData) => void;
  globals?: Record<string, unknown>;
}

interface GpuInfo {
  index: number;
  name: string;
}

export default function GpuConfig(props: GpuConfigProps): JSX.Element {
  const config = (): GpuConfigData => props.config ?? {};
  function update<K extends keyof GpuConfigData>(k: K, v: GpuConfigData[K]) {
    props.onChange({ ...config(), [k]: v });
  }

  const [gpus, setGpus] = createSignal<GpuInfo[]>([]);

  invoke('list_gpus')
    .then(g => setGpus(g))
    .catch(() => {});

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          <Show when={gpus().length > 1}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gpu.gpusLabel')}</span>
                <span class="design-desc">{t('gpu.gpusDesc')}</span>
              </div>
              <MultiSelectDropdown
                items={gpus().map(g => ({ key: String(g.index), label: g.name }))}
                visibleKeys={selectedGpuIndexes(config()).map(String)}
                onChange={(keys) => {
                  const idx = keys.map(Number).filter(n => Number.isInteger(n) && n >= 0);
                  // Migrate to the array field and drop the legacy scalar so there's
                  // one source of truth ([S-24]).
                  props.onChange({ ...config(), gpuIndexes: idx, gpuIndex: undefined });
                }}
                searchable={false}
              />
            </div>
          </Show>
        </Show>
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
        </Show>
        <Show when={tab === 'refresh'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.refreshLabel')}</span>
              <span class="design-desc">{t('widgetUi.refreshDescPoll')}</span>
            </div>
            <NumberStepper
              value={refreshInterval(config().interval, 'gpu')}
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
              value={String(graphSpanSec(config().graphSpanSec, GRAPH_SPAN_DEFAULTS.gpu))}
              options={spanOptions()}
              onChange={(v) => update('graphSpanSec', Number(v))}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
