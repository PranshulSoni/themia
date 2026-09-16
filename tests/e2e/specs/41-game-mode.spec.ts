/**
 * Game-mode (fullscreen / presentation) suspension.
 *
 * The real OS detector runs in Rust via `SHQueryUserNotificationState` polling.
 * We can't induce a real fullscreen D3D app from WebDriver, so these tests
 * exercise the frontend contract end-to-end:
 *
 *   1. Frontend toggle persists `themia-game-mode-suspend` in localStorage.
 *   2. Rust emits the `themia:game-mode` event -> frontend unmounts the grid
 *      and shows the bottom-right "Stale data" badge when the toggle is ON.
 *   3. When the toggle is OFF the event has no effect (defence against stale
 *      state leaking through).
 *   4. Flipping the toggle off while suspended immediately restores widgets.
 *   5. Pref survives reload.
 *
 * We stub `set_game_mode_watcher` so the Rust polling thread never spins up,
 * and inject the `themia:game-mode` event via the same `plugin:event|emit` IPC
 * path the Rust backend uses, so the exact `listen()` callback in
 * `gameMode.ts` fires.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, waitForReady,
  pickWidget, clickGridCell, widgetCount, switchToConfigWindow, switchToMain,
} from '../helpers/app.js';

const SUSPEND_KEY = 'themia-game-mode-suspend';
const BADGE_SEL   = '.game-mode-badge';

/**
 * Install a `set_game_mode_watcher` stub so the real Rust watcher never
 * starts. Fall through for every other command so Tauri keeps working.
 * Idempotent: safe to call multiple times per test.
 */
async function installGameModeStub(): Promise<void> {
  await switchToMain();
  await browser.execute(() => {
    const w = window as any;
    const internals = w.__TAURI_INTERNALS__ ?? (w.__TAURI_INTERNALS__ = {});
    // Preserve the real invoke once, even if we're re-wrapped.
    if (!internals.__originalInvoke) internals.__originalInvoke = internals.invoke;
    const original = internals.__originalInvoke;
    internals.invoke = async (cmd: string, args: any) => {
      if (cmd === 'set_game_mode_watcher') return null;
      return original(cmd, args);
    };
  });
}

/**
 * Inject a `themia:game-mode` Tauri event. `plugin:event|emit` is the same
 * IPC path `app.emit(..)` uses in Rust: it fans out to every webview
 * listener, including the one `gameMode.ts` registers via `listen()`.
 *
 * Payload is sent as a raw object (NOT a JSON string). Tauri serialises it
 * into a `JsonValue` and `listen()` callbacks receive the deserialised object.
 */
async function fireGameModeEvent(active: boolean): Promise<void> {
  await switchToMain();
  await browser.execute(async (act: boolean) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    // Bypass our stub so the emit actually reaches Rust.
    const real = internals?.__originalInvoke ?? internals?.invoke;
    await real('plugin:event|emit', {
      event: 'themia:game-mode',
      payload: { active: act },
    });
  }, active);
  // Small settle for the Tauri IPC round-trip + Solid reactivity.
  await browser.pause(200);
}

/** Click the "Suspend while gaming" switch in Settings → General.
 *  Settings now live in the detached config window (target prefix "app");
 *  the row markup is identical (.design-row + .design-label + .design-switch)
 *  but without the old .design-modal wrapper. We switch into the config
 *  window before the DOM query. */
async function clickGameModeSwitch(): Promise<void> {
  await switchToConfigWindow('app');
  const ok = await browser.execute(() => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (/suspend while gaming/i.test(lbl)) {
        const b = row.querySelector<HTMLButtonElement>('.design-switch');
        if (!b) return false;
        b.click();
        return true;
      }
    }
    return false;
  });
  if (!ok) throw new Error('no .design-switch row labelled "Suspend while gaming"');
}

async function gameModeSwitchIsOn(): Promise<boolean> {
  await switchToConfigWindow('app');
  const cls = await browser.execute(() => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (/suspend while gaming/i.test(lbl)) {
        return row.querySelector<HTMLButtonElement>('.design-switch')?.className ?? '';
      }
    }
    return '';
  });
  return (cls as string).includes(' on');
}

async function badgeIsVisible(): Promise<boolean> {
  await switchToMain();
  return (await $$(BADGE_SEL).length) > 0;
}

async function readSuspendPref(): Promise<string | null> {
  await switchToMain();
  return browser.execute((k: string) => localStorage.getItem(k), SUSPEND_KEY);
}

/** Place a single widget so grid-unmount assertions have something observable. */
async function seedOneWidget(): Promise<void> {
  await pickWidget('Clock');
  await clickGridCell(2, 2);
  await browser.waitUntil(async () => (await widgetCount()) >= 1, { timeout: 2_000 });
  // Close any auto-opened config modal.
  await browser.keys(['Escape']);
  await browser.keys(['Escape']);
}

describe('game-mode suspension', () => {
  beforeEach(async () => {
    await resetAppState();
    await installGameModeStub();
  });

  afterEach(async () => {
    try { await closeSettings(); } catch { /* ignore */ }
    // Game-mode state is in-memory in the main window. If a test fired an
    // `active=true` event without firing the corresponding `active=false`,
    // the grid stays unmounted, and the next test's `resetAppState` then
    // can't find the `.grid` selector (the localStorage reset doesn't
    // restore the in-memory gameModeActive signal). Send a final `false`
    // event so the grid is always mounted again.
    try { await fireGameModeEvent(false); } catch { /* ignore */ }
  });

  it('toggle defaults OFF and no badge is shown', async () => {
    expect(await readSuspendPref()).to.not.equal('true');
    expect(await badgeIsVisible()).to.equal(false);
  });

  it('flipping the switch persists the pref to localStorage', async () => {
    await openSettings();
    await (await settingsTab('General')).click();

    expect(await gameModeSwitchIsOn()).to.equal(false);
    expect(await readSuspendPref()).to.not.equal('true');

    await clickGameModeSwitch();

    await browser.waitUntil(() => gameModeSwitchIsOn(), { timeout: 2_000 });
    expect(await readSuspendPref()).to.equal('true');
  });

  it('event with active=true hides the grid and shows the badge (when enabled)', async () => {
    await seedOneWidget();
    await openSettings();
    await (await settingsTab('General')).click();
    await clickGameModeSwitch();
    await browser.waitUntil(() => gameModeSwitchIsOn(), { timeout: 2_000 });
    await closeSettings();

    expect(await badgeIsVisible()).to.equal(false);
    expect(await widgetCount()).to.be.greaterThan(0);

    await fireGameModeEvent(true);

    await browser.waitUntil(() => badgeIsVisible(), { timeout: 3_000 });
    // Grid is wrapped in <Show>, so the .grid element itself disappears.
    await browser.waitUntil(async () => (await $$('.grid').length) === 0, { timeout: 2_000 });
    expect(await widgetCount()).to.equal(0);
  });

  it('event with active=false restores the grid', async () => {
    await seedOneWidget();
    await openSettings();
    await (await settingsTab('General')).click();
    await clickGameModeSwitch();
    await browser.waitUntil(() => gameModeSwitchIsOn(), { timeout: 2_000 });
    await closeSettings();

    await fireGameModeEvent(true);
    await browser.waitUntil(() => badgeIsVisible(), { timeout: 3_000 });

    await fireGameModeEvent(false);
    await browser.waitUntil(async () => !(await badgeIsVisible()), { timeout: 3_000 });
    await browser.waitUntil(async () => (await $$('.grid').length) > 0, { timeout: 2_000 });
    expect(await widgetCount()).to.be.greaterThan(0);
  });

  it('event with active=true is a no-op while the toggle is OFF', async () => {
    await seedOneWidget();
    expect(await readSuspendPref()).to.not.equal('true');

    await fireGameModeEvent(true);

    expect(await badgeIsVisible()).to.equal(false);
    expect(await widgetCount()).to.be.greaterThan(0);
  });

  it('disabling the toggle while suspended restores the grid immediately', async () => {
    await seedOneWidget();
    await openSettings();
    await (await settingsTab('General')).click();
    await clickGameModeSwitch();
    await browser.waitUntil(() => gameModeSwitchIsOn(), { timeout: 2_000 });
    await closeSettings();

    await fireGameModeEvent(true);
    await browser.waitUntil(() => badgeIsVisible(), { timeout: 3_000 });

    // Flip the switch back off: setGameModeSuspend() forces gameModeActive
    // false locally so the grid comes back without waiting for the Rust
    // watcher to emit a "not active" event.
    await openSettings();
    await (await settingsTab('General')).click();
    await clickGameModeSwitch();
    await browser.waitUntil(async () => !(await gameModeSwitchIsOn()), { timeout: 2_000 });
    await closeSettings();

    expect(await badgeIsVisible()).to.equal(false);
    await browser.waitUntil(async () => (await $$('.grid').length) > 0, { timeout: 2_000 });
    expect(await widgetCount()).to.be.greaterThan(0);
    expect(await readSuspendPref()).to.equal('false');
  });

  it('suspend pref survives a reload', async () => {
    await openSettings();
    await (await settingsTab('General')).click();
    await clickGameModeSwitch();
    await browser.waitUntil(() => gameModeSwitchIsOn(), { timeout: 2_000 });
    await closeSettings();

    await browser.refresh();
    await waitForReady();

    expect(await readSuspendPref()).to.equal('true');
  });
});
