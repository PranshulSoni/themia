/**
 * Demo mode: "Demo mode" switch on Settings > General.
 *
 * When turned on:
 *  - designState.demoMode persists in localStorage `widget-global-design`
 *  - App.tsx reloads (setTimeout → window.location.reload after ~50ms)
 *  - On reload, injectDemoViews() writes 4 demo views with id prefix
 *    `demo-` into localStorage `widget-screens` and their per-view
 *    layouts under `widget-layout-demo-<name>`.
 *  - Turning it off calls removeDemoViews() before flipping the flag,
 *    then reloads; demo views should be gone.
 *
 * The demo-view ids are well-known constants: demo-office, demo-online,
 * demo-system, demo-utilities (see src/demo/views.ts DEMO_SCREENS).
 *
 * Note: the switch is on the General tab under the "Demo" section. There
 * are several .design-switch buttons on that tab; we click the one inside
 * the row whose label text is "Demo mode".
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
  attachToAppWindow, waitForReady, readDesign, enableDevMode,
} from '../helpers/app.js';

async function readViews(): Promise<Array<{ id: string; name: string }>> {
  return browser.execute(() => {
    try { return JSON.parse(localStorage.getItem('widget-screens') ?? '[]'); }
    catch { return []; }
  });
}

async function clickDemoModeSwitch(): Promise<void> {
  const ok = await browser.execute(() => {
    // Find the design-row whose label is "Demo mode", then click its switch.
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.config-panel .design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent?.trim();
      if (lbl === 'Demo mode') {
        const sw = row.querySelector<HTMLButtonElement>('.design-switch');
        if (sw) { sw.click(); return true; }
      }
    }
    return false;
  });
  if (!ok) throw new Error('Demo mode switch not found');
}

describe('demo mode', () => {
  beforeEach(async () => {
    await resetAppState();
    // Demo mode switch is only shown when devMode is on.
    await enableDevMode();
    await openSettings();
    await (await settingsTab('General')).click();
  });

  afterEach(async () => {
    // Settings modal may be gone (after reload). Close is best-effort.
    try { await closeSettings(); } catch {}
  });

  it('enabling demo mode injects the demo views', async () => {
    // Precondition: no demo views.
    const pre = await readViews();
    expect(pre.some(s => s.id.startsWith('demo-'))).to.equal(false);

    await clickDemoModeSwitch();

    // Toggle triggers a reload (~50ms later). Wait for the new window.
    await browser.pause(200);
    await attachToAppWindow();
    await waitForReady();

    const design = await readDesign();
    expect(design.demoMode).to.equal(true);

    const post = await readViews();
    const demoIds = post.map(s => s.id).filter(id => id.startsWith('demo-'));
    // Four demo views (see DEMO_SCREENS in src/demo/views.ts).
    expect(demoIds).to.include('demo-office');
    expect(demoIds).to.include('demo-online');
    expect(demoIds).to.include('demo-system');
    expect(demoIds).to.include('demo-utilities');
  });

  it('disabling demo mode removes the demo views', async () => {
    // Turn on.
    await clickDemoModeSwitch();
    await browser.pause(200);
    await attachToAppWindow();
    await waitForReady();

    let post = await readViews();
    expect(post.some(s => s.id.startsWith('demo-'))).to.equal(true);

    // Re-open settings and flip it off. devMode survived the reload, but
    // ensure it's on anyway (localStorage write is idempotent).
    await enableDevMode();
    await openSettings();
    await (await settingsTab('General')).click();
    await clickDemoModeSwitch();
    await browser.pause(200);
    await attachToAppWindow();
    await waitForReady();

    const design = await readDesign();
    expect(design.demoMode).to.equal(false);

    post = await readViews();
    expect(post.some(s => s.id.startsWith('demo-'))).to.equal(false);
    // Always at least one non-demo view.
    expect(post.length).to.be.greaterThan(0);
  });

  it('demo layouts contain only registered widget types (no disabled refs)', async () => {
    // A1: demo views must not persist widget types that aren't registered;
    // those would render as blank tiles. injectDemoViews filters them out.
    // (`ping` is now a registered widget so it may legitimately appear; only
    // unregistered types like `bookmarks` must be filtered.)
    await clickDemoModeSwitch();
    await browser.pause(200);
    await attachToAppWindow();
    await waitForReady();

    const types = await browser.execute(() => {
      const keys = [
        'widget-layout-demo-office', 'widget-layout-demo-online',
        'widget-layout-demo-system', 'widget-layout-demo-utilities',
      ];
      const out: string[] = [];
      for (const k of keys) {
        try {
          const l = JSON.parse(localStorage.getItem(k) ?? '{}');
          for (const w of (l.widgets ?? [])) out.push(w.type);
        } catch { /* ignore */ }
      }
      return out;
    });

    expect(types.length).to.be.greaterThan(0);
    expect(types).to.not.include('bookmarks');
  });

  it('demo-office layout contains the expected widget types', async () => {
    await clickDemoModeSwitch();
    await browser.pause(200);
    await attachToAppWindow();
    await waitForReady();

    const layout = await browser.execute(() => {
      try { return JSON.parse(localStorage.getItem('widget-layout-demo-office') ?? '{}'); }
      catch { return {}; }
    });
    const types: string[] = (layout.widgets ?? []).map((w: any) => w.type);
    expect(types).to.include('folder');
    expect(types).to.include('email');
    expect(types).to.include('contacts');
  });
});
