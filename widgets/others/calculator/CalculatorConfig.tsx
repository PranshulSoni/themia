import { Show } from 'solid-js';
import { Monitor } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface CalculatorConfigData {
  decimalPlaces?: number;
}

interface CalculatorConfigProps {
  config: CalculatorConfigData;
  onChange: (c: CalculatorConfigData) => void;
}

const DECIMAL_OPTS = [2, 4, 6, 8];

export default function CalculatorConfig(props: CalculatorConfigProps) {
  function update<K extends keyof CalculatorConfigData>(key: K, value: CalculatorConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('calculator.decimalsLabel')}</span>
              <span class="design-desc">{t('calculator.decimalsDesc')}</span>
            </div>
            <Dropdown
              value={String(props.config.decimalPlaces ?? 4)}
              options={DECIMAL_OPTS.map(d => ({ label: String(d), value: String(d) }))}
              onChange={(v) => update('decimalPlaces', Number(v))}
            />
          </div>

        </Show>
      </>}
    </ConfigTabs>
  );
}
