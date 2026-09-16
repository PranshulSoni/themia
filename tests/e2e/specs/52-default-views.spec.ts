/**
 * Default views on a fresh install.
 *
 * Rule: a brand-new install (no `widget-screens` key in localStorage) seeds
 * TWO views, `Home` (id `default`) and `Work` (id `work`), so the
 * multi-view feature is discoverable out of the box. `Home` keeps the legacy
 * seed (folder + clock + help + shortcut); `Work` gets a distinct showcase
 * layout (calendar + todo + notes + clock) and NO folder widget.
 *
 * Existing users who ever touched views have a persisted `widget-screens`
 * key, so `loadList()` returns that and never falls back to the new default.
 */
import { expect } from 'chai';
import { browser, $$ } from '@wdio/globals';
import { attachToAppWindow, waitForReady, clickView } from '../helpers/app.js';

/** Fresh-launch reset: clear ALL localStorage (so the view + layout seeds
 *  actually fire) and reload. */
async function freshLaunch(): Promise<void> {
  await attachToAppWindow();
  await browser.execute(() => {
    try { localStorage.clear(); } catch {}
    location.reload();
  });
  await browser.pause(300);
  await attachToAppWindow();
  await waitForReady();
}

async function viewNames(): Promise<string[]> {
  const names: string[] = [];
  for await (const t of $$('.view-chip .view-name-text')) {
    names.push((await t.getText()).trim());
  }
  return names;
}

/** Count widgets of a given root class inside a view-root by id. */
async function widgetCountOnView(viewId: string, rootClass: string): Promise<number> {
  return browser.execute((id: string, cls: string) => {
    return document.querySelectorAll(`.view-root[data-view-id="${id}"] .${cls}`).length;
  }, viewId, rootClass);
}

describe('default views (fresh install)', () => {
  beforeEach(async () => { await freshLaunch(); });

  it('seeds two views named Home and Work', async () => {
    await browser.waitUntil(async () => (await $$('.view-chip').length) === 2, {
      timeout: 5_000,
      timeoutMsg: 'expected exactly 2 view chips on fresh install',
    });
    expect(await viewNames()).to.have.members(['Home', 'Work']);
  });

  it('Work view renders the showcase widgets (calendar + todo + notes + clock)', async () => {
    await browser.waitUntil(async () => (await $$('.view-chip').length) === 2, { timeout: 5_000 });
    await clickView('Work');

    for (const cls of ['calendar-widget', 'todo-widget', 'notes-widget', 'clock-widget']) {
      await browser.waitUntil(async () => (await widgetCountOnView('work', cls)) >= 1, {
        timeout: 5_000,
        timeoutMsg: `Work view missing a .${cls}`,
      });
    }
  });

  it('folder widget seeds on Home only, not on Work', async () => {
    await browser.waitUntil(async () => (await widgetCountOnView('default', 'folder-view')) === 1, {
      timeout: 5_000,
      timeoutMsg: 'folder widget should seed on the Home (default) view',
    });
    expect(await widgetCountOnView('work', 'folder-view')).to.equal(0);
  });
});
