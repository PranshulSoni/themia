// ---------------------------------------------------------------------------
// Safe-mode boot guard (1.19 B2)
//
// Guards against a crash-loop where corrupt persisted state makes a widget
// window throw during App import/render every time it opens. `beginBoot()` is
// called synchronously in main.tsx BEFORE the App chunk loads, so a crash during
// App init still leaves the bumped counter behind. `markBootSuccess()` (called
// from App.onMount, i.e. once we're past the synchronous render crash point)
// resets it, so a NORMAL boot never accrues. After THRESHOLD consecutive boots
// that never reached a stable mount, main.tsx renders the minimal SafeMode
// recovery UI instead of App.
//
// PER-WINDOW: localStorage is SHARED across every Tauri webview window (one
// widget window per monitor), so a single shared counter would falsely trip on a
// normal MULTI-MONITOR startup (each window bumps the same key → reaches THRESHOLD
// with no real crash). The counter is therefore keyed by the window label, so a
// window only enters safe mode on ITS OWN repeated failures.
//
// Every localStorage access is guarded: the safe-mode machinery must never be
// the thing that breaks boot, so on any storage error it fails OPEN (boot normally).
// ---------------------------------------------------------------------------

const BOOT_PREFIX = 'themia-boot-attempts:';

/** Consecutive failed boots (of one window) before falling into safe mode. */
export const SAFE_MODE_THRESHOLD = 3;

const bootKey = (windowKey: string): string => BOOT_PREFIX + windowKey;

/**
 * Increment this window's consecutive-boot-attempt counter and report whether it
 * has failed to reach a stable mount THRESHOLD times in a row. Must be called
 * once, synchronously, before the App chunk is imported. `windowKey` is the Tauri
 * window label so each monitor's widget window tracks its own attempts.
 */
export function beginBoot(windowKey: string): boolean {
  try {
    const k = bootKey(windowKey);
    const n = (parseInt(localStorage.getItem(k) ?? '0', 10) || 0) + 1;
    localStorage.setItem(k, String(n));
    return n >= SAFE_MODE_THRESHOLD;
  } catch {
    return false; // fail open: never let the guard itself block boot
  }
}

/** Reset this window's boot-attempt counter, called from App.onMount once the
 *  window has mounted past the synchronous render/early-init crash point. */
export function markBootSuccess(windowKey: string): void {
  try { localStorage.removeItem(bootKey(windowKey)); } catch { /* ignore: best-effort reset */ }
}

/** "Try again" recovery action: clear this window's counter so the next load
 *  attempts a normal boot instead of re-entering safe mode. */
export function clearBootAttempts(windowKey: string): void {
  try { localStorage.removeItem(bootKey(windowKey)); } catch { /* ignore */ }
}

/** "Reset settings" recovery action: wipe ALL persisted state (the likely cause
 *  of the crash-loop) + every window's boot counter, for a clean first-run. */
export function resetAllState(): void {
  try { localStorage.clear(); } catch { /* ignore */ }
}
