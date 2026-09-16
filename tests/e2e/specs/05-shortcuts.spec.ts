/**
 * Hotkey dispatch: exercises the window-keydown path in hotkeys.ts.
 * The app-key bridge (Rust WH_KEYBOARD_LL) is not tested here; it can't be
 * triggered via webdriver. Cover that in Rust unit tests.
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import { resetAppState, isEditMode, pickerIsOpen, waitForReady, switchToMain, switchToConfigWindow } from '../helpers/app.js';

describe('keyboard shortcuts', () => {
  beforeEach(async () => { await resetAppState(); });

  it('ctrl+e toggles edit mode', async () => {
    await switchToMain();
    expect(await isEditMode()).to.equal(false);
    await browser.keys(['Control', 'e']);
    await browser.waitUntil(() => isEditMode(), { timeout: 2_000 });
    await browser.keys(['Control', 'e']);
    await browser.waitUntil(async () => !(await isEditMode()), { timeout: 2_000 });
  });

  it('ctrl+w opens picker', async () => {
    await switchToMain();
    expect(await pickerIsOpen()).to.equal(false);
    await browser.keys(['Control', 'w']);
    await browser.waitUntil(() => pickerIsOpen(), { timeout: 2_000 });
  });

  it('escape closes picker (escapeStack integration)', async () => {
    await switchToMain();
    await browser.keys(['Control', 'w']);
    await browser.waitUntil(() => pickerIsOpen(), { timeout: 3_000 });
    // Escape must be dispatched to the picker config window (where the
    // escapeStack handler + focused search input live).
    await switchToConfigWindow('add');
    const pickerHandle = await browser.getWindowHandle();
    // Small settle so the OS focus-change after switch propagates before
    // we send the first Escape.
    await browser.pause(150);
    // Picker search input auto-focuses → 1st Escape blurs input, 2nd closes
    // window. Loop up to three escapes; the OS focus race after
    // switchToWindow can swallow the first keystroke entirely on some runs.
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

  it('hotkey is suppressed while an input is focused', async () => {
    await switchToMain();
    // Open picker → search input auto-focuses.
    await browser.keys(['Control', 'w']);
    await browser.waitUntil(() => pickerIsOpen(), { timeout: 2_000 });

    // Ctrl+E must target the picker config window (where the focused input
    // lives), that's the window whose hotkey handler should be suppressed.
    await switchToConfigWindow('add');
    await browser.keys(['Control', 'e']);
    // Give the listener a tick.
    await browser.pause(100);
    // isEditMode reads main-window localStorage.
    await switchToMain();
    expect(await isEditMode()).to.equal(false);
  });
});
