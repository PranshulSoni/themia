// Pure gesture state machine for the polling-based mouse tracking
// (press → click | rubber-band | OLE file-drag). Shared by the folder widget
// (widgets/office/folder) AND the shortcut widget (widgets/others/shortcut),
// which drive press/poll/release through the same beginPress/pollStep/reset API.
//
// WHY THIS EXISTS ([S-65], issue #58):
// The live loop polls the OS cursor via a Tauri command; CI/webdriver cannot
// drive that hook, so the "click vs drag" decision and the state-reset are
// extracted here as pure, deterministic functions and unit-tested. The bug:
// after an aborted poll (e.g. a transient `get_cursor_state` failure) the old
// code left `fileDrag`/`bandOrigin` set. The NEXT left press then measured the
// drag threshold against STALE origin coordinates, immediately handed off to a
// native OLE drag, and that modal loop swallowed the `mouseup` so no `click`
// ever fired — single-click open silently died until reload. Right-click was
// unaffected (it never depends on `click`). The fix: every exit path resets the
// in-flight candidate to a known-clean baseline, `beginPress` fully wipes prior
// state before arming, and a monotonic generation token guarantees a stale poll
// loop tears itself down instead of racing a fresh one.

/** 5px dead-zone; movement below this stays a click. */
export const DRAG_THRESHOLD_PX = 5;
export const DRAG_THRESHOLD_SQ = DRAG_THRESHOLD_PX * DRAG_THRESHOLD_PX;

export interface Pt { x: number; y: number; }

/** Pure: has the pointer moved far enough from the press origin to be a drag? */
export function pastDragThreshold(dx: number, dy: number): boolean {
  return dx * dx + dy * dy >= DRAG_THRESHOLD_SQ;
}

export interface GestureState {
  /** poll loop is running */
  polling: boolean;
  /**
   * Monotonic token bumped on every reset / new press. The poll loop captures
   * the value it started with and self-terminates once it no longer matches, so
   * a fast re-press can never leave two loops sharing this state.
   */
  generation: number;
  /**
   * Origin of a file-drag candidate (press landed on an item), else null.
   * Checked with priority over `bandOrigin` — mirroring the live
   * `if (fileDrag) … else if (bandOrigin)` order — which is exactly why a stale
   * value here is the #58 failure mode.
   */
  fileStart: Pt | null;
  /** Paths captured for a file-drag candidate. */
  filePaths: readonly string[];
  /** Origin of a rubber-band candidate (press landed on background), else null. */
  bandOrigin: Pt | null;
  /** A real OLE drag was handed off this gesture; suppresses the trailing click. */
  fileDragWasActive: boolean;
  /** A real rubber-band ran this gesture; suppresses the trailing click and
   *  keeps banding active once started. */
  bandWasActive: boolean;
}

export function freshGestureState(): GestureState {
  return {
    polling: false,
    generation: 0,
    fileStart: null,
    filePaths: [],
    bandOrigin: null,
    fileDragWasActive: false,
    bandWasActive: false,
  };
}

/**
 * Reset every in-flight candidate to a known-clean baseline and bump the
 * generation so any still-suspended poll loop terminates on its next check.
 * Does NOT clear the `*WasActive` suppress flags: those are consumed by the
 * `click` that fires AFTER the loop exits. A NEW gesture clears them via
 * `beginPress`.
 */
export function resetGestureCandidates(s: GestureState): void {
  s.polling = false;
  s.fileStart = null;
  s.filePaths = [];
  s.bandOrigin = null;
  s.generation++;
}

/**
 * Begin a fresh press. Fully wipes prior state (candidates AND stale suppress
 * flags) BEFORE arming exactly one candidate, so leftover state from an aborted
 * gesture can never bleed into this one (#58). Returns the generation token the
 * poll loop for this press should own.
 */
export function beginPress(
  s: GestureState,
  opts: { x: number; y: number; onItem: boolean; paths?: readonly string[] },
): number {
  resetGestureCandidates(s);
  s.fileDragWasActive = false;
  s.bandWasActive = false;
  if (opts.onItem) {
    s.fileStart = { x: opts.x, y: opts.y };
    s.filePaths = opts.paths ?? [];
  } else {
    s.bandOrigin = { x: opts.x, y: opts.y };
  }
  return s.generation;
}

export type PollAction =
  | { kind: 'idle' }
  | { kind: 'startDrag'; paths: readonly string[] }
  | { kind: 'band'; rect: { x1: number; y1: number; x2: number; y2: number } }
  | { kind: 'release'; wasDrag: boolean; wasBand: boolean };

/**
 * Pure decision for one poll tick. Mutates `s` to reflect activation/teardown
 * and returns the action the caller must carry out (start a native drag, paint
 * the band, or finish). Mirrors the live loop body so the two cannot drift.
 */
export function pollStep(
  s: GestureState,
  cursor: { cx: number; cy: number; leftDown: boolean },
): PollAction {
  if (!cursor.leftDown) {
    const wasDrag = s.fileDragWasActive;
    const wasBand = s.bandWasActive;
    resetGestureCandidates(s);
    return { kind: 'release', wasDrag, wasBand };
  }

  // fileStart takes priority (mirrors live `if (fd) … else if (bandOrigin)`).
  if (s.fileStart) {
    const dx = cursor.cx - s.fileStart.x;
    const dy = cursor.cy - s.fileStart.y;
    if (pastDragThreshold(dx, dy)) {
      const paths = s.filePaths;
      s.fileDragWasActive = true;
      resetGestureCandidates(s);
      return { kind: 'startDrag', paths };
    }
    return { kind: 'idle' };
  }

  if (s.bandOrigin) {
    const dx = cursor.cx - s.bandOrigin.x;
    const dy = cursor.cy - s.bandOrigin.y;
    if (s.bandWasActive || pastDragThreshold(dx, dy)) {
      s.bandWasActive = true;
      return {
        kind: 'band',
        rect: { x1: s.bandOrigin.x, y1: s.bandOrigin.y, x2: cursor.cx, y2: cursor.cy },
      };
    }
  }

  return { kind: 'idle' };
}
