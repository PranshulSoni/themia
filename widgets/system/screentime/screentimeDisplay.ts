// Pure display helpers for the Screen Time widget (SPEC.md [S-03]..[S-05]).
// Lucide-free and node-importable so vitest runs them without a DOM.

export interface ScreentimeApp {
  exe: string;
  name: string;
  secs: number;
  /// [S-38] full exe path (when known) so the frontend can extract the icon.
  path?: string | null;
}

/// [S-04] `Nh Nm` at >= 1h, `Nm` at >= 1min, `<1m` below.
export function formatDuration(secs: number): string {
  if (secs >= 3600) {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    return `${h}h ${m}m`;
  }
  if (secs >= 60) return `${Math.floor(secs / 60)}m`;
  return '<1m';
}

/// [S-10] the `maxApps` fallback for an absent or corrupt config value.
export const DEFAULT_MAX_APPS = 7;

/// [S-03] descending by seconds, capped at maxApps ([S-53] 0 = none; non-finite
/// config values fall back to the default). The backend already sorts, but
/// re-sorting keeps the widget correct against any data source (demo fixtures,
/// stale stubs).
export function topApps(apps: ScreentimeApp[], maxApps: number): ScreentimeApp[] {
  const cap = Number.isFinite(maxApps) ? Math.max(0, Math.floor(maxApps)) : DEFAULT_MAX_APPS;
  return [...apps].sort((a, b) => b.secs - a.secs).slice(0, cap);
}

/// [S-53] whether the app breakdown is listed at all — `maxApps` 0 means "no
/// apps", the setting that replaced the old `apps` metric.
export function showsApps(maxApps: number): boolean {
  return Number.isFinite(maxApps) ? Math.floor(maxApps) > 0 : true;
}

/// [S-10] the highest `maxApps` the stepper offers.
export const MAX_APPS_LIMIT = 50;

/// [S-56] how many invisible filler rows the app list needs so its height stays
/// put: enough to reach the configured `maxApps` when fewer apps were used. A
/// list already at (or past, via the "Other" entry) that many rows needs none.
export function ghostRows(shownRows: number, maxApps: number): number {
  if (!Number.isFinite(maxApps)) return 0;
  return Math.max(0, Math.floor(maxApps) - shownRows);
}

/// [S-53] the cap the DRAWING segments by. It still needs a sensible number of
/// buckets when the list is switched off — a cap of 0 would fold every app into
/// one grey "Other" segment — so 0 falls back to the default.
export function chartCap(maxApps: number): number {
  const n = Number.isFinite(maxApps) ? Math.floor(maxApps) : DEFAULT_MAX_APPS;
  return n > 0 ? n : DEFAULT_MAX_APPS;
}

/// [S-05] share of the largest app's seconds, clamped to [0, 1].
export function barFraction(secs: number, maxSecs: number): number {
  if (maxSecs <= 0) return 0;
  return Math.min(1, secs / maxSecs);
}

/// [S-36] a stable color for an app bucket, derived from its exe key so the
/// same app keeps its color across days and reorderings. djb2 hash → hue; fixed
/// saturation/lightness tuned for the dark theme. Returns a modern-syntax
/// `hsl(H S% L%)` string (WebView2 is current Chromium).
export function appColor(exe: string): string {
  let hash = 5381;
  for (let i = 0; i < exe.length; i++) {
    hash = ((hash << 5) + hash + exe.charCodeAt(i)) | 0; // *33 + c, wrapped to i32
  }
  const hue = ((hash % 360) + 360) % 360; // non-negative 0..359
  return `hsl(${hue} 62% 60%)`;
}

/// [S-37] fallback avatar letter when no app icon is available.
export function appInitial(name: string): string {
  const c = name.trim().charAt(0);
  return c ? c.toUpperCase() : '?';
}

/// [S-37] distinct, non-empty exe paths across the given apps, for a single
/// batched icon fetch.
export function uniquePaths(apps: ScreentimeApp[]): string[] {
  const seen = new Set<string>();
  for (const a of apps) {
    if (a.path) seen.add(a.path);
  }
  return [...seen];
}

/// [S-39] coerce the persisted `hiddenApps` config (an untrusted value from
/// localStorage) into a clean list of exe keys — never a half-built object.
export function parseHidden(raw: unknown): string[] {
  if (!Array.isArray(raw)) return [];
  return raw.filter((x): x is string => typeof x === 'string' && x.length > 0);
}

/// [S-39] drop the buckets the user has hidden (by exe key) from the display.
export function filterHidden(apps: ScreentimeApp[], hidden: Iterable<string>): ScreentimeApp[] {
  const set = hidden instanceof Set ? hidden : new Set(hidden);
  return apps.filter((a) => !set.has(a.exe));
}

/// [S-39] total seconds of the given apps; negatives (bad backend data) count as
/// zero. Used to re-base the total + ring after hiding apps, so what is shown
/// always sums to what is drawn.
export function sumSecs(apps: ScreentimeApp[]): number {
  return apps.reduce((s, a) => s + Math.max(0, a.secs), 0);
}

/// [S-36] one wedge of the screen-time doughnut.
export interface DonutSegment {
  key: string; // exe, or '__other__' for the aggregate remainder
  name: string;
  secs: number;
  color: string;
  fraction: number; // share of the day total, in [0, 1]
  startFraction: number; // cumulative start offset, in [0, 1)
  isOther: boolean;
}

export const OTHER_KEY = '__other__';

/// [S-36] build the doughnut wedges: the top `maxApps` apps by share of the
/// day `totalSecs`, plus a single "Other" wedge for everything below the cap so
/// the ring always sums to the whole day (and matches the total in its center).
/// `otherLabel`/`otherColor` are injected so this stays pure and localizable.
/// Returns [] when there is nothing to draw (`totalSecs <= 0`).
export function donutSegments(
  apps: ScreentimeApp[],
  totalSecs: number,
  maxApps: number,
  otherLabel: string,
  otherColor: string,
): DonutSegment[] {
  if (!(totalSecs > 0)) return [];
  const top = topApps(apps, maxApps);
  const segs: DonutSegment[] = [];
  // Offsets are derived from cumulative SECONDS (not summed fractions) so the
  // wedge boundaries stay exact — floating-point drift in a fraction sum would
  // leave a visible seam in the ring.
  let coveredSecs = 0;
  for (const a of top) {
    const s = Math.max(0, a.secs);
    segs.push({
      key: a.exe,
      name: a.name,
      secs: a.secs,
      color: appColor(a.exe),
      fraction: Math.min(1, s / totalSecs),
      startFraction: Math.min(1, coveredSecs / totalSecs),
      isOther: false,
    });
    coveredSecs += s;
  }
  const otherSecs = totalSecs - coveredSecs;
  if (otherSecs >= 1) {
    segs.push({
      key: OTHER_KEY,
      name: otherLabel,
      secs: otherSecs,
      color: otherColor,
      fraction: Math.min(1, otherSecs / totalSecs),
      startFraction: Math.min(1, coveredSecs / totalSecs),
      isOther: true,
    });
  }
  return segs;
}

// ── Period × style ([S-46]) and the rolling-window views ([S-41]..[S-43]) ────

/// [S-41] window lengths. Both windows are ROLLING and end today — the last 7 /
/// last 30 days, never the current calendar week or month.
export const WEEK_DAYS = 7;
export const MONTH_DAYS = 30;

/// [S-46] WHAT the widget covers: a single day, or a rolling window.
export type Period = 'day' | 'week' | 'month';

/// [S-35] HOW that period is drawn. `doughnut` draws one day's APP buckets;
/// `heatmap` draws a window's DAYS; `bars` draws either (split by app); `none`
/// draws nothing ([S-53]) and leaves the readout and the app list.
export type DisplayStyle = 'doughnut' | 'bars' | 'heatmap' | 'none';

/// [S-46] the styles that can draw a given period. The period picks the data,
/// the style picks the drawing — the two are separate config fields, and this
/// is the only place that says which pairs exist. `bars` draws both: one
/// horizontal bar split by app for a day ([S-50]), one vertical bar per day
/// split by app for a window ([S-48]).
export function stylesFor(period: Period): DisplayStyle[] {
  return period === 'day'
    ? ['doughnut', 'bars', 'none']
    : ['bars', 'heatmap', 'none'];
}

/// [S-46] `null` when the value isn't a period at all, so a caller can tell
/// "absent" from "explicitly day" and fall back to a legacy value.
export function parsePeriod(raw: unknown): Period | null {
  return raw === 'day' || raw === 'week' || raw === 'month' ? raw : null;
}

/// [S-46] narrow a style to one the period can actually draw. A style from the
/// other axis (or junk) becomes that period's first style, never an empty body.
export function clampStyle(period: Period, raw: unknown): DisplayStyle {
  const allowed = stylesFor(period);
  return allowed.find((s) => s === raw) ?? allowed[0];
}

/// [S-46] resolve the two persisted fields into a valid pair, absorbing the
/// pre-[S-46] single field where `displayStyle` itself held the period
/// (`'week'` → week + bars, `'month'` → month + heatmap). An explicit `period`
/// always wins, so once the panel has written both fields the legacy reading
/// never applies again.
export function resolveView(rawPeriod: unknown, rawStyle: unknown): { period: Period; style: DisplayStyle } {
  const legacy =
    rawStyle === 'week' ? { period: 'week' as Period, style: 'bars' as DisplayStyle } :
    rawStyle === 'month' ? { period: 'month' as Period, style: 'heatmap' as DisplayStyle } :
    null;
  const period = parsePeriod(rawPeriod) ?? legacy?.period ?? 'day';
  return { period, style: clampStyle(period, legacy?.style ?? rawStyle) };
}

/// [S-41] the window periods read a range of days, not a single day.
export function isRangePeriod(period: Period): period is 'week' | 'month' {
  return period === 'week' || period === 'month';
}

/// [S-41] how many days the period covers (a day period reads one day).
export function rangeLength(period: Period): number {
  if (period === 'month') return MONTH_DAYS;
  if (period === 'week') return WEEK_DAYS;
  return 1;
}

/// [S-41] one day as returned by `screentime_get_range` (a `DayStats` without
/// the fields the charts don't read).
/// [S-41] the part of a day the charts read: its date and its app buckets.
export interface DayApps {
  date: string;
  apps: ScreentimeApp[];
}

export interface RangeDay extends DayApps {
  /// [S-55] foreground switches recorded for the day.
  switches: number;
}

/// [S-41] one day of a window: its date key and the SHOWN seconds.
export interface DayTotal {
  date: string;
  secs: number;
}

function isApp(v: unknown): v is ScreentimeApp {
  if (!v || typeof v !== 'object') return false;
  const a = v as Record<string, unknown>;
  return typeof a.exe === 'string' && typeof a.name === 'string' && Number.isFinite(a.secs);
}

/// [S-41] coerce the `screentime_get_range` response at the IPC trust boundary:
/// entries without a usable date key are dropped, and a non-array response
/// yields an empty window rather than a half-built one. Order is preserved (the
/// backend sends oldest first).
export function parseRange(raw: unknown): RangeDay[] {
  if (!Array.isArray(raw)) return [];
  const out: RangeDay[] = [];
  for (const d of raw) {
    if (!d || typeof d !== 'object') continue;
    const rec = d as { date?: unknown; apps?: unknown; switch_count?: unknown };
    if (typeof rec.date !== 'string' || !rec.date) continue;
    out.push({
      date: rec.date,
      apps: Array.isArray(rec.apps) ? rec.apps.filter(isApp) : [],
      // [S-55] absent in a response from a backend without the counter.
      switches: Number.isFinite(rec.switch_count) ? Math.max(0, rec.switch_count as number) : 0,
    });
  }
  return out;
}

/// [S-58] an app's share of the period's shown total, in [0, 1]. Zero when
/// there is no total to divide by, so a "0%" never reads as a real figure.
export function shareOf(secs: number, totalSecs: number): number {
  if (!(totalSecs > 0) || !(secs > 0)) return 0;
  return Math.min(1, secs / totalSecs);
}

/// [S-59] a goal is a DAILY limit in seconds. Coerce the persisted value at the
/// trust boundary: anything not a positive finite number means "no goal", never
/// a zero-second goal that everything would instantly blow past.
export function parseGoalSecs(raw: unknown): number | null {
  if (typeof raw !== 'number' || !Number.isFinite(raw) || raw <= 0) return null;
  return Math.floor(raw);
}

/// [S-59] per-app daily limits, keyed by exe. Entries without a usable key or a
/// positive limit are dropped, so a corrupt map can't produce phantom goals.
export function parseAppGoals(raw: unknown): Record<string, number> {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: Record<string, number> = {};
  for (const [exe, v] of Object.entries(raw as Record<string, unknown>)) {
    const secs = parseGoalSecs(v);
    if (exe && secs !== null) out[exe] = secs;
  }
  return out;
}

/// [S-59] how a figure stands against its daily limit. `null` when no limit is
/// set, so the caller renders nothing at all rather than an empty comparison.
export interface GoalState {
  goal: number;
  used: number;
  /// Share of the limit, uncapped — 1.4 means 40% over.
  ratio: number;
  over: boolean;
}

export function goalState(used: number, goal: number | null): GoalState | null {
  if (goal === null) return null;
  const u = Math.max(0, used);
  return { goal, used: u, ratio: u / goal, over: u > goal };
}

/// [S-55] the window's switches: one number for the whole period.
export function totalSwitches(days: RangeDay[]): number {
  return days.reduce((s, d) => s + Math.max(0, d.switches), 0);
}

/// [S-41] per-day SHOWN totals, oldest first. Each day is re-based through
/// `filterHidden` + `sumSecs` rather than trusting a backend total, so hiding an
/// app ([S-39]) shrinks the chart and its readout together. Days with no usage
/// stay in the list as explicit zeros — a gap must render as a gap.
export function dayTotals(days: DayApps[], hidden: Iterable<string>): DayTotal[] {
  const set = hidden instanceof Set ? hidden : new Set(hidden);
  return days.map((d) => ({ date: d.date, secs: sumSecs(filterHidden(d.apps, set)) }));
}

/// [S-41] the busiest day in the window; the scale both charts normalize to.
export function maxDaySecs(totals: DayTotal[]): number {
  return totals.reduce((m, d) => Math.max(m, Math.max(0, d.secs)), 0);
}

/// [S-41] sum of the window's shown seconds.
export function totalSecs(totals: DayTotal[]): number {
  return totals.reduce((s, d) => s + Math.max(0, d.secs), 0);
}

/// [S-43] heat bucket for a day, against the window's busiest day. 0 means NO
/// usage at all; any usage is at least 1 and the busiest day is always 4, so a
/// quiet-but-nonzero day never renders as an empty cell.
export function heatLevel(secs: number, maxSecs: number): number {
  if (!(secs > 0) || !(maxSecs > 0)) return 0;
  const q = Math.ceil((Math.min(secs, maxSecs) / maxSecs) * 4);
  return Math.min(4, Math.max(1, q));
}

/// [S-43] the heatmap is 7 cells wide, so wrapping the window chronologically
/// puts a FIXED weekday in each column (7 days per row) with today in the last
/// filled cell — no week-start setting needed, the window's own dates decide.
export const HEATMAP_COLS = 7;

/// [S-43] pad the window with leading blanks so it ends exactly at the end of a
/// row; `null` slots are days before the window started and render empty.
export function heatmapCells(totals: DayTotal[]): (DayTotal | null)[] {
  const lead = (HEATMAP_COLS - (totals.length % HEATMAP_COLS)) % HEATMAP_COLS;
  return [...new Array<DayTotal | null>(lead).fill(null), ...totals];
}

/// [S-43] one sample date per column, for the weekday header row: the first day
/// that lands in that column (all days in a column share its weekday).
export function heatmapColumnDates(cells: (DayTotal | null)[]): (string | null)[] {
  const out: (string | null)[] = new Array<string | null>(HEATMAP_COLS).fill(null);
  cells.forEach((c, i) => {
    const col = i % HEATMAP_COLS;
    if (c && out[col] === null) out[col] = c.date;
  });
  return out;
}

/// [S-48] the window's apps as ONE bucket list, seconds summed per exe across
/// its days (hidden apps [S-39] dropped first). Lets a window period show the
/// same app breakdown a day period does — and gives the stacked bars a stable
/// legend to order themselves by.
export function aggregateApps(days: DayApps[], hidden: Iterable<string>): ScreentimeApp[] {
  const set = hidden instanceof Set ? hidden : new Set(hidden);
  const byExe = new Map<string, ScreentimeApp>();
  for (const day of days) {
    for (const a of day.apps) {
      if (set.has(a.exe)) continue;
      const prev = byExe.get(a.exe);
      if (prev) {
        prev.secs += Math.max(0, a.secs);
        if (!prev.path && a.path) prev.path = a.path;
      } else {
        byExe.set(a.exe, { exe: a.exe, name: a.name, secs: Math.max(0, a.secs), path: a.path });
      }
    }
  }
  return [...byExe.values()].sort((a, b) => b.secs - a.secs || a.exe.localeCompare(b.exe));
}

/// [S-48] one slice of a stacked bar.
export interface StackPart {
  key: string;
  name: string;
  color: string;
  secs: number;
  /// Share of the window's BUSIEST day, so a day's parts sum to its bar height
  /// ([S-42] semantics) and bars stay comparable across the window.
  fraction: number;
}

/// [S-48] split one day into stacked parts, in the LEGEND's order so a colour
/// means the same app in every bar. Apps below the legend's cap fold into its
/// "Other" entry, so the parts always sum to the day's shown total. (An "Other"
/// entry always exists when any app is off the legend, since both are derived
/// from the same aggregate.)
export function stackForDay(
  apps: ScreentimeApp[],
  legend: DonutSegment[],
  maxSecs: number,
): StackPart[] {
  if (!(maxSecs > 0)) return [];
  const bySecs = new Map<string, number>();
  for (const a of apps) bySecs.set(a.exe, (bySecs.get(a.exe) ?? 0) + Math.max(0, a.secs));
  let total = 0;
  for (const v of bySecs.values()) total += v;

  const out: StackPart[] = [];
  let covered = 0;
  for (const seg of legend) {
    if (seg.isOther) continue;
    const secs = bySecs.get(seg.key) ?? 0;
    covered += secs;
    if (secs <= 0) continue;
    out.push({ key: seg.key, name: seg.name, color: seg.color, secs, fraction: secs / maxSecs });
  }
  const other = legend.find((s) => s.isOther);
  const rest = total - covered;
  if (other && rest > 0) {
    out.push({
      key: other.key, name: other.name, color: other.color,
      secs: rest, fraction: rest / maxSecs,
    });
  }
  return out;
}

/// [S-52] an app's per-day average over a window: its total divided by the
/// WINDOW LENGTH, so idle days count as zero and the per-app averages still sum
/// to the window's overall daily average. Zero for an empty window.
export function dailyAverage(secs: number, days: number): number {
  if (!(days > 0) || !(secs > 0)) return 0;
  return secs / days;
}

/// [S-57] the metric keys that became SETTINGS: the drawing is `displayStyle`
/// (`none`), the app list is `maxApps` (0). (`total` was the pre-[S-49] name for
/// the drawing.)
const LEGACY_METRIC_KEYS = ['total', 'graphic', 'apps'];

/// [S-57] narrow a persisted metric set to the two that are still metrics. A
/// legacy set (one naming any of the retired keys) cannot have had an opinion
/// about `average`, which did not exist yet — so it keeps that default rather
/// than reading as "switched off". Its two real opinions are carried over by
/// `applyLegacyMetrics`, not here.
export function migrateMetrics(raw: unknown): unknown {
  if (!Array.isArray(raw)) return raw;
  const legacy = raw.some((k) => LEGACY_METRIC_KEYS.includes(k as string));
  const out: string[] = [];
  for (const k of raw) {
    if (typeof k !== 'string' || LEGACY_METRIC_KEYS.includes(k) || out.includes(k)) continue;
    out.push(k);
  }
  if (legacy && !out.includes('average')) out.unshift('average');
  return out;
}

/// [S-57] carry a legacy metric set's two switches onto the settings that
/// replaced them: no drawing metric reads as style `none`, no `apps` metric as
/// an empty app list — so an existing widget never lights up what its owner
/// switched off. A set that names none of the retired keys was written after the
/// change and is left alone.
export function applyLegacyMetrics(
  raw: unknown,
  style: DisplayStyle,
  maxApps: number,
): { style: DisplayStyle; maxApps: number } {
  if (!Array.isArray(raw)) return { style, maxApps };
  if (!raw.some((k) => LEGACY_METRIC_KEYS.includes(k as string))) return { style, maxApps };
  const has = (k: string) => raw.includes(k);
  return {
    style: has('graphic') || has('total') ? style : 'none',
    maxApps: has('apps') ? maxApps : 0,
  };
}

/// [S-11] UI history window: today plus 89 calendar days back. (The backend
/// retains the 90 most recent dates WITH USAGE [S-28], which can span more
/// calendar days; days outside this window are not reachable from the UI.)
export const MAX_HISTORY_OFFSET = 89;

function toDateKey(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

/// [S-11] local date `offset` days before `now`, as YYYY-MM-DD.
export function dateForOffset(offset: number, now: Date = new Date()): string {
  const clamped = Number.isFinite(offset) ? Math.max(0, Math.floor(offset)) : 0;
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  d.setDate(d.getDate() - clamped); // calendar-aware, DST-safe
  return toDateKey(d);
}

/// A YYYY-MM-DD key as a Date at local midnight (for locale formatting).
export function parseDateKey(key: string): Date {
  const [y, m, d] = key.split('-').map(Number);
  return new Date(y, (m || 1) - 1, d || 1);
}

/// [S-11] the key `deltaDays` after `dateKey` (negative = back), DST-safe.
export function shiftDateKey(dateKey: string, deltaDays: number): string {
  const d = parseDateKey(dateKey);
  d.setDate(d.getDate() + deltaDays);
  return toDateKey(d);
}

/// [S-12] what the nav label should say for a pinned day. Derived from BOTH
/// the selection and the current today-key, so a pinned "Yesterday" degrades
/// to a plain date after midnight instead of silently lying. The caller
/// localizes: today/yesterday via t('common.*'), date via toLocaleDateString.
export type DayDescriptor = { kind: 'today' } | { kind: 'yesterday' } | { kind: 'date' };

export function dayDescriptor(selected: string | null, todayKey: string): DayDescriptor {
  if (selected === null || selected >= todayKey) return { kind: 'today' };
  if (selected === shiftDateKey(todayKey, -1)) return { kind: 'yesterday' };
  return { kind: 'date' };
}
