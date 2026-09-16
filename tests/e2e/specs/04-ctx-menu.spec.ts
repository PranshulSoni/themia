import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, ctxMenuItem, ctxMenuIsOpen, pickerIsOpen, isEditMode, enterEditMode, switchToMain, pickWidget, clickGridCell, rightClick, seedWidgets } from '../helpers/app.js';

/**
 * The window-level contextmenu handler in App.tsx only renders the desktop
 * ctx menu when editMode is true. We enter edit mode before each test, then
 * dispatch a synthetic contextmenu event from JS, bypassing OS focus and
 * webdriver right-click quirks that intermittently swallow the event when a
 * detached config window from a prior spec held focus.
 */
async function fireGridContextMenu(): Promise<void> {
  await switchToMain();
  await browser.execute(() => {
    const g = document.querySelector('.grid') as HTMLElement | null;
    if (!g) throw new Error('.grid missing');
    const r = g.getBoundingClientRect();
    const ev = new MouseEvent('contextmenu', {
      bubbles: true, cancelable: true, view: window,
      button: 2, buttons: 2,
      clientX: r.left + r.width / 2, clientY: r.top + r.height / 2,
    });
    g.dispatchEvent(ev);
  });
  await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
}

describe('context menu', () => {
  beforeEach(async () => { await resetAppState(); });

  it('desktop right-click → Leave Edit Mode toggles edit mode off', async () => {
    await enterEditMode();
    expect(await isEditMode()).to.equal(true);
    await fireGridContextMenu();
    const item = await ctxMenuItem('Leave Edit Mode');
    await item.click();
    await browser.waitUntil(async () => !(await isEditMode()), { timeout: 2_000 });
  });

  it('desktop right-click → Add Widget opens picker', async () => {
    await enterEditMode();
    await fireGridContextMenu();
    const item = await ctxMenuItem('Add Widget');
    await item.click();
    await browser.waitUntil(() => pickerIsOpen(), { timeout: 3_000 });
  });

  it('widget right-click menu has no title header at the top', async () => {
    await pickWidget('Folder');
    await clickGridCell(2, 2);
    await enterEditMode();
    await rightClick('.widget');
    const firstClass = await browser.execute(() =>
      document.querySelector('.ctx-menu')?.firstElementChild?.className ?? '',
    );
    expect(firstClass).to.not.include('ctx-header');
  });

  /**
   * Regression: with a menu open, right-clicking a DIFFERENT widget used to
   * only dismiss the menu — the full-window `.ctx-backdrop` swallowed the
   * event, so a second right-click was needed to open the new menu. The
   * backdrop now replays the event onto whatever sits under the pointer.
   *
   * The second click is fired at the element the OS hit-test would reach
   * (`elementFromPoint` → the backdrop), NOT at the widget directly, which
   * would bypass the backdrop and pass even before the fix.
   */
  it('right-click on another widget while a menu is open swaps menus in one click', async () => {
    await seedWidgets([
      { type: 'quote',    col: 1, row: 1, colSpan: 3, rowSpan: 2 },
      { type: 'countdown', col: 8, row: 1, colSpan: 3, rowSpan: 2 },
    ]);
    await switchToMain();
    await $('.grid > .widget').waitForExist({ timeout: 8_000 });

    // Menu for widget A.
    const firstLeft = await browser.execute(() => {
      const a = document.querySelectorAll('.grid > .widget')[0] as HTMLElement;
      const r = a.getBoundingClientRect();
      a.dispatchEvent(new MouseEvent('contextmenu', {
        bubbles: true, cancelable: true, view: window,
        button: 2, buttons: 2,
        clientX: r.left + r.width / 2, clientY: r.top + r.height / 2,
      }));
      return (document.querySelector('.ctx-menu') as HTMLElement | null)?.style.left ?? '';
    });
    expect(await ctxMenuIsOpen()).to.equal(true);

    // One right-click over widget B, delivered where the OS would deliver it.
    const hit = await browser.execute(() => {
      const b = document.querySelectorAll('.grid > .widget')[1] as HTMLElement;
      const r = b.getBoundingClientRect();
      const x = r.left + r.width / 2, y = r.top + r.height / 2;
      const top = document.elementFromPoint(x, y) as HTMLElement | null;
      top?.dispatchEvent(new MouseEvent('contextmenu', {
        bubbles: true, cancelable: true, view: window,
        button: 2, buttons: 2, clientX: x, clientY: y,
      }));
      return {
        topClass: top?.className ?? '',
        left: (document.querySelector('.ctx-menu') as HTMLElement | null)?.style.left ?? '',
      };
    });
    // Guard the guard: the click really did land on the backdrop.
    expect(hit.topClass).to.include('ctx-backdrop');
    expect(await ctxMenuIsOpen(), 'menu closed instead of swapping').to.equal(true);
    expect(hit.left, 'menu did not move to the second widget').to.not.equal(firstLeft);
  });

  it('ctx menu closes on Escape', async () => {
    await enterEditMode();
    await fireGridContextMenu();
    expect(await ctxMenuIsOpen()).to.equal(true);
    await browser.keys(['Escape']);
    await browser.waitUntil(async () => !(await ctxMenuIsOpen()), { timeout: 2_000 });
  });
});
