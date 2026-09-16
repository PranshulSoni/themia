import { createEffect, onCleanup } from 'solid-js';
import { appActive } from './appActive';
import { debugLog } from './debugLog';
import { isDemo } from './demo/invoke';

/**
 * Drop-in replacement for the common setInterval + onCleanup pattern.
 *
 * - Pauses while `appActive()` is false (hibernation / sleep).
 * - On reactivation: waits a random stagger (0–2 s) then calls `fn` once
 *   and restarts the interval, avoiding thundering-herd when all widgets
 *   wake simultaneously.
 * - Automatically cleans up via `onCleanup`.
 *
 * @param fn        The function to call on each tick.
 * @param intervalMs  Reactive accessor returning the interval in milliseconds.
 * @param label     Optional label for debug logging (e.g. 'email', 'cpu').
 */
export function createPolling(fn: () => void, intervalMs: () => number, label?: string): void {
  let tickCount = 0;

  createEffect(() => {
    const ms = intervalMs();
    const active = appActive();

    if (!active) {
      if (label) debugLog(label, `poll PAUSED (appActive=false)`);
      return;
    }

    if (label) debugLog(label, `poll START interval=${ms}ms`);

    const wrappedFn = label ? () => {
      tickCount++;
      const t0 = performance.now();
      try {
        fn();
      } finally {
        debugLog(label!, `poll tick #${tickCount} elapsed=${(performance.now() - t0) | 0}ms`);
      }
    } : fn;

    let intervalId: ReturnType<typeof setInterval> | undefined;
    // The stagger spreads the wake-up burst when every widget on a desktop
    // reactivates at once. In demo / picker-preview mode there is no herd —
    // one widget is mounted at a time — and the delay is purely a blank card
    // the user stares at for up to two seconds while choosing a widget. It also
    // made the docs screenshot export non-deterministic: whether a widget had
    // data by capture time came down to its random draw.
    const stagger = isDemo() ? 0 : Math.random() * 2000;
    const initTimer = setTimeout(() => {
      wrappedFn();
      intervalId = setInterval(wrappedFn, Math.max(ms, 100));
    }, stagger);

    onCleanup(() => {
      clearTimeout(initTimer);
      if (intervalId !== undefined) clearInterval(intervalId);
    });
  });
}
