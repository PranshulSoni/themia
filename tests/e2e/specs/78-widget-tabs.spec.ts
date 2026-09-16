/**
 * Tabs widget: container that holds other widgets and shows ONE at a time via
 * a tab bar (tabs SPEC [S-01]..[S-08], src/SPEC.md [S-19]/[S-21]). A SEPARATE
 * widget from the accordion stack.
 *
 * Covers:
 *  - tab bar renders, first child auto-active, only the active child mounted ([S-02]/[S-04])
 *  - clicking a tab switches the mounted child ([S-02]/[S-04])
 *  - grid renders only top-level widgets ([S-19])
 *  - pop out via the tab context menu ([S-07]); removing the container pops children ([S-21])
 *  - the container is hidden from the picker unless developer mode is on ([S-08])
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, waitForReady, stubConfirm, rightClick, ctxMenuItem,
  openPicker, closePicker, readNote,
} from '../helpers/app.js';

interface SeedChild { id: string; type: string; config?: Record<string, unknown> }

/** Seed a tabs container + parented children in PERCENT shape (parentId passes
 *  through the seed hook untouched; the cell-shape helper in app.ts can't carry
 *  it). */
async function seedTabs(
  cfg: Record<string, unknown>,
  kids: SeedChild[],
): Promise<void> {
  await browser.execute((tabsCfg: Record<string, unknown>, children: SeedChild[]) => {
    const seed = (window as any).__themiaSeedWidgets;
    if (typeof seed !== 'function') throw new Error('__themiaSeedWidgets not exposed');
    seed([
      { id: 'tabs-e2e', type: 'tabs', x: 0.3, y: 0.1, w: 0.3, h: 0.6, config: tabsCfg },
      ...children.map((c, i) => ({
        id: c.id, type: c.type, x: 0.1, y: 0.1 + i * 0.2, w: 0.15, h: 0.15,
        parentId: 'tabs-e2e', config: c.config ?? {},
      })),
    ]);
  }, cfg, kids);
  await browser.pause(100);
  await $('.tabs-widget').waitForDisplayed({ timeout: 3_000 });
}

const gridWidgetCount = () =>
  browser.execute(() => document.querySelectorAll('.grid > .widget').length);

describe('widget: Tabs', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
  });

  it('S-02_tab bar renders with the first child active; only it is mounted', async () => {
    await seedTabs({}, [
      { id: 'clock-e2e', type: 'clock' },
      { id: 'notes-e2e', type: 'notes' },
    ]);
    expect(await gridWidgetCount()).to.equal(1); // children excluded ([S-19])
    await $('.tabs-tabbar').waitForDisplayed({ timeout: 3_000 });
    const tabs = await $$('.tabs-tab');
    expect(tabs.length).to.equal(2);
    expect(await tabs[0].getAttribute('class')).to.include('active');
    // Only the active child's body is mounted ([S-04]).
    expect(await $$('.tabs-child').length).to.equal(1);
  });

  it('S-04_clicking a tab switches the mounted child', async () => {
    await seedTabs({ activeChildId: 'clock-e2e' }, [
      { id: 'clock-e2e', type: 'clock' },
      { id: 'notes-e2e', type: 'notes', config: { content: 'inside tabs' } },
    ]);
    // Notes not mounted while the clock tab is active.
    expect(await $('.tabs-widget .notes-widget__editor').isExisting()).to.equal(false);
    const tabs = await $$('.tabs-tab');
    await tabs[1].click();
    await $('.tabs-widget .notes-widget__editor').waitForDisplayed({ timeout: 3_000 });
    // The editor mounts empty and fills once the note store answers ([S-08]):
    // the legacy inline `content` seeds a real note on first mount ([S-32]), so
    // the text arrives a tick after the element does.
    await browser.waitUntil(
      async () => (await readNote('.tabs-widget .notes-widget__editor')) === 'inside tabs',
      { timeout: 3_000, timeoutMsg: 'the notes child never showed its seeded text' },
    );
    expect(await tabs[1].getAttribute('class')).to.include('active');
  });

  it('S-07_pop out via the tab context menu returns the child to the grid', async () => {
    await seedTabs({}, [
      { id: 'clock-e2e', type: 'clock' },
      { id: 'notes-e2e', type: 'notes' },
    ]);
    expect(await gridWidgetCount()).to.equal(1);
    await rightClick('.tabs-tab');
    const item = await ctxMenuItem('Pop out');
    await item.click();
    await browser.waitUntil(
      async () => (await gridWidgetCount()) === 2,
      { timeout: 3_000, timeoutMsg: 'popped-out child did not appear on the grid' },
    );
    expect(await $$('.tabs-tab').length).to.equal(1);
  });

  it('S-08_tabs is discoverable in the picker with developer mode OFF (shipped GA)', async () => {
    // The pre-release gate was removed when the container shipped: it must show
    // up for a normal user, without touching globalDesign.devMode.
    await openPicker();
    const search = await $('.picker-search');
    await search.setValue('Tabs');
    await browser.pause(150);
    const labels = await browser.execute(() =>
      [...document.querySelectorAll('.widget-card-label')].map(e => e.textContent ?? ''),
    );
    await closePicker();
    expect(labels).to.include('Tabs');
  });

  it('S-21_removing the container pops its children back to the grid', async () => {
    await seedTabs({}, [
      { id: 'clock-e2e', type: 'clock' },
      { id: 'notes-e2e', type: 'notes' },
    ]);
    await stubConfirm(true);
    // Right-click the tab bar band (the active child body would swallow a body click).
    await rightClick('.tabs-tabbar');
    const remove = await ctxMenuItem('Remove widget');
    await remove.click();
    await browser.waitUntil(
      async () => (await gridWidgetCount()) === 2, // clock + notes, container gone
      { timeout: 3_000, timeoutMsg: 'children did not reappear after container removal' },
    );
    expect(await $$('.tabs-widget').length).to.equal(0);
  });
});
