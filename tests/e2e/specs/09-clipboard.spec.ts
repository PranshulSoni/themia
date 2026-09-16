/**
 * Widget clipboard: copy / cut / paste flow.
 *
 *   copy → placing mode (original stays) → click cell → 2 widgets
 *   cut  → placing mode (original removed on paste) → click cell → 1 widget moved
 *   ⌃V   → paste hotkey (in edit mode, when clipboard has content)
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, pickWidget, clickGridCell, widgetCount,
  rightClick, ctxMenuItem, configModalIsOpen, closeConfigModal, waitForPersist,
  closeAllConfigWindows, switchToMain,
} from '../helpers/app.js';

async function seedAndClose(label = 'Folder', col = 2, row = 2): Promise<void> {
  await pickWidget(label);
  await clickGridCell(col, row);
  await waitForPersist();
  if (await configModalIsOpen()) await closeConfigModal();
  // Close any lingering detached config window (picker may still be alive
  // in placing-mode) so the main grid is the sole receiver of subsequent
  // right-clicks + cell-clicks.
  await closeAllConfigWindows();
  await switchToMain();
}

describe('widget clipboard', () => {
  beforeEach(async () => { await resetAppState(); });

  it('copy → placing mode → click places second widget', async () => {
    await seedAndClose('Folder', 2, 2);
    const before = await widgetCount();
    expect(before).to.equal(1);

    await enterEditMode();
    await rightClick('.widget');
    const copy = await ctxMenuItem('Copy widget');
    await copy.click();

    // Placing class signals the placing state.
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });

    // Click a different cell to place.
    await clickGridCell(5, 4);
    await waitForPersist();

    await browser.waitUntil(async () => (await widgetCount()) === before + 1, { timeout: 2_000 });
  });

  it('cut → paste moves the widget (no duplication)', async () => {
    await seedAndClose('Folder', 2, 2);
    const before = await widgetCount();

    await enterEditMode();
    await rightClick('.widget');
    await (await ctxMenuItem('Cut widget')).click();
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });

    await clickGridCell(6, 3);
    await waitForPersist();

    // Count stays the same: original removed, new placed.
    expect(await widgetCount()).to.equal(before);
  });

  it('⌃V pastes when clipboard has a widget and we are in edit mode', async () => {
    await seedAndClose('Folder', 2, 2);
    await enterEditMode();
    await rightClick('.widget');
    await (await ctxMenuItem('Copy widget')).click();
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });

    // Cancel current placing to ensure we're testing the ⌃V trigger freshly.
    await browser.keys(['Escape']);
    await browser.waitUntil(async () =>
      (await $$('.grid.placing').length) === 0, { timeout: 2_000 });

    // Now ⌃V should re-enter placing.
    await browser.keys(['Control', 'v']);
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });
  });

  it('Escape exits placing mode without adding a widget', async () => {
    await seedAndClose('Folder', 2, 2);
    const before = await widgetCount();

    await enterEditMode();
    await rightClick('.widget');
    await (await ctxMenuItem('Copy widget')).click();
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });

    await browser.keys(['Escape']);
    await browser.waitUntil(async () =>
      (await $$('.grid.placing').length) === 0, { timeout: 2_000 });

    expect(await widgetCount()).to.equal(before);
  });
});
