/**
 * View Switcher widget: switch active view, filter via config.
 * No header, no add button (removed); a bare segmented switcher.
 *
 * Behaviors covered:
 *  [SS-01] Default config shows every view in the store.
 *  [SS-02] visibleViews filters which views render.
 *  [SS-03] Clicking a view button calls switchView and flips activeId.
 *  [SS-04] Active view's button has the `.active` modifier.
 *  [SS-07] iconOnly=true hides view-name labels.
 *  [SS-08] "Configure…" opens the widget's own config window, not the view's,
 *          and does NOT switch view.
 *  [SS-09] Widget config picks visible views with the shared MultiSelectDropdown
 *          (same control as the Display settings page).
 *  [SS-10] The widget never renders an add button.
 *  [SS-11] visibleScreens renders in global view order (so config reorder flows
 *          through), regardless of the stored array order.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, LS_KEYS, rightClick, ctxMenuItem, findConfigHandle,
  switchToMain, waitForConfigWindow,
} from '../helpers/app.js';

const SCREENS_KEY = 'widget-screens';

interface ViewRow { id: string; name: string; icon?: string }

async function seedViewsAndWidget(
  views: ViewRow[],
  widgetConfig: Record<string, any> = {},
): Promise<void> {
  // Views store is loaded once at module init from localStorage, and the
  // first view with id 'default' is activated by default. Seed BOTH the
  // views list and the widget layout, then reload so the store rebuilds.
  await browser.execute(
    (viewsKey: string, layoutKey: string, list: ViewRow[], cfg: Record<string, any>) => {
      localStorage.setItem(viewsKey, JSON.stringify(list));
      const layout = {
        widgets: [{
          id: 'view-switcher-1', type: 'viewSwitcher',
          col: 1, row: 1, colSpan: 4, rowSpan: 2,
          config: {
            visibleScreens: [], iconOnly: false, ...cfg,
          },
        }],
        widgetGap: 12, bgImage: '', bgImageFit: 'cover',
      };
      localStorage.setItem(layoutKey, JSON.stringify(layout));
      location.reload();
    },
    SCREENS_KEY, LS_KEYS.layoutRoot, views, widgetConfig,
  );
  await browser.pause(400);
  await $('.grid').waitForExist({ timeout: 10_000 });
  await $('.view-switcher-widget').waitForExist({ timeout: 5_000 });
}

async function buttonViewIds(): Promise<string[]> {
  return browser.execute(() => Array.from(
    document.querySelectorAll<HTMLElement>('.view-switcher-btn[data-view-id]'),
  ).map(b => b.dataset.viewId ?? ''));
}

describe('widget: View Switcher', () => {
  beforeEach(async () => { await resetAppState(); });

  it('[SS-01] shows every view by default', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
      { id: 'view-b', name: 'Home' },
    ]);
    const ids = await buttonViewIds();
    expect(ids).to.deep.equal(['default', 'view-a', 'view-b']);
  });

  it('[SS-02] visibleScreens filters the rendered list', async () => {
    await seedViewsAndWidget(
      [
        { id: 'default', name: 'Default' },
        { id: 'view-a', name: 'Work' },
        { id: 'view-b', name: 'Home' },
      ],
      { visibleScreens: ['view-b', 'default'] },
    );
    // Filtered to {default, view-b} but rendered in global view order.
    const ids = await buttonViewIds();
    expect(ids).to.deep.equal(['default', 'view-b']);
  });

  it('[SS-03] clicking a view button switches activeId', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
    ]);
    await browser.execute(() => {
      const btn = document.querySelector<HTMLElement>(
        '.view-switcher-btn[data-view-id="view-a"]',
      );
      btn?.click();
    });
    // After the switch, the default view (which hosts this widget) unmounts;
    // only the active view's ViewRoot is rendered. Asserting on the .active
    // DOM class would race the unmount. Read the persisted monitor→view map
    // instead, which switchView writes synchronously.
    await browser.waitUntil(async () => {
      return await browser.execute(() => {
        try {
          const map = JSON.parse(localStorage.getItem('widget-monitor-screen-map') ?? '{}');
          return Object.values(map).includes('view-a');
        } catch { return false; }
      });
    }, { timeout: 3_000, timeoutMsg: 'monitor-view-map never moved to view-a' });
  });

  it('[SS-04] active view button has the .active modifier', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
    ]);
    const activeId = await browser.execute(() => {
      const el = document.querySelector<HTMLElement>('.view-switcher-btn.active');
      return el?.dataset.viewId ?? '';
    });
    expect(activeId).to.equal('default');
  });

  it('[SS-10] never renders an add button', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
    ]);
    expect(await $$('.view-switcher-add').length).to.equal(0);
    expect(await $$('.view-switcher-btn[data-action="add-view"]').length).to.equal(0);
  });

  it('[SS-08] Configure opens the widget config window without switching', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
    ]);

    // Right-click the NON-active view, then choose Configure.
    await rightClick('.view-switcher-btn[data-view-id="view-a"]');
    await (await ctxMenuItem('Configure…')).click();

    // The widget's OWN config window (target prefix `widget:`) must appear;
    // Configure targets the screen switcher, not the view.
    let handle: string | null = null;
    await browser.waitUntil(async () => {
      handle = await findConfigHandle('widget');
      return !!handle;
    }, { timeout: 5_000, timeoutMsg: 'widget config window never opened' });

    // The active view must NOT have changed: Configure configures, it does
    // not switch. The widget is still mounted on the (still-active) default.
    await switchToMain();
    const activeId = await browser.execute(() => {
      const el = document.querySelector<HTMLElement>('.view-switcher-btn.active');
      return el?.dataset.viewId ?? '';
    });
    expect(activeId).to.equal('default');
  });

  it('[SS-09] widget config uses the shared visible-views dropdown', async () => {
    await seedViewsAndWidget([
      { id: 'default', name: 'Default' },
      { id: 'view-a', name: 'Work' },
      { id: 'view-b', name: 'Home' },
    ]);

    await rightClick('.view-switcher-btn[data-view-id="view-a"]');
    await (await ctxMenuItem('Configure…')).click();
    await waitForConfigWindow('widget');

    // The Views tab renders the same MultiSelectDropdown the Display page uses.
    const trigger = await $('.msd-trigger');
    await trigger.waitForExist({ timeout: 5_000 });

    // Opening it lists every view as a selectable row.
    await trigger.click();
    await $('.msd-panel').waitForDisplayed({ timeout: 2_000 });
    expect(await $$('.msd-panel .msd-row').length).to.equal(3);
  });

  it('[SS-11] renders in global view order, not stored-array order', async () => {
    await seedViewsAndWidget(
      [
        { id: 'default', name: 'Default' },
        { id: 'view-a', name: 'Work' },
        { id: 'view-b', name: 'Home' },
      ],
      // Stored whitelist in reverse order; render must still follow the
      // global view order so drag-reorder in the config flows through.
      { visibleScreens: ['view-b', 'view-a', 'default'] },
    );
    const ids = await buttonViewIds();
    expect(ids).to.deep.equal(['default', 'view-a', 'view-b']);
  });

  it('[SS-07] iconOnly=true hides the view-name labels', async () => {
    await seedViewsAndWidget(
      [
        { id: 'default', name: 'Default' },
        { id: 'view-a', name: 'Work' },
      ],
      { iconOnly: true },
    );
    const root = await $('.view-switcher-widget');
    const cls = await root.getAttribute('class');
    expect(cls).to.match(/icon-only/);
    expect(await $$('.view-switcher-widget .view-switcher-name').length).to.equal(0);
  });
});
