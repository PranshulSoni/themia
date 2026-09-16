/**
 * Context-menu "Refresh" sweep [S-20].
 *
 * A whole class of widgets grew a dead "Refresh" context action: the item was
 * present but the widget never tracked the `_refresh` config token it bumped,
 * so nothing re-fetched. The fixes wire each widget through
 * `createManualRefresh(() => props._refresh, ...)`. That token->refetch
 * contract is pinned by the unit test
 * widgets/office/calendar/manualRefresh.test.ts; this spec pins the other half
 * of the chain end-to-end in the real app:
 *
 *  1. every refreshable widget's context menu actually renders its Refresh item
 *     (guards the "action silently dropped from the menu" regression), and
 *  2. clicking it bumps the widget's persisted `_refresh` token (guards the
 *     contextAction -> setConfig -> updateWidgetConfig wiring).
 *
 * We drive the menu with a synthetic `contextmenu` event (as 04-ctx-menu does)
 * to bypass the webdriver right-click quirks that intermittently swallow the
 * event when a detached window from a prior spec held OS focus. No accounts or
 * network are needed: an unconfigured widget still mounts a `.widget` wrapper
 * whose onContextMenu builds the menu from the registry contextActions.
 *
 * NOTE: `type` is the REGISTRY KEY, which is the imported variable name in
 * src/widgets/registry.tsx (camelCase: weatherCurrent, webElement), NOT the
 * kebab folder name — seeding an unknown type renders no widget. Gallery labels
 * its action "Refresh images"; the rest use the shared "Refresh".
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, seedWidgets, switchToMain, ctxMenuItem, waitForPersist,
} from '../helpers/app.js';

// Widgets whose registry def exposes a manual-refresh context action AND that
// render without an account/provider connection, so they mount a plain
// `.widget` under webdriver. Account-gated widgets (calendar, contacts, email,
// onedrive, todo, github*) share the identical contextActions shape and the
// same createManualRefresh wiring, and are exercised by their own specs.
const REFRESH_WIDGETS: Array<{ type: string; refreshLabel: string }> = [
  { type: 'stocks',          refreshLabel: 'Refresh' },
  { type: 'rss',             refreshLabel: 'Refresh' },
  { type: 'weatherCurrent',  refreshLabel: 'Refresh' },
  { type: 'weatherForecast', refreshLabel: 'Refresh' },
  { type: 'webElement',      refreshLabel: 'Refresh' },
  { type: 'apps',            refreshLabel: 'Refresh' },
  { type: 'gallery',         refreshLabel: 'Refresh images' },
  { type: 'bluetooth',       refreshLabel: 'Refresh' },
];

/** Fire a synthetic contextmenu at the centre of the (single) seeded widget
 *  and wait for the menu. Mirrors 04-ctx-menu's fireGridContextMenu. */
async function fireWidgetCtx(): Promise<void> {
  await switchToMain();
  await browser.execute(() => {
    const w = document.querySelector('.grid > .widget') as HTMLElement | null;
    if (!w) throw new Error('.widget missing');
    const r = w.getBoundingClientRect();
    const ev = new MouseEvent('contextmenu', {
      bubbles: true, cancelable: true, view: window,
      button: 2, buttons: 2,
      clientX: r.left + r.width / 2, clientY: r.top + r.height / 2,
    });
    w.dispatchEvent(ev);
  });
  await $('.ctx-menu').waitForDisplayed({ timeout: 3_000 });
}

describe('context-menu Refresh sweep [S-20]', () => {
  for (const w of REFRESH_WIDGETS) {
    it(`${w.type}: widget context menu renders its refresh item`, async () => {
      await resetAppState();
      await seedWidgets([{ type: w.type, col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
      // Cold-boot on WebView2 can be slow; give the first paint room.
      await $('.grid > .widget').waitForExist({ timeout: 8_000 });
      await fireWidgetCtx();
      // ctxMenuItem throws (listing what it did find) if the label is absent.
      const item = await ctxMenuItem(w.refreshLabel);
      expect(await item.isExisting()).to.equal(true);
      // Close the menu so it can't leak into the next iteration.
      await browser.keys(['Escape']);
    });
  }

  it('stocks: clicking Refresh bumps the persisted _refresh token', async () => {
    await resetAppState();
    await seedWidgets([{
      type: 'stocks', id: 'stocks-refresh', col: 2, row: 2, colSpan: 3, rowSpan: 2,
    }]);
    await $('.grid > .widget').waitForExist({ timeout: 8_000 });

    const readToken = () => browser.execute(() => {
      try {
        const raw = localStorage.getItem('widget-layout') ?? '{}';
        const w = (JSON.parse(raw).widgets ?? [])
          .find((x: { id?: string }) => x.id === 'stocks-refresh');
        return (w?.config?._refresh ?? null) as number | null;
      } catch { return null; }
    });

    await fireWidgetCtx();
    const item = await ctxMenuItem('Refresh');
    await item.click();
    await waitForPersist();

    const token = await readToken();
    expect(token, 'expected _refresh to be written to the stocks widget config')
      .to.be.a('number');
  });
});
