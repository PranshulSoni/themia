import { For, Show, createMemo, createSignal, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { Angry, Frown, Meh, Smile, Laugh, Flame, Trash2 } from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import { createPolling } from '../../../src/createPolling.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { t, lang } from '../../../src/i18n.ts';
import {
  parseEntries, parseHistoryWeeks, addEntry, setEntryNote, removeEntry,
  dayTotals, heatmapCells, heatmapColumnDates, heatBucket, dayAverage,
  windowAverage, loggedStreak, entriesForDay, latestOn, parseDateKey,
  newEntryId, dateKey,
  MOOD_LEVELS, MOOD_LEVEL_KEYS, HEATMAP_COLS,
  type MoodLevel, type MoodEntry, type DayMood,
} from './mood.ts';
import './MoodTracker.css';

export interface MoodTrackerProps {
  /** Untrusted config bag values — narrowed through `mood.ts` parsers. */
  entries?: unknown;
  historyWeeks?: unknown;
  /** [S-05] show the thought box under the scale. */
  showNote?: boolean;
  /** [S-09]/[S-10] show the average + streak footer. */
  showStats?: boolean;
  headerMode?: HeaderMode;
  label?: string;
  /** Injected by WidgetGrid; needed to persist an entry. */
  _widgetId?: string;
}

/** [S-01] one lucide face per level, low to high. */
const FACES: Record<MoodLevel, Component<{ size?: number; strokeWidth?: number }>> = {
  1: Angry,
  2: Frown,
  3: Meh,
  4: Smile,
  5: Laugh,
};

/** At or below this many rows the grid would stretch its cells over the whole
 *  body, so they get a fixed chip height instead (the 4-week window). */
const SHORT_WINDOW_ROWS = 4;

export default function MoodTracker(props: MoodTrackerProps) {
  const layout = useLayout();

  const entries = createMemo(() => parseEntries(props.entries));
  const weeks = () => parseHistoryWeeks(props.historyWeeks);
  const showNote = () => props.showNote !== false;
  const showStats = () => props.showStats !== false;

  // Reactive clock so the window rolls over at midnight without a remount: the
  // memos below only recompute when a signal changes, and without this tick
  // yesterday's cell would keep reading as "today" until the widget remounted.
  // Same rule as the Habit Tracker's [S-08] reset tick.
  const [now, setNow] = createSignal(new Date());
  createPolling(() => setNow(new Date()), () => 60_000, 'mood');

  const totals = createMemo(() => dayTotals(entries(), now(), weeks() * 7));
  const cells = createMemo(() => heatmapCells(totals()));
  const columnDates = createMemo(() => heatmapColumnDates(cells()));
  const today = createMemo(() => dateKey(now()));
  /** [S-04] the entry the scale highlights and the thought box edits. */
  const current = createMemo(() => latestOn(entries(), now()));

  function persist(next: MoodEntry[]) {
    const id = props._widgetId;
    if (!id) return;
    const w = layout.layout.widgets.find((w) => w.id === id);
    layout.updateWidgetConfig(id, { ...(w?.config ?? {}), entries: next });
  }

  // The real clock, not the polled `now()`: the timestamp must be the instant
  // the user clicked, and it is only ever read (never compared against the
  // memos' bucket), so there is no rollover race to mirror here.
  function log(level: MoodLevel) {
    persist(addEntry(entries(), level, new Date().toISOString(), newEntryId()));
  }

  function saveNote(text: string) {
    const e = current();
    if (!e) return;
    persist(setEntryNote(entries(), e.id, text.trim()));
  }

  function formatDay(key: string): string {
    const d = parseDateKey(key);
    if (Number.isNaN(d.getTime())) return key;
    return d.toLocaleDateString(lang(), { weekday: 'short', day: 'numeric', month: 'short' });
  }

  function formatTime(at: string): string {
    const d = new Date(at);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleTimeString(lang(), { hour: '2-digit', minute: '2-digit' });
  }

  function levelName(level: MoodLevel): string {
    return t(`moodTracker.level.${MOOD_LEVEL_KEYS[level]}`);
  }

  /** Native multi-line tooltip: the day, its average, then each entry with the
   *  thought attached to it. Keeps the notes readable without a second UI
   *  surface inside a widget this small. */
  function cellTitle(day: DayMood): string {
    const head = formatDay(day.date);
    if (day.count <= 0) return `${head}\n${t('moodTracker.noEntries')}`;
    const lines = entriesForDay(entries(), day.date).map((e) => {
      const label = e.note.trim() || levelName(e.level);
      return `${formatTime(e.at)} · ${label}`;
    });
    return [`${head} · ${t('moodTracker.avgLabel')} ${dayAverage(day).toFixed(1)}`, ...lines].join('\n');
  }

  /** Right-click a day: read its entries back, and delete one from a submenu so
   *  a stray click on the list itself can never destroy an entry. */
  function dayMenu(e: MouseEvent, day: DayMood) {
    const rows = entriesForDay(entries(), day.date);
    if (rows.length === 0) return;
    showCtxMenu(e, [
      { header: true, label: formatDay(day.date) },
      ...rows.map((entry) => ({
        icon: FACES[entry.level],
        label: `${formatTime(entry.at)} · ${entry.note.trim() || levelName(entry.level)}`,
        children: [{
          icon: Trash2,
          label: t('moodTracker.deleteEntry'),
          danger: true,
          action: () => persist(removeEntry(entries(), entry.id)),
        }],
      })),
    ], 240);
  }

  const average = createMemo(() => windowAverage(totals()));
  const streak = createMemo(() => loggedStreak(totals()));

  return (
    <div class="mood-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Smile}
        title={props.label || t('widget.moodTracker.label')}
      />

      <div class="widget-body mood-body">
        {/* [S-03] one click on a face records the mood, timestamped now. */}
        <div class="mood-scale">
          <For each={MOOD_LEVELS}>
            {(level) => (
              <button
                class="mood-face"
                classList={{ selected: current()?.level === level }}
                data-level={level}
                title={levelName(level)}
                aria-label={levelName(level)}
                onClick={() => log(level)}
              >
                <Dynamic component={FACES[level]} size={20} strokeWidth={1.5} />
              </button>
            )}
          </For>
        </div>

        <Show when={showNote()}>
          {/* [S-05] the thought lands on the day's latest entry. `onChange`
              (blur / Enter), not `onInput`: the value is read straight from
              config, so a per-keystroke write would fight the caret. */}
          <input
            class="mood-note"
            type="text"
            value={current()?.note ?? ''}
            disabled={!current()}
            placeholder={current() ? t('moodTracker.notePlaceholder') : t('moodTracker.noteDisabled')}
            onChange={(e) => saveNote(e.currentTarget.value)}
          />
        </Show>

        <div class="mood-heat" classList={{ 'mood-heat--short': totals().length / HEATMAP_COLS <= SHORT_WINDOW_ROWS }}>
          <div class="mood-heat-head">
            <For each={columnDates()}>
              {(d) => (
                <span class="mood-heat-dow">
                  {d ? parseDateKey(d).toLocaleDateString(lang(), { weekday: 'narrow' }) : ''}
                </span>
              )}
            </For>
          </div>
          <div class="mood-heat-grid">
            <For each={cells()}>
              {(cell) => (
                <Show when={cell} fallback={<span class="mood-heat-cell is-blank" />}>
                  {(day) => (
                    <span
                      class={`mood-heat-cell mood-heat-l${heatBucket(day())}`}
                      classList={{ 'is-today': day().date === today() }}
                      title={cellTitle(day())}
                      onContextMenu={(e) => dayMenu(e, day())}
                    />
                  )}
                </Show>
              )}
            </For>
          </div>
        </div>

        <Show when={showStats()}>
          <div class="mood-stats">
            <Show
              when={average() !== null}
              fallback={<span class="mood-hint">{t('moodTracker.emptyHint')}</span>}
            >
              <span class="mood-avg" title={t('moodTracker.avgTitle')}>
                {t('moodTracker.avgLabel')} {average()!.toFixed(1)}
              </span>
              <span class="mood-streak" classList={{ active: streak() > 0 }} title={t('moodTracker.streakTitle')}>
                <Flame size={12} strokeWidth={1.5} /> {streak()}
              </span>
            </Show>
          </div>
        </Show>
      </div>
    </div>
  );
}
