import { Show, createSignal, onMount, onCleanup } from 'solid-js';
import { invoke } from '../../../src/ipc.ts';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import { asPrStateFilter, asPrSortOrder, type PrStateFilter, type PrSortOrder } from './prFilterSort.ts';
import { bumpRevision, loadAccounts } from '../../../src/accountStore.ts';
import '../../../src/styles/config-base.css';
import './GitHubConfig.css';
import { t } from '../../../src/i18n.ts';

interface GitHubConfigData {
  repo?: string;
  accountIds?: string[];
  token?: string;
  commitCount?: number;
  prCount?: number;
  prState?: PrStateFilter;
  prSort?: PrSortOrder;
  issueCount?: number;
  pipelineCount?: number;
  refreshMinutes?: number;
}

// Functions, not consts: `t()` reads the reactive language signal, so building
// these once at import time would pin the labels to the startup language.
const prStateOptions = () => [
  { value: 'open', label: t('github.prStateOpen') },
  { value: 'closed', label: t('github.prStateClosed') },
  { value: 'all', label: t('github.prStateAll') },
];

const prSortOptions = () => [
  { value: 'newest', label: t('github.prSortNewest') },
  { value: 'oldest', label: t('github.prSortOldest') },
];

interface GitHubConfigProps {
  config: GitHubConfigData;
  onChange: (c: GitHubConfigData) => void;
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

function deriveApiBaseUrl(repo: string): string | null {
  if (!repo) return null;
  try {
    const url = new URL(repo);
    if (url.hostname === 'github.com') return 'https://api.github.com';
    return `${url.origin}/api/v3`;
  } catch {
    return null;
  }
}

export default function GitHubConfig(props: GitHubConfigProps) {
  const [migrating, setMigrating] = createSignal(false);
  const [migrationError, setMigrationError] = createSignal<string | null>(null);

  function update<K extends keyof GitHubConfigData>(key: K, value: GitHubConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  // The repo input is controlled directly by props.config.repo ([S-13]: config
  // panels keep no local mirror of props.config). The config WRITE is debounced
  // because props.config.repo is the display widget's fetch cache key ([S-35]) —
  // an un-debounced write fires a live batch of GitHub API requests per keystroke.
  // Solid only re-sets the input's DOM value when props.config.repo actually
  // changes, so the typed text stays put during the debounce window.
  let repoTimer: ReturnType<typeof setTimeout> | null = null;
  onCleanup(() => { if (repoTimer) clearTimeout(repoTimer); });

  function onRepoInput(v: string) {
    if (repoTimer) clearTimeout(repoTimer);
    repoTimer = setTimeout(() => update('repo', v.trim()), 400);
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

  // Migrate legacy `config.token` → a github account on first open.
  onMount(async () => {
    const legacyToken = typeof props.config.token === 'string' ? props.config.token.trim() : '';
    if (!legacyToken) return;
    if (accountIds().length > 0) {
      // Already migrated; drop stale token field.
      const { token: _t, ...rest } = props.config;
      props.onChange(rest);
      return;
    }
    setMigrating(true);
    try {
      const apiBaseUrl = deriveApiBaseUrl(props.config.repo ?? '');
      const info = await invoke('email_add_github_account', {
        token: legacyToken,
        apiBaseUrl,
        nickname: null,
      });
      await loadAccounts();
      bumpRevision();
      const { token: _t, ...rest } = props.config;
      props.onChange({ ...rest, accountIds: [info.id] });
    } catch (e) {
      setMigrationError(String(e));
    }
    setMigrating(false);
  });

  return (
    <div class="github-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('github.repositoryLabel')}</span>
                <span class="design-desc">{t('github.repositoryDesc')}</span>
              </div>
              <input
                class="text-input"
                type="text"
                placeholder="owner/repo"
                value={props.config.repo ?? ''}
                onInput={(e) => onRepoInput(e.currentTarget.value)}
              />
            </div>

            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.accountLabel')}</span>
                <span class="design-desc">{t('github.accountDesc')}</span>
              </div>
              <AccountToggleList
                providerFilter={['github']}
                providerBadges={{ github: 'GitHub' }}
                accountIds={accountIds}
                onToggle={toggleAccount}
                onManageAccounts={props.globals?.onManageAccounts}
              />
              <Show when={migrating()}>
                <div class="field-hint">{t('github.migratingHint')}</div>
              </Show>
              <Show when={migrationError()}>
                <div class="add-error">Token migration failed: {migrationError()}. Add a GitHub account manually in Accounts.</div>
              </Show>
            </div>
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.commitsLabel')}</span>
                <span class="design-desc">{t('github.commitsDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.commitCount ?? 3}
                min={0}
                max={10}
                step={1}
                onChange={(v) => update('commitCount', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.prsLabel')}</span>
                <span class="design-desc">{t('github.prsDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.prCount ?? 3}
                min={0}
                max={10}
                step={1}
                onChange={(v) => update('prCount', v)}
              />
            </div>

            <Show when={(props.config.prCount ?? 3) > 0}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('github.prStateLabel')}</span>
                  <span class="design-desc">{t('github.prStateDesc')}</span>
                </div>
                <Dropdown
                  value={asPrStateFilter(props.config.prState)}
                  options={prStateOptions()}
                  onChange={(v) => update('prState', asPrStateFilter(v))}
                />
              </div>

              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('github.prOrderLabel')}</span>
                  <span class="design-desc">{t('github.prSortDesc')}</span>
                </div>
                <Dropdown
                  value={asPrSortOrder(props.config.prSort)}
                  options={prSortOptions()}
                  onChange={(v) => update('prSort', asPrSortOrder(v))}
                />
              </div>
            </Show>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.issuesLabel')}</span>
                <span class="design-desc">{t('github.issuesDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.issueCount ?? 3}
                min={0}
                max={10}
                step={1}
                onChange={(v) => update('issueCount', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('github.pipelinesLabel')}</span>
                <span class="design-desc">{t('github.runsDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.pipelineCount ?? 5}
                min={0}
                max={10}
                step={1}
                onChange={(v) => update('pipelineCount', v)}
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
