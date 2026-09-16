// Regression tests for issue #58 on the SHORTCUT widget's
// press → click | rubber-band | OLE-drag gesture.
//
// WHY (issue #58): the shortcut widget used to run its own ad-hoc flag
// machinery (`pending` / `mousePolling` / `bandOrigin`). Two paths stranded
// state:
//   1. The poll-loop catch (a transient `get_cursor_state` failure) cleared
//      `mousePolling` + the band but LEFT the item candidate (`pending`) set.
//   2. `onListMouseDown` armed a rubber-band candidate WITHOUT wiping a stranded
//      `pending`, and the loop's `if (pending)` branch has priority over the
//      band branch.
// So the NEXT background press measured the drag threshold against the STALE
// `pending` origin, immediately handed off to a native OLE drag, and that modal
// loop swallowed the mouseup — no `click` fired, so single-click open silently
// died until the app was restarted. Right-click was unaffected (never depends
// on `click`).
//
// THE FIX ([S-74]): the shortcut is rewired onto the SHARED, unit-tested folder
// gesture state machine. `beginPress` fully wipes prior state before arming
// exactly one candidate; every exit path resets the in-flight candidate via
// `resetGestureCandidates`; a monotonic generation token makes a stale poll loop
// self-terminate. These tests reproduce both shortcut sequences against the real
// shared-module primitives (exactly as Shortcut.tsx now drives them) and assert
// the next fresh press is classified as a CLICK, never a phantom drag.

import { describe, it, expect } from 'vitest';
import {
  freshGestureState, beginPress, pollStep, resetGestureCandidates,
  type GestureState,
} from '../../office/folder/gesture.ts';

// ── Faithful models of the two shortcut implementations ─────────────────────
// Only the decision path is modelled (no wall-clock, no DOM): the loop's
// abort/teardown reset, and the background press. The OLD variants reproduce the
// removed inline code verbatim; the NEW variants call the shared-module
// primitives exactly as the rewired Shortcut.tsx does.

/** OLD shortcut poll-loop catch: `mousePolling = false; endBand(false)` — clears
 *  polling and the band but LEAVES the item candidate (`pending`) behind. */
function oldAbortReset(g: GestureState): void {
  g.polling = false;
  g.bandOrigin = null; // endBand cleared the band candidate only
}
/** OLD shortcut `onListMouseDown`: arms a band candidate, never wipes `pending`. */
function oldBackgroundPress(g: GestureState, x: number, y: number): void {
  g.bandOrigin = { x, y };
  g.bandWasActive = false;
}
/** OLD shortcut `onItemMouseDown`: overwrites `pending` directly (no wipe). */
function oldItemPress(g: GestureState, id: string, x: number, y: number): void {
  g.fileStart = { x, y };
  g.filePaths = [id];
}

/** NEW (rewired) abort/teardown reset used by the catch AND external mouseup. */
function newAbortReset(g: GestureState): void {
  resetGestureCandidates(g);
}
/** NEW background press: routes through beginPress, which wipes ALL prior state. */
function newBackgroundPress(g: GestureState, x: number, y: number): number {
  return beginPress(g, { x, y, onItem: false });
}
/** NEW item press: routes through beginPress; item id rides in `paths`. */
function newItemPress(g: GestureState, id: string, x: number, y: number): number {
  return beginPress(g, { x, y, onItem: true, paths: [id] });
}

describe('shortcut gesture #58: aborted poll must not phantom-drag the next press', () => {
  it('s58_shortcut_old_reset_phantom_drags_next_background_press (characterizes the bug)', () => {
    // Reproduce the removed inline machinery step for step.
    const g = freshGestureState();
    oldItemPress(g, 'itemA', 100, 100); // press item A
    g.polling = true;

    oldAbortReset(g);                   // transient poll failure: `pending` stranded
    expect(g.fileStart).toEqual({ x: 100, y: 100 }); // the leak

    oldBackgroundPress(g, 600, 600);    // background press, no wipe of `pending`
    g.polling = true;

    // The loop's `if (fileStart)` branch has priority over the band branch, and
    // the 500px gap to the stale origin trips the 5px threshold → phantom drag.
    const first = pollStep(g, { cx: 600, cy: 600, leftDown: true });
    expect(first).toEqual({ kind: 'startDrag', paths: ['itemA'] });
  });

  it('s58_shortcut_rewired_reset_makes_next_background_press_a_click', () => {
    // Same sequence, driven by the real rewired primitives.
    const g = freshGestureState();
    newItemPress(g, 'itemA', 100, 100); // press item A
    g.polling = true;

    newAbortReset(g);                   // rewired catch fully resets the candidate
    expect(g.fileStart).toBeNull();
    expect(g.polling).toBe(false);

    const gen = newBackgroundPress(g, 600, 600); // beginPress wipes prior state
    g.polling = true;

    // No movement: must NOT phantom-drag on any stale origin.
    const first = pollStep(g, { cx: 600, cy: 600, leftDown: true });
    expect(first.kind).not.toBe('startDrag');

    // Release with no movement → a plain click (band never activated).
    const up = pollStep(g, { cx: 600, cy: 600, leftDown: false });
    expect(up).toEqual({ kind: 'release', wasDrag: false, wasBand: false });
    expect(gen).toBeGreaterThan(0);
  });

  it('s58_shortcut_rewired_reset_next_item_press_is_a_click_not_stale_drag', () => {
    // Item press → abort → fresh item press elsewhere, cursor stationary.
    const g = freshGestureState();
    newItemPress(g, 'itemA', 10, 10);
    g.polling = true;

    newAbortReset(g);

    newItemPress(g, 'itemB', 400, 400); // beginPress re-arms with the NEW origin
    g.polling = true;
    expect(g.fileStart).toEqual({ x: 400, y: 400 });
    expect(g.filePaths).toEqual(['itemB']);

    // Stationary → idle (a click), never a drag of the stale itemA.
    expect(pollStep(g, { cx: 400, cy: 400, leftDown: true }).kind).toBe('idle');
    const up = pollStep(g, { cx: 400, cy: 400, leftDown: false });
    expect(up.kind).toBe('release');
  });
});

describe('shortcut gesture #58: external mouseup must not double-drive the loop', () => {
  it('s58_shortcut_external_mouseup_generation_guard_tears_down_stale_loop', () => {
    const g = freshGestureState();
    const gen0 = newItemPress(g, 'itemA', 50, 50);
    g.polling = true; // a poll loop is running, holding gen0

    // Rewired onItemMouseUp beats the loop's own release detection and resets.
    newAbortReset(g);
    expect(g.polling).toBe(false);

    // Fast re-press before the stale loop resumes from its await.
    const gen1 = newItemPress(g, 'itemB', 400, 400);
    g.polling = true;
    expect(gen1).toBeGreaterThan(gen0);

    // The stale loop's guard `g.generation === gen0` is now false → it exits,
    // leaving exactly one loop; and the fresh stationary press is a click.
    expect(g.generation).not.toBe(gen0);
    expect(pollStep(g, { cx: 400, cy: 400, leftDown: true }).kind).toBe('idle');
  });
});
