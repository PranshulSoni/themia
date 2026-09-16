import { Show } from 'solid-js';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import { List, AlignJustify, Rows3, Monitor, Database, Inbox, Send, FilePen, Trash2, ShieldAlert } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { COLUMN_DEFS, LIST_FIELD_DEFS } from './columns.ts';
import { resolveMetrics } from '../../../src/widgets/metricSet.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';

export interface EmailConfigData {
  folder?: string;
  messageCount?: number;
  accountIds?: string[];
  accountId?: string;
  viewMode?: string;
  previewLines?: number;
  rowColumns?: string[];
  listFields?: string[];
}

interface EmailConfigGlobals {
  onManageAccounts?: () => void;
}

export interface EmailConfigProps {
  config?: EmailConfigData;
  onChange: (config: EmailConfigData) => void;
  globals?: EmailConfigGlobals;
}

export default function EmailConfig(props: EmailConfigProps): JSX.Element {
  const config  = (): EmailConfigData => props.config ?? {};
  const globals = (): EmailConfigGlobals => props.globals ?? {};

  // [S-13] Stateless: derive every control's value straight from props.config, so
  // an external change (e.g. the widget's own context-menu view/folder switcher)
  // shows immediately and is never reverted by a stale local copy.
  const folder = (): string => config().folder ?? 'inbox';
  const messageCount = (): number => config().messageCount ?? 10;
  const accountIds = (): string[] => config().accountIds ?? (config().accountId ? [config().accountId!] : []);
  const viewMode = (): string => config().viewMode ?? 'list';
  const previewLines = (): number => config().previewLines ?? 2;
  const rowColumns = (): string[] => config().rowColumns ?? ['from', 'subject', 'date'];
  const listFields = (): string[] => resolveMetrics(config().listFields, LIST_FIELD_DEFS).visible;

  // Write-through: patch props.config directly, stripping legacy keys `accountId`
  // (pre-multi-account) and `itemGap` (removed control), so the panel keeps no copy.
  function update(patch: Partial<EmailConfigData>): void {
    const { accountId: _old, itemGap: _gap, ...rest } = config() as EmailConfigData & { itemGap?: number | null };
    props.onChange({ ...rest, accountIds: accountIds(), ...patch });
  }

  function toggleAccount(id: string): void {
    const ids = accountIds();
    update({ accountIds: ids.includes(id) ? ids.filter(a => a !== id) : [...ids, id] });
  }

  return (
    <div class="email-config-body">
      <ConfigTabs tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <AccountToggleList
              providerFilter={['microsoft', 'google', 'imap']}
              providerBadges={{ microsoft: 'M365', google: 'Google', imap: 'IMAP' }}
              accountIds={accountIds}
              onToggle={toggleAccount}
              onManageAccounts={globals().onManageAccounts}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('email.view.label')}</span>
                <span class="design-desc">{t('email.view.desc')}</span>
              </div>
              <Dropdown
                value={viewMode()}
                options={[
                  { value: 'list',    label: t('ctxMenu.list'),    icon: () => <List size={13} strokeWidth={1.5} /> },
                  { value: 'compact', label: t('ctxMenu.compact'), icon: () => <AlignJustify size={13} strokeWidth={1.5} /> },
                  { value: 'row',     label: t('ctxMenu.row'),     icon: () => <Rows3 size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ viewMode: v })}
              />
            </div>

            <Show when={viewMode() === 'list' || viewMode() === 'compact'}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('email.fields.label')}</span>
                  <span class="design-desc">{t('email.fields.desc')}</span>
                </div>
                <MetricMultiSelect
                  defs={LIST_FIELD_DEFS}
                  value={listFields()}
                  onChange={(v) => update({ listFields: v })}
                />
              </div>
            </Show>

            <Show when={viewMode() === 'list' && listFields().includes('preview')}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('email.previewLines.label')}</span>
                  <span class="design-desc">{t('email.previewLines.desc')}</span>
                </div>
                <NumberStepper
                  value={previewLines()}
                  min={1}
                  max={5}
                  step={1}
                  onChange={(v) => update({ previewLines: v })}
                />
              </div>
            </Show>

            <Show when={viewMode() === 'row'}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('email.columns.label')}</span>
                  <span class="design-desc">{t('email.columns.desc')}</span>
                </div>
                <MetricMultiSelect
                  defs={COLUMN_DEFS}
                  value={rowColumns()}
                  onChange={(v) => { if (v.length === 0) return; update({ rowColumns: v }); }}
                  reorder={true}
                />
              </div>
            </Show>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('email.folder.label')}</span>
                <span class="design-desc">{t('email.folder.desc')}</span>
              </div>
              <Dropdown
                value={folder()}
                options={[
                  { label: t('ctxMenu.inbox'), value: 'inbox', icon: () => <Inbox size={13} strokeWidth={1.5} /> },
                  { label: t('ctxMenu.sentItems'), value: 'sentitems', icon: () => <Send size={13} strokeWidth={1.5} /> },
                  { label: t('ctxMenu.drafts'), value: 'drafts', icon: () => <FilePen size={13} strokeWidth={1.5} /> },
                  { label: t('ctxMenu.deletedItems'), value: 'deleteditems', icon: () => <Trash2 size={13} strokeWidth={1.5} /> },
                  { label: t('ctxMenu.junkEmail'), value: 'junkemail', icon: () => <ShieldAlert size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ folder: v })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('email.messagesToShow.label')}</span>
                <span class="design-desc">{t('email.messagesToShow.desc')}</span>
              </div>
              {/* Was a raw number input while this same file drove previewLines
                  with NumberStepper — self-inconsistent in one panel. */}
              <NumberStepper
                value={messageCount()}
                min={1}
                max={50}
                onChange={(v) => update({ messageCount: v })}
              />
            </div>

          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
