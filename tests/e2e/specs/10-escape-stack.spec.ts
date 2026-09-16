/**
 * escapeStack LIFO: each overlay registers a handler on mount and pops it
 * on unmount. See App.tsx createEffect(onEscape) blocks. The order verified:
 *
 *   editMode < showPicker < showDesign < configWidget < inputFocused < ctxMenu < placing
 *
 * Tests assert Esc "peels" layers in the expected order instead of jumping
 * straight to the base.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, isEditMode, pickerIsOpen, pickWidget,
  clickGridCell, waitForPersist, settingsIsOpen, openSettings, closeSettings,
  rightClick, ctxMenuIsOpen, configModalIsOpen, closeConfigModal, activeElementTag,
  switchToMain, switchToConfigWindow,
} from '../helpers/app.js';

describe('escapeStack: LIFO peeling', () => {
  beforeEach(async () => { await resetAppState(); });

  it('Esc closes picker (escapeStack closes window even with input focused)', async () => {
    await switchToMain();
    await browser.keys(['Control', 'w']);
    await browser.waitUntil(() => pickerIsOpen(), { timeout: 3_000 });
    // Picker UI / focused input live in the detached config window.
    await switchToConfigWindow('add');
    const pickerHandle = await browser.getWindowHandle();
    await browser.waitUntil(async () => (await activeElementTag()) === 'input', { timeout: 2_000 });
    expect(await activeElementTag(), 'picker search auto-focuses').to.equal('input');

    // ConfigWindow registers a single escapeStack handler that closes the
    // window. The picker's auto-focused input does not block it because the
    // global escape listener catches the keydown before it reaches the input.
    // One Esc is enough; loop up to three to absorb OS focus races.
    for (let i = 0; i < 3; i++) {
      await browser.keys(['Escape']);
      await browser.pause(150);
      if (!(await browser.getWindowHandles()).includes(pickerHandle)) break;
    }
    await browser.waitUntil(async () => {
      const hs = await browser.getWindowHandles();
      return !hs.includes(pickerHandle);
    }, { timeout: 3_000 });
  });

  it('Esc closes ctx menu before edit mode', async () => {
    await enterEditMode();
    expect(await isEditMode()).to.equal(true);
    await rightClick('.grid');
    expect(await ctxMenuIsOpen()).to.equal(true);

    await browser.keys(['Escape']);
    await browser.waitUntil(async () => !(await ctxMenuIsOpen()), { timeout: 2_000 });
    // Edit mode should still be on: ctx menu was on top of the stack.
    expect(await isEditMode()).to.equal(true);
  });

  it('Esc closes settings modal before edit mode', async () => {
    // Enter edit mode first, then open settings, then assert closing settings
    // peels only the settings layer; edit mode survives.
    await enterEditMode();
    expect(await isEditMode(), 'edit mode entered').to.equal(true);
    await openSettings();
    expect(await settingsIsOpen()).to.equal(true);

    // closeSettings switches into the detached settings window, presses Esc,
    // waits for the OS window to close, and switches back to main.
    await closeSettings();
    expect(await settingsIsOpen()).to.equal(false);
    expect(await isEditMode(), 'edit mode survives').to.equal(true);
  });

  it('Esc cancels placing mode without destroying edit mode', async () => {
    // Seed a widget so clipboard has something to paste.
    await pickWidget('Folder');
    await clickGridCell(1, 1);
    await waitForPersist();
    // Placement auto-spawns the widget config window; close it before the
    // placing-mode test, otherwise the leaked window churns z-order.
    await closeConfigModal();

    await enterEditMode();
    await rightClick('.widget');
    // Click "Copy widget" to start placing.
    const items = await $$('.ctx-menu .ctx-item');
    for await (const it of items) {
      const t = (await it.$('.ctx-label').getText()).trim();
      if (t === 'Copy widget') { await it.click(); break; }
    }
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });

    await browser.keys(['Escape']);
    await browser.waitUntil(async () =>
      (await $$('.grid.placing').length) === 0, { timeout: 2_000 });
    expect(await isEditMode()).to.equal(true);
  });

  it('Esc eventually exits edit mode', async () => {
    await enterEditMode();
    await browser.keys(['Escape']);
    await browser.waitUntil(async () => !(await isEditMode()), { timeout: 2_000 });
  });
});
