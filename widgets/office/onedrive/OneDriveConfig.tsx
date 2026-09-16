import { createSignal, onCleanup, Show } from 'solid-js';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import { Clock, FolderRoot, Monitor, Database } from 'lucide-solid';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';
import NumberStepper from '../../../src/components/NumberStepper.tsx';

interface OneDriveConfigData {
  accountId?: string;
  showPath?: string;
  fileCount?: number;
}

interface OneDriveConfigProps {
  config: OneDriveConfigData;
  onChange: (c: OneDriveConfigData) => void;
  globals?: { onManageAccounts?: () => void };
}

export default function OneDriveConfig(props: OneDriveConfigProps) {
  // [S-13] Stateless: read accountId straight from props.config.
  const accountId = (): string => props.config.accountId ?? '';

  function update<K extends keyof OneDriveConfigData>(key: K, value: OneDriveConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  // File count feeds a Graph fetch, so the config write is debounced: holding the
  // stepper (repeats every 50ms) must not fire a fetch per step. `pending` is
  // transient edit state (null when idle), NOT a mirror of config ([S-13]) — the
  // displayed value falls back to props.config the moment the edit settles.
  const [pendingFileCount, setPendingFileCount] = createSignal<number | null>(null);
  const fileCount = (): number => pendingFileCount() ?? props.config.fileCount ?? 15;
  let fileCountTimer: number | undefined;
  onCleanup(() => clearTimeout(fileCountTimer));
  function setFileCountDebounced(v: number): void {
    setPendingFileCount(v);
    clearTimeout(fileCountTimer);
    fileCountTimer = window.setTimeout(() => { update('fileCount', v); setPendingFileCount(null); }, 250);
  }

  function toggleAccount(id: string): void {
    update('accountId', accountId() === id ? undefined : id);
  }

  return (
    <div class="onedrive-config">
      <ConfigTabs tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <AccountToggleList
              providerFilter={['microsoft']}
              providerBadges={{ microsoft: 'M365' }}
              accountIds={() => accountId() ? [accountId()] : []}
              onToggle={toggleAccount}
              onManageAccounts={props.globals?.onManageAccounts}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('onedrive.defaultViewLabel')}</span>
                <span class="design-desc">{t('onedrive.defaultViewDesc')}</span>
              </div>
              <Dropdown
                value={props.config.showPath ?? 'recent'}
                options={[
                  { value: 'recent', label: t('onedrive.viewRecent'), icon: () => <Clock size={13} strokeWidth={1.5} /> },
                  { value: 'root',   label: t('onedrive.viewRoot'),   icon: () => <FolderRoot size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update('showPath', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('onedrive.fileCountLabel')}</span>
                <span class="design-desc">{t('onedrive.fileCountDesc')}</span>
              </div>
              <NumberStepper
                value={fileCount()}
                min={5}
                max={50}
                step={5}
                onChange={setFileCountDebounced}
              />
            </div>

          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
