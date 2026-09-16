/**
 * Widget config modal + per-widget edit toolbar.
 *
 * Covers:
 *  - open config via ctx menu "Configure"
 *  - open via toolbar gear button
 *  - header input updates widget label (updateWidgetLabel)
 *  - close persists config (_refresh marker is set in App.tsx closeConfig)
 *  - toolbar Copy / Cut / Remove
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, pickWidget, clickGridCell, waitForPersist,
  rightClick, ctxMenuItem, configModalIsOpen, closeConfigModal,
  setConfigHeader, clickToolbarBtn, widgetCount, stubConfirm, LS_KEYS,
  waitForConfigWindow, switchToMain, closeAllConfigWindows,
} from '../helpers/app.js';
import { settle } from '../helpers/wait.js';

interface W { id: string; type: string; col: number; row: number; colSpan: number; rowSpan: number; label?: string; config?: Record<string, any> }

async function readWidgets(): Promise<W[]> {
  return browser.execute((prefix: string) => {
    const keys = Object.keys(localStorage).filter(k => k.startsWith(prefix));
    let found: W[] = [];
    for (const k of keys) {
      try {
        const v = JSON.parse(localStorage.getItem(k) ?? '{}');
        if (Array.isArray(v.widgets) && v.widgets.length > found.length) found = v.widgets;
      } catch {}
    }
    return found as W[];
  }, LS_KEYS.layoutPrefix);
}

async function seedWidget(label = 'Folder', col = 2, row = 2): Promise<W> {
  await pickWidget(label);
  await clickGridCell(col, row);
  await waitForPersist();
  // Config modal auto-opens when adding via picker (App.tsx handlePlace).
  // Close it so downstream tests start from a neutral state.
  if (await configModalIsOpen()) await closeConfigModal();
  // Also tear down any lingering detached config window (e.g. picker still
  // alive in placing mode) so downstream right-clicks + cell-clicks hit
  // the main grid only.
  await closeAllConfigWindows();
  await switchToMain();
  const ws = await readWidgets();
  return ws[ws.length - 1];
}

describe('widget config modal', () => {
  beforeEach(async () => { await resetAppState(); });

  it('opens via ctx menu → Configure', async () => {
    await seedWidget();
    await rightClick('.widget');
    const item = await ctxMenuItem('Configure');
    await item.click();
    await waitForConfigWindow('widget', 5_000);
    await switchToMain();
    // Tear down via Tauri close: Escape-driven close is flaky when the
    // widget window has lost OS focus to the main window.
    await closeAllConfigWindows();
  });

  it('opens via edit-mode toolbar gear', async () => {
    await seedWidget();
    await enterEditMode();
    await clickToolbarBtn('Configure');
    await waitForConfigWindow('widget', 5_000);
    await switchToMain();
    await closeAllConfigWindows();
  });

  it('header input updates widget label + persists', async () => {
    const w = await seedWidget();
    await rightClick('.widget');
    await (await ctxMenuItem('Configure')).click();
    await waitForConfigWindow('widget', 5_000);
    await switchToMain();

    await setConfigHeader('My Files');
    await waitForPersist();

    const updated = (await readWidgets()).find(ww => ww.id === w.id);
    expect(updated?.label).to.equal('My Files');
    await closeConfigModal();
  });

  it('closing modal writes a _refresh marker into config', async () => {
    const w = await seedWidget();
    await rightClick('.widget');
    await (await ctxMenuItem('Configure')).click();
    await waitForConfigWindow('widget', 5_000);
    await switchToMain();

    await closeAllConfigWindows();
    await waitForPersist();

    const updated = (await readWidgets()).find(ww => ww.id === w.id);
    expect(updated?.config?._refresh, 'closeConfig should stamp _refresh').to.be.a('number');
  });

  it('toolbar Remove deletes the widget (after confirm)', async () => {
    await seedWidget();
    const before = await widgetCount();
    await enterEditMode();
    await stubConfirm(true);
    await clickToolbarBtn('Remove');
    await browser.waitUntil(async () => (await widgetCount()) === before - 1, { timeout: 2_000 });
  });

  it('toolbar Remove is cancellable', async () => {
    await seedWidget();
    const before = await widgetCount();
    await enterEditMode();
    await stubConfirm(false);   // user "clicks Cancel" in the native dialog
    await clickToolbarBtn('Remove');
    // Negative assertion (verify Remove did NOT fire after Cancel): no
    // condition to wait on, so a labelled fixed delay is the honest tool.
    await settle(100, 'verify Remove did not fire after Cancel');
    expect(await widgetCount()).to.equal(before);
  });

  it('toolbar Copy starts clipboard placing mode', async () => {
    await seedWidget();
    await enterEditMode();
    await clickToolbarBtn('Copy');
    // After copy, grid gets `.placing` class (handlePlace flow).
    await $('.grid.placing').waitForDisplayed({ timeout: 2_000 });
  });
});
