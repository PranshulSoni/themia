import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, enterEditMode, pickWidget, clickGridCell, waitForReady, waitForPersist, switchToMain, LS_KEYS } from '../helpers/app.js';

/**
 * JS-dispatched mouse drag. WebDriver pointer actions in WebView2 don't
 * always reach legacy window `mousemove` listeners, and gridDragManager relies
 * on them. Dispatching synthetic MouseEvents through the DOM reliably
 * triggers the code path we care about.
 */
async function mouseDrag(
  target: any,              // wdio v9 $ types: awaiting still returns a ChainablePromiseElement
  dx: number, dy: number,
  steps = 20,
): Promise<void> {
  await browser.execute((el: HTMLElement, dx: number, dy: number, steps: number) => {
    const r = el.getBoundingClientRect();
    const sx = Math.round(r.left + r.width / 2);
    const sy = Math.round(r.top  + r.height / 2);
    const fire = (type: string, x: number, y: number, tgt: EventTarget) => {
      tgt.dispatchEvent(new MouseEvent(type, {
        bubbles: true, cancelable: true, view: window,
        clientX: x, clientY: y, button: 0, buttons: type === 'mouseup' ? 0 : 1,
      }));
    };
    fire('mousedown', sx, sy, el);
    for (let i = 1; i <= steps; i++) {
      const x = sx + Math.round((dx * i) / steps);
      const y = sy + Math.round((dy * i) / steps);
      // Dispatch on both window and document: different listeners attach to
      // different targets and the WebView2 sometimes routes synthetic events
      // to one but not the other.
      fire('mousemove', x, y, window);
      fire('mousemove', x, y, document);
    }
    fire('mouseup', sx + dx, sy + dy, window);
    fire('mouseup', sx + dx, sy + dy, document);
  }, target as unknown as HTMLElement, dx, dy, steps);
}


// Fractional layout schema (gridGeometry.FracRect): widgets store x/y/w/h as
// fractions of the screen, not cell col/row/colSpan/rowSpan (pre-v0.12).
interface W { id: string; type: string; x: number; y: number; w: number; h: number }

/**
 * Read widgets from the active layout in localStorage.
 * Keys are `widget-layout` (default view) and `widget-layout-<viewId>`.
 * We return the last-non-empty one to cover both cases.
 */
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

/** Find a widget of `type`, first match. */
async function findWidget(type: string): Promise<W | null> {
  const ws = await readWidgets();
  return ws.find(w => w.type === type) ?? null;
}

describe('drag + resize + persistence', () => {
  beforeEach(async () => { await resetAppState(); });

  it('drag moves a widget to a new cell', async () => {
    await pickWidget('Folder');
    await clickGridCell(1, 1);
    await waitForPersist();
    const before = await findWidget('folder');
    expect(before, 'folder widget seeded').to.not.equal(null);

    await enterEditMode();
    // Ensure we're in the main app window: clickGridCell may have caused
    // window-handle churn when it auto-dismissed the post-placement config window.
    await switchToMain();
    // Only one widget on the grid (layout was seeded empty in beforeEach).
    const w = await $('.widget');
    const size = await w.getSize();
    const dx = Math.round(size.width) + 10;
    await mouseDrag(w, dx, 0);
    await waitForPersist();

    await browser.waitUntil(async () => {
      const after = await findWidget('folder');
      return !!after && after.x !== before!.x;
    }, { timeout: 3_000, timeoutMsg: 'widget x did not change after drag' });
  });

  it('resize grows a widget span', async () => {
    await pickWidget('Folder');
    await clickGridCell(1, 1);
    await waitForPersist();
    await enterEditMode();
    await switchToMain();

    const before = await findWidget('folder');
    expect(before, 'folder widget seeded').to.not.equal(null);
    const handle = await $('.corner-overlay .rh-br');
    await mouseDrag(handle, 200, 200);
    await waitForPersist();

    await browser.waitUntil(async () => {
      const after = await findWidget('folder');
      return !!after && (after.w + after.h) > (before!.w + before!.h);
    }, { timeout: 3_000, timeoutMsg: 'widget span did not grow after resize drag' });
  });

  it('reload preserves widget position', async () => {
    await pickWidget('Folder');
    await clickGridCell(3, 2);
    await waitForPersist();
    const before = await findWidget('folder');
    expect(before).to.not.equal(null);

    await browser.refresh();
    await waitForReady();

    const after = await findWidget('folder');
    expect(after).to.deep.equal(before);
  });
});
