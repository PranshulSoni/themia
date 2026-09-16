/**
 * Deterministic waiting for e2e specs (Phase 0.3).
 *
 * Replaces `browser.pause(ms)`, a fixed sleep that is too short on a loaded
 * CI box (flaky) and too long on a fast one (slow). Always prefer waiting on an
 * observable CONDITION. Use the TIMEOUT scale below instead of ad-hoc numbers
 * so a spec that legitimately needs longer is visible at a glance.
 *
 * Migration status: the shared helpers in app.ts are converted; the per-spec
 * `browser.pause()` calls are being replaced opportunistically as each spec is
 * next touched (the ratchet). A handful of waits on a debounce/animation are
 * legitimate fixed delays and stay, but as `settle()`, clearly labelled.
 */
import { browser } from '@wdio/globals';

export const TIMEOUT = {
  /** modal opens, async setter applies, a window handle appears */
  quick: 1_000,
  /** most UI updates, form submits, cross-window propagation */
  normal: 3_000,
  /** image load, larger data fetch, debounced persistence chains */
  slow: 5_000,
  /** system ops, multi-window churn */
  crawl: 10_000,
} as const;

/**
 * Wait until `cond` returns truthy (polled). The deterministic replacement for
 * `browser.pause()`. Throws with `msg` on timeout so failures are legible.
 */
export async function waitFor(
  cond: () => boolean | Promise<boolean>,
  msg = 'condition not met',
  timeout: number = TIMEOUT.normal,
): Promise<void> {
  await browser.waitUntil(async () => !!(await cond()), {
    timeout,
    interval: 30,
    timeoutMsg: msg,
  });
}

/**
 * A *deliberate* fixed delay, only for genuinely time-based waits that have no
 * observable condition (a CSS transition mid-flight, a known debounce window).
 * Named so reviewers can tell "I gave up and slept" (bad) from "this is
 * intrinsically time-based" (acceptable). Do NOT use to paper over ordering.
 */
export async function settle(ms: number, _reason: string): Promise<void> {
  await browser.pause(ms);
}
