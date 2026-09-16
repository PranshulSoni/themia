/**
 * Expand / collapse ANIMATION state for area groups ([S-43]).
 *
 * `config.collapsed` stays the truth ([S-37]) and syncs across every window;
 * this module is the per-WINDOW transient that says "this group is mid-flight",
 * which three places need at the same time:
 *
 *   - the grid keeps a closing group's members MOUNTED so they can fade out
 *     instead of blinking away, and puts the rect transition on the tile;
 *   - the group widget keeps its area mounted while it closes;
 *   - the hit-mask push must NOT shrink the window region while the area is
 *     still painting. The region is a PAINT clip ([S-17]), so a region that
 *     jumps straight to the collapsed bar ERASES the animation rather than
 *     revealing it — `anyGroupCollapsing()` holds the last pushed shape until
 *     the group has finished closing.
 *
 * The phase is cleared by a DURATION timer, not by an `animationend` listener:
 * the tile's rendered rect and the mask both hang off it, so a missed event
 * would leave a group stuck half-collapsed with a frozen mask. CSS reads the
 * SAME constant through `--group-collapse-ms` (the grid sets it on its root),
 * so the clock and the animation cannot drift apart.
 */
import { createSignal } from 'solid-js';

/** How long an expand / collapse takes. Mirrored into CSS as
 *  `--group-collapse-ms` by WidgetGrid — never hardcode the duration again. */
export const GROUP_COLLAPSE_MS = 200;

/** `in` = the area is opening, `out` = it is closing. Absent = at rest. */
export type CollapsePhase = 'in' | 'out';

/** One group's collapse flag, as the grid observes it. */
export interface GroupCollapseState {
  id: string;
  collapsed: boolean;
}

/**
 * Which groups just toggled, and in which direction. Pure.
 *
 * A group seen for the FIRST time never animates (`prev` has no entry): a view
 * that mounts with a collapsed group must render it collapsed, not play a close.
 */
export function collapseTransitions(
  prev: ReadonlyMap<string, boolean>,
  next: readonly GroupCollapseState[],
): { id: string; phase: CollapsePhase }[] {
  const out: { id: string; phase: CollapsePhase }[] = [];
  for (const g of next) {
    const was = prev.get(g.id);
    if (was === undefined || was === g.collapsed) continue;
    out.push({ id: g.id, phase: g.collapsed ? 'out' : 'in' });
  }
  return out;
}

const [phases, setPhases] = createSignal<Readonly<Record<string, CollapsePhase>>>({});
const timers = new Map<string, ReturnType<typeof setTimeout>>();
let seen = new Map<string, boolean>();

/** The animation this group is playing, or undefined at rest. Reactive. */
export function groupCollapsePhase(id: string): CollapsePhase | undefined {
  return phases()[id];
}

/** Is any group CLOSING? While true the hit-mask must keep the shape it last
 *  pushed: shrinking the region mid-animation clips the closing area away
 *  instead of animating it ([S-17]/[S-43]). Reactive. */
export function anyGroupCollapsing(): boolean {
  const p = phases();
  for (const id in p) if (p[id] === 'out') return true;
  return false;
}

/** Reduced motion suppresses the animation exactly like the widget intro
 *  ([S-15]): no phase is ever started, so every surface flips at once. */
function prefersReducedMotion(): boolean {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
}

function endPhase(id: string): void {
  const t = timers.get(id);
  if (t !== undefined) clearTimeout(t);
  timers.delete(id);
  setPhases(p => {
    if (!(id in p)) return p;
    const next = { ...p };
    delete next[id];
    return next;
  });
}

function beginPhase(id: string, phase: CollapsePhase): void {
  const t = timers.get(id);
  // Re-toggled mid-flight: the new direction replaces the old one on the same
  // element, so the old timer must not fire and end the new animation early.
  if (t !== undefined) clearTimeout(t);
  setPhases(p => ({ ...p, [id]: phase }));
  timers.set(id, setTimeout(() => endPhase(id), GROUP_COLLAPSE_MS));
}

/**
 * Observe the view's groups and start an animation for each one that toggled
 * since the last call. Called from a grid effect, so it re-runs on every layout
 * change; only a real false↔true flip starts anything.
 */
export function syncGroupCollapse(groups: readonly GroupCollapseState[]): void {
  const starts = collapseTransitions(seen, groups);
  const next = new Map<string, boolean>();
  for (const g of groups) next.set(g.id, g.collapsed);
  // A group deleted mid-animation leaves no element to animate; drop its phase
  // so `anyGroupCollapsing()` can't stay true with nothing on screen.
  for (const id of timers.keys()) if (!next.has(id)) endPhase(id);
  seen = next;
  if (prefersReducedMotion()) return;
  for (const s of starts) beginPhase(s.id, s.phase);
}

/** Drop every phase and forget what was seen (grid unmount, tests). */
export function resetGroupCollapse(): void {
  for (const t of timers.values()) clearTimeout(t);
  timers.clear();
  seen = new Map();
  setPhases({});
}
