import { expect } from 'chai';
import { resetAppState, openPicker, closePicker } from '../helpers/app.js';

/** Write a partial global-design update + fire a storage event so design.ts picks it up. */
async function setGlobalDesignPartial(partial: Record<string, any>): Promise<void> {
  await browser.execute((p: Record<string, any>) => {
    const k = 'widget-global-design';
    const cur = JSON.parse(localStorage.getItem(k) ?? '{}');
    const next = { ...cur, ...p };
    localStorage.setItem(k, JSON.stringify(next));
    window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: JSON.stringify(next) }));
  }, partial);
}

// The Add-Widget picker shows a persistent preview pane on the right that
// renders the hovered widget with demo data. The pane is always visible and
// swaps its content as the cursor moves across the card list.
describe('widget picker: preview pane', () => {
  beforeEach(async () => { await resetAppState(); });
  afterEach(async () => { await closePicker(); });

  it('preview pane is always visible while the picker is open', async () => {
    await openPicker();
    const pane = await $('.picker-preview-pane');
    expect(await pane.isDisplayed()).to.equal(true);
  });

  it('the widget list does not overflow its column (no clipping)', async () => {
    await openPicker();
    // scrollWidth > clientWidth means cards spill past the scroll area and get
    // clipped by the panel's `overflow: hidden`.
    const overflow = await browser.execute(() => {
      const el = document.querySelector('.picker-scroll') as HTMLElement | null;
      if (!el) return null;
      return { scrollW: el.scrollWidth, clientW: el.clientWidth };
    });
    expect(overflow, 'picker-scroll present').to.not.equal(null);
    expect(overflow!.scrollW, 'list must not overflow horizontally')
      .to.be.at.most(overflow!.clientW + 1);
  });

  // Regression: the preview box mirrors the desktop widget chrome, including
  // the corner radius, not just the border. A hardcoded radius made the preview
  // show rounded corners at cornerRadius:0 whenever a border was on (the bug was
  // only visible WITH a border, since the border traced the rounded clip). The
  // box must track --radius so radius:0 + a border = square corners, matching
  // the real `.widget`.
  it('preview box corner radius tracks --radius (square at 0 with a border on)', async () => {
    await setGlobalDesignPartial({ cornerRadius: 0, widgetBorderStyle: 'solid', widgetBorderWidth: 2 });
    await openPicker();

    // Ensure a preview is mounted (hover the first card, like the tests below).
    await (await $$('.widget-card'))[0].moveTo();
    const box = await $('.widget-pane-preview');
    await box.waitForExist({ timeout: 3_000 });

    const radius = await browser.execute(() => {
      const el = document.querySelector('.widget-pane-preview') as HTMLElement | null;
      return el ? getComputedStyle(el).borderRadius : null;
    });
    expect(radius, 'square corners at cornerRadius:0 even with a border').to.equal('0px');
  });

  it('hovering a card updates the preview pane with that widget', async () => {
    await openPicker();

    const cards = await $$('.widget-card');
    const a = cards[0];
    const aLabel = await a.$('.widget-card-label').getText();

    await a.moveTo();
    await browser.waitUntil(async () => {
      const t = await $('.picker-preview-label').getText().catch(() => '');
      return t === aLabel;
    }, { timeout: 3_000, timeoutMsg: `preview did not switch to "${aLabel}"` });

    // Move to a different card → pane follows.
    let bIdx = -1, bLabel = '';
    for (let i = 1; i < (await $$('.widget-card')).length; i++) {
      const l = await (await $$('.widget-card'))[i].$('.widget-card-label').getText();
      if (l !== aLabel) { bIdx = i; bLabel = l; break; }
    }
    expect(bIdx, 'need a second distinct card').to.be.greaterThan(0);

    await (await $$('.widget-card'))[bIdx].moveTo();
    await browser.waitUntil(async () => {
      const t = await $('.picker-preview-label').getText().catch(() => '');
      return t === bLabel;
    }, { timeout: 3_000, timeoutMsg: `preview did not switch to "${bLabel}"` });
  });

  // The folder widget's preview has no configured path. Demo/preview mode must
  // suppress the "Select folder…" empty-state and render demo file entries
  // instead, so the picker preview shows meaningful content. [folder S-02]
  it('folder preview shows demo entries, not the select-folder empty state', async () => {
    await openPicker();

    // Hover the Folder card.
    let folderLabel = '';
    await browser.waitUntil(async () => {
      const cards = await $$('.widget-card');
      for (const c of cards) {
        const l = await c.$('.widget-card-label').getText().catch(() => '');
        if (/^folder$/i.test(l)) { await c.moveTo(); folderLabel = l; return true; }
      }
      return false;
    }, { timeout: 3_000, timeoutMsg: 'folder card not found' });

    await browser.waitUntil(async () => {
      const t = await $('.picker-preview-label').getText().catch(() => '');
      return t === folderLabel;
    }, { timeout: 3_000, timeoutMsg: 'preview did not switch to folder' });

    // The preview pane must render demo file rows and must NOT show the
    // empty-state prompt. It must also render grid view with MULTIPLE columns:
    // a too-small render span fits only one column and upscales into a single
    // giant icon (the scaling bug we're guarding against).
    const state = await browser.execute(() => {
      const pane = document.querySelector('.picker-preview-pane');
      const fv = pane?.querySelector('.folder-view');
      const items = fv ? Array.from(fv.querySelectorAll<HTMLElement>('[data-fp]')) : [];
      // Column count = number of items sharing the topmost row's offsetTop.
      let cols = 0;
      if (items.length) {
        const top = Math.min(...items.map(el => el.offsetTop));
        cols = items.filter(el => Math.abs(el.offsetTop - top) < 4).length;
      }
      return {
        hasFolderView: !!fv,
        items: items.length,
        firstRowCols: cols,
        stateMsg: fv?.querySelector('.state-msg')?.textContent ?? null,
      };
    });

    expect(state.hasFolderView, 'folder-view mounted in preview').to.equal(true);
    expect(state.stateMsg, 'no select-folder empty state').to.equal(null);
    expect(state.items, 'demo file entries rendered').to.be.greaterThan(0);
    expect(state.firstRowCols, 'grid renders multiple columns (not a single upscaled icon)')
      .to.be.greaterThan(1);
  });

  // The calculator's history lives in an in-memory signal (not config), so the
  // preview would otherwise render a blank list. Demo/preview mode must seed a
  // few past calculations so the pane shows what the widget does. [calculator S-12]
  it('calculator preview shows seeded past calculations', async () => {
    await openPicker();

    let calcLabel = '';
    await browser.waitUntil(async () => {
      const cards = await $$('.widget-card');
      for (const c of cards) {
        const l = await c.$('.widget-card-label').getText().catch(() => '');
        if (/^calculator$/i.test(l)) { await c.moveTo(); calcLabel = l; return true; }
      }
      return false;
    }, { timeout: 3_000, timeoutMsg: 'calculator card not found' });

    await browser.waitUntil(async () => {
      const t = await $('.picker-preview-label').getText().catch(() => '');
      return t === calcLabel;
    }, { timeout: 3_000, timeoutMsg: 'preview did not switch to calculator' });

    const histCount = await browser.execute(() => {
      const pane = document.querySelector('.picker-preview-pane');
      return pane ? pane.querySelectorAll('.calc-widget__hist-item').length : 0;
    });

    expect(histCount, 'seeded history rows rendered in preview').to.be.greaterThan(0);
  });
});
