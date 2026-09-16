import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface GitHubNotificationsConfigData {
  accountIds?: string[];
  count?: number;
  includeRead?: boolean;
  participatingOnly?: boolean;
  refreshMinutes?: number;
}

interface Props {
  config: GitHubNotificationsConfigData;
  onChange: (c: GitHubNotificationsConfigData) => void;
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

export default function GitHubNotificationsConfig(props: Props) {
  function update<K extends keyof GitHubNotificationsConfigData>(key: K, value: GitHubNotificationsConfigData[K]) {
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
    <div class="gh-notif-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.accountsLabel')}</span>
                <span class="design-desc">{t('github.accountsDesc')}</span>
              </div>
              <AccountToggleList
                providerFilter={['github']}
                providerBadges={{ github: 'GitHub' }}
                accountIds={accountIds}
                onToggle={toggleAccount}
                onManageAccounts={props.globals?.onManageAccounts}
              />
            </div>
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.notificationsLabel')}</span>
                <span class="design-desc">{t('github.notificationsCountDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.count ?? 5}
                min={1}
                max={20}
                step={1}
                onChange={(v) => update('count', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.showReadLabel')}</span>
                <span class="design-desc">{t('github.showReadDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.includeRead === true}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update('includeRead', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.participatingLabel')}</span>
                <span class="design-desc">{t('github.participatingDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.participatingOnly === true}
                onLabel={t('toggle.on')}
                offLabel={t('toggle.off')}
                onChange={(v) => update('participatingOnly', v)}
              />
            </div>

          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.refreshLabel')}</span>
                <span class="design-desc">{t('github.notificationsRefreshDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshMinutes ?? 5}
                min={5}
                max={60}
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
