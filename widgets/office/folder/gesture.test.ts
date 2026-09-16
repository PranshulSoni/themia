import { describe, it, expect } from 'vitest';
import {
  freshGestureState,
  beginPress,
  pollStep,
  resetGestureCandidates,
  pastDragThreshold,
  DRAG_THRESHOLD_PX,
} from './gesture.ts';

describe('folder gesture: click vs drag decision', () => {
  it('movement under the threshold is a click, at/over is a drag', () => {
    expect(pastDragThreshold(0, 0)).toBe(false);
    expect(pastDragThreshold(DRAG_THRESHOLD_PX - 1, 0)).toBe(false);
    expect(pastDragThreshold(3, 3)).toBe(false); // 18 < 25
    expect(pastDragThreshold(DRAG_THRESHOLD_PX, 0)).toBe(true); // 25 >= 25
    expect(pastDragThreshold(4, 4)).toBe(true);  // 32 >= 25
  });

  it('a fresh press that does not move is released as a plain click', () => {
    const s = freshGestureState();
    beginPress(s, { x: 100, y: 100, onItem: true, paths: ['C:/a.txt'] });
    expect(pollStep(s, { cx: 100, cy: 100, leftDown: true }).kind).toBe('idle');
    const up = pollStep(s, { cx: 100, cy: 100, leftDown: false });
    expect(up).toEqual({ kind: 'release', wasDrag: false, wasBand: false });
  });

  it('a press dragged past the threshold hands off to a native drag once', () => {
    const s = freshGestureState();
    beginPress(s, { x: 100, y: 100, onItem: true, paths: ['C:/a.txt'] });
    const a = pollStep(s, { cx: 130, cy: 100, leftDown: true });
    expect(a).toEqual({ kind: 'startDrag', paths: ['C:/a.txt'] });
    // The drag handoff must have torn the candidate down.
    expect(s.fileStart).toBeNull();
    expect(s.polling).toBe(false);
    // The trailing click is suppressed.
    const up = pollStep(s, { cx: 130, cy: 100, leftDown: false });
    expect(up.kind).toBe('release');
    expect((up as { wasDrag: boolean }).wasDrag).toBe(true);
  });
});

describe('folder gesture: #58 stale-state reset must not misclassify the next click', () => {
  // Reproduces the reported bug: after the poll loop aborts on a transient
  // cursor-read failure, the NEXT left press must still be classified as a
  // click. The old code left the file-drag origin behind, so the next press
  // measured the drag threshold against stale coordinates and phantom-dragged,
  // swallowing the mouseup so single-click open died until reload.

  it('an aborted poll leaves no in-flight candidate behind', () => {
    const s = freshGestureState();
    beginPress(s, { x: 100, y: 100, onItem: true, paths: ['C:/a.txt'] });
    s.polling = true;

    // The catch/abort path (transient get_cursor_state failure) must fully reset.
    resetGestureCandidates(s);

    expect(s.fileStart).toBeNull();
    expect(s.bandOrigin).toBeNull();
    expect(s.filePaths).toEqual([]);
    expect(s.polling).toBe(false);
  });

  it('a background press after a stale file-drag origin is NOT a phantom drag', () => {
    const s = freshGestureState();

    // WORST CASE: a prior gesture aborted and left a stale file-drag origin AND
    // a stale suppress flag behind (exactly what the buggy catch path did).
    s.fileStart = { x: 100, y: 100 };
    s.filePaths = ['C:/old.txt'];
    s.fileDragWasActive = true;
    s.bandWasActive = true;

    // A fresh press far from the stale origin must wipe all of it before arming.
    beginPress(s, { x: 500, y: 500, onItem: false });
    expect(s.fileStart).toBeNull();
    expect(s.filePaths).toEqual([]);
    expect(s.fileDragWasActive).toBe(false);
    expect(s.bandWasActive).toBe(false);
    expect(s.bandOrigin).toEqual({ x: 500, y: 500 });

    // First poll tick with no movement: must NOT phantom-drag on the stale origin.
    const first = pollStep(s, { cx: 500, cy: 500, leftDown: true });
    expect(first.kind).not.toBe('startDrag');

    // Release with no movement → a real click (nothing to suppress it).
    const up = pollStep(s, { cx: 500, cy: 500, leftDown: false });
    expect(up).toEqual({ kind: 'release', wasDrag: false, wasBand: false });
  });

  it('an item press after a stale file-drag origin classifies by the NEW origin', () => {
    const s = freshGestureState();
    // Stale origin from an aborted gesture.
    s.fileStart = { x: 10, y: 10 };
    s.filePaths = ['C:/old.txt'];

    // New press on an item at (400,400); cursor stays there → click, not drag.
    beginPress(s, { x: 400, y: 400, onItem: true, paths: ['C:/new.txt'] });
    expect(s.fileStart).toEqual({ x: 400, y: 400 });
    expect(s.filePaths).toEqual(['C:/new.txt']);
    expect(pollStep(s, { cx: 400, cy: 400, leftDown: true }).kind).toBe('idle');
  });

  it('a fresh press bumps the generation so a stale poll loop tears down', () => {
    const s = freshGestureState();
    const gen1 = beginPress(s, { x: 0, y: 0, onItem: true, paths: ['C:/a.txt'] });
    const gen2 = beginPress(s, { x: 9, y: 9, onItem: false });
    expect(gen2).toBeGreaterThan(gen1);
    // A loop that captured gen1 sees the mismatch and must stop.
    expect(s.generation).toBe(gen2);
  });
});
