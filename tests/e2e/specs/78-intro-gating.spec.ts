import { expect } from 'chai';
import {
  resetAppState, seedWidgets, pickWidget, clickGridCell, widgetCount,
  attachToAppWindow, waitForReady,
} from '../helpers/app.js';

// [S-27] Intro plays are armed one-shots: only boot / a real view transition /
// a settings replay animate a grid mount. Widgets placed or seeded into a LIVE
// grid must mount silent: no `.intro` class, no animation.

/** Widgets in the active view currently carrying an intro animation class. */
async function introCount(): Promise<number> {
  return browser.execute(
    () => document.querySelectorAll('.view-root.active .widget.intro').length,
  );
}

describe('[S-27] intro animation gating', () => {
  beforeEach(async () => { await resetAppState(); });

  it('S-27: widgets present at BOOT mount with the startup intro', async () => {
    await seedWidgets([{ type: 'clock', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
    // Reload: the beforeunload flushSave persists the seeded widget, the new
    // page boots with it in the layout → the boot-armed play animates it.
    await browser.execute(() => location.reload());
    await browser.pause(300);
    await attachToAppWindow();
    await waitForReady();
    expect(await widgetCount()).to.equal(1);
    expect(await introCount()).to.equal(1);
  });

  it('S-27: a widget seeded into a live grid mounts WITHOUT an intro', async () => {
    await seedWidgets([{ type: 'clock', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
    expect(await widgetCount()).to.equal(1);
    expect(await introCount()).to.equal(0);
  });

  it('S-27: a widget PLACED from the picker mounts WITHOUT an intro', async () => {
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    expect(await widgetCount()).to.equal(1);
    expect(await introCount()).to.equal(0);
  });
});
