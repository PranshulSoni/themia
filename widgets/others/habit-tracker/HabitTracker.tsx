import { For, Index, Show, createEffect, createMemo, createSignal, onCleanup, onMount, untrack } from 'solid-js';
import { ListChecks, Check, Flame, Pencil, Plus, Trash2 } from 'lucide-solid';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { useLayout } from '../../../src/viewContext.tsx';
import { createPolling } from '../../../src/createPolling.ts';
import { invoke } from '../../../src/ipc.ts';
import { isDemo } from '../../../src/demo/invoke.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { pastDragThreshold } from '../../office/folder/gesture.ts';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import { onAppBlur } from '../../../src/input/appBlur.ts';
import { caretOffsetFromPoint, focusWithCaret } from '../../../src/input/caret.ts';
import {
  periodKey, streak, resetTickMs, moveHabit, renameHabit, addHabit, newHabitId,
  rowIndexAtY, sameHabitOrder, recentHistory, clampHistoryDots,
  DEFAULT_CUSTOM_MINUTES, PERIOD_NOUNS,
  type Habit, type ResetPeriod,
} from './habit.ts';
import { findHabitList, parseHabits, parseLog } from './habitLists.ts';
import {
  habitLists, setHabitListHabits, setHabitListLog, seedHabitList, ensureHabitList,
} from './habitListStore.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './HabitTracker.css';
import { t, lang } from '../../../src/i18n.ts';

export interface HabitTrackerProps {
  /** Which device list this widget shows ([S-18]). An id into the device-wide
   *  pool, not the habits themselves. */
  localListId?: string;
  /** LEGACY in-config habits, from before device lists existed. Read once and
   *  migrated into a real list ([S-21]); never written again. Untrusted config
   *  value — validated via `parseHabits` at the trust boundary. */
  habits?: unknown;
  /** LEGACY in-config completion log, migrated alongside `habits`. Untrusted —
   *  validated via `parseLog`. */
  log?: unknown;
  resetPeriod?: ResetPeriod;
  customMinutes?: number;
  historyDots?: number;
  headerMode?: HeaderMode;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
}

export default function HabitTracker(props: HabitTrackerProps) {
  const layout = useLayout();
  // [S-18] Habits and their streaks live in the device-wide pool, not in this
  // widget's config; config holds only the id of the list being shown. So two
  // widgets on one list agree, and a pop-out agrees with its tile.
  const activeList = createMemo(() => findHabitList(habitLists(), props.localListId));
  // Demo / picker-preview reads its habits straight from the config bag rather
  // than the device-wide pool ([S-57]). Two reasons: the preview has no
  // `_widgetId`, so the migration effect below bails and the pool stays empty
  // (the card read "No habits yet"); and resolving through the pool would mean
  // SEEDING it — opening the Add-Widget picker would create real habit lists on
  // the user's machine. A preview must not write to device state.
  const habits = () => (isDemo() ? parseHabits(props.habits) : activeList()?.habits ?? []);
  const log = () => (isDemo() ? parseLog(props.log) : activeList()?.log ?? {});
  const period = (): ResetPeriod => props.resetPeriod ?? 'daily';
  const customMinutes = () => props.customMinutes ?? DEFAULT_CUSTOM_MINUTES;
  /** [S-15] How many history dots each row shows. Clamped here as well as in
   *  `recentHistory`, because the strip's own layout reads it too. */
  const historyDots = () => clampHistoryDots(props.historyDots);

  // Reactive clock so a rolled-over period clears the checkbox live (the `done`
  // memo only recomputes when one of its signals changes, and without this tick a
  // box checked yesterday would stay checked until the widget remounted).
  const [now, setNow] = createSignal(new Date());
  createPolling(() => setNow(new Date()), () => resetTickMs(period()), 'habit');
  const current = () => periodKey(now(), period(), customMinutes());

  /** Merge a patch into this widget's stored config. `updateWidgetConfig`
   *  REPLACES config wholesale, so the current value is spread first. */
  function writeConfig(patch: Record<string, unknown>) {
    const id = props._widgetId;
    if (!id) return;
    const current = layout.layout.widgets.find((w) => w.id === id)?.config ?? {};
    layout.updateWidgetConfig(id, { ...current, ...patch });
  }

  // [S-18] Both mutations land in the SELECTED device list. With no list there
  // is nothing on screen to mutate, so they are no-ops rather than writes that
  // would have to invent a list.
  function persistLog(next: Record<string, string[]>) {
    const id = activeList()?.id;
    if (!id) return;
    setHabitListLog(id, next);
  }

  function persistHabits(next: Habit[]) {
    const id = activeList()?.id;
    if (!id) return;
    setHabitListHabits(id, next);
  }

  // [S-21] One-shot migration of the pre-pool in-config habits into a real
  // device list. The new list's id is DERIVED from the widget id, so the grid
  // tile and a pop-out of the same widget — which mount at the same time and
  // both see the legacy bag — converge on one list instead of seeding two;
  // `seedHabitList` then leaves it alone if it is already there. The bag is
  // cleared in the same config write that points at the new list, so the habits
  // exist in exactly one place afterwards and this can never run twice.
  createEffect(() => {
    if (props.localListId) return;
    // Never migrate or create lists from a preview: it has no widget to write
    // back to, and the pool is device-wide state a preview must not touch.
    if (isDemo()) return;
    const widgetId = props._widgetId;
    if (!widgetId) return;
    untrack(() => {
      const legacyHabits = parseHabits(props.habits);
      if (legacyHabits.length) {
        const listId = `hlist-migrated-${widgetId}`;
        seedHabitList(listId, props.label || t('habitTracker.lists.defaultName'), legacyHabits, parseLog(props.log));
        writeConfig({ localListId: listId, habits: [], log: {} });
        return;
      }
      // A fresh widget lands on something usable: the device's first list if it
      // has one, else a new list. Never a duplicate empty one.
      writeConfig({ localListId: ensureHabitList(undefined, t('habitTracker.lists.defaultName')) });
    });
  });

  // ─── Drag to reorder, in the widget ([S-13]) ────────────────────────────
  // Driven by POLLING the OS cursor, not by mouse events: this window is
  // WS_EX_NOACTIVATE + HWND_BOTTOM, and Chromium withholds mousemove from an
  // unfocused window while a button is held, so a mousemove-based drag would
  // freeze the moment the button went down. HTML5 `draggable` is no use either
  // -- the widget window's OLE drop target is registered by Rust
  // (`drag_drop::register_drop_target`), so WebView2 never delivers dragover to
  // the DOM here; that is exactly why the CONFIG window disables the handler to
  // let its own grip ([S-09]) work. Same mechanism the folder and shortcut
  // widgets use for their drags ([S-65] there), reusing that gesture module's
  // 5px threshold so a click on the name still renames ([S-14]).
  //
  // The drag reorders a LOCAL copy and commits once on release: every hop of a
  // live-persisted reorder would be a config write plus a cross-window
  // broadcast, and an interrupted drag would leave that half-applied.
  interface CursorState { x: number; y: number; left_down: boolean; ctrl_down: boolean }

  const rowEls = new Map<string, HTMLElement>();
  const [dragId, setDragId] = createSignal<string | null>(null);
  const [dragOrder, setDragOrder] = createSignal<Habit[] | null>(null);
  /** What the list renders: the in-flight order while dragging, else the config. */
  const order = () => dragOrder() ?? habits();

  // Non-reactive gesture bookkeeping. `generation` is the same discipline the
  // folder widget documents: a fresh press bumps it, so a still-suspended poll
  // loop tears itself down instead of racing the new one.
  let press: { id: string; x: number; y: number } | null = null;
  let polling = false;
  let generation = 0;
  /** A real drag ran; consumed by the click that follows the release so the
   *  gesture that ends on the habit's name cannot also open the rename. */
  let dragRan = false;

  // Physical-pixel origin of this window, to turn GetCursorPos into client px.
  let winOriginPx = { x: 0, y: 0 };
  try {
    getCurrentWindow().innerPosition()
      .then((p) => { winOriginPx = { x: p.x, y: p.y }; })
      .catch(() => {});
  } catch {
    // ignore: no Tauri window in the picker preview / dev harness
  }

  function physToClient(px: number, py: number): [number, number] {
    const dpr = window.devicePixelRatio || 1;
    return [(px - winOriginPx.x) / dpr, (py - winOriginPx.y) / dpr];
  }

  function resetDrag() {
    press = null;
    polling = false;
    generation++;
    setDragId(null);
    setDragOrder(null);
  }

  function commitDrag() {
    const next = dragOrder();
    resetDrag();
    if (!next || sameHabitOrder(next, habits())) return;
    persistHabits(next);
  }

  async function runDragPoll(gen: number): Promise<void> {
    while (polling && generation === gen) {
      let st: CursorState;
      try {
        st = await invoke('get_cursor_state');
      } catch {
        resetDrag();
        return;
      }
      if (!polling || generation !== gen) return;

      if (!st.left_down) { commitDrag(); return; }

      const p = press;
      if (!p) { resetDrag(); return; }
      const [cx, cy] = physToClient(st.x, st.y);

      if (!dragId()) {
        if (pastDragThreshold(cx - p.x, cy - p.y)) {
          dragRan = true;
          setDragId(p.id);
          setDragOrder(habits().slice());
        }
      }

      const list = dragOrder();
      const id = dragId();
      if (list && id) {
        // Rects are re-read each tick because the rows move under the cursor as
        // the order changes; a snapshot taken at drag start would aim at where
        // the rows USED to be.
        const rects = list.map((h) => rowEls.get(h.id)?.getBoundingClientRect());
        if (rects.every((r): r is DOMRect => !!r)) {
          const target = list[rowIndexAtY(rects, cy)];
          if (target) {
            const next = moveHabit(list, id, target.id);
            if (next !== list) setDragOrder(next);
          }
        }
      }

      await new Promise((r) => setTimeout(r, 16)); // ~60 fps
    }
  }

  function onRowMouseDown(e: MouseEvent, habit: Habit) {
    if (e.button !== 0 || props._editMode) return;
    const target = e.target as HTMLElement | null;
    // The checkbox completes a habit and the rename input owns its caret;
    // neither is a drag handle, so a press there arms nothing.
    if (target?.closest('.habit-check, .habit-name-input')) return;
    // Wipe any leftover gesture BEFORE arming, so this press can never inherit
    // a stale origin from an aborted one (the folder widget's #58 lesson).
    resetDrag();
    dragRan = false;
    press = { id: habit.id, x: e.clientX, y: e.clientY };
    polling = true;
    const gen = generation;
    runDragPoll(gen).catch(() => resetDrag());
  }

  onCleanup(resetDrag);

  // ─── Rename from the widget ([S-14]) ────────────────────────────────────
  // Habits used to be renameable only by opening config. Click the name (or use
  // the row menu) to edit it in place; the config panel's list writes the same
  // `habits` array through the same `renameHabit`, so the two can't disagree.
  const [editingId, setEditingId] = createSignal<string | null>(null);
  const [draftName, setDraftName] = createSignal('');
  // Where the caret goes when the editor opens: a click carries the offset it
  // landed on, the row menu / F2 carry `null` and get the end of the name.
  // Non-reactive — read once by the editor's ref, in the same tick.
  let pendingCaret: number | null = null;

  /** True if the gesture that just ended was a real drag, which the CLICK that
   *  follows the release must swallow — a reorder that finished on a name is not
   *  a rename ([S-13]). Consumed here, re-armed by the next press, never on a
   *  timer. Only the click paths ask: F2 and the row menu are not the tail of a
   *  drag, so they must still open the editor. */
  function consumeDrag(): boolean {
    if (!dragRan) return false;
    dragRan = false;
    return true;
  }

  function startEdit(habit: Habit, caret: number | null = null) {
    if (props._editMode) return; // the grid owns clicks while arranging
    pendingCaret = caret;
    setEditingId(habit.id);
    setDraftName(habit.name);
  }

  function cancelEdit() {
    setEditingId(null);
    setDraftName('');
  }

  function commitEdit(habit: Habit) {
    const next = draftName();
    cancelEdit();
    // Blank keeps the habit as it was: removing one is config's job, and the
    // log is keyed by id, so a habit lost here would strand its history.
    if (!next.trim() || next.trim() === habit.name) return;
    persistHabits(renameHabit(habits(), habit.id, next));
  }

  function handleEditKeyDown(e: KeyboardEvent, habit: Habit) {
    if (e.key === 'Enter') { e.preventDefault(); commitEdit(habit); }
    else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    // [S-17] Tab commits and opens the NEXT row's editor (Shift+Tab the
    // previous), so a list can be corrected end to end from the keyboard. Past
    // the last row it falls into the add row, which is where the next name
    // would go anyway. The Rust hook stands down while the editor has focus, so
    // this is a real DOM keydown.
    else if (e.key === 'Tab') {
      e.preventDefault();
      const list = order();
      const idx = list.findIndex((h) => h.id === habit.id);
      commitEdit(habit);
      if (idx < 0) return;
      const next = list[e.shiftKey ? idx - 1 : idx + 1];
      if (next) {
        setFocusedIdx(e.shiftKey ? idx - 1 : idx + 1);
        startEdit(next);
      } else if (!e.shiftKey) {
        startCreate();
      }
    }
  }

  // ─── Add a habit from the widget ([S-16]) ───────────────────────────────
  // Habits could only be created in config, which meant opening a window to jot
  // down "drink water". The row mirrors To Do's "Add task" ([S-32] there): it
  // sits at the bottom, turns into an input on click, and commits on Enter or
  // blur. `addHabit` enforces the same no-nameless-habit rule as the config
  // panel's draft row ([S-11]), so the two entry points can't disagree.
  const [creating, setCreating] = createSignal(false);
  const [draftNew, setDraftNew] = createSignal('');

  function startCreate() {
    if (props._editMode) return;
    if (consumeDrag()) return; // a drag that let go over the add row is not a click
    setCreating(true);
  }

  function commitCreate() {
    const name = draftNew();
    setDraftNew('');
    setCreating(false);
    if (!name.trim()) return;
    persistHabits(addHabit(habits(), name, newHabitId()));
  }

  function handleCreateKeyDown(e: KeyboardEvent) {
    if (e.key === 'Enter') { e.preventDefault(); commitCreate(); }
    else if (e.key === 'Escape') { e.preventDefault(); setDraftNew(''); setCreating(false); }
    // [S-17] Tab commits and leaves the row open for the next habit; Shift+Tab
    // commits and closes it.
    else if (e.key === 'Tab') {
      e.preventDefault();
      commitCreate();
      if (!e.shiftKey) setCreating(true);
    }
  }

  // ─── Keyboard focus + Tab navigation ([S-17]) ───────────────────────────
  // Widget windows never take Win32 focus, so DOM keydown never fires here; the
  // Rust low-level hook routes the whitelisted keys to the widget the user last
  // clicked, exactly as the To Do / folder / email widgets do.
  const [focusedIdx, setFocusedIdx] = createSignal(-1);

  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return; // the editor owns its keys
    if (props._editMode) return;                       // the grid owns the widget
    const list = order();
    if (!list.length) return;
    const curr = focusedIdx();
    const last = list.length - 1;
    if (e.key === 'Tab') {
      // Tab WRAPS at the ends, the way Tab does everywhere else — it is a "next
      // one" key, not a directional one.
      e.preventDefault();
      setFocusedIdx(e.shiftKey ? (curr <= 0 ? last : curr - 1) : (curr < 0 || curr >= last ? 0 : curr + 1));
    }
    else if (e.key === 'ArrowDown') { e.preventDefault(); setFocusedIdx(curr < 0 ? 0 : Math.min(last, curr + 1)); }
    else if (e.key === 'ArrowUp')   { e.preventDefault(); setFocusedIdx(curr < 0 ? last : Math.max(0, curr - 1)); }
    else if (e.key === 'Home')      { e.preventDefault(); setFocusedIdx(0); }
    else if (e.key === 'End')       { e.preventDefault(); setFocusedIdx(last); }
    else if (e.key === 'Escape')    { setFocusedIdx(-1); }
    // Enter checks the focused habit off — the same thing its checkbox does, and
    // the only thing that completes one ([S-02]).
    else if (e.key === 'Enter')     { if (curr >= 0 && curr <= last) { e.preventDefault(); toggle(list[curr].id); } }
    // F2 is the Windows rename key, matching To Do ([S-50] there).
    else if (e.key === 'F2')        { if (curr >= 0 && curr <= last) { e.preventDefault(); startEdit(list[curr]); } }
    // Delete removes the focused habit ([S-22]), matching To Do. The highlight
    // is pulled back inside the list by the effect below.
    else if (e.key === 'Delete')    { if (curr >= 0 && curr <= last) { e.preventDefault(); removeHabit(list[curr]); } }
  }

  // Clicking outside any row (the widget's own chrome, another widget) drops the
  // highlight, and so does the whole app losing focus. Mirrors To Do [S-37].
  function handleGlobalMouseDown(e: MouseEvent): void {
    const target = e.target as HTMLElement | null;
    if (target?.closest('.ctx-menu, .ctx-submenu, .ctx-backdrop')) return;
    if (!target?.closest('.habit-row')) setFocusedIdx(-1);
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;
  let _cleanupBlur: (() => void) | undefined;
  onMount(() => {
    _kbd = registerWidgetKeyboard(handleKey);
    document.addEventListener('mousedown', handleGlobalMouseDown);
    _cleanupBlur = onAppBlur(() => setFocusedIdx(-1));
  });
  onCleanup(() => {
    _kbd?.dispose();
    document.removeEventListener('mousedown', handleGlobalMouseDown);
    _cleanupBlur?.();
  });

  /** A removed habit must not leave the highlight past the end of the list. */
  createEffect(() => {
    const n = order().length;
    if (focusedIdx() >= n) setFocusedIdx(n - 1);
  });

  function handleRowCtx(e: MouseEvent, habit: Habit) {
    // Edit mode belongs to the grid: the widget is being moved/resized, so its
    // rows must not offer their own actions.
    if (props._editMode) return;
    e.preventDefault();
    e.stopPropagation();
    // Reordering is the drag ([S-13]); the menu carries only what a drag can't
    // express.
    showCtxMenu(e, [
      {
        icon: Pencil,
        label: t('common.rename'),
        action: () => startEdit(habit),
      },
      { sep: true },
      {
        icon: Trash2,
        label: t('common.delete'),
        action: () => removeHabit(habit),
        danger: true,
      },
    ]);
  }

  /** Delete a habit from the widget ([S-22]). No dialog: it sits behind a
   *  right-click (or the Delete key on a focused row), not a stray click, which
   *  is the same bargain To Do makes for a task. `setHabitListHabits` prunes the
   *  habit's log with it ([S-20]), so its streak cannot be inherited by a later
   *  habit reusing the id. */
  function removeHabit(habit: Habit) {
    if (props._editMode) return; // the grid owns the widget while arranging
    // Cancel an open editor for the row being removed, or it would commit a
    // rename onto a habit that no longer exists.
    if (editingId() === habit.id) cancelEdit();
    persistHabits(habits().filter((h) => h.id !== habit.id));
  }

  /** Tooltip for one history dot ([S-15]), phrased in the cadence's own units so
   *  an hourly habit's dots don't all claim to be the same day. */
  function dotTitle(d: Date): string {
    const p = period();
    if (p === 'hourly' || p === 'custom') {
      return d.toLocaleString(lang(), {
        month: 'short', day: 'numeric', hour: 'numeric',
        minute: p === 'custom' ? '2-digit' : undefined,
      });
    }
    if (p === 'monthly') return d.toLocaleDateString(lang(), { month: 'long', year: 'numeric' });
    return d.toLocaleDateString(lang(), { weekday: 'short', month: 'short', day: 'numeric' });
  }

  function toggle(habitId: string) {
    const cur = log();
    // Toggle the SAME bucket the `done` memo reads (`current()`, off the polled
    // `now()`), so the checkbox flips immediately. Writing the live-clock bucket
    // here while `done` compared the lagging tick left the box unresponsive for a
    // full tick after every period rollover.
    const key = current();
    const keys = new Set(cur[habitId] ?? []);
    if (keys.has(key)) keys.delete(key);
    else keys.add(key);
    persistLog({ ...cur, [habitId]: [...keys].sort() });
  }

  return (
    <div class="habit-widget">
      {/* [S-10] The fallback mirrors the registry `defaultHeaderMode`, so the
          render sites that pass no prop (picker preview, dev harnesses) show
          the same header the grid does. */}
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        icon={ListChecks}
        title={props.label || 'Habits'}
      />

      <div class="widget-body">
      <div class="habit-list" classList={{ reordering: dragId() !== null }}>
        {/* [S-16] The empty state now sits INSIDE the list, above the add row:
            the first habit is created right here, so pointing at the config gear
            would be sending the user the long way round. */}
        <Show when={habits().length === 0}>
          <div class="habit-empty">
            <span>{t('habitTracker.emptyHint')}</span>
          </div>
        </Show>
          <For each={order()}>
            {(h, i) => {
              const done = createMemo(() => (log()[h.id] ?? []).includes(current()));
              const s = createMemo(() => streak(log()[h.id] ?? [], now(), period(), customMinutes()));
              const history = createMemo(() => recentHistory(log()[h.id] ?? [], now(), period(), customMinutes(), historyDots()));
              onCleanup(() => rowEls.delete(h.id));
              return (
                <div
                  class="habit-row"
                  classList={{
                    done: done(),
                    dragging: dragId() === h.id,
                    'kbd-focused': focusedIdx() === i(),
                  }}
                  ref={(el) => rowEls.set(h.id, el)}
                  onMouseDown={(e) => { setFocusedIdx(i()); onRowMouseDown(e, h); }}
                  onContextMenu={(e) => handleRowCtx(e, h)}
                >
                  <button
                    class="habit-check"
                    classList={{ checked: done() }}
                    onClick={() => toggle(h.id)}
                    title={done() ? `Mark not done ${PERIOD_NOUNS[period()]}` : `Mark done ${PERIOD_NOUNS[period()]}`}
                  >
                    <Show when={done()}><Check size={14} strokeWidth={2.5} /></Show>
                  </button>
                  {/* [S-14] Click the name to rename in place. The checkbox
                      above is still the only thing that completes a habit. */}
                  <Show
                    when={editingId() === h.id}
                    fallback={
                      <span
                        class="habit-name"
                        onClick={(e) => {
                          if (consumeDrag()) return;
                          startEdit(h, caretOffsetFromPoint(e.clientX, e.clientY, h.name.length));
                        }}
                      >
                        {h.name || 'Untitled'}
                      </span>
                    }
                  >
                    <input
                      class="habit-name-input"
                      placeholder={t('habitTracker.habitPlaceholder')}
                      value={draftName()}
                      onInput={(e) => setDraftName(e.currentTarget.value)}
                      onKeyDown={(e) => handleEditKeyDown(e, h)}
                      onBlur={() => commitEdit(h)}
                      /* [S-14] The caret lands where the click did. Selecting
                         the whole name armed the next keystroke to wipe it,
                         which is not what clicking into text means anywhere
                         else. */
                      ref={(el) => queueMicrotask(() => focusWithCaret(el, pendingCaret))}
                    />
                  </Show>
                  <span class="habit-streak" classList={{ active: s() > 0 }} title={t('habitTracker.streakTitle')}>
                    <Flame size={12} strokeWidth={1.5} /> {s()}
                  </span>
                  {/* [S-15] The last `historyDots` buckets, oldest left, current
                      period rightmost. `Index` keys by POSITION, so the minute
                      tick that rebuilds the array swaps each dot's state instead
                      of remounting the whole strip. At zero dots the element is
                      gone, not empty — an off strip must not sit in the row's
                      flex line claiming a gap. */}
                  <Show when={historyDots() > 0}>
                    <span class="habit-history">
                      <Index each={history()}>
                        {(e) => (
                          <span
                            class="habit-dot"
                            classList={{ done: e().done }}
                            title={dotTitle(e().date)}
                          />
                        )}
                      </Index>
                    </span>
                  </Show>
                </div>
              );
            }}
          </For>

          {/* [S-16] "Add habit", at the bottom, mirroring To Do's add row. It is
              fixed-height (`flex: 0 0 auto`), so it never joins the equal-slice
              share-out that sizes the habit rows ([S-12]). */}
          <Show when={!props._editMode}>
            <Show when={creating()} fallback={
              <div class="habit-add-row" onClick={startCreate}>
                <Plus size={13} strokeWidth={1.5} />
                <span>{t('habitTracker.addHabit')}</span>
              </div>
            }>
              <div class="habit-add-row editing">
                <Plus size={13} strokeWidth={1.5} />
                <input
                  class="habit-add-input"
                  placeholder={t('habitTracker.habitPlaceholder')}
                  value={draftNew()}
                  onInput={(e) => setDraftNew(e.currentTarget.value)}
                  onKeyDown={handleCreateKeyDown}
                  onBlur={commitCreate}
                  ref={(el) => queueMicrotask(() => el.focus())}
                />
              </div>
            </Show>
          </Show>
      </div>
      </div>
    </div>
  );
}
