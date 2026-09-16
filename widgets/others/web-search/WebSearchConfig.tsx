import { Search } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import '../../../src/styles/config-base.css';
import { ENGINES, ENGINE_KEYS, resolveEngine } from './engines.ts';
import { t } from '../../../src/i18n.ts';

interface WebSearchConfigData {
  engine?: string;
  [k: string]: unknown;
}

interface WebSearchConfigProps {
  config: WebSearchConfigData;
  onChange: (c: WebSearchConfigData) => void;
}

export default function WebSearchConfig(props: WebSearchConfigProps) {
  const engine = () => resolveEngine(props.config.engine);

  return (
    <ConfigTabs tabs={[{ key: 'general', label: t('common.general'), icon: Search }]}>
      {() => (
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('webSearch.engine.label')}</span>
            <span class="design-desc">{t('webSearch.engine.desc')}</span>
          </div>
          <Dropdown
            value={engine()}
            options={ENGINE_KEYS.map(k => ({ value: k, label: ENGINES[k].name }))}
            onChange={(v) => props.onChange({ ...props.config, engine: resolveEngine(v) })}
          />
        </div>
      )}
    </ConfigTabs>
  );
}
