import { Monitor, ShieldAlert } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { Show } from 'solid-js';
import { ACTION_DEFS } from './actions.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';

interface PowerConfigData {
  visibleActions?: unknown;
  showLabels?: boolean;
  confirm?: boolean;
}

interface PowerConfigProps {
  config: PowerConfigData;
  onChange: (c: PowerConfigData) => void;
}

export default function PowerConfig(props: PowerConfigProps) {
  // Reads props directly and writes through onChange; no local mirror of
  // props.config (Settings.SPEC [S-13]).
  function update<K extends keyof PowerConfigData>(key: K, value: PowerConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'safety', label: t('power.safetySection'), icon: ShieldAlert },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('power.actionsLabel')}</span>
              <span class="design-desc">{t('power.actionsDesc')}</span>
            </div>
            <MetricMultiSelect
              defs={ACTION_DEFS}
              value={props.config.visibleActions}
              onChange={(v) => update('visibleActions', v)}
              reorder
            />
          </div>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('power.labelsLabel')}</span>
              <span class="design-desc">{t('power.labelsDesc')}</span>
            </div>
            <ToggleSwitch
              checked={props.config.showLabels !== false}
              onChange={(v) => update('showLabels', v)}
            />
          </div>
        </Show>
        <Show when={tab === 'safety'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('power.confirmLabel')}</span>
              <span class="design-desc">{t('power.confirmDesc')}</span>
            </div>
            <ToggleSwitch
              checked={props.config.confirm !== false}
              onChange={(v) => update('confirm', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
