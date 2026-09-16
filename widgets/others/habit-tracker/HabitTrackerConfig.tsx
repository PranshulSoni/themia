import { For, Show, type JSX } from 'solid-js';
import { ask } from '@tauri-apps/plugin-dialog';
import {
  X as XIcon, Clock, CalendarDays, CalendarRange, Calendar, Repeat,
  Database, HardDrive, Circle, CheckCircle2, Monitor,
} from 'lucide-solid';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import '../../../src/styles/config-base.css';
import {
  RESET_PERIODS, RESET_LABELS,
  DEFAULT_CUSTOM_MINUTES, MIN_CUSTOM_MINUTES, MAX_CUSTOM_MINUTES,
  clampHistoryDots, MIN_HISTORY_DOTS, MAX_HISTORY_DOTS,
  type ResetPeriod,
} from './habit.ts';
import { type HabitList } from './habitLists.ts';
import {
  habitLists, createHabitList, renameHabitListById, deleteHabitList,
} from './habitListStore.ts';
import './HabitTrackerConfig.css';
import { t } from '../../../src/i18n.ts';

interface HabitConfigData {
  /** [S-18] Which device list this widget shows. The habits themselves live in
   *  the device-wide pool, not here. */
  localListId?: string;
  resetPeriod?: ResetPeriod;
  customMinutes?: number;
  historyDots?: number;
  [k: string]: unknown;
}

interface HabitConfigProps {
  config: HabitConfigData;
  onChange: (c: HabitConfigData) => void;
}

// [S-23] There is NO habit editor here. Habits are added, renamed, reordered and
// deleted in the widget itself ([S-16]/[S-14]/[S-13]/[S-22]) — right where the
// user is looking at them — so a second editor in config was a parallel path to
// the same data with no reason to exist. Config owns the SOURCE (which list) and
// the widget's settings; the list's contents belong to the widget.
export default function HabitTrackerConfig(props: HabitConfigProps) {
  const RESET_ICONS: Record<ResetPeriod, () => JSX.Element> = {
    hourly:  () => <Clock size={13} strokeWidth={1.5} />,
    daily:   () => <CalendarDays size={13} strokeWidth={1.5} />,
    weekly:  () => <CalendarRange size={13} strokeWidth={1.5} />,
    monthly: () => <Calendar size={13} strokeWidth={1.5} />,
    custom:  () => <Repeat size={13} strokeWidth={1.5} />,
  };

  // [S-13 settings] Stateless: every persisted control reads straight from
  // props.config or the shared pool, never from a local mirror. The habit POOL
  // is shared state and lives in the store; only the SELECTION is a config value.
  const localListId = () => props.config.localListId ?? '';
  const resetPeriod = (): ResetPeriod => props.config.resetPeriod ?? 'daily';
  const customMinutes = () => props.config.customMinutes ?? DEFAULT_CUSTOM_MINUTES;
  const historyDots = () => clampHistoryDots(props.config.historyDots);

  // ─── The device's habit lists ([S-18]/[S-19]) ───────────────────────────
  // The pool itself lives in the shared store, not in this widget's config; only
  // the SELECTION is a config value. So creating or renaming here shows up in
  // every widget already displaying that list, and deleting one is a
  // device-wide action, not this widget's private cleanup.
  function addList(): void {
    props.onChange({ ...props.config, localListId: createHabitList(t('habitTracker.lists.newName')) });
  }

  async function removeList(list: HabitList): Promise<void> {
    // [S-24] ALWAYS confirms, empty or not. Deleting a list takes its habits and
    // every streak they carry, nothing can bring them back, and the X sits one
    // row from the name field the user was just typing in.
    const ok = await ask(
      list.habits.length > 0
        ? t('habitTracker.lists.deleteConfirm', { name: list.name, count: list.habits.length })
        // "and its 0 habits" is not a sentence; an empty list gets the shared
        // noun-free body instead.
        : t('common.deleteEmptyConfirm', { name: list.name }),
      { title: t('habitTracker.lists.deleteTitle'), kind: 'warning' },
    );
    if (!ok) return;
    deleteHabitList(list.id);
    // The widget showing it falls back to the "pick a list" prompt rather than
    // being silently handed someone else's habits.
    if (localListId() === list.id) props.onChange({ ...props.config, localListId: '' });
  }

  // [S-19] The list picker + editor, rendered inside the "On this device" source
  // card via the AccountToggleList slot — which list this widget shows is a
  // question ABOUT that source, not a setting beside it.
  const deviceLists = (): JSX.Element => (
    <div class="habit-source-lists">
      <span class="habit-source-lists-label">{t('habitTracker.lists.label')}</span>
      <span class="habit-source-lists-desc">{t('habitTracker.lists.desc')}</span>
      <Show when={habitLists().length > 0}>
        {/* Settings.SPEC [S-18]/[S-11]: the shared collection-editor primitive,
            ended by a `btn-add`. Only the ROW is ours. */}
        <div class="items-list">
          <For each={habitLists()}>
            {(list) => (
              <div class={`habit-list-row ${localListId() === list.id ? 'active' : ''}`}>
                {/* The same dot the To Do pool uses, for the same reason: it is
                    the one control that changes state, and the name next to it
                    stays editable text. */}
                <button
                  class="habit-list-pick"
                  title={t('habitTracker.lists.select')}
                  aria-label={t('habitTracker.lists.select')}
                  onClick={() => props.onChange({ ...props.config, localListId: list.id })}
                >
                  <Show when={localListId() === list.id} fallback={<Circle size={15} strokeWidth={1.5} />}>
                    <CheckCircle2 size={15} strokeWidth={1.5} />
                  </Show>
                </button>
                <input
                  class="habit-list-name"
                  type="text"
                  value={list.name}
                  placeholder={t('habitTracker.lists.namePlaceholder')}
                  onInput={(e) => renameHabitListById(list.id, e.currentTarget.value)}
                />
                <span class="habit-list-count">{list.habits.length}</span>
                <button
                  class="item-remove"
                  title={t('common.delete')}
                  aria-label={t('common.delete')}
                  onClick={() => { void removeList(list); }}
                ><XIcon size={12} strokeWidth={2} /></button>
              </div>
            )}
          </For>
        </div>
      </Show>
      <button class="btn-add" onClick={addList}>{t('habitTracker.lists.add')}</button>
    </div>
  );

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          {/* [S-18] The same source list every account-bound widget shows, with
              "On this device" as its leading row (Mood Tracker [S-16]). Habits
              have no account backend, so this is the ONE source: its switch is
              disabled rather than inviting a click that could only take the
              widget's storage away. `providerFilter` is empty, so no account
              rows are fetched. */}
          <AccountToggleList
            providerFilter={[]}
            providerBadges={{}}
            accountIds={() => []}
            onToggle={() => { /* ignore: no account sources for habits */ }}
            leadingOptions={[{
              id: 'local',
              name: t('habitTracker.storage.local'),
              badge: t('habitTracker.storage.localBadge'),
              sublabel: t('habitTracker.storage.localDesc'),
              icon: <HardDrive class="provider-logo" size={20} strokeWidth={1.5} />,
              active: () => true,
              disabled: true,
              onToggle: () => { /* ignore: the only source, never switched off */ },
              extra: deviceLists,
            }]}
          />
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('habitTracker.resetLabel')}</span>
              <span class="design-desc">{t('habitTracker.resetDesc')}</span>
            </div>
            <Dropdown
              value={resetPeriod()}
              options={RESET_PERIODS.map((p) => ({ value: p, label: RESET_LABELS[p], icon: RESET_ICONS[p] }))}
              onChange={(v) => props.onChange({ ...props.config, resetPeriod: v as ResetPeriod })}
            />
          </div>

          <Show when={resetPeriod() === 'custom'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('habitTracker.intervalLabel')}</span>
                <span class="design-desc">{t('habitTracker.intervalDesc')}</span>
              </div>
              <NumberStepper
                value={customMinutes()}
                min={MIN_CUSTOM_MINUTES}
                max={MAX_CUSTOM_MINUTES}
                step={5}
                unit="min"
                width={52}
                onChange={(v) => props.onChange({ ...props.config, customMinutes: v })}
              />
            </div>
          </Show>

          {/* [S-15] How many history dots each row carries. 0 turns the strip
              off — a habit list is allowed to be just a list. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('habitTracker.historyLabel')}</span>
              <span class="design-desc">{t('habitTracker.historyDesc')}</span>
            </div>
            <NumberStepper
              value={historyDots()}
              min={MIN_HISTORY_DOTS}
              max={MAX_HISTORY_DOTS}
              step={1}
              width={52}
              onChange={(v) => props.onChange({ ...props.config, historyDots: clampHistoryDots(v) })}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
