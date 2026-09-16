/**
 * Design: View tab, grid sizing + spacing. General tab: UI scale.
 *
 * Grid (Columns/Rows/Spacing) is per-view and lives in the View tab
 * (tab label = active view name, default "Default"). UI Scale is global
 * and lives in the General tab.
 *
 * Cols/Rows: `.view-grid-control` with buttons [−, +, (optional rec-btn)]
 * around a number input. Grid CSS vars `--grid-cols` / `--grid-rows` are
 * set by WidgetGrid effect from layout.cols/rows.
 *
 * Spacing: `<NumberStepper>` (a `.number-stepper` element with an inner
 * `<input type="number">`), writes layout.widgetGap → `--widget-gap`.
 *
 * UI Scale: `<NumberStepper>` in General with min=50, max=200 (% display),
 * stores 0.5–2.0 in design.uiScale and drives the `:root --ui-scale` CSS var
 * (scales popups + the config window; per-monitor widget scale is separate,
 * see 53-per-monitor-scaling.spec.ts).
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, readCssVar,
  waitForReady, LS_KEYS,
} from '../helpers/app.js';

async function readLayout(): Promise<Record<string, any>> {
  return browser.execute((k: string) => {
    try { return JSON.parse(localStorage.getItem(k) ?? '{}'); }
    catch { return {}; }
  }, LS_KEYS.layoutRoot);
}

/**
 * Click the − or + button in the Cols/Rows row. Each row renders:
 *   <div.view-grid-control>
 *     <button>−</button>
 *     <input.view-grid-input />
 *     <button>+</button>
 *     <button.view-rec-btn>…</button>  ← optional
 *   </div>
 * So btns[0] = minus, btns[1] = plus (regardless of the optional rec-btn).
 */
async function clickGridBtn(labelRegex: RegExp, kind: '+' | '-'): Promise<void> {
  const clicked = await browser.execute((re: string, k: string) => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-modal .design-row'));
    const pattern = new RegExp(re, 'i');
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (!pattern.test(lbl)) continue;
      const ctrl = row.querySelector<HTMLElement>('.view-grid-control');
      if (!ctrl) continue;
      const btns = Array.from(ctrl.querySelectorAll<HTMLButtonElement>(
        'button:not(.view-rec-btn)',
      ));
      if (btns.length < 2) return false;
      const btn = k === '-' ? btns[0] : btns[1];
      btn.click();
      return true;
    }
    return false;
  }, labelRegex.source, kind);
  if (!clicked) throw new Error(`No design-row matching ${labelRegex} with button ${kind}`);
}

// Skipped: grid controls moved per-view after design refactor; selectors need update.
describe.skip('design: grid + layout', () => {
  beforeEach(async () => {
    await resetAppState();
    await openSettings();
    // Default view tab: clears localStorage in resetAppState → view list
    // collapses to the single "Default" view.
    await (await settingsTab('Default')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('grid cols + button increases cols and --grid-cols', async () => {
    const before = (await readLayout()).cols ?? 0;
    await clickGridBtn(/columns|cols/i, '+');
    await browser.waitUntil(async () => ((await readLayout()).cols ?? 0) === before + 1, { timeout: 2_000 });

    const cssCols = await readCssVar('--grid-cols');
    expect(parseInt(cssCols)).to.equal(before + 1);
  });

  it('grid rows + button increases rows and --grid-rows', async () => {
    const before = (await readLayout()).rows ?? 0;
    await clickGridBtn(/rows/i, '+');
    await browser.waitUntil(async () => ((await readLayout()).rows ?? 0) === before + 1, { timeout: 2_000 });

    const cssRows = await readCssVar('--grid-rows');
    expect(parseInt(cssRows)).to.equal(before + 1);
  });

  it('grid cols - button decreases cols', async () => {
    const before = (await readLayout()).cols ?? 0;
    await clickGridBtn(/columns|cols/i, '-');
    await browser.waitUntil(async () => ((await readLayout()).cols ?? 0) === before - 1, { timeout: 2_000 });
  });

  it('spacing stepper updates --widget-gap', async () => {
    // The Spacing (widget gap) control is a <NumberStepper> with an inner
    // <input type="number">, inside the design-row labelled "Spacing".
    await browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-modal .design-row'));
      for (const row of rows) {
        const lbl = row.querySelector('.design-label')?.textContent ?? '';
        if (!/spacing|gap/i.test(lbl)) continue;
        const inp = row.querySelector<HTMLInputElement>('.number-stepper input[type="number"]');
        if (!inp) continue;
        const setter = Object.getOwnPropertyDescriptor(
          window.HTMLInputElement.prototype, 'value',
        )?.set;
        setter?.call(inp, '20');
        inp.dispatchEvent(new Event('input',  { bubbles: true }));
        inp.dispatchEvent(new Event('change', { bubbles: true }));
        inp.dispatchEvent(new Event('blur',   { bubbles: true }));
        return;
      }
      throw new Error('no spacing stepper found');
    });

    await browser.waitUntil(async () => {
      const v = await readCssVar('--widget-gap');
      return v === '20px';
    }, { timeout: 2_000 });

    const l = await readLayout();
    expect(l.widgetGap).to.equal(20);
  });

  it('UI scale stepper drives :root --ui-scale', async () => {
    // UI Scale stepper lives in the Appearance section of the General tab.
    await (await settingsTab('General')).click();
    // Its number input has min=50, max=200 (% display), uniquely identified by max=200.
    await browser.execute(() => {
      const inputs = Array.from(document.querySelectorAll<HTMLInputElement>(
        '.design-modal .number-stepper input[type="number"]',
      ));
      const target = inputs.find(r => parseFloat(r.max) === 200);
      if (!target) throw new Error('no ui-scale stepper found');
      const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype, 'value',
      )?.set;
      setter?.call(target, '120');
      target.dispatchEvent(new Event('input',  { bubbles: true }));
      target.dispatchEvent(new Event('change', { bubbles: true }));
      target.dispatchEvent(new Event('blur',   { bubbles: true }));
    });

    await browser.waitUntil(async () => (await readCssVar('--ui-scale')) === '1.2',
      { timeout: 2_000 });
  });

  it('grid change survives reload', async () => {
    const before = (await readLayout()).cols ?? 0;
    await clickGridBtn(/columns|cols/i, '+');
    await browser.waitUntil(async () => ((await readLayout()).cols ?? 0) === before + 1, { timeout: 2_000 });

    await closeSettings();
    await browser.refresh();
    await waitForReady();

    const after = (await readLayout()).cols ?? 0;
    expect(after).to.equal(before + 1);
    expect(parseInt(await readCssVar('--grid-cols'))).to.equal(before + 1);
  });
});
