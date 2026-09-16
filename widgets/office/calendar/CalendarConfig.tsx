import { createMemo, createSignal, createEffect, untrack, For, Show } from 'solid-js';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import ColorPicker from '../../../src/ColorPicker.tsx';
import { CalendarDays, CalendarRange, Calendar, List, Monitor, Database } from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { resolveCalendarColor } from './colors.ts';
import { resolveCalendarTargets, calendarColorKey } from './targets.ts';
import { type WeekStart } from './dates.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';
import './CalendarConfig.css';

/** One calendar from `calendar_list_calendars` [S-48]. */
interface CalInfo { id: string; name: string; is_default: boolean; color: string | null }

export interface CalendarConfigData {
  accountIds?: string[];
  accountId?: string;
  // [S-49] Per-account selected calendar ids; an account absent here shows its
  // default calendar. [S-51] color overrides keyed by calendar id (or account id
  // for a default calendar). `accountColors` is the legacy per-account key,
  // migrated into `calendarColors` on the next write.
  calendars?: Record<string, string[]>;
  calendarColors?: Record<string, string>;
  accountColors?: Record<string, string>;
  defaultView?: string;
  showBirthdays?: boolean;
  agendaCount?: number;
  weekStart?: WeekStart;
}

interface CalendarConfigProps {
  config: CalendarConfigData;
  onChange: (c: CalendarConfigData) => void;
  globals?: { onManageAccounts?: () => void };
}

export default function CalendarConfig(props: CalendarConfigProps) {
  // [S-13] Stateless: derive every control's value straight from props.config, so
  // an external change (e.g. the widget's own context-menu view switcher [S-21])
  // shows immediately and is never reverted by a stale local copy.
  const accountIds = () => props.config.accountIds ?? (props.config.accountId ? [props.config.accountId] : []);
  const defaultView = () => props.config.defaultView ?? 'month';
  const showBirthdays = () => props.config.showBirthdays ?? true;
  const agendaCount = () => props.config.agendaCount ?? 20;
  // [S-46] Narrow to the closed set; a legacy/absent value defaults to Monday.
  const weekStart = (): WeekStart => props.config.weekStart === 'sunday' ? 'sunday' : 'monday';
  const calendars = () => props.config.calendars ?? {};
  // [S-51] Legacy per-account overrides fold under per-calendar overrides so a
  // migrated color still resolves (the default calendar shares its account key).
  const calendarColors = () => ({ ...(props.config.accountColors ?? {}), ...(props.config.calendarColors ?? {}) });

  // [S-48] Per-account calendar lists, fetched lazily and cached here (remote
  // data, not a mirror of props.config, so [S-13] is respected). An error caches
  // an empty list — the widget still shows the account's default calendar.
  const [calLists, setCalLists] = createSignal<Record<string, CalInfo[]>>({});
  const [calLoading, setCalLoading] = createSignal<Record<string, boolean>>({});
  createEffect(() => {
    const ids = accountIds();
    untrack(() => {
      for (const id of ids) {
        if (calLists()[id] !== undefined || calLoading()[id]) continue;
        setCalLoading(m => ({ ...m, [id]: true }));
        invoke('calendar_list_calendars', { accountId: id })
          .then(list => setCalLists(m => ({ ...m, [id]: list })))
          .catch(() => setCalLists(m => ({ ...m, [id]: [] })))
          .finally(() => setCalLoading(m => ({ ...m, [id]: false })));
      }
    });
  });

  // Effective selection for an account: the explicit list, else the default
  // calendar(s) — mirrors the widget's [S-49] resolution.
  function currentSelection(accId: string): string[] {
    const explicit = calendars()[accId];
    if (explicit) return explicit;
    return (calLists()[accId] ?? []).filter(c => c.is_default).map(c => c.id);
  }
  const isCalChecked = (accId: string, cal: CalInfo): boolean => {
    const explicit = calendars()[accId];
    return explicit ? explicit.includes(cal.id) : cal.is_default;
  };

  // The exact fetch targets [S-50]; a displayed calendar's palette index +
  // color key are read from here so the config swatch matches the widget chip.
  const allTargets = createMemo(() => resolveCalendarTargets(accountIds(), props.config.calendars));
  // Index of a calendar's fetch target, or -1 when it isn't displayed. A default
  // target (calendarId undefined) matches the account's default calendar.
  const targetIndexFor = (accId: string, cal: CalInfo): number =>
    allTargets().findIndex(tg =>
      tg.accountId === accId &&
      (tg.calendarId === cal.id || (tg.calendarId === undefined && cal.is_default)),
    );

  // Write-through: patch props.config directly, dropping the legacy `accountId`
  // and folding legacy `accountColors` into `calendarColors`, so the panel never
  // holds its own copy and the config converges to one schema.
  function update(patch: Partial<CalendarConfigData>) {
    const { accountId: _dropId, accountColors: _dropAC, ...rest } = props.config;
    props.onChange({ ...rest, accountIds: accountIds(), calendarColors: calendarColors(), ...patch });
  }

  function toggleAccount(id: string) {
    const ids = accountIds();
    update({ accountIds: ids.includes(id) ? ids.filter(a => a !== id) : [...ids, id] });
  }

  function toggleCalendar(accId: string, calId: string) {
    const cur = currentSelection(accId);
    const next = cur.includes(calId) ? cur.filter(x => x !== calId) : [...cur, calId];
    const map = { ...calendars() };
    // Empty selection means "default calendar" (see resolveCalendarTargets), so
    // drop the key rather than persisting an empty list.
    if (next.length === 0) delete map[accId];
    else map[accId] = next;
    update({ calendars: map });
  }

  function setCalendarColor(keyId: string, hex: string) {
    update({ calendarColors: { ...calendarColors(), [keyId]: hex } });
  }

  // [S-49]/[S-51] Per-account calendar picker, rendered under the account row via
  // the AccountToggleList slot. Each calendar row carries its color swatch (shown
  // while the calendar is displayed) and — when the account has more than one
  // calendar — a show/hide toggle. The default calendar starts on.
  const accountCalendars = (accId: string) => {
    const list = () => calLists()[accId] ?? [];
    return (
      <Show when={accountIds().includes(accId) && list().length >= 1}>
        <div class="cal-cal-list">
          <span class="cal-cal-list-label">{t('calendar.calendars.label')}</span>
          <For each={list()}>
            {(cal) => {
              const idx = () => targetIndexFor(accId, cal);
              const keyId = () => {
                const i = idx();
                return i >= 0 ? calendarColorKey(accId, allTargets()[i].calendarId) : cal.id;
              };
              return (
                <div class="cal-cal-row">
                  <span class="cal-cal-name">{cal.name}</span>
                  <div class="cal-cal-controls">
                    <Show when={idx() >= 0}>
                      <ColorPicker
                        value={resolveCalendarColor(keyId(), idx(), calendarColors())}
                        onChange={(hex) => setCalendarColor(keyId(), hex)}
                      />
                    </Show>
                    <Show when={list().length > 1}>
                      <ToggleSwitch
                        checked={isCalChecked(accId, cal)}
                        onLabel={t('toggle.shown')}
                        offLabel={t('toggle.hidden')}
                        onChange={() => toggleCalendar(accId, cal.id)}
                      />
                    </Show>
                  </div>
                </div>
              );
            }}
          </For>
        </div>
      </Show>
    );
  };

  return (
    <div class="calendar-config-body">
      <ConfigTabs tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            {/* [S-49]/[S-51] Per-account calendar picker renders directly beneath
                its account row (via AccountToggleList's slot): each calendar's
                show/hide toggle and its color swatch, together. */}
            <AccountToggleList
              providerFilter={['microsoft', 'google', 'caldav']}
              providerBadges={{ microsoft: 'M365', google: 'Google', caldav: 'CalDAV' }}
              accountIds={accountIds}
              onToggle={toggleAccount}
              onManageAccounts={props.globals?.onManageAccounts}
              perAccountExtra={accountCalendars}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('calendar.view.label')}</span>
                <span class="design-desc">{t('calendar.view.desc')}</span>
              </div>
              <Dropdown
                value={defaultView()}
                options={[
                  { value: 'month',  label: t('ctxMenu.month'),  icon: () => <CalendarDays size={13} strokeWidth={1.5} /> },
                  { value: 'week',   label: t('ctxMenu.week'),   icon: () => <CalendarRange size={13} strokeWidth={1.5} /> },
                  { value: 'day',    label: t('ctxMenu.day'),    icon: () => <Calendar size={13} strokeWidth={1.5} /> },
                  { value: 'agenda', label: t('ctxMenu.agenda'), icon: () => <List size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ defaultView: v })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('calendar.weekStart.label')}</span>
                <span class="design-desc">{t('calendar.weekStart.desc')}</span>
              </div>
              <Dropdown
                value={weekStart()}
                options={[
                  { value: 'monday', label: t('calendar.weekStart.monday'), icon: () => <CalendarDays size={13} strokeWidth={1.5} /> },
                  { value: 'sunday', label: t('calendar.weekStart.sunday'), icon: () => <CalendarRange size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update({ weekStart: v === 'sunday' ? 'sunday' : 'monday' })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('calendar.agendaEvents.label')}</span>
                <span class="design-desc">{t('calendar.agendaEvents.desc')}</span>
              </div>
              <NumberStepper
                value={agendaCount()}
                min={1}
                max={100}
                step={1}
                onChange={(v) => update({ agendaCount: v })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('calendar.showBirthdays.label')}</span>
                <span class="design-desc">{t('calendar.showBirthdays.desc')}</span>
              </div>
              <ToggleSwitch
                checked={showBirthdays()}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update({ showBirthdays: v })}
              />
            </div>

          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
