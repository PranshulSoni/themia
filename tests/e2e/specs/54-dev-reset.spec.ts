/**
 * "Reset Themia" button: Settings > General > Data (next to Export / Import).
 *
 * Always visible. Clicking it shows a native confirm dialog (Tauri ask()); on
 * accept it runs localStorage.clear() + window.location.reload(), wiping all
 * frontend state (views, layouts, designs, cached widget data) back to
 * fresh-install defaults. License + connected accounts are not touched
 * (they live in Rust-side disk files, not localStorage).
 *
 * The confirm dialog is a native OS dialog WebDriver can't drive, so we stub the
 * `plugin:dialog|ask` invoke (see 15-import-export.spec.ts for why real
 * plugin-dialog flows are otherwise skipped).
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
  attachToAppWindow, waitForReady, stubTauriInvoke,
  switchToConfigWindow,
} from '../helpers/app.js';

const RESET_LABEL = 'Reset Themia';

/** Install the `window.__settingsHooks.askReset` test hook in the config
 *  window so the reset button skips the real Tauri dialog (whose `invoke`
 *  is non-writable and can't be stubbed at the window level). */
async function stubConfigAsk(accept: boolean): Promise<void> {
  await switchToConfigWindow('app');
  await browser.execute((a: boolean) => {
    const w = window as any;
    w.__settingsHooks = w.__settingsHooks ?? {};
    w.__settingsHooks.askReset = () => Promise.resolve(a);
  }, accept);
}

/** Click the button inside the "Reset Themia" row. Settings live in the
 *  detached config window (target prefix "app"). */
async function clickResetButton(): Promise<void> {
  await switchToConfigWindow('app');
  const ok = await browser.execute((label: string) => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    for (const r of rows) {
      if (r.querySelector('.design-label')?.textContent?.trim() === label) {
        const btn = r.querySelector<HTMLButtonElement>('button');
        if (btn) { btn.click(); return true; }
      }
    }
    return false;
  }, RESET_LABEL);
  if (!ok) throw new Error('Reset button not found');
}

describe('reset Themia', () => {
  beforeEach(async () => {
    await resetAppState();
    await openSettings();
    await (await settingsTab('General')).click();
  });

  afterEach(async () => {
    // Settings modal may already be gone (after the reset reload). Best-effort.
    try { await closeSettings(); } catch {}
  });

  it('confirming the reset clears localStorage and reloads to defaults', async () => {
    // Seed state a reset must wipe (origin-shared with the main window).
    await browser.execute(() => {
      localStorage.setItem('reset-test-marker', 'present');
      localStorage.setItem('widget-screens', JSON.stringify([
        { id: 'default', name: 'Default' },
        { id: 'view-extra', name: 'Extra' },
      ]));
    });

    // Stub the native confirm dialog to accept (both main and the config
    // window, since the click handler fires from the config window's invoke
    // binding, which `stubTauriInvoke` doesn't reach).
    await stubTauriInvoke({ 'plugin:dialog|ask': true });
    await stubConfigAsk(true);

    await clickResetButton();

    // Handler: ask() → localStorage.clear() → window.location.reload().
    // The settings window reloads; re-init writes fresh defaults back.
    await browser.pause(500);
    await attachToAppWindow();
    await waitForReady();

    const marker = await browser.execute(() => localStorage.getItem('reset-test-marker'));
    expect(marker).to.equal(null);

    const views = await browser.execute(() => {
      try { return JSON.parse(localStorage.getItem('widget-screens') ?? '[]'); }
      catch { return []; }
    }) as { id: string; name: string }[];
    expect(views.some(s => s.id === 'view-extra')).to.equal(false);
  });

  it('cancelling the reset leaves state untouched', async () => {
    await browser.execute(() => localStorage.setItem('reset-test-marker', 'present'));
    await stubTauriInvoke({ 'plugin:dialog|ask': false });
    await stubConfigAsk(false);

    await clickResetButton();
    await browser.pause(200);

    const marker = await browser.execute(() => localStorage.getItem('reset-test-marker'));
    expect(marker).to.equal('present');
  });
});
