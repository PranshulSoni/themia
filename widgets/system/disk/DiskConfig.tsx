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
import { resolveMounts } from './diskMounts.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, spanOptions, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

export interface DiskConfigData {
  /** @deprecated legacy single-mount config; migrated to `mounts` on load (#67, [S-23]). */
  mount?: string;
  mounts?: string[];
  visibleMetrics?: string[];
  interval?: number;
  graphSpanSec?: number;
}

export interface DiskConfigProps {
  config: DiskConfigData;
  onChange: (config: DiskConfigData) => void;
  globals?: Record<string, unknown>;
}

interface DiskInfo {
  mount: string;
  used: number;
  total: number;
}

export default function DiskConfig(props: DiskConfigProps): JSX.Element {
  const config = (): DiskConfigData => props.config ?? {};
  function update<K extends keyof DiskConfigData>(k: K, v: DiskConfigData[K]) {
    props.onChange({ ...config(), [k]: v });
  }

  const [disks, setDisks] = createSignal<DiskInfo[]>([]);

  invoke('get_system_stats', {})
    .then(s => { setDisks(s.disks ?? []); })
    .catch(() => {}); // ignore: best-effort list; empty keeps the mount picker hidden

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          <Show when={disks().length >= 1}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('disk.mountsLabel')}</span>
                <span class="design-desc">{t('disk.mountsDesc')}</span>
              </div>
              <MultiSelectDropdown
                items={disks().map(d => ({ key: d.mount, label: d.mount }))}
                visibleKeys={resolveMounts(config())}
                onChange={(keys) => update('mounts', keys)}
                searchable={false}
                placeholder={t('disk.primaryPlaceholder')}
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
              value={refreshInterval(config().interval, 'disk')}
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
              value={String(graphSpanSec(config().graphSpanSec, GRAPH_SPAN_DEFAULTS.disk))}
              options={spanOptions()}
              onChange={(v) => update('graphSpanSec', Number(v))}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
