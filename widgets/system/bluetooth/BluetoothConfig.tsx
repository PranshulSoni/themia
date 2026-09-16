import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import { Monitor, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import '../../../src/styles/config-base.css';
import { refreshInterval } from '../refreshInterval.ts';
import { t } from '../../../src/i18n.ts';

interface BluetoothConfigData {
  visibleMetrics?: string[];
  interval?: number;
}

interface BluetoothConfigProps {
  config: BluetoothConfigData;
  onChange: (c: BluetoothConfigData) => void;
}

export default function BluetoothConfig(props: BluetoothConfigProps) {
  function update<K extends keyof BluetoothConfigData>(key: K, value: BluetoothConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
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
              value={props.config.visibleMetrics ?? canonicalVisible(METRIC_DEFS)}
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
              value={refreshInterval(props.config.interval, 'bluetooth')}
              min={1}
              max={60}
              step={1}
              unit="s"
              onChange={(v) => update('interval', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
