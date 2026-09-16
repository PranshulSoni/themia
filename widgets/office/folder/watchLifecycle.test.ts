// [S-63] Folder fs-watcher unload lifecycle (widgets/office/folder/SPEC.md).
//
// A page reload tears the document down WITHOUT running Solid's onCleanup, so
// the Rust notify watcher behind tauri-plugin-fs `watch()` survived and kept
// streaming events into a dead Channel: "[TAURI] Couldn't find callback id N"
// spam on every fs event, forever. The handle must fire unwatch on
// `beforeunload` too, exactly once, and late watch() resolutions after release
// must unwatch immediately instead of re-arming.
import { describe, it, expect, vi } from 'vitest';
import { createWatchHandle } from './watchLifecycle.ts';

type Listener = () => void;

function fakeWin() {
  const listeners = new Map<string, Set<Listener>>();
  return {
    addEventListener(type: string, fn: Listener) {
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type)!.add(fn);
    },
    removeEventListener(type: string, fn: Listener) {
      listeners.get(type)?.delete(fn);
    },
    fire(type: string) {
      for (const fn of [...(listeners.get(type) ?? [])]) fn();
    },
    count(type: string): number {
      return listeners.get(type)?.size ?? 0;
    },
  };
}

describe('[S-63] createWatchHandle', () => {
  it('beforeunload fires the adopted unwatch', () => {
    const win = fakeWin();
    const unwatch = vi.fn();
    const handle = createWatchHandle(win);
    handle.adopt(unwatch);
    win.fire('beforeunload');
    expect(unwatch).toHaveBeenCalledTimes(1);
  });

  it('release is idempotent: cleanup + beforeunload never double-unwatch', () => {
    const win = fakeWin();
    const unwatch = vi.fn();
    const handle = createWatchHandle(win);
    handle.adopt(unwatch);
    handle.release();      // Solid onCleanup path
    win.fire('beforeunload'); // reload path on top, must be a no-op
    handle.release();
    expect(unwatch).toHaveBeenCalledTimes(1);
  });

  it('adopt after release unwatches immediately (watch() resolved on a dying page)', () => {
    const win = fakeWin();
    const unwatch = vi.fn();
    const handle = createWatchHandle(win);
    handle.release();
    expect(handle.released()).toBe(true);
    handle.adopt(unwatch);
    expect(unwatch).toHaveBeenCalledTimes(1);
  });

  it('release detaches the beforeunload listener (no leak per path change)', () => {
    const win = fakeWin();
    const handle = createWatchHandle(win);
    expect(win.count('beforeunload')).toBe(1);
    handle.release();
    expect(win.count('beforeunload')).toBe(0);
  });
});
