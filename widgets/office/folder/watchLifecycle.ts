// [S-63] Folder fs-watcher unload lifecycle.
//
// A page reload tears the document down WITHOUT running Solid's onCleanup, so
// the Rust notify watcher behind tauri-plugin-fs `watch()` survived the reload
// and kept streaming events into a dead Channel: "[TAURI] Couldn't find
// callback id N" spam on every fs event until the app restarted. This handle
// owns the unwatch fn and guarantees:
//   - unwatch fires on `beforeunload` as well as on Solid cleanup,
//   - it fires exactly once no matter how many paths race,
//   - a watch() that resolves after release (dying page) unwatches immediately.

export interface WatchHandle {
  /** Adopt the unwatch fn once watch() resolves. If the handle is already
   *  released, the fn is invoked immediately instead of re-arming. */
  adopt(unwatch: () => void): void;
  /** True once released; late watch() resolutions must not mark the watcher
   *  active on a dying page / cancelled effect. */
  released(): boolean;
  /** Fire unwatch exactly once and detach the beforeunload listener. */
  release(): void;
}

// Structural minimum of `window` we need, keeps the handle testable with a
// plain fake (the DOM lib's overloaded signatures reject simple stubs).
interface WindowLike {
  addEventListener(type: 'beforeunload', listener: () => void): void;
  removeEventListener(type: 'beforeunload', listener: () => void): void;
}

export function createWatchHandle(win: WindowLike = window): WatchHandle {
  let unwatch: (() => void) | null = null;
  let done = false;

  const release = (): void => {
    if (done) return;
    done = true;
    win.removeEventListener('beforeunload', release);
    const fn = unwatch;
    unwatch = null;
    fn?.();
  };

  win.addEventListener('beforeunload', release);

  return {
    adopt(fn: () => void): void {
      if (done) fn();
      else unwatch = fn;
    },
    released: () => done,
    release,
  };
}
