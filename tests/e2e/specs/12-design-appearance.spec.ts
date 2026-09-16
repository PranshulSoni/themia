/**
 * Design: global appearance, primary color.
 *
 * All visual design lives in `widget-global-design` and is applied to :root
 * by applyGlobalDesign(). Tests drive primaryColor via direct localStorage
 * writes (the ColorPicker is too UI-heavy to exercise reliably here; see
 * spec 34 for ColorPicker-specific coverage).
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, readCssVar, readDesign, waitForReady,
} from '../helpers/app.js';

/** Write a partial global-design update + fire a storage event so design.ts picks it up. */
async function setGlobalDesignPartial(partial: Record<string, any>): Promise<void> {
  await browser.execute((p: Record<string, any>) => {
    const k = 'widget-global-design';
    const cur = JSON.parse(localStorage.getItem(k) ?? '{}');
    const next = { ...cur, ...p };
    localStorage.setItem(k, JSON.stringify(next));
    window.dispatchEvent(new StorageEvent('storage', {
      key: k, newValue: JSON.stringify(next),
    }));
  }, partial);
}

describe('design: global appearance', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  it('setting global primaryColor updates --primary', async () => {
    await setGlobalDesignPartial({ primaryColor: '#ff00aa' });
    await browser.waitUntil(async () =>
      (await readCssVar('--primary')).toLowerCase().includes('#ff00aa')
      || (await readCssVar('--primary')).toLowerCase().includes('rgb(255'),
      { timeout: 2_000 },
    );
  });

  it('primary color survives reload', async () => {
    await setGlobalDesignPartial({ primaryColor: '#0ea5e9' });
    await browser.refresh();
    await waitForReady();

    const d = await readDesign();
    expect(d.primaryColor).to.equal('#0ea5e9');
  });
});
