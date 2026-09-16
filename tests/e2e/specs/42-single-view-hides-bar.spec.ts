/**
 * View selector bar visibility rules.
 *
 * Rule: when only one view exists AND we're in compact (non-edit) mode,
 * the `.views-bar` is not rendered, to avoid a lone chip with nothing to
 * switch to. In edit mode the bar is always rendered so the `+` add button
 * stays reachable.
 *
 * A fresh install seeds two views, so these specs explicitly seed a
 * single-view `widget-screens` key to exercise the single-view path.
 */
import { expect } from 'chai';
import { browser, $$ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, leaveEditMode, attachToAppWindow, waitForReady,
} from '../helpers/app.js';

const SCREENS_KEY = 'widget-screens';

async function readViews(): Promise<{ id: string; name: string }[]> {
  return browser.execute((k: string) => {
    try { return JSON.parse(localStorage.getItem(k) ?? '[]'); }
    catch { return []; }
  }, SCREENS_KEY);
}

/** Persist a single-view list and reload, so `loadList()` returns exactly
 *  one view instead of the two-view fresh-install default. */
async function seedSingleView(): Promise<void> {
  await browser.execute((k: string) => {
    localStorage.setItem(k, JSON.stringify([{ id: 'default', name: 'Home' }]));
    location.reload();
  }, SCREENS_KEY);
  await browser.pause(300);
  await attachToAppWindow();
  await waitForReady();
}

async function viewBarPresent(): Promise<boolean> {
  return (await $$('.views-bar').length) > 0;
}

// Add a view by seeding `widget-screens` directly. The add-view UI moved out of
// the view bar into the config window (ConfigTree `.config-tree-add-view`), so
// these visibility specs seed the list + fire a storage event (views.ts reloads
// the list on it) rather than driving the relocated button.
async function addView(name: string): Promise<void> {
  await browser.execute((k: string, viewName: string) => {
    const list = JSON.parse(localStorage.getItem(k) ?? '[]');
    list.push({ id: `view-${viewName.toLowerCase()}`, name: viewName, icon: 'home' });
    const next = JSON.stringify(list);
    localStorage.setItem(k, next);
    window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: next }));
  }, SCREENS_KEY, name);
  await browser.waitUntil(async () => {
    const list = await readViews();
    return list.some(s => s.name === name);
  }, { timeout: 2_000 });
}

describe('view bar single-view visibility', () => {
  beforeEach(async () => {
    await resetAppState();
    await seedSingleView();
  });

  it('hides the view bar when only 1 view exists (non-edit mode)', async () => {
    const list = await readViews();
    expect(list.length).to.equal(1);

    await browser.waitUntil(async () => !(await viewBarPresent()), {
      timeout: 2_000,
      timeoutMsg: '.views-bar should be absent when only 1 view and not in edit mode',
    });
  });

  it('shows the view bar in edit mode even with 1 view', async () => {
    await enterEditMode();
    // Always rendered in edit mode (App.tsx: `editMode() || …`) so view
    // management stays reachable. (The add-view button itself now lives in the
    // config window, not the bar (see addView() above).
    await browser.waitUntil(() => viewBarPresent(), {
      timeout: 2_000,
      timeoutMsg: '.views-bar should appear once edit mode is on',
    });
  });

  it('keeps the view bar visible after adding a 2nd view and leaving edit mode', async () => {
    await enterEditMode();
    await addView('Work');

    await leaveEditMode();

    // With 2 views and non-edit mode the bar stays rendered (compact form).
    await browser.waitUntil(() => viewBarPresent(), {
      timeout: 2_000,
      timeoutMsg: '.views-bar should remain visible when more than 1 view exists',
    });

    const chipCount = await $$('.view-chip').length;
    expect(chipCount).to.equal(2);
  });
});
