/**
 * Detached config window z-order regression.
 *
 * Reproduces the bug: after opening a widget's Configure window, right-clicking
 * the widget area (which opens Themia's in-widget context menu) must NOT push
 * the config window behind the widget window.
 *
 * Background:
 *   - Widget windows are subclassed to anchor "just above Progman" (z_above_desktop).
 *   - Detached config windows are plain decorated webviews in the normal z-band.
 *   - In theory: normal z-band > Progman-level z, so config always sits above.
 *   - In practice: widget activation (set_interactive(true) while ctx menu is
 *     open, followed by a click on a menu item) can race with the widget
 *     subclass and momentarily raise the widget above the config window.
 *
 * [ZS-02] These cases also guard HOW that order is kept: the overlay is demoted
 * back to its desktop anchor on WM_ACTIVATE, the config window is never raised.
 * The old repair raised the config window to the top of the whole non-topmost
 * band, which put it in front of unrelated applications on every widget hover.
 * That symptom needs a third-party window to observe, so it is verified by
 * hand; what CI can hold onto is that the invariant below still holds with no
 * raise in the code.
 *
 * Verification uses the `diag_window_above` Rust command which walks the OS
 * z-order and returns whether one HWND is strictly above another.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist,
  rightClick, ctxMenuItem, LS_KEYS, closeAllConfigWindows,
  switchToMain, findConfigHandle,
} from '../helpers/app.js';

interface W { id: string; type: string }

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

async function windowAbove(topLabel: string, bottomLabel: string): Promise<boolean> {
  return browser.execute(async (top: string, bot: string) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    return await invoke('diag_window_above', { topLabel: top, bottomLabel: bot });
  }, topLabel, bottomLabel) as unknown as Promise<boolean>;
}

async function seedWidget(): Promise<W> {
  await pickWidget('Clock');
  // clickGridCell auto-dismisses the post-placement widget config window;
  // that's fine here: the next thing we do is right-click the placed widget
  // to reopen Configure ourselves. Belt + braces: explicitly close any
  // lingering config windows before reading the layout, in case the
  // auto-close race left one open.
  await clickGridCell(2, 2);
  await waitForPersist();
  await switchToMain();
  if (await findConfigHandle()) await closeAllConfigWindows();
  const ws = await readWidgets();
  return ws[ws.length - 1];
}

describe('detached config window z-order', () => {
  beforeEach(async () => {
    // resetAppState handles closing leaked config windows (via the same
    // diag_close_windows_by_prefix invoke). Calling closeAllConfigWindows
    // before reset is fragile when the cached main handle is stale, so let
    // resetAppState do it.
    await resetAppState();
  });

  after(async () => {
    await closeAllConfigWindows();
  });

  it('config window is above the widget right after spawning', async () => {
    await seedWidget();
    await rightClick('.widget');
    await (await ctxMenuItem('Configure')).click();

    // The unified config window uses the fixed label "config" (see
     // show_config_window in src-tauri/src/lib.rs).
    const cfgLabel = 'config';
    // Wait for the config window to come into existence and become above.
    await browser.waitUntil(async () => windowAbove(cfgLabel, 'monitor-0'), {
      timeout: 5_000,
      timeoutMsg: `config window ${cfgLabel} not above monitor-0 after spawn`,
    });
  });

  it('config window stays above widget after right-clicking the widget area', async () => {
    await seedWidget();

    // Open Configure from widget ctx menu.
    await rightClick('.widget');
    await (await ctxMenuItem('Configure')).click();
    // The unified config window uses the fixed label "config" (see
     // show_config_window in src-tauri/src/lib.rs).
    const cfgLabel = 'config';
    await browser.waitUntil(() => windowAbove(cfgLabel, 'monitor-0'), { timeout: 5_000 });

    // Right-click the widget area again. This opens the widget's in-widget
    // context menu and toggles `set_interactive(true)` on the widget HWND.
    await rightClick('.widget');

    // Dismiss the menu via Escape so the test doesn't leak state, then verify
    // the config window did NOT slip behind the widget at any point. We poll
    // for a short window so a transient demotion is caught.
    await browser.keys(['Escape']);

    const deadline = Date.now() + 1_500;
    while (Date.now() < deadline) {
      const above = await windowAbove(cfgLabel, 'monitor-0');
      expect(above, `config ${cfgLabel} dropped below monitor-0 after widget right-click`).to.be.true;
      await browser.pause(100);
    }
  });

  it('config window stays above widget after activating-then-dismissing the widget ctx menu', async () => {
    // Mirrors the real user flow: user right-clicks widget → menu opens →
    // user clicks empty space → menu closes → set_interactive(false) flips
    // widget back to non-activatable + re-anchors via SetWindowPos.
    await seedWidget();

    await rightClick('.widget');
    await (await ctxMenuItem('Configure')).click();
    // The unified config window uses the fixed label "config" (see
     // show_config_window in src-tauri/src/lib.rs).
    const cfgLabel = 'config';
    await browser.waitUntil(() => windowAbove(cfgLabel, 'monitor-0'), { timeout: 5_000 });

    // Re-open the widget ctx menu and dismiss by clicking inside the widget
    // (outside the menu): this is the activation path most likely to bury
    // the config window.
    await rightClick('.widget');
    const widget = await $('.widget');
    const loc = await widget.getLocation() as { x: number; y: number };
    const size = await widget.getSize() as { width: number; height: number };
    await browser.performActions([{
      type: 'pointer',
      id: 'mouse',
      parameters: { pointerType: 'mouse' },
      actions: [
        { type: 'pointerMove', x: Math.round(loc.x + size.width / 2), y: Math.round(loc.y + size.height / 2) },
        { type: 'pointerDown', button: 0 },
        { type: 'pointerUp', button: 0 },
      ],
    }]);
    await browser.releaseActions();

    const deadline = Date.now() + 1_500;
    while (Date.now() < deadline) {
      expect(
        await windowAbove(cfgLabel, 'monitor-0'),
        `config ${cfgLabel} dropped below monitor-0 after widget click-through`,
      ).to.be.true;
      await browser.pause(100);
    }
  });
});
