import { Show } from 'solid-js';
import { Database, Monitor, CalendarRange, HardDrive, Trash2 } from 'lucide-solid';
import { ask } from '@tauri-apps/plugin-dialog';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';
import {
  parseEntries, parseHistoryWeeks, parseStorageMode,
  HISTORY_WEEKS, type HistoryWeeks,
} from './mood.ts';

interface MoodConfigData {
  entries?: unknown;
  storageMode?: unknown;
  historyWeeks?: unknown;
  showNote?: boolean;
  showStats?: boolean;
  [k: string]: unknown;
}

interface MoodConfigProps {
  config: MoodConfigData;
  onChange: (c: MoodConfigData) => void;
}

export default function MoodTrackerConfig(props: MoodConfigProps) {
  // [S-13] Stateless: derive every persisted control's value straight from
  // props.config, so an external change is never reverted by a stale local copy.
  const storageMode = () => parseStorageMode(props.config.storageMode);
  const weeks = () => parseHistoryWeeks(props.config.historyWeeks);
  const count = () => parseEntries(props.config.entries).length;

  function update(patch: MoodConfigData) {
    props.onChange({ ...props.config, ...patch });
  }

  async function clearHistory() {
    // Wiping the log is unrecoverable, so it confirms through the same native
    // dialog account disconnect uses.
    const ok = await ask(t('moodTracker.clearConfirm', { count: count() }), {
      title: t('moodTracker.clearTitle'),
      kind: 'warning',
    });
    if (!ok) return;
    update({ entries: [] });
  }

  return (
    <ConfigTabs
      tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}
    >
      {(tab) => <>
        <Show when={tab === 'source'}>
          {/* [S-16] The same source list the To Do widget shows, with "On this
              device" as its leading row. Mood entries have no account backend,
              so this is the ONE source: its switch is disabled rather than
              inviting a click that could only take the widget's storage away.
              `providerFilter` is empty, so no account rows are fetched. */}
          <AccountToggleList
            providerFilter={[]}
            providerBadges={{}}
            accountIds={() => []}
            onToggle={() => { /* ignore: no account sources for mood entries */ }}
            leadingOptions={[{
              id: 'local',
              name: t('moodTracker.storage.local'),
              badge: t('moodTracker.storage.localBadge'),
              sublabel: t('moodTracker.storage.localDesc'),
              icon: <HardDrive class="provider-logo" size={20} strokeWidth={1.5} />,
              active: () => storageMode() === 'local',
              disabled: true,
              onToggle: () => update({ storageMode: 'local' }),
            }]}
          />

          <div class="divider" />

          {/* Standard row: what it is on the left, the control on the right,
              like every other setting in the panel. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('moodTracker.historySection')}</span>
              <span class="design-desc">
                <Show
                  when={count() > 0}
                  fallback={t('moodTracker.noHistoryHint')}
                >
                  {t('moodTracker.historyCount', { count: count() })}
                </Show>
              </span>
            </div>
            <button class="btn-danger" disabled={count() === 0} onClick={clearHistory}>
              <Trash2 size={14} strokeWidth={1.5} />
              {t('moodTracker.clearBtn')}
            </button>
          </div>
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('moodTracker.historyLabel')}</span>
              <span class="design-desc">{t('moodTracker.historyDesc')}</span>
            </div>
            <Dropdown
              value={String(weeks())}
              options={HISTORY_WEEKS.map((w) => ({
                value: String(w),
                label: t('moodTracker.weeksOption', { n: w }),
                icon: () => <CalendarRange size={13} strokeWidth={1.5} />,
              }))}
              onChange={(v) => update({ historyWeeks: Number(v) as HistoryWeeks })}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('moodTracker.noteToggleLabel')}</span>
              <span class="design-desc">{t('moodTracker.noteToggleDesc')}</span>
            </div>
            <ToggleSwitch
              checked={props.config.showNote !== false}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update({ showNote: v })}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('moodTracker.statsToggleLabel')}</span>
              <span class="design-desc">{t('moodTracker.statsToggleDesc')}</span>
            </div>
            <ToggleSwitch
              checked={props.config.showStats !== false}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update({ showStats: v })}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
