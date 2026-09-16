import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface GitHubAssignedIssuesConfigData {
  accountIds?: string[];
  count?: number;
  includeMentioned?: boolean;
  refreshMinutes?: number;
}

interface Props {
  config: GitHubAssignedIssuesConfigData;
  onChange: (c: GitHubAssignedIssuesConfigData) => void;
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

export default function GitHubAssignedIssuesConfig(props: Props) {
  function update<K extends keyof GitHubAssignedIssuesConfigData>(key: K, value: GitHubAssignedIssuesConfigData[K]) {
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
    <div class="gh-ai-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.accountsLabel')}</span>
                <span class="design-desc">{t('github.assignedAccountsDesc')}</span>
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
                <span class="design-label">{t('github.issuesLabel')}</span>
                <span class="design-desc">{t('github.maxMergedDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.count ?? 8}
                min={1}
                max={25}
                step={1}
                onChange={(v) => update('count', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.includeMentionedLabel')}</span>
                <span class="design-desc">{t('github.includeMentionedDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.includeMentioned === true}
                onLabel={t('toggle.on')}
                offLabel={t('toggle.off')}
                onChange={(v) => update('includeMentioned', v)}
              />
            </div>

          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.refreshLabel')}</span>
                <span class="design-desc">{t('widgetUi.refreshDescUpdates')}</span>
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
