import { expect } from 'chai';
import {
  resetAppState, openPicker, pickerIsOpen, closePicker, pickWidget, widgetCount,
  enterEditMode, leaveEditMode, clickGridCell, rightClick, ctxMenuItem, stubConfirm,
} from '../helpers/app.js';

describe('widget picker: add / remove', () => {
  beforeEach(async () => { await resetAppState(); });

  it('opens picker via ctrl+w and closes via escape', async () => {
    expect(await pickerIsOpen()).to.equal(false);
    await openPicker();
    expect(await pickerIsOpen()).to.equal(true);
    await closePicker();
    expect(await pickerIsOpen()).to.equal(false);
  });

  it('picking a widget from the grid adds it', async () => {
    const before = await widgetCount();
    await pickWidget('Folder');
    // Picker closes on select; placing mode is active. Click a grid cell.
    await clickGridCell(1, 1);
    const after = await widgetCount();
    expect(after).to.equal(before + 1);
  });

  it('web-element[S-33] is hidden from the picker unless developer mode is on', async () => {
    const pickerLabels = async (q: string) => {
      await openPicker();
      const search = await $('.picker-search');
      await search.setValue(q);
      await browser.pause(150);
      const labels = await browser.execute(() =>
        [...document.querySelectorAll('.widget-card-label')].map(e => e.textContent ?? ''),
      );
      await closePicker();
      return labels;
    };

    // Default (dev mode off): no Web Element card.
    expect(await pickerLabels('Web Element')).to.not.include('Web Element');

    // Dev mode on (fresh picker window reads the persisted design): visible.
    await browser.execute(() => {
      const raw = localStorage.getItem('widget-global-design');
      const d = raw ? JSON.parse(raw) : {};
      d.devMode = true;
      localStorage.setItem('widget-global-design', JSON.stringify(d));
    });
    expect(await pickerLabels('Web Element')).to.include('Web Element');
  });

  it('remove via widget ctx menu drops widget count', async () => {
    await pickWidget('Folder');
    await clickGridCell(2, 2);
    const after = await widgetCount();

    await enterEditMode();
    await stubConfirm(true);                // WidgetGrid uses window.confirm
    await rightClick('.widget');
    const rm = await ctxMenuItem('Remove widget');
    await rm.click();
    await leaveEditMode();

    expect(await widgetCount()).to.equal(after - 1);
  });
});
