import { Show, For } from 'solid-js';
import { ask } from '@tauri-apps/plugin-dialog';
import { invoke } from '../../../src/ipc.ts';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import { accountRevision } from '../../../src/accountStore.ts';
import {
  List, AlignJustify, Monitor, Database, RefreshCw, HardDrive,
  Circle, CheckCircle2, Plus, X as XIcon,
} from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { clampTitleLines, TITLE_LINES_MIN, TITLE_LINES_MAX } from './titleLines.ts';
import { TODAY_LIST_ID } from './todayList.ts';
import { createTaskLists, type TodoTaskList } from './taskLists.ts';
import type { LocalTaskList } from './localLists.ts';
import {
  localLists, createLocalList, renameLocalList, deleteLocalList, ensureLocalList,
} from './localListStore.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';
import './TodoConfig.css';
import type { JSX } from 'solid-js';

export interface TodoConfigData {
  storageMode?: string;
  /** [S-57] Which device list this widget shows, when the source is "This device". */
  localListId?: string;
  accountIds?: string[];
  accountId?: string;
  listId?: string;
  listName?: string;
  showCompleted?: boolean;
  refreshInterval?: number;
  viewMode?: string;
  titleLines?: number;
}

export interface TodoConfigProps {
  config?: TodoConfigData;
  onChange: (config: TodoConfigData) => void;
  globals?: { onManageAccounts?: () => void };
}

export default function TodoConfig(props: TodoConfigProps): JSX.Element {
  const config = (): TodoConfigData => props.config ?? {};

  // [S-13] Stateless: derive every persisted control's value straight from
  // props.config, so an external change (e.g. the widget's own context-menu
  // view/completed switcher [S-21]) shows immediately and is never reverted by a
  // stale local copy.
  const storageMode = (): string => config().storageMode ?? 'account';
  const localListId = (): string => config().localListId ?? '';
  const accountIds = (): string[] => config().accountIds ?? (config().accountId ? [config().accountId!] : []);
  const listId = (): string => config().listId ?? '';
  const showCompleted = (): boolean => config().showCompleted ?? false;
  const refreshInterval = (): number => config().refreshInterval ?? 60;
  const viewMode = (): string => config().viewMode ?? 'list';
  const titleLines = (): number => clampTitleLines(config().titleLines);

  // The Refresh section (polling cadence) is meaningless for on-device tasks, so
  // it's dropped from the section list in local mode. [S-47]
  const tabs = () => {
    const base = [
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ];
    return storageMode() === 'local'
      ? base
      : [...base, { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw }];
  };

  // Write-through: patch props.config directly, dropping the legacy `accountId`
  // (pre-multi-account), so the panel keeps no copy.
  function update(patch: Partial<TodoConfigData>): void {
    const { accountId: _old, ...rest } = config();
    props.onChange({ ...rest, accountIds: accountIds(), ...patch });
  }

  function toggleAccount(id: string): void {
    const selected = accountIds().includes(id) ? [] : [id];
    // Switching account invalidates the previously-selected list, which belongs
    // to the old account. Clear it (and the cached lists) so the fetch effect
    // re-selects the new account's default list instead of a stale id. Picking an
    // account also switches the source off "This device" (single-select). [S-47]
    taskLists.reset();
    update({ storageMode: 'account', accountIds: selected, listId: '', listName: '' });
  }

  // "This device" is one selectable source among the accounts (single-select).
  // Turning it on clears any account + cached list; turning it off returns to
  // account mode with no account (the widget then prompts "select account"),
  // mirroring how toggling the active account off leaves no source. [S-47]
  //
  // [S-57] Turning it ON also lands the widget on a usable list: the one it had
  // before if it still exists, else the first list on the device, else a new
  // one. Only the last case creates anything, so switching source back and forth
  // can't litter the pool with empty lists.
  function toggleLocal(): void {
    if (storageMode() === 'local') {
      update({ storageMode: 'account' });
    } else {
      taskLists.reset();
      const id = ensureLocalList(localListId(), t('todo.localLists.defaultName'));
      update({ storageMode: 'local', accountIds: [], listId: '', listName: '', localListId: id });
    }
  }

  // ── Device lists ([S-58]) ──────────────────────────────────────────────────
  // The pool itself lives in the shared store, not in this widget's config; only
  // the SELECTION is a config value. So creating or renaming a list here shows
  // up in every widget already displaying it, and deleting one is a device-wide
  // action, not this widget's private cleanup.
  //
  // The editor renders INSIDE the "This device" row's card ([S-61]), not as a
  // row beside it, because a list belongs to its source: a device list is a
  // thing you create and rename here, while an account's lists are read-only
  // ones fetched from the provider.
  function addLocalList(): void {
    update({ localListId: createLocalList(t('todo.localLists.newName')) });
  }

  async function removeLocalList(list: LocalTaskList): Promise<void> {
    // [S-62] ALWAYS confirms, empty or not, through the same native dialog the
    // mood log's "clear history" uses. Deleting a list takes its tasks with it
    // and nothing can bring them back; the X sits one row from the name field
    // the user was just typing in, so skipping the prompt for an empty list
    // made a whole list one stray click from gone.
    const ok = await ask(
      list.tasks.length > 0
        ? t('todo.localLists.deleteConfirm', { name: list.name, count: list.tasks.length })
        // "and its 0 tasks" is not a sentence; an empty list gets the shared
        // noun-free body instead.
        : t('common.deleteEmptyConfirm', { name: list.name }),
      { title: t('todo.localLists.deleteTitle'), kind: 'warning' },
    );
    if (!ok) return;
    deleteLocalList(list.id);
    // The widget showing it falls back to the "pick a list" prompt rather than
    // being silently handed someone else's tasks.
    if (localListId() === list.id) update({ localListId: '' });
  }

  // [S-53] Fetch the lists for the SELECTED ACCOUNT, and only when that changes
  // (or accounts are added/removed). `createTaskLists` memoises the account id,
  // so the whole-new-config-object every `update()` writes no longer re-runs
  // this — which is what used to refetch, and flash the picker, on every click.
  const taskLists = createTaskLists({
    accountId: () => accountIds()[0] ?? '',
    revision: accountRevision,
    fetch: (accountId) => invoke('todo_get_lists', { accountId }),
    // [S-63] `network_error` is the backend's transport-failure sentinel.
    mapError: (raw) =>
      raw === 'todo_not_authorized' ? t('todo.reauthError')
      : raw === 'network_error' ? t('todo.networkError')
      : raw,
    // Auto-select the default list if none is selected yet ([S-16]).
    onLoaded: (result) => {
      if (!listId() && result.length > 0) {
        const def = result.find(l => l.is_default) || result[0];
        update({ listId: def.id, listName: def.display_name });
      }
    },
  });

  function selectList(list: TodoTaskList): void {
    update({ listId: list.id, listName: list.display_name });
  }

  // [S-61] The account's task-list picker, rendered under ITS OWN account row
  // via the AccountToggleList slot. Only the selected account has lists to show,
  // and only in account mode.
  const accountLists = (accId: string): JSX.Element => (
    <Show when={storageMode() === 'account' && accountIds().includes(accId)}>
      <div class="todo-source-lists">
        <span class="todo-source-lists-label">{t('todo.taskList.label')}</span>
        <span class="todo-source-lists-desc">{t('todo.taskList.desc')}</span>
        <Show when={taskLists.error()}>
          <div class="todo-config-error">{taskLists.error()}</div>
        </Show>
        {/* [S-53] The loading line only stands in for the picker while there is
            no picker to draw. A refresh that already has lists leaves them on
            screen — swapping them for this line and back is what made the panel
            jump. */}
        <Show when={taskLists.pending()}>
          <div class="todo-config-hint">{t('todo.loadingLists')}</div>
        </Show>
        <Show when={taskLists.lists().length > 0}>
          <div class="todo-list-picker">
            {/* [S-52] Today leads the picker: it is a view over all the lists
                below it, not one of them. Rendered client-side because no
                backend has such a list to return. */}
            <button
              class={`todo-list-btn ${listId() === TODAY_LIST_ID ? 'active' : ''}`}
              onClick={() => selectList({ id: TODAY_LIST_ID, display_name: t('todo.todayList'), is_default: false })}
            >
              {t('todo.todayList')}
              <span class="todo-list-default">{t('todo.todayBadge')}</span>
            </button>
            <For each={taskLists.lists()}>
              {(list) => (
                <button
                  class={`todo-list-btn ${listId() === list.id ? 'active' : ''}`}
                  onClick={() => selectList(list)}
                >
                  {list.display_name}
                  <Show when={list.is_default}>
                    <span class="todo-list-default">{t('todo.defaultBadge')}</span>
                  </Show>
                </button>
              )}
            </For>
          </div>
        </Show>
      </div>
    </Show>
  );

  // [S-58]/[S-61] "This device" owns many named lists, the same way an account
  // does; this is their picker + editor, in that source's own card.
  const deviceLists = (): JSX.Element => (
    <Show when={storageMode() === 'local'}>
      <div class="todo-source-lists">
        <span class="todo-source-lists-label">{t('todo.localLists.label')}</span>
        <span class="todo-source-lists-desc">{t('todo.localLists.desc')}</span>
        <Show when={localLists().length > 0}>
          {/* Settings.SPEC [S-18]/[S-11]: the shared collection-editor
              primitive, ended by a `btn-add`. Only the ROW is todo's. */}
          <div class="items-list">
            <For each={localLists()}>
              {(list) => (
                <div class={`todo-local-list ${localListId() === list.id ? 'active' : ''}`}>
                  {/* The same dot the widget's rows use, for the same reason: it
                      is the one control that changes state, and the name next to
                      it stays editable text. */}
                  <button
                    class="todo-local-list-pick"
                    title={t('todo.localLists.select')}
                    aria-label={t('todo.localLists.select')}
                    onClick={() => update({ localListId: list.id })}
                  >
                    <Show when={localListId() === list.id} fallback={<Circle size={15} strokeWidth={1.5} />}>
                      <CheckCircle2 size={15} strokeWidth={1.5} />
                    </Show>
                  </button>
                  <input
                    class="todo-local-list-name"
                    type="text"
                    value={list.name}
                    placeholder={t('todo.localLists.namePlaceholder')}
                    onInput={(e) => renameLocalList(list.id, e.currentTarget.value)}
                  />
                  <span class="todo-local-list-count">{list.tasks.length}</span>
                  <button
                    class="item-remove"
                    title={t('common.delete')}
                    aria-label={t('common.delete')}
                    onClick={() => { void removeLocalList(list); }}
                  ><XIcon size={12} strokeWidth={2} /></button>
                </div>
              )}
            </For>
          </div>
        </Show>
        <button class="btn-add" onClick={addLocalList}>
          <Plus size={14} strokeWidth={1.5} />
          {t('todo.localLists.add')}
        </button>
      </div>
    </Show>
  );

  return (
    <div class="todo-config-body">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            {/* "This device" (local storage) is a selectable source rendered as
                the first row of the account list, styled like an account and
                selected the same way. [S-47]

                [S-61] Each source carries ITS OWN list picker in its own card:
                what a "list" is, and what you can do to one, is provider
                specific — the device's lists are created and renamed here,
                an account's are read back from Microsoft or Google. */}
            <AccountToggleList
              providerFilter={['microsoft', 'google']}
              providerBadges={{ microsoft: 'M365', google: 'Google' }}
              accountIds={accountIds}
              onToggle={toggleAccount}
              onManageAccounts={props.globals?.onManageAccounts}
              perAccountExtra={accountLists}
              leadingOptions={[{
                id: 'local',
                name: t('todo.storage.local'),
                badge: t('todo.storage.localBadge'),
                sublabel: t('todo.storage.localDesc'),
                icon: <HardDrive class="provider-logo" size={20} strokeWidth={1.5} />,
                active: () => storageMode() === 'local',
                onToggle: toggleLocal,
                extra: deviceLists,
              }]}
            />

          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('todo.view.label')}</span>
                <span class="design-desc">{t('todo.view.desc')}</span>
              </div>
              <Dropdown
                value={viewMode()}
                options={[
                  { value: 'list',    label: t('ctxMenu.list'),    icon: () => <List size={13} strokeWidth={1.5} /> },
                  { value: 'compact', label: t('ctxMenu.compact'), icon: () => <AlignJustify size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ viewMode: v })}
              />
            </div>

            {/* [S-49] How many wrapped lines of a title to show before it
                clamps. */}
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('todo.titleLines.label')}</span>
                <span class="design-desc">{t('todo.titleLines.desc')}</span>
              </div>
              <NumberStepper
                value={titleLines()}
                min={TITLE_LINES_MIN}
                max={TITLE_LINES_MAX}
                step={1}
                onChange={(v) => update({ titleLines: clampTitleLines(v) })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('todo.showCompleted.label')}</span>
                <span class="design-desc">{t('todo.showCompleted.desc')}</span>
              </div>
              <ToggleSwitch
                checked={showCompleted()}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update({ showCompleted: v })}
              />
            </div>

          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('todo.refreshInterval.label')}</span>
                <span class="design-desc">{t('todo.refreshInterval.desc')}</span>
              </div>
              <NumberStepper
                value={refreshInterval()}
                min={30}
                max={600}
                step={30}
                unit="s"
                onChange={(v) => update({ refreshInterval: v })}
              />
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
