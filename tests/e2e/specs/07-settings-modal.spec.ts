/**
 * Settings modal: open paths, tab navigation, close paths.
 *
 * The modal lives at `.design-modal`. Navigation pages (navItems in
 * SettingsModal.tsx): Appearance, General, Accounts, License, Logs, plus a
 * View Bar entry and one per view. `initialPage` routing is exercised
 * via the "View Settings" desktop ctx menu item (routes to 'view')
 * and the license flow (routes to 'license').
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsIsOpen, settingsTab,
  activeSettingsTab, rightClick, ctxMenuItem, ctxMenuIsOpen,
} from '../helpers/app.js';

// navItems in SettingsModal.tsx (post-refactor): General, Accounts, License, Logs +
// View Bar + one per view. Accounts + License trigger Tauri invokes
// (oauth_get_accounts / license_validate) that can hang under WebDriver; skip
// them from this smoke loop. They're covered in their own specs.
const LABELS = ['General', 'Logs', 'View Bar'];

// Skipped while we investigate a tauri-driver DELETE-session hang that
// cascades to subsequent specs ("Chrome instance exited"). Individual tests
// pass in isolation; re-enable once per-spec driver lifecycle is solved.
describe.skip('settings modal', () => {
  beforeEach(async () => { await resetAppState(); });

  it('opens from edit-mode Settings button', async () => {
    expect(await settingsIsOpen()).to.equal(false);
    await openSettings();
    expect(await settingsIsOpen()).to.equal(true);
    await closeSettings();
    expect(await settingsIsOpen()).to.equal(false);
  });

  it('opens from desktop ctx menu "Settings"', async () => {
    await rightClick('.grid');
    const item = await ctxMenuItem('Settings');
    await item.click();
    await browser.waitUntil(() => settingsIsOpen(), { timeout: 2_000 });
    await closeSettings();
  });

  it('routes to View tab via ctx menu "View Settings"', async () => {
    await rightClick('.grid');
    const item = await ctxMenuItem('View Settings');
    await item.click();
    await browser.waitUntil(() => settingsIsOpen(), { timeout: 2_000 });
    // View tab is dynamic (one per view). The default view is auto-
    // selected, so its name appears as the active tab. We just assert
    // *some* view tab is active, i.e. not one of the top-level labels.
    const active = await activeSettingsTab();
    expect(LABELS).to.not.include(active);
    await closeSettings();
  });

  it('clicks through each top-level tab', async () => {
    await openSettings();
    for (const label of LABELS) {
      const tab = await settingsTab(label);
      await tab.click();
      // waitUntil: the tab switch is reactive; may not be immediate.
      await browser.waitUntil(async () => (await activeSettingsTab()) === label, { timeout: 2_000 });
    }
    await closeSettings();
  });

  it('closes via the X button', async () => {
    await openSettings();
    // Esc inside settings blurs focus first if an input is focused; use the
    // dedicated close button to avoid that race.
    const close = await $('.design-modal .modal-header .close-btn');
    await close.click();
    await browser.waitUntil(async () => !(await settingsIsOpen()), { timeout: 2_000 });
  });

  it('Escape closes after input blur (LIFO)', async () => {
    await openSettings();
    // Make sure we're on General (no auto-focused input). initialPage may
    // have been seeded to a view tab from a previous flow.
    await (await settingsTab('General')).click();
    await browser.waitUntil(async () => (await activeSettingsTab()) === 'General', { timeout: 2_000 });
    await browser.keys(['Escape']);
    if (await settingsIsOpen()) await browser.keys(['Escape']);
    await browser.waitUntil(async () => !(await settingsIsOpen()), { timeout: 2_000 });
  });

  it('backdrop click closes', async () => {
    await openSettings();
    // Fire mousedown on backdrop at (10, 10). Using page-level dispatch
    // avoids the WebDriver "element click intercepted" issue when the
    // modal child sits in the default click zone.
    await browser.execute(() => {
      const bd = document.querySelector('.modal-backdrop') as HTMLElement | null;
      bd?.dispatchEvent(new MouseEvent('mousedown', {
        bubbles: true, cancelable: true, clientX: 10, clientY: 10, button: 0,
      }));
    });
    await browser.waitUntil(async () => !(await settingsIsOpen()), { timeout: 2_000 });
  });

  it('ctx menu closes without touching settings', async () => {
    // Regression guard: opening a ctx menu on desktop shouldn't open settings.
    await rightClick('.grid');
    expect(await ctxMenuIsOpen()).to.equal(true);
    expect(await settingsIsOpen()).to.equal(false);
    await browser.keys(['Escape']);
  });
});
