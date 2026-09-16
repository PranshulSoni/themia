/**
 * View lifecycle CRUD: rename, duplicate, delete, reorder.
 *
 * The view chip UI in edit mode exposes:
 *   - `.view-add-btn` → `.view-add-input` → Enter  → add
 *   - double-click `.view-name-btn`                   → rename (input)
 *   - right-click chip → ctx menu "Rename"/"Duplicate"/"Delete view"
 *   - chip `.view-del-btn[title="Move left"|"Move right"|"Delete view"]`
 *
 * Delete (both via ctx menu and chip X button) prompts through Tauri
 * plugin-dialog's `ask()` which goes over `invoke('plugin:dialog|message')`;
 * we stub that to return `'Yes'` to auto-confirm.
 *
 * Pro is auto-activated under WebDriver via navigator.webdriver
 * (license.ts) so multi-view features are unlocked unconditionally.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, activeViewName, ctxMenuItem, rightClick,
} from '../helpers/app.js';

const SCREENS_KEY = 'widget-screens';

/** Stub Tauri invoke so plugin-dialog's ask() resolves to true (YesNo → 'Yes'). */
async function stubAskYes(): Promise<void> {
  await browser.execute(() => {
    const internals = (window as any).__TAURI_INTERNALS__ ?? ((window as any).__TAURI_INTERNALS__ = {});
    const original = internals.__originalInvoke ?? internals.invoke;
    if (!internals.__originalInvoke) internals.__originalInvoke = original;
    internals.invoke = async (cmd: string, args: any) => {
      if (cmd === 'plugin:dialog|message') return 'Yes';
      if (original) return original(cmd, args);
      throw new Error(`invoke stub miss: "${cmd}"`);
    };
  });
}

/** Read views list from localStorage. */
async function readViews(): Promise<{ id: string; name: string }[]> {
  return browser.execute((k: string) => {
    try { return JSON.parse(localStorage.getItem(k) ?? '[]'); }
    catch { return []; }
  }, SCREENS_KEY);
}

/** Add a new view via the `+` button + input. */
async function addView(name: string): Promise<void> {
  await $('.view-add-btn').click();
  const input = await $('.view-add-input');
  await input.setValue(name);
  await browser.keys(['Enter']);
  await browser.waitUntil(async () => {
    const list = await readViews();
    return list.some(s => s.name === name);
  }, { timeout: 2_000 });
}

/** Get the chip element whose `.view-name-text` matches `name`. */
async function chipByName(name: string): Promise<WebdriverIO.Element> {
  for await (const c of $$('.view-chip')) {
    const t = (await c.$('.view-name-text').getText()).trim();
    if (t === name) return c;
  }
  throw new Error(`no view chip named "${name}"`);
}

// Skipped: delete flow uses plugin-dialog.ask() which hangs session cleanup.
describe.skip('view lifecycle: rename / duplicate / delete / reorder', () => {
  beforeEach(async () => { await resetAppState(); });

  it('adds a new view via edit-mode + button', async () => {
    await enterEditMode();
    await addView('Foo');

    const list = await readViews();
    expect(list.map(s => s.name)).to.include('Foo');

    // The chip is visible and clickable.
    const chip = await chipByName('Foo');
    expect(await chip.isDisplayed()).to.equal(true);
  });

  it('double-click renames a chip via the inline input', async () => {
    await enterEditMode();
    await addView('Foo');

    // WebDriver's native doubleClick is flaky on small buttons with inline
    // SVG icons, so dispatch a bubbling `dblclick` event directly against the
    // `.view-name-btn` for the Foo chip. Solid's `onDblClick` handler only
    // starts rename when editMode && !isDemoView, both true here.
    await browser.execute(() => {
      const chips = Array.from(document.querySelectorAll<HTMLElement>('.view-chip'));
      for (const c of chips) {
        const name = c.querySelector('.view-name-text')?.textContent ?? '';
        if (name === 'Foo') {
          const btn = c.querySelector<HTMLButtonElement>('.view-name-btn');
          btn?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
          return;
        }
      }
    });

    // Rename input appears and is auto-focused.
    const input = await $('.view-chip .view-input');
    await input.waitForDisplayed({ timeout: 2_000 });

    // Clear + type new value via DOM setter (WebDriver's setValue fights auto-focus).
    await browser.execute(() => {
      const inp = document.querySelector<HTMLInputElement>('.view-chip .view-input');
      if (!inp) return;
      const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype, 'value',
      )?.set;
      setter?.call(inp, 'Bar');
      inp.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await browser.keys(['Enter']);

    await browser.waitUntil(async () => {
      const list = await readViews();
      return list.some(s => s.name === 'Bar') && !list.some(s => s.name === 'Foo');
    }, { timeout: 2_000 });
  });

  it('ctx menu "Duplicate" creates a new view with "(Copy)" suffix', async () => {
    await enterEditMode();
    await addView('Bar');

    const chip = await chipByName('Bar');
    await chip.click({ button: 'right' });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    const dup = await ctxMenuItem('Duplicate');
    await dup.click();

    await browser.waitUntil(async () => {
      const list = await readViews();
      return list.some(s => s.name === 'Bar (Copy)');
    }, { timeout: 2_000 });

    // Original still exists too.
    const list = await readViews();
    expect(list.map(s => s.name)).to.include('Bar');
    expect(list.map(s => s.name)).to.include('Bar (Copy)');
  });

  it('ctx menu "Delete view" removes the chip after confirm', async () => {
    await enterEditMode();
    await addView('Bar');
    await stubAskYes();

    const chip = await chipByName('Bar');
    await chip.click({ button: 'right' });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    // Click the "Delete view" ctx item via DOM click: WebDriver's
    // geometric click can be intercepted by the inline lucide-solid SVG
    // icon inside the item, leaving the async `ask()` action unexecuted.
    const clicked = await browser.execute(() => {
      const items = Array.from(document.querySelectorAll<HTMLElement>('.ctx-menu .ctx-item'));
      for (const it of items) {
        const label = it.querySelector('.ctx-label')?.textContent?.trim() ?? '';
        if (label === 'Delete view') { it.click(); return true; }
      }
      return false;
    });
    expect(clicked).to.equal(true);

    await browser.waitUntil(async () => {
      const list = await readViews();
      return !list.some(s => s.name === 'Bar');
    }, { timeout: 5_000 });
  });

  it('chip "Move left" button reorders views in localStorage', async () => {
    await enterEditMode();
    await addView('A');
    await addView('B');

    // Initial order: Default, A, B.
    let list = await readViews();
    expect(list.map(s => s.name)).to.deep.equal(['Default', 'A', 'B']);

    // Click Move-left on the B chip.
    await browser.execute(() => {
      const chips = Array.from(document.querySelectorAll<HTMLElement>('.view-chip'));
      for (const c of chips) {
        const name = c.querySelector('.view-name-text')?.textContent ?? '';
        if (name === 'B') {
          const btn = c.querySelector<HTMLButtonElement>('.view-del-btn[title="Move left"]');
          btn?.click();
          return;
        }
      }
    });

    // Order after one move-left on B: Default, B, A.
    await browser.waitUntil(async () => {
      list = await readViews();
      return list.map(s => s.name).join(',') === 'Default,B,A';
    }, { timeout: 2_000 });
  });

  it('ctx menu "Move left" is disabled on the leftmost chip', async () => {
    await enterEditMode();
    await addView('A');

    // Right-click the Default chip (index 0).
    const chip = await chipByName('Default');
    await chip.click({ button: 'right' });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    // The Move-left item exists but is `.disabled`.
    const disabled = await browser.execute(() => {
      const items = Array.from(document.querySelectorAll<HTMLElement>('.ctx-menu .ctx-item'));
      for (const it of items) {
        const label = it.querySelector('.ctx-label')?.textContent?.trim() ?? '';
        if (label === 'Move left') return it.className.includes('disabled');
      }
      return null;
    });
    expect(disabled).to.equal(true);

    await browser.keys(['Escape']);
  });

  it('duplicating a view also copies its layout contents', async () => {
    await enterEditMode();
    await addView('Src');

    // Seed a distinctive widget-list into Src's layout key (widget-layout-<id>).
    const srcId = (await readViews()).find(s => s.name === 'Src')!.id;
    await browser.execute((id: string) => {
      localStorage.setItem(`widget-layout-${id}`, JSON.stringify({
        widgets: [{ id: 'marker-1', type: 'clock', col: 1, row: 1, cols: 2, rows: 2 }],
        widgetGap: 12, bgImage: '', bgImageFit: 'cover',
      }));
    }, srcId);

    // Duplicate via ctx menu.
    const chip = await chipByName('Src');
    await chip.click({ button: 'right' });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
    await (await ctxMenuItem('Duplicate')).click();

    await browser.waitUntil(async () => {
      const list = await readViews();
      return list.some(s => s.name === 'Src (Copy)');
    }, { timeout: 2_000 });

    // The new view's layout has the same number of widgets (IDs are
    // regenerated by duplicateView(), but count + type are preserved).
    const newId = (await readViews()).find(s => s.name === 'Src (Copy)')!.id;
    const layout = await browser.execute((id: string) => {
      try { return JSON.parse(localStorage.getItem(`widget-layout-${id}`) ?? '{}'); }
      catch { return {}; }
    }, newId) as any;
    expect(layout.widgets).to.have.lengthOf(1);
    expect(layout.widgets[0].type).to.equal('clock');
  });
});
