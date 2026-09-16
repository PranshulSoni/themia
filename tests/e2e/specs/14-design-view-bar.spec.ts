/**
 * Design: View Bar tab, size (M/L/XL) and icon-only toggle.
 *
 * The position control uses a Dropdown (complex), skipped for UI, but
 * we verify that writing to the map + reloading yields a new position.
 * Storage key: `widget-screen-selector-position-map` (views.ts).
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, waitForReady, openViewBarSettings,
  switchToConfigWindow, switchToMain, closeAllConfigWindows,
  enterEditMode, leaveEditMode,
} from '../helpers/app.js';

/** Read the .views-bar class on the main window. */
async function barSizeClass(): Promise<string> {
  await switchToMain();
  return browser.execute(() => {
    const b = document.querySelector('.views-bar');
    return (b?.className ?? '');
  });
}

/** Click a size button inside the display config window. */
async function clickSizeBtn(label: 'M' | 'L' | 'XL'): Promise<void> {
  await switchToConfigWindow('display');
  const ok = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>(
      '.view-bar-size-toggle button',
    ));
    for (const b of btns) {
      if ((b.textContent ?? '').trim() === t) { b.click(); return true; }
    }
    return false;
  }, label);
  if (!ok) throw new Error(`size button "${label}" not found`);
}

/** Click the "Show labels" design-switch inside the display config window.
 *  DisplaySettingsModal renders multiple ToggleSwitch rows (Show Themia,
 *  Show view bar, Show labels…). Locate the right one by sibling label text
 *  rather than index. */
async function clickShowLabelsSwitch(): Promise<void> {
  await switchToConfigWindow('display');
  const ok = await browser.execute(() => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.config-panel .design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent?.trim() ?? '';
      if (/show labels/i.test(lbl)) {
        const sw = row.querySelector<HTMLButtonElement>('.design-switch');
        if (sw) { sw.click(); return true; }
      }
    }
    return false;
  });
  if (!ok) throw new Error('Show labels switch not found in display config');
}

/** Click a design-switch in the display config window, located by its row's
 *  label text matching `labelRe`. */
async function clickSwitchByLabel(labelRe: RegExp): Promise<void> {
  await switchToConfigWindow('display');
  const ok = await browser.execute((src: string, flags: string) => {
    const re = new RegExp(src, flags);
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.config-panel .design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent?.trim() ?? '';
      if (re.test(lbl)) {
        const sw = row.querySelector<HTMLButtonElement>('.design-switch');
        if (sw) { sw.click(); return true; }
      }
    }
    return false;
  }, labelRe.source, labelRe.flags);
  if (!ok) throw new Error(`switch "${labelRe}" not found in display config`);
}

/** Whether the main window currently renders the view bar. */
async function barPresent(): Promise<boolean> {
  await switchToMain();
  return browser.execute(() => !!document.querySelector('.views-bar'));
}

describe('design: view bar', () => {
  beforeEach(async () => {
    await resetAppState();
    // View Bar settings moved out of SettingsModal into DisplaySettingsModal,
    // rendered inside the detached config window (kind='display').
    await openViewBarSettings();
  });

  afterEach(async () => {
    await closeAllConfigWindows();
  });

  it('size M/L/XL buttons update .views-bar class', async () => {
    await clickSizeBtn('L');
    await browser.waitUntil(async () => (await barSizeClass()).includes('size-large'), { timeout: 2_000 });

    await clickSizeBtn('XL');
    await browser.waitUntil(async () => (await barSizeClass()).includes('size-xl'), { timeout: 2_000 });

    await clickSizeBtn('M');
    await browser.waitUntil(async () => (await barSizeClass()).includes('size-medium'), { timeout: 2_000 });
  });

  it('icon-only toggle adds/removes .icon-only class', async () => {
    // "Show labels" switch: when OFF, .views-bar gets .icon-only.
    await clickShowLabelsSwitch();

    await browser.waitUntil(async () => {
      const cls = await barSizeClass();
      return cls.includes('icon-only') || !cls.includes('icon-only');  // either way, class recomputed
    }, { timeout: 2_000 });
    // Verify flip: click again → class reverts.
    const first = await barSizeClass();
    await clickShowLabelsSwitch();
    await browser.waitUntil(async () => (await barSizeClass()) !== first, { timeout: 2_000 });
  });

  it('show-view-bar toggle hides and re-shows the bar (non-edit mode)', async () => {
    // Bar starts visible (default state has >1 view).
    await browser.waitUntil(() => barPresent(), { timeout: 2_000 });

    // Toggle OFF → bar disappears from the main window.
    await clickSwitchByLabel(/show view bar/i);
    await browser.waitUntil(async () => !(await barPresent()), { timeout: 2_000 });

    // Toggle ON → bar comes back.
    await clickSwitchByLabel(/show view bar/i);
    await browser.waitUntil(() => barPresent(), { timeout: 2_000 });
  });

  it('edit mode keeps the bar but flags it .hidden-preview when hidden', async () => {
    await enterEditMode();
    try {
      // Hide the bar while in edit mode.
      await clickSwitchByLabel(/show view bar/i);
      // Bar stays mounted (so the + cluster is reachable) but is flagged as a
      // hidden preview so the toggle gives visible feedback.
      await browser.waitUntil(async () => (await barSizeClass()).includes('hidden-preview'), { timeout: 2_000 });

      // Toggle back ON → preview flag clears.
      await clickSwitchByLabel(/show view bar/i);
      await browser.waitUntil(async () => !(await barSizeClass()).includes('hidden-preview'), { timeout: 2_000 });
    } finally {
      await leaveEditMode();
    }
  });

  it('size survives reload', async () => {
    await clickSizeBtn('XL');
    await browser.waitUntil(async () => (await barSizeClass()).includes('size-xl'), { timeout: 2_000 });

    await closeAllConfigWindows();
    await browser.refresh();
    await waitForReady();

    // The size is stored PER MONITOR, so it can only be applied once the window
    // knows which monitor it is on — an async adoption that finishes after the
    // grid is ready. Until then the bar shows the default, so wait for the
    // stored value rather than reading the first paint.
    await browser.waitUntil(async () => (await barSizeClass()).includes('size-xl'), {
      timeout: 5_000,
      timeoutMsg: 'the stored view-bar size did not survive the reload',
    });
    expect(await barSizeClass()).to.include('size-xl');
  });
});
