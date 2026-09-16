import { expect } from 'chai';
import { browser, $$ } from '@wdio/globals';
import { resetAppState, activeViewName, clickView } from '../helpers/app.js';

/**
 * Multi-view tests require Pro. `initLicense()` auto-promotes WebDriver
 * sessions to Pro via `navigator.webdriver` so these run unconditionally
 * in the e2e suite.
 *
 * A fresh install seeds two views, `Home` (id `default`) and `Work`
 * (id `work`), so these switching tests use the built-in pair directly.
 */
describe('view switching', () => {
  beforeEach(async () => { await resetAppState(); });

  it('ctrl+1 .. ctrl+N switches views', async () => {
    // Fresh install already has 2 views: Home (Ctrl+1) + Work (Ctrl+2).
    await browser.waitUntil(async () => (await $$('.view-chip').length) === 2, {
      timeout: 5_000,
      timeoutMsg: 'expected 2 default views',
    });

    await browser.keys(['Control', '2']);
    expect(await activeViewName()).to.equal('Work');

    await browser.keys(['Control', '1']);
    expect(await activeViewName()).to.equal('Home');
  });

  it('clicking a chip switches active view', async () => {
    await browser.waitUntil(async () => (await $$('.view-chip').length) === 2, { timeout: 5_000 });

    await clickView('Work');
    expect(await activeViewName()).to.equal('Work');

    await clickView('Home');
    expect(await activeViewName()).to.equal('Home');
  });

  it('ctrl+N switches to a newly added third view', async () => {
    // The add-view UI moved out of the view bar into the config window, so seed
    // a third view directly; this stays a focused test of the Ctrl+3 switch.
    // (Home + Work are the fresh-install defaults; append Side as the 3rd.)
    await browser.execute((k: string) => {
      const existing = JSON.parse(localStorage.getItem(k) ?? '[]');
      const base = existing.length ? existing : [
        { id: 'default', name: 'Home', icon: 'home' },
        { id: 'work', name: 'Work', icon: 'home' },
      ];
      base.push({ id: 'side', name: 'Side', icon: 'home' });
      const next = JSON.stringify(base);
      localStorage.setItem(k, next);
      window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: next }));
    }, 'widget-screens');

    await browser.waitUntil(async () => (await $$('.view-chip').length) === 3, {
      timeout: 3_000, timeoutMsg: 'expected 3 views after seeding the third',
    });

    // Now 3 views: Home + Work + Side. Ctrl+3 = Side.
    await browser.keys(['Control', '3']);
    expect(await activeViewName()).to.equal('Side');
  });
});
