/**
 * Per-widget error isolation (1.17).
 *
 * A widget that throws during render must degrade to a single dead tile; it
 * must NOT take down the rest of the grid. We force a render throw via the
 * webdriver-only `__crashTest` config seam (WidgetGrid.tsx) and assert that a
 * second, healthy widget on the same view still renders.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

async function seedTwo(): Promise<void> {
  await browser.execute((key: string) => {
    const layout = {
      widgets: [
        // Forced to throw during render (webdriver + __crashTest).
        { id: 'crash-1', type: 'countdown', col: 1, row: 1, colSpan: 2, rowSpan: 2,
          config: { __crashTest: true, timers: [], hideHeader: false, label: '' } },
        // Healthy neighbour: must still render despite the crash above.
        { id: 'ok-1', type: 'countdown', col: 3, row: 1, colSpan: 2, rowSpan: 2,
          config: { timers: [], hideHeader: false, label: '' } },
      ],
      widgetGap: 12,
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot);
  await $('.widget').waitForExist({ timeout: 5_000 });
}

describe('widget: per-widget error boundary', () => {
  beforeEach(async () => { await resetAppState(); });

  it('a throwing widget degrades to one dead tile; the grid survives', async () => {
    await seedTwo();

    // The crashing widget shows the error fallback...
    await $('.widget-error').waitForExist({ timeout: 5_000 });
    const errorTiles = await $$('.widget-error');
    expect(errorTiles.length).to.equal(1);

    // ...and the healthy neighbour still renders its real content.
    await $('.countdown-widget').waitForExist({ timeout: 5_000 });
    const healthy = await $$('.countdown-widget');
    expect(healthy.length).to.equal(1);

    // Both tiles exist: the grid kept rendering instead of collapsing.
    const tiles = await $$('.widget');
    expect(tiles.length).to.equal(2);
  });
});
