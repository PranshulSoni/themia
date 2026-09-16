import { createSignal, createMemo, createEffect, on, For, Show, Switch, Match } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoScreentime, demoScreentimeRange } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import {
  formatDuration, topApps, dateForOffset, shiftDateKey, dayDescriptor,
  parseDateKey, MAX_HISTORY_OFFSET, appColor, appInitial, uniquePaths, donutSegments,
  parseHidden, filterHidden, sumSecs, resolveView, isRangePeriod, rangeLength,
  parseRange, dayTotals, maxDaySecs, totalSecs, heatLevel, heatmapCells, heatmapColumnDates,
  aggregateApps, stackForDay, migrateMetrics, applyLegacyMetrics, dailyAverage,
  showsApps, chartCap, totalSwitches, ghostRows, shareOf, parseGoalSecs, parseAppGoals,
  goalState,
  DEFAULT_MAX_APPS, HEATMAP_COLS, WEEK_DAYS,
  type RangeDay, type DonutSegment,
} from './screentimeDisplay.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { t, lang } from '../../../src/i18n.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import { EyeOff } from 'lucide-solid';
import './ScreentimeWidget.css';
import type { JSX } from 'solid-js';

export interface ScreentimeWidgetProps {
  headerMode?: HeaderMode;
  // [S-10] how many apps to list; 0 lists none ([S-53]).
  maxApps?: number;
  label?: string;
  // [S-57] the extra figures: `average`, `switches`. A pre-[S-57] set also
  // carried the drawing/app-list switches, which are settings now.
  visibleMetrics?: unknown;
  // [S-46] what the widget covers: 'day' (default), 'week' or 'month'.
  period?: unknown;
  // [S-35] how that period is drawn: 'doughnut'/'bars' for a day,
  // 'bars'/'heatmap' for a window. Pre-[S-46] configs held the period here.
  displayStyle?: unknown;
  // [S-39] exe keys the user has hidden from this widget (persisted config).
  hiddenApps?: unknown;
  // [S-59] daily limits, in seconds: one for the whole screen time, and one per
  // app keyed by exe.
  goalSecs?: unknown;
  appGoals?: unknown;
  // Injected by WidgetGrid; needed to persist a "Hide from list" action.
  _widgetId?: string;
}

interface DayStats {
  date: string;
  total_secs: number;
  apps: { exe: string; name: string; secs: number; path?: string | null }[];
  // [S-55] absent from a backend without the counter.
  switch_count?: number;
}

// [S-07] fixed cadence; the backend only accrues in 30s ticks, so polling
// faster would not surface new data.
const POLL_MS = 30_000;

// [S-36] doughnut geometry (SVG user units in a 0..100 viewBox). Stroke is
// centred on the radius, so outer edge = R + SW/2 = 47.5, leaving a 2.5u margin.
const DONUT_R = 40;
const DONUT_SW = 15;
const DONUT_C = 2 * Math.PI * DONUT_R;
// [S-36] the aggregate "Other" wedge; a design token so the ring stays themable.
const OTHER_COLOR = 'var(--sctw-other-color)';

export default function ScreentimeWidget(props: ScreentimeWidgetProps): JSX.Element {
  const label = () => props.label ?? '';
  // [S-46] two independent axes: the period picks the DATA, the style picks the
  // DRAWING. `resolveView` also absorbs the pre-[S-46] single-field config, and
  // [S-53] the pre-[S-53] metric set, whose two switches these two settings are.
  const view = createMemo(() => {
    const v = resolveView(props.period, props.displayStyle);
    // [S-57] a pre-metric-split config still says whether it wanted the drawing
    // and the app list; those are settings now, so it speaks through them.
    const m = applyLegacyMetrics(props.visibleMetrics, v.style, props.maxApps ?? DEFAULT_MAX_APPS);
    return { period: v.period, style: m.style, maxApps: m.maxApps };
  });
  const period = () => view().period;
  const style = () => view().style;
  const maxApps = () => view().maxApps;
  const segmentCap = () => chartCap(maxApps());

  const visible = createMemo<string[]>(() => {
    // [S-57] drop the metric keys that became settings before resolving, so an
    // older config doesn't read as "everything off".
    const raw = migrateMetrics(props.visibleMetrics);
    if (raw === undefined || raw === null) return canonicalVisible(METRIC_DEFS);
    return resolveMetrics(raw, METRIC_DEFS).visible;
  });
  const shows = (k: string) => visible().includes(k);
  // [S-53] the settings alone shape the body: `none` = no drawing, `maxApps` 0 =
  // no app list. The drawing keeps its own sensible cap either way.
  const drawChart = () => style() !== 'none';
  const listApps = () => showsApps(maxApps());
  // [S-41] week/month read a rolling WINDOW of days; a day period reads one day.
  const isRange = () => isRangePeriod(period());
  const rangeLen = () => rangeLength(period());
  const layout = useLayout();

  const [stats, setStats] = createSignal<DayStats | null>(null);
  // [S-41] the rolling window's per-day stats, oldest first (null = not loaded).
  const [range, setRange] = createSignal<RangeDay[] | null>(null);
  // [S-11] the pinned history day as a concrete YYYY-MM-DD key (null = follow
  // today). Pinning a DATE, not an offset, keeps the shown day stable across
  // midnight. Transient view state, never written to config, so it can never
  // leak into the settings export [S-34].
  const [selectedDate, setSelectedDate] = createSignal<string | null>(null);
  // Local today-key, re-sampled on every poll so labels and clamps follow the
  // calendar without a dedicated timer.
  const [todayKey, setTodayKey] = createSignal(dateForOffset(0));

  // [S-37] app icons, keyed by exe bucket. Fetched from the persisted exe path
  // ([S-38]) via one batched IPC call; missing icons fall back to an initial.
  const [iconByExe, setIconByExe] = createSignal<Record<string, string>>({});
  const requestedPaths = new Set<string>();

  const oldestReachable = () => shiftDateKey(todayKey(), -MAX_HISTORY_OFFSET);
  const atCap = () => (selectedDate() ?? todayKey()) <= oldestReachable();
  const onToday = () => selectedDate() === null;

  async function refresh() {
    setTodayKey(dateForOffset(0));
    if (isRange()) {
      await refreshRange(rangeLen());
      return;
    }
    const sel = selectedDate();
    try {
      // Following today sends no date: the backend picks "today" with its
      // own local clock, avoiding a frontend/backend midnight race [S-12].
      const s = await invoke('screentime_get_day', sel === null ? {} : { date: sel });
      // Drop a late response for a day the user has navigated away from; the
      // guard compares the pinned DATE, which stays valid across midnight.
      if (selectedDate() === sel) setStats(s);
    } catch (e) {
      logger.error('screentime', `screentime_get_day failed: ${e}`);
    }
  }

  // [S-41] the window always ends on the BACKEND's today, so no date is sent —
  // only how many days back to go.
  async function refreshRange(days: number) {
    try {
      const raw = await invoke('screentime_get_range', { days });
      // Drop a late response for a window the user has since switched away from.
      if (rangeLen() === days) setRange(parseRange(raw));
    } catch (e) {
      logger.error('screentime', `screentime_get_range failed: ${e}`);
    }
  }

  function select(next: string | null) {
    if (next === selectedDate()) return;
    setSelectedDate(next);
    setStats(null); // show loading state, not the previous day's numbers
    void refresh();
  }

  function navigate(deltaDays: number) {
    const from = selectedDate() ?? todayKey();
    const next = shiftDateKey(from, deltaDays);
    if (next < oldestReachable()) return; // [S-11] clamp at the cap
    select(next >= todayKey() ? null : next);
  }

  const navLabel = createMemo(() => {
    const desc = dayDescriptor(selectedDate(), todayKey());
    if (desc.kind === 'today') return t('common.today');
    if (desc.kind === 'yesterday') return t('common.yesterday');
    return parseDateKey(selectedDate()!).toLocaleDateString(lang(), {
      weekday: 'short', month: 'short', day: 'numeric',
    });
  });

  // [S-41] the window periods state their span instead of a day nav.
  const rangeLabel = () =>
    period() === 'month' ? t('screentime.last30Days') : t('screentime.last7Days');

  if (isDemo()) {
    setStats(demoScreentime());
    if (isRange()) setRange(parseRange(demoScreentimeRange(rangeLen())));
  }

  createPolling(refresh, () => POLL_MS, 'screentime');

  // [S-46] a PERIOD change swaps which command feeds the body (or how many days
  // it asks for), so refetch at once instead of waiting out the poll interval.
  // Keyed on the window length, so a pure STYLE change — same data, different
  // drawing — keeps what it already has. Deferred: on mount the poller's first
  // tick already fetches.
  createEffect(on(rangeLen, () => {
    setStats(null);
    setRange(null);
    void refresh();
  }, { defer: true }));

  // [S-39] hidden apps are dropped from every view, and the shown totals re-base
  // to what remains, so the numbers always agree with the picture.
  const hidden = createMemo(() => new Set(parseHidden(props.hiddenApps)));
  const shownApps = createMemo(() => filterHidden(stats()?.apps ?? [], hidden()));
  const shownTotal = createMemo(() => sumSecs(shownApps()));

  // [S-37] the day's shown apps, for the icon batch (the legend draws from the
  // segments, which cap the same way).
  const apps = createMemo(() => topApps(shownApps(), segmentCap()));
  const hasActivity = () => !!stats() && shownTotal() > 0;

  // [S-41] the window's per-day shown totals, re-based past hidden apps [S-39].
  const totals = createMemo(() => dayTotals(range() ?? [], hidden()));
  const maxDay = createMemo(() => maxDaySecs(totals()));
  const windowTotal = createMemo(() => totalSecs(totals()));

  // [S-52] the per-day averages are their own metric, and only a window has one.
  const avgDays = () => (isRange() && shows('average') ? totals().length : undefined);
  // [S-55] switches over the shown period, when the metric is on. Hidden apps
  // ([S-39]) can't be subtracted from it — a switch is a moment, not a bucket —
  // so it is the period's raw count.
  const switches = () => {
    if (!shows('switches')) return null;
    const n = isRange() ? totalSwitches(range() ?? []) : stats()?.switch_count ?? 0;
    return n > 0 ? n : null;
  };

  const hasWindowActivity = () => range() !== null && windowTotal() > 0;

  // [S-59] goals are DAILY limits, so a window compares its average per day —
  // not its total, which would blow past a daily limit by construction.
  const goal = createMemo(() => parseGoalSecs(props.goalSecs));
  const appGoals = createMemo(() => parseAppGoals(props.appGoals));
  const perDay = (secs: number) =>
    (isRange() ? dailyAverage(secs, totals().length) : secs);
  const totalGoal = () => goalState(perDay(isRange() ? windowTotal() : shownTotal()), goal());
  const appGoal = (exe: string) => goalState(perDay(segSecs(exe)), appGoals()[exe] ?? null);
  const segSecs = (exe: string) =>
    legendSegments().find((s) => s.key === exe)?.secs ?? 0;
  // [S-43] heatmap grid: 7-wide, padded so the window ends on the last cell.
  const cells = createMemo(() => heatmapCells(totals()));
  const columnDates = createMemo(() => heatmapColumnDates(cells()));
  // A one-row heatmap (a week) would stretch its cells over the whole body;
  // keep them chip-sized instead. [S-48] the same idea for bars: a month's 30
  // columns are too narrow for a weekday label under each.
  const shortGrid = () => cells().length <= 2 * HEATMAP_COLS;
  const denseBars = () => totals().length > WEEK_DAYS;

  // [S-36]/[S-48] the app breakdown as coloured segments: the shown day's apps,
  // or the whole window's apps summed. One list feeds the ring, the legend and
  // the stacked bars, so a colour means the same app everywhere.
  const daySegments = createMemo(() =>
    donutSegments(shownApps(), shownTotal(), segmentCap(), t('screentime.other'), OTHER_COLOR),
  );
  const windowSegments = createMemo(() =>
    donutSegments(
      aggregateApps(range() ?? [], hidden()), windowTotal(), segmentCap(),
      t('screentime.other'), OTHER_COLOR,
    ),
  );
  const legendSegments = () => (isRange() ? windowSegments() : daySegments());

  // [S-48] one stacked bar per day of the window, split in legend order.
  const stacks = createMemo(() => {
    const legend = windowSegments();
    const scale = maxDay();
    const hide = hidden();
    return (range() ?? []).map((day) => {
      const shown = filterHidden(day.apps, hide);
      return {
        date: day.date,
        secs: sumSecs(shown),
        parts: stackForDay(shown, legend, scale),
      };
    });
  });

  // [S-40] hover focus, keyed by app: the hovered entry stays lit, the rest dim,
  // and the readout swaps to that app's figures. Shared by the ring, the legend,
  // the day's horizontal bar and the stacked bars.
  const [hovered, setHovered] = createSignal<string | null>(null);
  const focus = createMemo(() => {
    const h = hovered();
    return h ? legendSegments().find((s) => s.key === h) ?? null : null;
  });
  const dimApp = (key: string) => hovered() !== null && hovered() !== key;

  // [S-44] the same idea keyed by DATE, for the bars and heat cells.
  const [hoveredDay, setHoveredDay] = createSignal<string | null>(null);
  const focusDay = createMemo(() => {
    const h = hoveredDay();
    return h ? totals().find((d) => d.date === h) ?? null : null;
  });
  const enterDay = (date: string) => setHoveredDay(date);
  const leaveDay = (date: string) => setHoveredDay((h) => (h === date ? null : h));

  const dayLabel = (key: string) => parseDateKey(key).toLocaleDateString(lang(), {
    weekday: 'short', month: 'short', day: 'numeric',
  });
  const weekdayShort = (key: string) =>
    parseDateKey(key).toLocaleDateString(lang(), { weekday: 'short' });
  const weekdayNarrow = (key: string) =>
    parseDateKey(key).toLocaleDateString(lang(), { weekday: 'narrow' });

  // [S-39] hide an app: persist its exe key into this widget's config. Guarded
  // on _widgetId (absent in some previews) and against duplicates.
  function hideApp(exe: string) {
    const id = props._widgetId;
    if (!id) return;
    const current = parseHidden(props.hiddenApps);
    if (current.includes(exe)) return;
    const w = layout.layout.widgets.find((w) => w.id === id);
    layout.updateWidgetConfig(id, { ...(w?.config ?? {}), hiddenApps: [...current, exe] });
  }

  function onAppContextMenu(e: MouseEvent, exe: string) {
    e.preventDefault();
    e.stopPropagation();
    showCtxMenu(e, [
      { icon: EyeOff, label: t('screentime.hideApp'), action: () => hideApp(exe) },
    ]);
  }

  // [S-37] fetch any not-yet-requested icons for the currently shown apps in a
  // single batch. `requestedPaths` is a plain Set (non-reactive), so populating
  // it can't re-trigger this effect — each path is fetched at most once.
  async function loadIcons(list: DayStats['apps'], paths: string[]) {
    try {
      const map = await invoke('get_file_icons_batch', { paths, size: 32 });
      const add: Record<string, string> = {};
      for (const a of list) {
        if (a.path && map[a.path]) add[a.exe] = map[a.path];
      }
      if (Object.keys(add).length) setIconByExe((prev) => ({ ...prev, ...add }));
    } catch (e) {
      // ignore-worthy but logged: a missing icon degrades to an initial chip.
      logger.error('screentime', `get_file_icons_batch failed: ${e}`);
    }
  }

  createEffect(() => {
    // [S-48] window periods need icons for the aggregated apps, day periods for
    // the shown day's — same batch, whichever list is on screen.
    const list = isRange() ? aggregateApps(range() ?? [], hidden()) : apps();
    const paths = uniquePaths(list).filter((p) => !requestedPaths.has(p));
    if (!paths.length) return;
    paths.forEach((p) => requestedPaths.add(p));
    void loadIcons(list, paths);
  });

  // [S-37] icon element for a bucket: the extracted PNG, else a colored initial
  // chip (the chip color is the app's stable donut color, tying the two views).
  function AppAvatar(p: { exe: string; name: string }): JSX.Element {
    const src = () => iconByExe()[p.exe];
    return (
      <Show
        when={src()}
        fallback={
          <span
            class="sctw-app-icon sctw-app-icon--fallback"
            style={{ 'background-color': appColor(p.exe) }}
          >{appInitial(p.name)}</span>
        }
      >
        <img class="sctw-app-icon" src={src()!} alt="" draggable={false} />
      </Show>
    );
  }

  /** [S-06]/[S-41] the shared "nothing recorded" placeholder, once data is in. */
  function EmptyState(p: { when: boolean }): JSX.Element {
    return (
      <Show when={p.when}>
        <div class="sctw-empty">{t('screentime.noActivity')}</div>
      </Show>
    );
  }

  /** [S-03] the app list: one row per segment, icon + name + duration.
   *  Shared by every style that isn't itself the app list. `showDot` adds the
   *  [S-51] colour key after the name, for the styles that colour by app. */
  function Legend(p: {
    segments: DonutSegment[];
    /** [S-58] the period total each row's share is measured against. */
    total: number;
    showDot?: boolean;
    /** [S-52] window length; set only for a window period, where an average
     *  per day is meaningful. */
    avgDays?: number;
  }): JSX.Element {
    // [S-56] pad to the configured app count with invisible rows, so a quiet
    // day (fewer apps than the cap) doesn't resize the chart above.
    const ghosts = () => ghostRows(p.segments.length, maxApps());
    const avgLabel = (secs: number) =>
      t('screentime.perDay', { value: formatDuration(dailyAverage(secs, p.avgDays ?? 0)) });
    return (
      <div class="sctw-legend">
        <For each={p.segments}>
          {(seg) => (
            <div
              class="sctw-legend-row"
              classList={{ 'is-active': hovered() === seg.key, 'is-dim': dimApp(seg.key) }}
              title={
                p.avgDays
                  ? `${seg.name} — ${formatDuration(seg.secs)} · ${avgLabel(seg.secs)}`
                  : `${seg.name} — ${formatDuration(seg.secs)}`
              }
              onMouseEnter={() => setHovered(seg.key)}
              onMouseLeave={() => setHovered((h) => (h === seg.key ? null : h))}
              onContextMenu={(e) => !seg.isOther && onAppContextMenu(e, seg.key)}
            >
              <Show
                when={!seg.isOther}
                fallback={<span class="sctw-legend-dot" style={{ 'background-color': seg.color }} />}
              >
                <AppAvatar exe={seg.key} name={seg.name} />
              </Show>
              <span class="sctw-app-name">{seg.name}</span>
              {/* [S-51] the app's colour in the drawing, directly after its
                  name. The "Other" entry already shows its colour in the icon
                  slot. */}
              <Show when={p.showDot && !seg.isOther}>
                <span class="sctw-legend-swatch" style={{ 'background-color': seg.color }} />
              </Show>
              {/* [S-58] the app's share of the period. */}
              <Show when={shows('percent')}>
                <span class="sctw-app-pct">
                  {`${Math.round(shareOf(seg.secs, p.total) * 100)}%`}
                </span>
              </Show>
              {/* [S-59] this app's daily limit, when one is set. */}
              <Show when={appGoal(seg.key)}>
                {(g) => (
                  <span class="sctw-app-goal" classList={{ 'is-over': g().over }}>
                    {t('screentime.ofGoal', { value: formatDuration(g().goal) })}
                  </span>
                )}
              </Show>
              {/* [S-52] over a window, the total alone hides how much of it is
                  a normal day; the average per day says that outright. It sits
                  LEFT of the total, which stays the row's last column. */}
              <Show when={p.avgDays}>
                <span class="sctw-app-avg">{avgLabel(seg.secs)}</span>
              </Show>
              <span class="sctw-app-secs">{formatDuration(seg.secs)}</span>
            </div>
          )}
        </For>
        {/* [S-56] filler rows: same box, hidden — `visibility: hidden` also
            takes them out of hit-testing, so they can't be hovered or
            right-clicked. */}
        <For each={new Array(ghosts()).fill(0)}>
          {() => (
            <div class="sctw-legend-row sctw-legend-row--ghost" aria-hidden="true">
              <span class="sctw-app-icon" />
              <span class="sctw-app-name">&nbsp;</span>
            </div>
          )}
        </For>
      </div>
    );
  }

  /** [S-59] the daily limit beside the figure it judges. Its own component so
   *  the readout row and the doughnut's centre — the two places a period total
   *  is shown — state the limit from ONE definition rather than two. */
  function TotalGoal(): JSX.Element {
    return (
      <Show when={totalGoal()}>
        {(g) => (
          <span class="sctw-total-goal" classList={{ 'is-over': g().over }}>
            {t(isRange() ? 'screentime.goalPerDay' : 'screentime.ofGoal', {
              value: formatDuration(g().goal),
            })}
          </span>
        )}
      </Show>
    );
  }

  /** [S-55] the period's app switches, when that metric is on. Shared with the
   *  doughnut centre for the same reason as [`TotalGoal`]. */
  function Switches(): JSX.Element {
    return (
      <Show when={switches() !== null}>
        <span class="sctw-switches">
          {t(switches() === 1 ? 'screentime.switchOne' : 'screentime.switches', {
            count: switches() ?? 0,
          })}
        </span>
      </Show>
    );
  }

  /** [S-44]/[S-40] the readout above a chart: the period's total, or the hovered
   *  day's / app's figures. [S-52] a window also states its average per day —
   *  but not while a single day or app is focused, where it wouldn't apply. */
  function Readout(p: { total: number; avgDays?: number }): JSX.Element {
    const focused = () => focusDay() ?? focus();
    return (
      <div class="sctw-total-row">
        <span
          class="sctw-total"
          classList={{ 'is-over': !focused() && !!totalGoal()?.over }}
        >
          {formatDuration(focusDay()?.secs ?? focus()?.secs ?? p.total)}
        </span>
        {/* [S-59] the daily limit this period is measured against. Over a
            window the comparison is against the average per day, so the label
            says which figure it judges. */}
        <Show when={!focused()}>
          <TotalGoal />
        </Show>
        <Show when={focusDay()}>
          {(d) => <span class="sctw-range-sub">{dayLabel(d().date)}</span>}
        </Show>
        <Show when={!focusDay() && focus()}>
          {(seg) => <span class="sctw-range-sub">{seg().name}</span>}
        </Show>
        <Show when={p.avgDays && !focused()}>
          <span class="sctw-total-avg">
            {t('screentime.perDay', { value: formatDuration(dailyAverage(p.total, p.avgDays ?? 0)) })}
          </span>
        </Show>
        {/* [S-55] the period's app switches, when that metric is on. */}
        <Show when={!focused()}>
          <Switches />
        </Show>
      </div>
    );
  }

  return (
    <div class="sctw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || t('widget.screentime.label')}
        secondary={
          /* Day nav lives in the header, calendar-widget pattern ([S-01]):
             ‹ title › plus the today dot. The window periods ([S-41]) have no
             day to navigate, so they name their span instead. */
          <Show
            when={isRange()}
            fallback={
              <div class="sctw-nav">
                <button
                  class="sctw-nav-btn sctw-nav-prev"
                  disabled={atCap()}
                  onClick={() => navigate(-1)}
                  aria-label={t('screentime.prevDay')}
                >‹</button>
                <span class="sctw-nav-title">{navLabel()}</span>
                <button
                  class="sctw-nav-btn sctw-nav-next"
                  disabled={onToday()}
                  onClick={() => navigate(1)}
                  aria-label={t('screentime.nextDay')}
                >›</button>
                <button
                  class="sctw-today-btn"
                  onClick={() => select(null)}
                  title={t('screentime.backToToday')}
                  aria-label={t('screentime.backToToday')}
                >●</button>
              </div>
            }
          >
            <span class="sctw-range-label">{rangeLabel()}</span>
          </Show>
        }
      />

      <div
        class="sctw-body widget-body"
        classList={{
          'sctw-body--donut': style() === 'doughnut',
          'sctw-body--range': isRange(),
        }}
      >
        <Switch>
          {/* ── No drawing ([S-53]): style `none`, or the `graphic` metric off.
                 What is left is the readout, and the app list if any. ─────── */}
          <Match when={!drawChart()}>
            <Show
              when={isRange() ? hasWindowActivity() : hasActivity()}
              fallback={<EmptyState when={isRange() ? range() !== null : !!stats()} />}
            >
              <Readout total={isRange() ? windowTotal() : shownTotal()} avgDays={avgDays()} />
              <Show when={listApps()}>
                <Legend
                  segments={legendSegments()}
                  total={isRange() ? windowTotal() : shownTotal()}
                  avgDays={avgDays()}
                />
              </Show>
            </Show>
          </Match>

          {/* ── Day · doughnut ([S-36]) ──────────────────────────────────── */}
          <Match when={!isRange() && style() === 'doughnut'}>
            <Show when={hasActivity()} fallback={<EmptyState when={!!stats()} />}>
              <div class="sctw-donut">
                <svg class="sctw-donut-svg" viewBox="0 0 100 100" aria-hidden="true">
                  <g transform="rotate(-90 50 50)">
                    <For each={daySegments()}>
                      {(seg) => (
                        <circle
                          class="sctw-donut-seg"
                          classList={{
                            'is-active': hovered() === seg.key,
                            'is-dim': dimApp(seg.key),
                          }}
                          cx="50"
                          cy="50"
                          r={DONUT_R}
                          fill="none"
                          stroke={seg.color}
                          stroke-width={DONUT_SW}
                          stroke-dasharray={`${(seg.fraction * DONUT_C).toFixed(3)} ${(DONUT_C - seg.fraction * DONUT_C).toFixed(3)}`}
                          stroke-dashoffset={(-seg.startFraction * DONUT_C).toFixed(3)}
                          onMouseEnter={() => setHovered(seg.key)}
                          onMouseLeave={() => setHovered((h) => (h === seg.key ? null : h))}
                          onContextMenu={(e) => !seg.isOther && onAppContextMenu(e, seg.key)}
                        >
                          <title>{`${seg.name} — ${formatDuration(seg.secs)} · ${Math.round(seg.fraction * 100)}%`}</title>
                        </circle>
                      )}
                    </For>
                  </g>
                </svg>
                <div class="sctw-donut-center">
                  <Show
                    when={focus()}
                    fallback={
                      /* [S-36] The ring's centre IS the doughnut's readout —
                         there is no readout row above it — so the figures that
                         sit beside a total elsewhere ([S-55]/[S-59]) stack
                         under it here instead of going unrendered. */
                      <div class="sctw-donut-readout">
                        <span
                          class="sctw-total sctw-donut-total"
                          classList={{ 'is-over': !!totalGoal()?.over }}
                        >
                          {formatDuration(shownTotal())}
                        </span>
                        <TotalGoal />
                        <Switches />
                      </div>
                    }
                  >
                    {(seg) => (
                      <div class="sctw-donut-focus">
                        <span class="sctw-donut-focus-name" style={{ color: seg().color }}>{seg().name}</span>
                        <span class="sctw-donut-total">{formatDuration(seg().secs)}</span>
                        <span class="sctw-donut-focus-pct">{Math.round(seg().fraction * 100)}%</span>
                      </div>
                    )}
                  </Show>
                </div>
              </div>

              <Show when={listApps()}>
                <Legend segments={daySegments()} total={shownTotal()} showDot />
              </Show>
            </Show>
          </Match>

          {/* ── Day · bars ([S-50]): one horizontal bar, split by app ─────── */}
          <Match when={!isRange() && style() === 'bars'}>
            <Show when={hasActivity()} fallback={<EmptyState when={!!stats()} />}>
              <>
                <Readout total={shownTotal()} />
                <div class="sctw-hbar">
                  <For each={daySegments()}>
                    {(seg) => (
                      <div
                        class="sctw-hbar-part"
                        classList={{ 'is-active': hovered() === seg.key, 'is-dim': dimApp(seg.key) }}
                        style={{
                          width: `${(seg.fraction * 100).toFixed(2)}%`,
                          'background-color': seg.color,
                        }}
                        title={`${seg.name} — ${formatDuration(seg.secs)} · ${Math.round(seg.fraction * 100)}%`}
                        onMouseEnter={() => setHovered(seg.key)}
                        onMouseLeave={() => setHovered((h) => (h === seg.key ? null : h))}
                        onContextMenu={(e) => !seg.isOther && onAppContextMenu(e, seg.key)}
                      />
                    )}
                  </For>
                </div>
              </>

              <Show when={listApps()}>
                <Legend segments={daySegments()} total={shownTotal()} showDot />
              </Show>
            </Show>
          </Match>

          {/* ── Window · bars ([S-48]) / heatmap ([S-43]) ─────────────────── */}
          <Match when={isRange()}>
            <Show when={hasWindowActivity()} fallback={<EmptyState when={range() !== null} />}>
              <>
                <Readout total={windowTotal()} avgDays={avgDays()} />
                <Show
                  when={style() === 'bars'}
                  fallback={
                    /* [S-43] heatmap: 7-wide grid, one cell per day. */
                    <div class="sctw-heat" classList={{ 'sctw-heat--short': shortGrid() }}>
                      <div class="sctw-heat-head">
                        <For each={columnDates()}>
                          {(key) => (
                            <span class="sctw-heat-dow">{key ? weekdayNarrow(key) : ''}</span>
                          )}
                        </For>
                      </div>
                      <div class="sctw-heat-grid">
                        <For each={cells()}>
                          {(cell) => (
                            <Show when={cell} fallback={<span class="sctw-heat-cell is-blank" />}>
                              {(day) => (
                                <span
                                  class="sctw-heat-cell"
                                  classList={{
                                    [`sctw-heat-l${heatLevel(day().secs, maxDay())}`]: true,
                                    'is-today': day().date === todayKey(),
                                    'is-active': hoveredDay() === day().date,
                                    'is-dim': hoveredDay() !== null && hoveredDay() !== day().date,
                                  }}
                                  title={`${dayLabel(day().date)} — ${formatDuration(day().secs)}`}
                                  onMouseEnter={() => enterDay(day().date)}
                                  onMouseLeave={() => leaveDay(day().date)}
                                />
                              )}
                            </Show>
                          )}
                        </For>
                      </div>
                    </div>
                  }
                >
                  {/* [S-48] one column per day, each split by app in legend
                      order so a colour means the same app in every bar. */}
                  <div class="sctw-week" classList={{ 'sctw-week--dense': denseBars() }}>
                    <For each={stacks()}>
                      {(day) => (
                        <div
                          class="sctw-week-col"
                          classList={{
                            'is-today': day.date === todayKey(),
                            'is-active': hoveredDay() === day.date,
                            'is-dim': hoveredDay() !== null && hoveredDay() !== day.date,
                          }}
                          title={`${dayLabel(day.date)} — ${formatDuration(day.secs)}`}
                          onMouseEnter={() => enterDay(day.date)}
                          onMouseLeave={() => leaveDay(day.date)}
                        >
                          <div class="sctw-week-track">
                            <For each={day.parts}>
                              {(part) => (
                                <div
                                  class="sctw-week-part"
                                  classList={{
                                    'is-active': hovered() === part.key,
                                    'is-dim': dimApp(part.key),
                                  }}
                                  style={{
                                    height: `${(part.fraction * 100).toFixed(2)}%`,
                                    'background-color': part.color,
                                  }}
                                  title={`${part.name} — ${formatDuration(part.secs)}`}
                                  onMouseEnter={() => setHovered(part.key)}
                                  onMouseLeave={() => setHovered((h) => (h === part.key ? null : h))}
                                />
                              )}
                            </For>
                          </div>
                          <Show when={!denseBars()}>
                            <span class="sctw-week-dow">{weekdayShort(day.date)}</span>
                          </Show>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>
              </>

              {/* [S-51] the heatmap has no per-app colour to key, so its legend
                  carries no dot. */}
              <Show when={listApps()}>
                <Legend
                  segments={windowSegments()}
                  total={windowTotal()}
                  showDot={style() === 'bars'}
                  avgDays={avgDays()}
                />
              </Show>
            </Show>
          </Match>
        </Switch>
      </div>
    </div>
  );
}
