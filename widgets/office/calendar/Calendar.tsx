import { createSignal, createMemo, createEffect, onCleanup, onMount, batch, untrack } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show, For, Switch, Match } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { CalendarDays, Lock, Cake, MapPin, User } from 'lucide-solid';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import { t, lang } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { showToast } from '../../../src/toastStore.ts';
import { createBirthdayCache, birthdayCacheKey, type Birthday } from './birthdays.ts';
import { createManualRefresh } from './manualRefresh.ts';
import { gdt, startOfWeek, weekdayOrder, type WeekStart } from './dates.ts';
import { resolveCalendarColor, isHexColor } from './colors.ts';
import { resolveCalendarTargets, calendarColorKey } from './targets.ts';
import { monthChipCapacity, eventDotColor } from './responsive.ts';
import { assignEventLanes, type LaneAssignment } from './dayLayout.ts';
import './Calendar.css';

export interface CalendarProps {
  headerMode?: HeaderMode;
  accountIds?: string[];
  accountId?: string;
  defaultView?: string;
  showBirthdays?: boolean;
  agendaCount?: number;
  weekStart?: WeekStart;
  // [S-49] Per-account selected calendar ids; an account absent here (or with an
  // empty list) shows its default calendar. [S-51] color overrides keyed by
  // calendar id (or account id for a default calendar); `accountColors` is the
  // legacy per-account key, merged under `calendarColors`.
  calendars?: Record<string, string[]>;
  calendarColors?: Record<string, string>;
  accountColors?: Record<string, string>;
  label?: string;
  _refresh?: number;
}

interface CalEvent {
  id: string;
  start_dt: string;
  end_dt: string;
  subject: string;
  location?: string | null;
  organizer?: string | null;
  is_all_day: boolean;
  web_link?: string;
  // Source identity, stamped at fetch. The chip color is resolved from these at
  // RENDER time (see evColor) so a config color change repaints without a
  // re-fetch; baking the resolved color here left it stale ([S-47]).
  // `_idx` is the fetch-target position, driving the fallback palette [S-08];
  // `_calendarId` is undefined for an account's default calendar.
  _accountId?: string;
  _calendarId?: string;
  _idx?: number;
}

type DayItem = { k: 'bd'; b: Birthday } | { k: 'ev'; e: CalEvent };

// -- Date helpers --
function sod(d: Date): Date { const r = new Date(d); r.setHours(0,0,0,0); return r; }
function addDays(d: Date, n: number): Date { const r = new Date(d); r.setDate(r.getDate() + n); return r; }
function ymd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
}
function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear()===b.getFullYear() && a.getMonth()===b.getMonth() && a.getDate()===b.getDate();
}

const H_START = 7, H_END = 22, H_SPAN = H_END - H_START;
const HOURS = Array.from({length:H_SPAN}, (_,i) => H_START+i);

function fmtH(h: number): string {
  const d = new Date(2000, 0, 1, h, 0, 0);
  return d.toLocaleTimeString(lang(), { hour: 'numeric' });
}

export default function Calendar(props: CalendarProps) {
  const effectiveIds = createMemo(() => {
    const ids = props.accountIds?.length ? props.accountIds : (props.accountId ? [props.accountId] : []);
    if (!ids.length && isDemo()) return ['demo-account'];
    return ids;
  });
  const viewMode = createMemo(() => props.defaultView || 'month');
  // [S-46] Trust-boundary narrow: any non-'sunday' persisted value (undefined,
  // garbage from an old/hand-edited config) folds to the Monday default.
  const weekStart = createMemo<WeekStart>(() => (props.weekStart === 'sunday' ? 'sunday' : 'monday'));
  // [S-49] The (account, calendar) pairs to fetch: one per selected calendar,
  // or one default-calendar target for an account with no selection.
  const targets = createMemo(() => resolveCalendarTargets(effectiveIds(), props.calendars));
  // [S-51] Legacy per-account overrides sit under per-calendar overrides (the
  // default calendar shares its account's key, so a migrated color still wins).
  const colorOverrides = createMemo(() => ({
    ...(props.accountColors ?? {}),
    ...(props.calendarColors ?? {}),
  }));
  // [S-47]/[S-51] Show color accents when more than one calendar is shown
  // (original [S-08] behavior) OR when the user set a custom color for any shown
  // calendar — so a single-calendar user's override is still visible.
  const showColors = createMemo(() => {
    if (targets().length > 1) return true;
    const ov = colorOverrides();
    return targets().some(tg => isHexColor(ov[calendarColorKey(tg.accountId, tg.calendarId)]));
  });

  const [navDate, setNavDate] = createSignal(sod(new Date()));
  const [events, setEvents] = createSignal<CalEvent[]>([]);
  // Measured body height → adaptive month chip count (bigger widget = more
  // chips per day; small = fold to one + "+N"). See responsive.ts [S-04].
  let bodyEl: HTMLDivElement | undefined;
  const [bodyH, setBodyH] = createSignal(0);
  const maxChips = createMemo(() => monthChipCapacity((bodyH() - 14) / 6));
  const [loadState, setLoadState] = createSignal<string>('loading');
  const [error, setError] = createSignal<string | null>(null);
  const [needsReauth, setNeedsReauth] = createSignal(false);
  const [TODAY, setTODAY] = createSignal(sod(new Date()));

  // [S-23]/[S-38] Birthdays come from a daily-TTL cache, never from the
  // per-fetch path: the backend enumerates the whole contact list per call.
  // The cache auto-(re)fetches on mount / account / showBirthdays changes;
  // nav clicks and poll ticks only nudge it (no-op while fresh).
  const bdayCache = createBirthdayCache(() =>
    birthdayCacheKey(props.showBirthdays, effectiveIds()),
  );
  const bdays = createMemo(() => bdayCache.data()?.birthdays ?? []);

  // [S-39] Surface missing contacts permission instead of silently retrying.
  let warnedDenied = '';
  createEffect(() => {
    const denied = bdayCache.data()?.deniedAccountIds ?? [];
    const sig = denied.join(',');
    if (denied.length && sig !== warnedDenied) {
      showToast(t('calendar.birthdaysDenied'), 'error', 5000);
    }
    warnedDenied = sig;
  });

  // -- View range --
  function viewRange(): [Date, Date] {
    const d = navDate();
    const vm = viewMode();
    if (vm === 'month') {
      const first = new Date(d.getFullYear(), d.getMonth(), 1);
      const last  = new Date(d.getFullYear(), d.getMonth()+1, 0);
      const ws    = weekStart();
      const start = startOfWeek(first, ws);
      // Fill to the end of the last week: 6 days past that week's own start.
      const end   = addDays(startOfWeek(last, ws), 6);
      return [start, end];
    }
    if (vm === 'week') {
      const start = startOfWeek(d, weekStart());
      return [start, addDays(start, 6)];
    }
    if (vm === 'day') return [d, d];
    return [TODAY(), addDays(TODAY(), 30)];
  }

  function rangeISO(): [string, string] {
    const [s, e] = viewRange();
    return [ymd(s)+'T00:00:00Z', ymd(addDays(e,1))+'T00:00:00Z'];
  }

  // -- Navigation --
  function nav(dir: number) {
    const d = new Date(navDate());
    const vm = viewMode();
    if (vm==='month') d.setMonth(d.getMonth()+dir);
    else if (vm==='week') d.setDate(d.getDate()+dir*7);
    else if (vm==='day')  d.setDate(d.getDate()+dir);
    setNavDate(sod(d));
    fetchData();
  }

  function goToday() { setNavDate(sod(new Date())); fetchData(); }

  // -- Keyboard nav --
  // ←/→: prev/next period. ↑/↓ in month view: move by a week.
  // T: today. Enter: open first event on the nav cursor day.
  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    const vm = viewMode();
    if (e.key === 'ArrowLeft')      { e.preventDefault(); nav(-1); }
    else if (e.key === 'ArrowRight'){ e.preventDefault(); nav(1); }
    else if (e.key === 'ArrowUp')   {
      if (vm === 'month' || vm === 'week') {
        e.preventDefault();
        const d = new Date(navDate()); d.setDate(d.getDate() - 7); setNavDate(sod(d)); fetchData();
      }
    }
    else if (e.key === 'ArrowDown') {
      if (vm === 'month' || vm === 'week') {
        e.preventDefault();
        const d = new Date(navDate()); d.setDate(d.getDate() + 7); setNavDate(sod(d)); fetchData();
      }
    }
    else if (e.key === 'Home')      { e.preventDefault(); goToday(); }
    else if (e.key === 'Enter')     {
      const items = dayItems(navDate());
      const ev = items.find(it => it.k === 'ev') as { k: 'ev'; e: CalEvent } | undefined;
      if (ev) { e.preventDefault(); openEvent(ev.e); }
    }
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;
  onMount(() => {
    _kbd = registerWidgetKeyboard(handleKey);
    if (bodyEl) {
      // Measure once synchronously so the first paint already uses the right
      // chip count (the RO first callback can be deferred).
      setBodyH(bodyEl.clientHeight);
      const ro = new ResizeObserver((entries) => setBodyH(entries[0].contentRect.height));
      ro.observe(bodyEl);
      onCleanup(() => ro.disconnect());
    }
  });
  onCleanup(() => _kbd?.dispose());
  function activateKbd(e: MouseEvent): void { if (e.button === 0) _kbd?.activate(); }

  // -- Data fetch --
  // Request-id guard: each fetchData() bumps this before awaiting; a slower
  // in-flight fetch for a previous month/view then bails instead of painting
  // its stale events over the current range. Mirrors the `seq` guard in
  // todo/Todo and stocks/SymbolSearch. No wall-clock timing.
  let fetchSeq = 0;
  async function fetchData() {
    const my = ++fetchSeq;
    setTODAY(sod(new Date()));
    const tgs = targets();
    if (!tgs.length) { setLoadState('empty'); return; }
    if (!events().length && !bdays().length) setLoadState('loading');
    setNeedsReauth(false);
    // Daily-TTL guarded: a no-op while the cached birthdays are fresh, so nav
    // clicks / arrow keys / poll ticks never re-enumerate contacts [S-38].
    void bdayCache.refresh(false);
    const [startDt, endDt] = rangeISO();
    try {
      // [S-50] One request per fetch target; `calendarId` undefined targets the
      // account's default calendar. Each event is stamped with its source id +
      // target index so its chip color resolves at render time.
      const results = await Promise.allSettled(
        tgs.map((tg, idx) =>
          invoke('calendar_get_events', { accountId: tg.accountId, startDt, endDt, calendarId: tg.calendarId })
            .then(evts => evts.map(e => ({ ...e, _accountId: tg.accountId, _calendarId: tg.calendarId, _idx: idx })))
        )
      );
      // A newer fetch started while we were awaiting: drop this result so
      // out-of-order completion never paints the wrong month's events.
      if (my !== fetchSeq) return;

      const allEvts: CalEvent[] = [];
      let hasError = false, authNeeded = false;
      for (const r of results) {
        if (r.status === 'fulfilled') {
          allEvts.push(...r.value);
        } else {
          const msg = String(r.reason);
          if (msg === 'calendar_not_authorized') authNeeded = true;
          else hasError = true;
          setError(msg);
        }
      }

      batch(() => {
        setEvents(allEvts);
        if (allEvts.length > 0 || bdays().length > 0) {
          setLoadState('loaded'); setError(null);
        } else if (authNeeded) {
          setNeedsReauth(true); setLoadState('error');
        } else if (hasError) {
          setLoadState('error');
        } else {
          setLoadState('loaded'); setError(null);
        }
      });
    } catch (ex) {
      if (my !== fetchSeq) return;
      const msg = String(ex);
      if (msg==='calendar_not_authorized') { setNeedsReauth(true); setLoadState('error'); }
      else if (msg.includes('Account not found')||msg.includes('no_accounts')) { setLoadState('empty'); }
      else { setError(msg); setLoadState('error'); }
    }
  }

  async function reauth() {
    const ids = effectiveIds();
    for (const id of ids) {
      await invoke('email_sign_out_account', { accountId: id }).catch(()=>{});
    }
    setLoadState('empty');
  }

  // -- Day helpers --
  function dayEvents(date: Date): CalEvent[] {
    const ds = sod(date), de = addDays(ds, 1);
    return events().filter(ev => {
      if (ev.is_all_day) {
        const evS = ev.start_dt.slice(0,10);
        const evE = ev.end_dt.slice(0,10);
        const dd  = ymd(date);
        return evS <= dd && dd < evE;
      }
      const s = gdt(ev.start_dt), e = gdt(ev.end_dt);
      return s < de && e > ds;
    });
  }

  function dayBirthdays(date: Date): Birthday[] {
    const m = date.getMonth()+1, d = date.getDate();
    return bdays().filter(b => b.month===m && b.day===d);
  }

  function dayItems(date: Date): DayItem[] {
    const bds: DayItem[]  = dayBirthdays(date).map(b => ({ k:'bd', b }));
    const evts: DayItem[] = dayEvents(date)
      .sort((a,b) => {
        if (a.is_all_day && !b.is_all_day) return -1;
        if (!a.is_all_day && b.is_all_day) return 1;
        return gdt(a.start_dt).getTime() - gdt(b.start_dt).getTime();
      })
      .map(e => ({ k:'ev', e }));
    return [...bds, ...evts];
  }

  // -- Derived grids --
  const monthCells = createMemo(() => {
    if (viewMode()!=='month') return [];
    const [start] = viewRange();
    return Array.from({length:42}, (_,i) => addDays(start,i));
  });

  const weekCells = createMemo(() => {
    if (viewMode()!=='week') return [];
    const [start] = viewRange();
    return Array.from({length:7}, (_,i) => addDays(start,i));
  });

  const dayData = createMemo(() => {
    if (viewMode()!=='day') return {allDay:[] as DayItem[], timed:[] as CalEvent[]};
    const items = dayItems(navDate());
    const allDay = items.filter(it => it.k==='bd' || (it.k==='ev' && it.e.is_all_day));
    const timed  = items.filter(it => it.k==='ev' && !it.e.is_all_day).map(it => (it as {k:'ev';e:CalEvent}).e);
    return {allDay, timed};
  });

  const agendaGroups = createMemo(() => {
    if (viewMode()!=='agenda') return [];
    const [s, e] = viewRange();
    const map = new Map<string, DayItem[]>();
    for (const ev of events()) {
      const dk = ev.is_all_day ? ev.start_dt.slice(0,10) : ymd(gdt(ev.start_dt));
      if (!map.has(dk)) map.set(dk, []);
      map.get(dk)!.push({k:'ev',e:ev});
    }
    for (const b of bdays()) {
      for (const yr of [s.getFullYear(), s.getFullYear()+1]) {
        const dk = `${yr}-${String(b.month).padStart(2,'0')}-${String(b.day).padStart(2,'0')}`;
        if (dk >= ymd(s) && dk <= ymd(e)) {
          if (!map.has(dk)) map.set(dk, []);
          map.get(dk)!.push({k:'bd',b});
        }
      }
    }
    const sorted = [...map.entries()].sort(([a],[b])=>a.localeCompare(b));
    const limit = props.agendaCount ?? 20;
    let count = 0;
    const out: [string, DayItem[]][] = [];
    for (const [dk, items] of sorted) {
      if (count >= limit) break;
      const take = items.slice(0, limit - count);
      out.push([dk, take]);
      count += take.length;
    }
    return out;
  });

  // -- Formatting --
  function fmtTitle(): string {
    const d = navDate();
    const vm = viewMode();
    if (vm==='month') return d.toLocaleDateString(lang(),{month:'long',year:'numeric'});
    if (vm==='week') {
      const [s,e] = viewRange();
      return `${s.toLocaleDateString(lang(),{month:'short',day:'numeric'})} – ${e.toLocaleDateString(lang(),{month:'short',day:'numeric'})}`;
    }
    if (vm==='day') return d.toLocaleDateString(lang(),{weekday:'short',month:'short',day:'numeric'});
    return '';
  }

  function fmtTime(iso: string): string {
    return gdt(iso).toLocaleTimeString(lang(),{hour:'numeric',minute:'2-digit'});
  }

  function fmtAgendaDay(dk: string): string {
    const d = new Date(dk+'T12:00:00');
    const diff = Math.round((sod(d).getTime()-TODAY().getTime())/86400000);
    if (diff===0) return t('calendar.today');
    if (diff===1) return t('calendar.tomorrow');
    return d.toLocaleDateString(lang(),{weekday:'short',month:'short',day:'numeric'});
  }

  function bdLabel(b: Birthday): string {
    return b.year ? `${b.display_name} (${new Date().getFullYear()-b.year})` : b.display_name;
  }

  function openEvent(ev: CalEvent) {
    if (ev.web_link || ev.id) invoke('calendar_open_event', { webLink: ev.web_link ?? '', eventId: ev.id ?? '' });
  }

  // [S-47]/[S-51] Resolve a chip's color at render time so a config color
  // override change repaints immediately, with no re-fetch.
  const evColor = (ev: CalEvent): string =>
    resolveCalendarColor(calendarColorKey(ev._accountId ?? '', ev._calendarId), ev._idx ?? 0, colorOverrides());

  function evTop(ev: CalEvent): number { const s=gdt(ev.start_dt); return Math.max(0,(s.getHours()*60+s.getMinutes()-H_START*60)/(H_SPAN*60)*100); }
  function evHeight(ev: CalEvent): number { const s=gdt(ev.start_dt),e=gdt(ev.end_dt); return Math.min(Math.max(30,(e.getTime()-s.getTime())/60000)/(H_SPAN*60)*100, 100-evTop(ev)); }

  // [S-06] Split overlapping day-view events into side-by-side lanes so none
  // hides another. Keyed by event object so the render can look up its column.
  const dayLanes = createMemo(() => {
    const timed = dayData().timed;
    const lanes = assignEventLanes(timed.map(ev => {
      const s = gdt(ev.start_dt).getTime();
      return { start: s, end: Math.max(gdt(ev.end_dt).getTime(), s) };
    }));
    const m = new Map<CalEvent, LaneAssignment>();
    timed.forEach((ev, i) => m.set(ev, lanes[i]));
    return m;
  });

  // -- Lifecycle --
  createPolling(fetchData, () => 5 * 60_000, 'calendar');

  createEffect(() => {
    // `targets()` tracks both the account set and the per-account calendar
    // selection, so changing either re-fetches [S-26]/[S-49].
    void targets(); void props.showBirthdays; void viewMode();
    untrack(() => fetchData());
  });

  // [S-40] Context-menu Refresh / config-close `_refresh` bump: re-fetch
  // events and force the birthday cache past its daily TTL (the one allowed
  // force-path; mount is skipped, see manualRefresh.ts).
  createManualRefresh(() => props._refresh, () => {
    void bdayCache.refresh(true);
    void fetchData();
  });

  return (
    <div class="calendar-widget" onMouseDown={activateKbd}>

      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={CalendarDays}
        title={props.label || t('widget.calendar.label')}
        secondary={
          /* Nav controls live in the header (was an in-body topbar). Hidden in
             agenda, which has no period to page through (fmtTitle()==='' and
             nav() is a no-op there). */
          <Show when={viewMode() !== 'agenda'}>
            <div class="cal-nav">
              <button class="nav-btn" onClick={() => nav(-1)}>‹</button>
              <span class="nav-title">{fmtTitle()}</span>
              <button class="nav-btn" onClick={() => nav(1)}>›</button>
              <button class="today-btn" onClick={goToday} title={t('calendar.goToday')}>●</button>
            </div>
          </Show>
        }
      />

      <div class="widget-body" ref={bodyEl}>

      <Switch>
        {/* -- States -- */}
        <Match when={loadState()==='empty'}>
          <div class="state-msg" innerHTML={t('calendar.selectAccount')} />
        </Match>

        <Match when={loadState()==='loading' && !events().length}>
          <div class="state-msg dim">{t('widgetUi.loading')}</div>
        </Match>

        <Match when={loadState()==='error' && needsReauth()}>
          <div class="reauth-box">
            <div class="reauth-icon"><Lock size={26} strokeWidth={1.5} /></div>
            <div class="reauth-title">{t('calendar.accessTitle')}</div>
            <div class="reauth-body">{t('calendar.accessBody')}</div>
            <button class="reauth-btn" onClick={reauth}>{t('calendar.reauthBtn')}</button>
          </div>
        </Match>

        <Match when={loadState()==='error'}>
          <div class="state-msg" style={{ color: 'var(--destructive)' }}>{error()}</div>
        </Match>

        {/* == Month view == */}
        <Match when={viewMode()==='month'}>
          <div class="dow-row">
            {/* [S-46] Header order follows weekStart. 2024-01-07 is a Sunday, so
                addDays(refSunday, getDay) yields that weekday's narrow label. */}
            <For each={weekdayOrder(weekStart())}>
              {(g) => <div class="dow">{new Date(2024, 0, 7 + g).toLocaleDateString(lang(), { weekday: 'narrow' })}</div>}
            </For>
          </div>
          <div class="month-grid">
            <For each={monthCells()}>
              {(day) => {
                const isToday = createMemo(() => sameDay(day, TODAY()));
                const isOther = day.getMonth() !== navDate().getMonth();
                // Reactive: the events fetch is async and resolves AFTER navDate
                // (and thus this cell) has already changed. A plain const would
                // snapshot the pre-fetch events, leaving the day blank until a
                // second navigation rebuilt the grid. A memo repaints on setEvents.
                const items = createMemo(() => dayItems(day));
                return (
                  <div class="day-cell" classList={{ today: isToday(), other: isOther }}>
                    <span class="dn" classList={{ 'dn-today': isToday() }}>{day.getDate()}</span>
                    <For each={items().slice(0, maxChips())}>
                      {(it) => (
                        <Show when={it.k==='bd'} fallback={
                          <div class="chip" classList={{ 'chip-allday': (it as {k:'ev';e:CalEvent}).e.is_all_day, 'chip-clickable': !!((it as {k:'ev';e:CalEvent}).e.web_link || (it as {k:'ev';e:CalEvent}).e.id) }}
                               style={showColors() ? { 'border-left': `2px solid ${evColor((it as {k:'ev';e:CalEvent}).e)}` } : {}}
                               onClick={() => openEvent((it as {k:'ev';e:CalEvent}).e)}>
                            {/* Time prefix reveals only on large widgets (CSS, [S-44]); DOM always carries it. */}
                            <Show when={!(it as {k:'ev';e:CalEvent}).e.is_all_day}>
                              <span class="chip-time">{fmtTime((it as {k:'ev';e:CalEvent}).e.start_dt)}</span>
                            </Show>
                            {(it as {k:'ev';e:CalEvent}).e.subject}
                          </div>
                        }>
                          <div class="chip chip-bd"><Cake class="cal-ic" size={9} strokeWidth={1.5} /> {(it as {k:'bd';b:Birthday}).b.display_name.split(' ')[0]}</div>
                        </Show>
                      )}
                    </For>
                    <Show when={items().length > maxChips()}>
                      <div class="chip-more">+{items().length - maxChips()}</div>
                    </Show>
                    {/* Compact fallback [S-43]: when the widget is too short for
                        chips (CSS hides them), show up to three colored dots so
                        days-with-events stay legible instead of a bare number. */}
                    <Show when={items().length}>
                      <div class="day-dots">
                        <For each={items().slice(0, 3)}>
                          {(it) => (
                            <span class="day-dot"
                                  style={{ background: eventDotColor(it.k, it.k === 'ev' ? evColor((it as {k:'ev';e:CalEvent}).e) : undefined, showColors()) }} />
                          )}
                        </For>
                        <Show when={items().length > 3}><span class="day-dot day-dot-more" /></Show>
                      </div>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>
        </Match>

        {/* == Week view == */}
        <Match when={viewMode()==='week'}>
          <div class="week-grid">
            <For each={weekCells()}>
              {(day) => {
                const isToday = createMemo(() => sameDay(day, TODAY()));
                const items = createMemo(() => dayItems(day));
                return (
                  <div class="week-col">
                    <div class="wk-hdr" classList={{ today: isToday() }}>
                      <span class="wk-dow">{day.toLocaleDateString(lang(),{weekday:'narrow'})}</span>
                      <span class="wk-num">{day.getDate()}</span>
                    </div>
                    <div class="wk-events">
                      <For each={items()}>
                        {(it) => (
                          <Show when={it.k==='bd'} fallback={
                            <div class="wk-chip" classList={{ 'chip-clickable': !!((it as {k:'ev';e:CalEvent}).e.web_link || (it as {k:'ev';e:CalEvent}).e.id) }}
                                 style={showColors() ? { 'border-left': `2px solid ${evColor((it as {k:'ev';e:CalEvent}).e)}` } : {}}
                                 onClick={() => openEvent((it as {k:'ev';e:CalEvent}).e)}>
                              {/* Time stacked ABOVE the title so the title keeps the full
                                  (narrow) column width instead of being crowded out. */}
                              <Show when={!(it as {k:'ev';e:CalEvent}).e.is_all_day}>
                                <span class="wk-time">{fmtTime((it as {k:'ev';e:CalEvent}).e.start_dt)}</span>
                              </Show>
                              <span class="wk-title">{(it as {k:'ev';e:CalEvent}).e.subject}</span>
                            </div>
                          }>
                            <div class="wk-chip chip-bd"><Cake class="cal-ic" size={9} strokeWidth={1.5} /> {(it as {k:'bd';b:Birthday}).b.display_name.split(' ')[0]}</div>
                          </Show>
                        )}
                      </For>
                    </div>
                  </div>
                );
              }}
            </For>
          </div>
        </Match>

        {/* == Day view == */}
        <Match when={viewMode()==='day'}>
          <Show when={dayData().allDay.length}>
            <div class="day-allday-bar">
              <For each={dayData().allDay}>
                {(it) => (
                  <Show when={it.k==='bd'} fallback={
                    <div class="allday-chip" classList={{ 'chip-clickable': !!((it as {k:'ev';e:CalEvent}).e.web_link || (it as {k:'ev';e:CalEvent}).e.id) }}
                         style={showColors() ? { 'border-left': `2px solid ${evColor((it as {k:'ev';e:CalEvent}).e)}` } : {}}
                         onClick={() => openEvent((it as {k:'ev';e:CalEvent}).e)}>
                      {(it as {k:'ev';e:CalEvent}).e.subject}
                    </div>
                  }>
                    <div class="allday-chip chip-bd"><Cake class="cal-ic" size={11} strokeWidth={1.5} /> {bdLabel((it as {k:'bd';b:Birthday}).b)}{t('calendar.birthdaySuffix')}</div>
                  </Show>
                )}
              </For>
            </div>
          </Show>

          <div class="day-scroll">
            <div class="day-timeline">
              <div class="hour-labels">
                <For each={HOURS}>
                  {(h) => <div class="hl">{fmtH(h)}</div>}
                </For>
              </div>
              <div class="hour-col">
                <For each={HOURS}>
                  {() => <div class="hour-slot" />}
                </For>
                <For each={dayData().timed}>
                  {(ev) => {
                    const lane = () => dayLanes().get(ev) ?? { col: 0, cols: 1 };
                    return (
                    <div class="timed-ev" classList={{ 'chip-clickable': !!(ev.web_link || ev.id) }}
                         style={{
                           top: `${evTop(ev).toFixed(2)}%`,
                           height: `${evHeight(ev).toFixed(2)}%`,
                           'min-height': '18px',
                           left: `${(lane().col * 100 / lane().cols).toFixed(2)}%`,
                           width: `calc(${(100 / lane().cols).toFixed(2)}% - 4px)`,
                           right: 'auto',
                           ...(showColors() ? { 'border-left': `2px solid ${evColor(ev)}` } : {}),
                         }}
                         onClick={() => openEvent(ev)}>
                      <span class="tev-time">{fmtTime(ev.start_dt)}</span>
                      <span class="tev-title">{ev.subject}</span>
                    </div>
                    );
                  }}
                </For>
                <Show when={!dayData().timed.length && !dayData().allDay.length}>
                  <div class="day-empty">{t('calendar.noEventsToday')}</div>
                </Show>
              </div>
            </div>
          </div>
        </Match>

        {/* == Agenda view == */}
        <Match when={viewMode()==='agenda'}>
          <Show when={!agendaGroups().length} fallback={
            <div class="agenda-scroll">
              <For each={agendaGroups()}>
                {([dk, items]) => (
                  <div class="ag-group">
                    <div class="ag-day-label">{fmtAgendaDay(dk)}</div>
                    <For each={items}>
                      {(it) => (
                        <Show when={it.k==='bd'} fallback={
                          <div class="ag-ev" classList={{ 'chip-clickable': !!((it as {k:'ev';e:CalEvent}).e.web_link || (it as {k:'ev';e:CalEvent}).e.id) }}
                               style={showColors() ? { 'border-left': `2px solid ${evColor((it as {k:'ev';e:CalEvent}).e)}`, 'padding-left': '8px' } : {}}
                               onClick={() => openEvent((it as {k:'ev';e:CalEvent}).e)}>
                            <div class="ag-time">
                              <Show when={!(it as {k:'ev';e:CalEvent}).e.is_all_day} fallback={<span class="ag-time-start">{t('calendar.allDay')}</span>}>
                                <span class="ag-time-start">{fmtTime((it as {k:'ev';e:CalEvent}).e.start_dt)}</span>
                                <span class="ag-time-end">{fmtTime((it as {k:'ev';e:CalEvent}).e.end_dt)}</span>
                              </Show>
                            </div>
                            <div class="ag-body">
                              <div class="ag-title">{(it as {k:'ev';e:CalEvent}).e.subject || t('calendar.noTitle')}</div>
                              <Show when={(it as {k:'ev';e:CalEvent}).e.location}>
                                <div class="ag-meta"><MapPin class="cal-ic" size={11} strokeWidth={1.5} /> {(it as {k:'ev';e:CalEvent}).e.location}</div>
                              </Show>
                              <Show when={(it as {k:'ev';e:CalEvent}).e.organizer}>
                                <div class="ag-meta"><User class="cal-ic" size={11} strokeWidth={1.5} /> {(it as {k:'ev';e:CalEvent}).e.organizer}</div>
                              </Show>
                            </div>
                          </div>
                        }>
                          <div class="ag-ev ag-bd">
                            <span class="ag-time"><Cake size={13} strokeWidth={1.5} /></span>
                            <span class="ag-title">{bdLabel((it as {k:'bd';b:Birthday}).b)}{t('calendar.birthdaySuffix')}</span>
                          </div>
                        </Show>
                      )}
                    </For>
                  </div>
                )}
              </For>
            </div>
          }>
            <div class="state-msg dim">{t('calendar.noUpcoming')}</div>
          </Show>
        </Match>
      </Switch>

      </div>
    </div>
  );
}
