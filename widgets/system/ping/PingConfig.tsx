import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import { Database, Monitor, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import '../../../src/styles/config-base.css';
import './PingConfig.css';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, spanOptions, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

interface PingConfigData {
  host?: { label: string; address: string };
  interval?: number;
  label?: string;
  visibleMetrics?: string[];
  graphSpanSec?: number;
}

interface PingConfigProps {
  config: PingConfigData;
  onChange: (c: PingConfigData) => void;
  globals?: Record<string, unknown>;
}


export default function PingConfig(props: PingConfigProps) {
  // [S-13] Read every field straight from props. This used to mirror `host`
  // into two signals at mount and rewrite `host` from them on every change, so
  // editing the interval wrote a mount-time snapshot back over a host that had
  // changed since — a lost update.
  const host = () => props.config.host ?? { label: '', address: '' };

  function setHost(patch: Partial<PingConfigData['host']>) {
    props.onChange({ ...props.config, host: { ...host(), ...patch } });
  }

  function update<K extends keyof PingConfigData>(key: K, value: PingConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          <div class="design-row stacked">
            <div class="design-left">
              <span class="design-label">{t('ping.hostLabel')}</span>
              <span class="design-desc">{t('ping.hostDesc')}</span>
            </div>
            <input
              id="ping-label"
              class="text-input"
              type="text"
              placeholder="e.g. Google DNS"
              value={host().label}
              onInput={(e) => setHost({ label: e.currentTarget.value.trim() })}
            />
          </div>
          <div class="design-row stacked">
            <div class="design-left">
              <span class="design-label">{t('ping.addressLabel')}</span>
              <span class="design-desc">{t('ping.addressDesc')}</span>
            </div>
            <input
              id="ping-address"
              class="text-input ping-config__addr-input"
              type="text"
              placeholder="8.8.8.8 or example.com"
              value={host().address}
              onInput={(e) => setHost({ address: e.currentTarget.value.trim() })}
            />
          </div>
        </Show>
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
              value={refreshInterval(props.config.interval, 'ping')}
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
              value={String(graphSpanSec(props.config.graphSpanSec, GRAPH_SPAN_DEFAULTS.ping))}
              options={spanOptions()}
              onChange={(v) => update('graphSpanSec', Number(v))}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
