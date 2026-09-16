import { Show, For } from 'solid-js';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import { List, AlignJustify, Columns3, LayoutGrid, Monitor, Database } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import NumberStepper from '../../../src/components/NumberStepper.tsx';

type ColKey = 'name' | 'email' | 'phone' | 'company' | 'title';
type ViewMode = 'list' | 'compact' | 'details' | 'grid';

interface ContactsConfigData {
  accountIds?: string[];
  accountId?: string;
  count?: number;
  defaultSearch?: string;
  viewMode?: ViewMode;
  rowColumns?: ColKey[];
  [key: string]: unknown;
}

interface ColDef {
  key: ColKey;
  label: string;
}

export interface ContactsConfigProps {
  config: ContactsConfigData;
  globals: Record<string, unknown>;
  onChange: (config: ContactsConfigData) => void;
}

const allCols = (): ColDef[] => [
  { key: 'name',    label: t('contacts.col.name')    },
  { key: 'email',   label: t('contacts.col.email')   },
  { key: 'phone',   label: t('contacts.col.phone')   },
  { key: 'company', label: t('contacts.col.company') },
  { key: 'title',   label: t('contacts.col.title')   },
];
const ALL_COLS = allCols();
const COL_ORDER: ColKey[] = ALL_COLS.map((c: ColDef) => c.key);

export default function ContactsConfig(props: ContactsConfigProps): JSX.Element {
  const config = (): ContactsConfigData => props.config ?? {};
  const globals = (): Record<string, unknown> => props.globals ?? {};

  // [S-13] Stateless: derive every control's value straight from props.config, so
  // an external change (e.g. the widget's own context-menu view switcher [S-21])
  // shows immediately and is never reverted by a stale local copy.
  const accountIds = (): string[] => config().accountIds ?? (config().accountId ? [config().accountId!] : []);
  const count = (): number => config().count ?? 20;
  const defaultSearch = (): string => config().defaultSearch ?? '';
  const viewMode = (): ViewMode => config().viewMode ?? 'list';
  const rowColumns = (): ColKey[] => config().rowColumns ?? ['name', 'email', 'phone'];

  // Write-through: patch props.config directly, stripping legacy keys `accountId`
  // (pre-multi-account) and `itemGap` (removed control), so the panel keeps no copy.
  function update(patch: Partial<ContactsConfigData>): void {
    const { accountId: _old, itemGap: _gap, ...rest } = config();
    props.onChange({ ...rest, accountIds: accountIds(), ...patch });
  }

  function toggleAccount(id: string): void {
    const ids = accountIds();
    update({ accountIds: ids.includes(id) ? ids.filter(a => a !== id) : [...ids, id] });
  }

  function toggleCol(key: ColKey): void {
    const prev = rowColumns();
    const next = prev.includes(key) ? prev.filter((k: ColKey) => k !== key) : [...prev, key];
    if (next.length === 0) return;
    update({ rowColumns: COL_ORDER.filter((k: ColKey) => next.includes(k)) });
  }

  return (
    <div class="contacts-config-body">
      <ConfigTabs tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <AccountToggleList
              providerFilter={['microsoft', 'google', 'carddav']}
              providerBadges={{ microsoft: 'M365', google: 'Google', carddav: 'CardDAV' }}
              accountIds={accountIds}
              onToggle={toggleAccount}
              onManageAccounts={globals().onManageAccounts as (() => void) | undefined}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('contacts.view.label')}</span>
                <span class="design-desc">{t('contacts.view.desc')}</span>
              </div>
              <Dropdown
                value={viewMode()}
                options={[
                  { value: 'list',    label: t('ctxMenu.list'),    icon: () => <List size={13} strokeWidth={1.5} /> },
                  { value: 'compact', label: t('ctxMenu.compact'), icon: () => <AlignJustify size={13} strokeWidth={1.5} /> },
                  { value: 'details', label: t('ctxMenu.details'), icon: () => <Columns3 size={13} strokeWidth={1.5} /> },
                  { value: 'grid',    label: t('ctxMenu.grid'),    icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ viewMode: v as ViewMode })}
              />
            </div>

            <Show when={viewMode() === 'details'}>
              <div class="design-row stacked">
                <div class="design-left">
                  <span class="design-label">{t('contacts.columns.label')}</span>
                  <span class="design-desc">{t('contacts.columns.desc')}</span>
                </div>
                <div class="col-picker">
                  <For each={allCols()}>{col =>
                    <button
                      classList={{ 'col-chip': true, active: rowColumns().includes(col.key) }}
                      onClick={() => toggleCol(col.key)}
                    >{col.label}</button>
                  }</For>
                </div>
              </div>
            </Show>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('contacts.shown.label')}</span>
                <span class="design-desc">{t('contacts.shown.desc')}</span>
              </div>
              {/* Was a raw number input while CalendarConfig drove the identical
                  "how many items to show" with NumberStepper. No upper bound:
                  a large address book should be showable in full. [S-17] */}
              <NumberStepper
                value={count()}
                min={5}
                step={5}
                onChange={(v) => update({ count: v })}
              />
            </div>

            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('contacts.defaultSearch.label')}</span>
                <span class="design-desc">{t('contacts.defaultSearch.desc')}</span>
              </div>
              <input
                id="contacts-search"
                class="text-input"
                type="text"
                placeholder={t('contacts.filterPlaceholder')}
                value={defaultSearch()}
                onInput={e => update({ defaultSearch: e.currentTarget.value })}
              />
            </div>

          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
