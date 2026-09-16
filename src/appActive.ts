import { createSignal } from 'solid-js';

/**
 * Detects hibernation / sleep by checking for time-jumps.
 * Exports a reactive `appActive()` signal that goes false during sleep
 * and true again after wake (with a brief stagger window).
 */

const HEARTBEAT_MS = 2000;
const JUMP_THRESHOLD_MS = 10_000; // >10s gap → assume sleep/hibernation
const REACTIVATION_DELAY_MS = 500;

const [appActive, setAppActive] = createSignal(true);
export { appActive };

let lastTick = Date.now();

// Heartbeat: detect time jumps (hibernation/sleep)
setInterval(() => {
  const now = Date.now();
  if (now - lastTick > JUMP_THRESHOLD_MS) {
    // Woke from sleep, briefly deactivate so polling helpers pause,
    // then reactivate after a short delay to stagger widget restarts.
    setAppActive(false);
    setTimeout(() => setAppActive(true), REACTIVATION_DELAY_MS);
  }
  lastTick = now;
}, HEARTBEAT_MS);
