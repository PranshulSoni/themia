import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import { MIN_REFRESH_MINUTES, MAX_REFRESH_MINUTES, DEFAULT_REFRESH_MINUTES } from './claudeUsage.ts';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface ClaudeUsageConfigData {
  accountIds?: string[];
  refreshMinutes?: number;
  label?: string;
  showOpus?: boolean;
  showSonnet?: boolean;
}

interface ClaudeUsageConfigProps {
  config: ClaudeUsageConfigData;
  onChange: (c: ClaudeUsageConfigData) => void;
  globals?: { onManageAccounts?: (providerFilter?: string[]) => void };
}

// A function, not a const: the labels must re-read t() after a
// language change (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'source', label: t('configTabs.source'), icon: Database },
    { key: 'display', label: t('configTabs.display'), icon: Monitor },
    { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
  ];
}

export default function ClaudeUsageConfig(props: ClaudeUsageConfigProps) {
  function update<K extends keyof ClaudeUsageConfigData>(key: K, value: ClaudeUsageConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  function accountIds(): string[] {
    const v = props.config.accountIds;
    return Array.isArray(v) ? v : [];
  }

  function toggleAccount(id: string): void {
    const cur = accountIds();
    const next = cur.includes(id) ? cur.filter(a => a !== id) : [...cur, id];
    update('accountIds', next);
  }

  return (
    <div class="claude-usage-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('claudeUsage.accountLabel')}</span>
                <span class="design-desc">{t('claudeUsage.accountDesc')}</span>
              </div>
              <AccountToggleList
                providerFilter={['claude']}
                providerBadges={{ claude: 'Claude' }}
                accountIds={accountIds}
                onToggle={toggleAccount}
                onManageAccounts={props.globals?.onManageAccounts}
              />
            </div>
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('claudeUsage.opusLabel')}</span>
                <span class="design-desc">{t('claudeUsage.opusDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.showOpus ?? false}
                onChange={(v) => update('showOpus', v)}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                aria-label={t('claudeUsage.opusAria')}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('claudeUsage.sonnetLabel')}</span>
                <span class="design-desc">{t('claudeUsage.sonnetDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.showSonnet ?? false}
                onChange={(v) => update('showSonnet', v)}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                aria-label={t('claudeUsage.sonnetAria')}
              />
            </div>
          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.refreshLabel')}</span>
                <span class="design-desc">{t('claudeUsage.refreshDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshMinutes ?? DEFAULT_REFRESH_MINUTES}
                min={MIN_REFRESH_MINUTES}
                max={MAX_REFRESH_MINUTES}
                step={5}
                unit="m"
                onChange={(v) => update('refreshMinutes', v)}
              />
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
