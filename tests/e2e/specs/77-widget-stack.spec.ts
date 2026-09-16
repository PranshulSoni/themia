/**
 * Stack widget: container that holds other widgets (stack SPEC [S-01]..[S-09],
 * src/SPEC.md [S-19]/[S-21]).
 *
 * Covers:
 *  - a filled stack shows TITLE ROWS only; clicking one opens that child and
 *    closes whichever was open -- at most ONE is ever open ([S-03])
 *  - a CLOSED child is not mounted at all; the stack draws its own row, so a
 *    child's size-responsive CSS can never blank it out ([S-04]/[S-05])
 *  - an OPEN child renders headerless BELOW its row, which never changes ([S-04])
 *  - grid renders only top-level widgets ([S-19])
 *  - pop out via row context menu ([S-08]); removing the stack pops children ([S-21])
 *  - an EMPTY stack stays visible at rest and offers the creation path ([S-07]/[S-13])
 *  - the stack is discoverable in the picker without developer mode ([S-10])
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, waitForReady, stubConfirm, rightClick, ctxMenuItem,
  openPicker, closePicker, readNote, enterEditMode, leaveEditMode, ctxMenuIsOpen,
} from '../helpers/app.js';

interface SeedChild { id: string; type: string; config?: Record<string, unknown> }

/** Seed a stack + parented children in PERCENT shape (parentId passes through
 *  the seed hook untouched; the cell-shape helper in app.ts can't carry it). */
async function seedStack(
  cfg: Record<string, unknown>,
  kids: SeedChild[],
): Promise<void> {
  await browser.execute((stackCfg: Record<string, unknown>, children: SeedChild[]) => {
    const seed = (window as any).__themiaSeedWidgets;
    if (typeof seed !== 'function') throw new Error('__themiaSeedWidgets not exposed');
    seed([
      { id: 'stack-e2e', type: 'stack', x: 0.3, y: 0.1, w: 0.3, h: 0.6, config: stackCfg },
      ...children.map((c, i) => ({
        id: c.id, type: c.type, x: 0.1, y: 0.1 + i * 0.2, w: 0.15, h: 0.15,
        parentId: 'stack-e2e', config: c.config ?? {},
      })),
    ]);
  }, cfg, kids);
  await browser.pause(100);
  await $('.stack-widget').waitForDisplayed({ timeout: 3_000 });
}

const gridWidgetCount = () =>
  browser.execute(() => document.querySelectorAll('.grid > .widget').length);

const sectionHeights = () => browser.execute(() =>
  [...document.querySelectorAll('.stack-section')].map(s =>
    Math.round(s.getBoundingClientRect().height)),
);

describe('widget: Stack', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
  });

  it('S-03_a filled stack shows TITLE ROWS only by default', async () => {
    await seedStack({}, [
      { id: 'notes-e2e', type: 'notes', config: { content: 'a' } },
      { id: 'folder-e2e', type: 'folder', config: { path: '', viewMode: 'list' } },
      { id: 'scratch-e2e', type: 'scratchpad' },
    ]);
    expect(await gridWidgetCount()).to.equal(1); // children excluded ([S-19])
    const heights = await sectionHeights();
    expect(heights.length).to.equal(3);
    // Every section is a header band, nothing opened.
    for (const h of heights) {
      expect(h, `band visible: ${heights}`).to.be.greaterThan(10);
      expect(h, `band only: ${heights}`).to.be.lessThan(60);
    }
    // Every row is the stack's OWN row: no child is mounted while closed, so
    // no child can render a blank band from its own size-responsive CSS.
    expect(await $$('.stack-widget .stack-row').length).to.equal(3);
    const titles = await browser.execute(() =>
      [...document.querySelectorAll('.stack-widget .stack-row-title')]
        .map(e => (e as HTMLElement).innerText.trim()),
    );
    expect(titles).to.deep.equal(['Notes', 'Folder', 'Scratchpad']);
    expect(await $$('.stack-widget .notes-widget').length).to.equal(0);
  });

  it('S-03_clicking a title row opens that child, and only that child', async () => {
    await seedStack({}, [
      { id: 'notes-e2e', type: 'notes', config: { content: 'inside stack' } },
      { id: 'scratch-e2e', type: 'scratchpad' },
    ]);
    // Closed -> the stack's own row is the only thing rendered for notes.
    const rows = await $$('.stack-widget .stack-row');
    await rows[0].click();
    await $('.stack-widget .notes-widget__editor').waitForDisplayed({ timeout: 3_000 });
    // The editor mounts empty and fills once the note store answers ([S-08]):
    // the legacy inline `content` seeds a real note on first mount ([S-32]), so
    // the text arrives a tick after the element does.
    await browser.waitUntil(
      async () => (await readNote('.stack-widget .notes-widget__editor')) === 'inside stack',
      { timeout: 3_000, timeoutMsg: 'the notes child never showed its seeded text' },
    );

    let heights = await sectionHeights();
    expect(heights[0], `notes opened: ${heights}`).to.be.greaterThan(60);
    expect(heights[1], `scratchpad still closed: ${heights}`).to.be.lessThan(60);

    // The row does not change when the child opens ([S-04]): the same band
    // closes it again.
    await (await $$('.stack-widget .stack-row'))[0].click();
    await browser.waitUntil(
      async () => !(await $('.stack-widget .notes-widget__editor').isDisplayed()),
      { timeout: 3_000, timeoutMsg: 'notes did not roll up' },
    );
    heights = await sectionHeights();
    for (const h of heights) expect(h, `all closed again: ${heights}`).to.be.lessThan(60);
  });

  it('S-03_opening a second child closes the first (only one is ever open)', async () => {
    await seedStack({}, [
      { id: 'notes-e2e', type: 'notes', config: { content: 'first' } },
      { id: 'scratch-e2e', type: 'scratchpad' },
    ]);
    await (await $$('.stack-widget .stack-row'))[0].click();
    await $('.stack-widget .notes-widget__editor').waitForDisplayed({ timeout: 3_000 });

    // Every child keeps its row ([S-04]), so row 1 is still scratchpad's;
    // clicking it must hand the space over, not split it.
    await (await $$('.stack-widget .stack-row'))[1].click();
    await browser.waitUntil(
      async () => (await $$('.stack-widget .notes-widget').length) === 0,
      { timeout: 3_000, timeoutMsg: 'first child stayed open' },
    );
    const heights = await sectionHeights();
    expect(heights[0], `notes closed: ${heights}`).to.be.lessThan(60);
    expect(heights[1], `scratchpad opened: ${heights}`).to.be.greaterThan(60);
    expect(await $$('.stack-section.active').length).to.equal(1);
  });

  it('S-04_a closed child is not mounted, and its row is never blank', async () => {
    // Regression: the closed row used to be the CHILD's live header, clipped to
    // 40px -- which trips a child's own size-responsive CSS (weather-current
    // hides its header under `@container (max-height: 80px)`) and rendered an
    // empty band. weatherCurrent is in this seed for exactly that reason.
    await seedStack(
      { expandedId: null },
      [
        { id: 'notes-e2e', type: 'notes' },
        { id: 'gh-e2e', type: 'githubNotifications' },
        { id: 'weather-e2e', type: 'weatherCurrent' },
        { id: 'clock-e2e', type: 'clock' },
      ],
    );
    const heights = await sectionHeights();
    expect(heights.length).to.equal(4);
    for (const h of heights) {
      expect(h, `band visible: ${heights}`).to.be.greaterThan(10);
      expect(h, `band only: ${heights}`).to.be.lessThan(60);
    }
    // One row per child, and every one of them carries visible title text.
    expect(await $$('.stack-widget .stack-row').length).to.equal(4);
    const titles = await browser.execute(() =>
      [...document.querySelectorAll('.stack-widget .stack-row-title')]
        .map(e => (e as HTMLElement).innerText.trim()),
    );
    expect(titles.length).to.equal(4);
    for (const tx of titles) expect(tx, `row not blank: ${JSON.stringify(titles)}`).to.not.equal('');
    // No child component is mounted at all while closed.
    expect(await $$('.stack-widget .notes-widget').length).to.equal(0);
    expect(await $$('.stack-widget .weather-current-widget').length).to.equal(0);
  });

  it('S-04_an OPEN child keeps the same 40px row and renders no header of its own', async () => {
    // Regression: the open child used to supply its own header AS the row. A
    // `compact` header (weather) is a 21px uppercase label with no icon and no
    // chevron, so opening such a child made the row the user just clicked
    // visibly disappear. The row is the STACK's chrome and never changes.
    await seedStack({ expandedId: 'weather-e2e' }, [
      { id: 'weather-e2e', type: 'weatherCurrent' },
      { id: 'notes-e2e', type: 'notes' },
    ]);
    const rows = await browser.execute(() =>
      [...document.querySelectorAll('.stack-widget .stack-row')].map(r => ({
        h: Math.round(r.getBoundingClientRect().height),
        text: (r as HTMLElement).innerText.trim(),
        chevron: !!r.querySelector('.stack-row-chevron'),
      })),
    );
    expect(rows.length).to.equal(2);
    for (const r of rows) {
      expect(r.h, `row keeps the band height: ${JSON.stringify(rows)}`).to.be.within(36, 44);
      expect(r.text, `row keeps its title: ${JSON.stringify(rows)}`).to.not.equal('');
      expect(r.chevron).to.equal(true);
    }
    // The open child is mounted, and brings no header of its own.
    expect(await $$('.stack-widget .weather-current-widget').length).to.equal(1);
    expect(await $$('.stack-widget .stack-child .widget-header').length).to.equal(0);
  });

  it('S-08_pop out via the row context menu returns the child to the grid', async () => {
    await seedStack({}, [
      { id: 'clock-e2e', type: 'clock' },
      { id: 'notes-e2e', type: 'notes' },
    ]);
    expect(await gridWidgetCount()).to.equal(1);
    // Right-click the notes row (second child); closed children render only
    // the stack's own row, so that is the menu's anchor.
    await rightClick('.stack-widget .stack-section:last-child .stack-row');
    const item = await ctxMenuItem('Pop out');
    await item.click();
    await browser.waitUntil(
      async () => (await gridWidgetCount()) === 2,
      { timeout: 3_000, timeoutMsg: 'popped-out child did not appear on the grid' },
    );
    expect(await $$('.stack-section').length).to.equal(1);
  });

  it('S-07_an empty stack stays visible at rest, and offers Add widget in both modes', async () => {
    await seedStack({}, []);
    // Not in edit mode: the placeholder and its button are both there, so a
    // freshly added stack is never an invisible box. Only the drag hint, an
    // edit-mode affordance, is withheld.
    await $('.stack-empty').waitForDisplayed({ timeout: 3_000 });
    expect(await $('.stack-empty-add').isDisplayed()).to.equal(true);
    expect(await $$('.stack-empty-hint').length).to.equal(0);

    await enterEditMode();
    await $('.stack-empty-hint').waitForDisplayed({ timeout: 3_000 });
    expect(await $('.stack-empty-add').isDisplayed()).to.equal(true);

    await leaveEditMode();
    await browser.waitUntil(
      async () => (await $$('.stack-empty-hint').length) === 0,
      { timeout: 3_000, timeoutMsg: 'drag hint survived leaving edit mode' },
    );
    expect(await $('.stack-empty-add').isDisplayed()).to.equal(true);
  });

  it('S-13_the stack context menu leads with "Add widget inside"', async () => {
    await seedStack({}, []);
    await enterEditMode();
    await rightClick('.stack-widget');
    expect(await ctxMenuIsOpen()).to.equal(true);
    const item = await ctxMenuItem('Add widget inside');
    expect(await item.isDisplayed()).to.equal(true);
    await browser.keys(['Escape']);
    await leaveEditMode();
  });

  it('S-10_stack is discoverable in the picker with developer mode OFF (shipped GA)', async () => {
    // The pre-release gate was removed when the stack shipped: it must show up
    // for a normal user, without touching globalDesign.devMode.
    await openPicker();
    const search = await $('.picker-search');
    await search.setValue('Stack');
    await browser.pause(150);
    const labels = await browser.execute(() =>
      [...document.querySelectorAll('.widget-card-label')].map(e => e.textContent ?? ''),
    );
    await closePicker();
    expect(labels).to.include('Stack');
  });

  it('S-21_removing the stack pops its children back to the grid', async () => {
    // Fully rolled up so the stack's own (empty) area is hit by the
    // right-click; an expanded child body would swallow it.
    await seedStack(
      { expandedId: null },
      [
        { id: 'clock-e2e', type: 'clock' },
        { id: 'notes-e2e', type: 'notes' },
      ],
    );
    await stubConfirm(true);
    await rightClick('.stack-widget');
    const remove = await ctxMenuItem('Remove widget');
    await remove.click();
    await browser.waitUntil(
      async () => (await gridWidgetCount()) === 2, // clock + notes, stack gone
      { timeout: 3_000, timeoutMsg: 'children did not reappear after stack removal' },
    );
    expect(await $$('.stack-widget').length).to.equal(0);
  });
});
