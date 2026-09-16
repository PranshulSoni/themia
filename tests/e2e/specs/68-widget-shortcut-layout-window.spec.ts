/**
 * Shortcut widget: detached app-layout editor window.
 *
 * Editing a layout item pops the visual tree editor out into its own OS window
 * (`open_layout_window` → `index.html?layout=1`). The window reads + writes the
 * item's `layout` tree through the shared per-view layout store, so edits made
 * there persist back to the widget (verified via localStorage).
 *
 * SPEC tags: [S-58] (window opens + targets the item), [S-59] (split / rename
 * mutate the tree), [S-60] (in-pane source assignment, no picker window).
 *
 * NOTE: `__TAURI_INTERNALS__.invoke` is non-writable in the built debug binary,
 * so `invoke('open_layout_window', …)` reaches real Rust and actually spawns
 * the window, exactly what we want to exercise.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

type AnyItem = { id: string; path: string; label?: string; layout?: any };

async function seedLayoutWidget(item: AnyItem): Promise<void> {
  await browser.execute((key: string, it: AnyItem) => {
    const layout = {
      widgets: [{
        id: 'shortcut-1', type: 'shortcut', col: 1, row: 1, colSpan: 4, rowSpan: 4,
        config: { items: [it], viewMode: 'list', clickMode: 'single', hideHeader: false, label: '' },
      }],
      widgetGap: 12,
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: serialized, oldValue: prior, storageArea: localStorage }));
  }, LS_KEYS.layoutRoot, item);
  await $('.shortcut-widget').waitForExist({ timeout: 5_000 });
}

async function openLayoutWindow(): Promise<void> {
  await browser.execute(() => {
    const internals: any = (window as any).__TAURI_INTERNALS__;
    const label = internals?.metadata?.currentWindow?.label ?? 'main';
    internals.invoke('open_layout_window', {
      parentLabel: label, widgetId: 'shortcut-1', viewId: 'default', itemId: 'lay',
    });
  });
}

async function switchToWindowWith(param: string, timeoutMs = 8_000): Promise<string> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const handles = await browser.getWindowHandles();
    for (const h of handles) {
      try {
        await browser.switchToWindow(h);
        const match = await browser.execute((p: string) => new URLSearchParams(location.search).has(p), param);
        if (match) return h;
      } catch { /* stale handle */ }
    }
    await browser.pause(150);
  }
  throw new Error(`no window with ?${param} appeared`);
}

async function storedLayoutItem(): Promise<AnyItem | undefined> {
  return browser.execute((key: string) => {
    try {
      const parsed = JSON.parse(localStorage.getItem(key) ?? '{}');
      const w = (parsed.widgets ?? []).find((x: any) => x.type === 'shortcut');
      return (w?.config?.items ?? []).find((i: any) => i.id === 'lay');
    } catch { return undefined; }
  }, LS_KEYS.layoutRoot);
}

describe('shortcut widget: app-layout editor window', () => {
  let mainHandle: string;

  beforeEach(async () => {
    await resetAppState();
    mainHandle = await browser.getWindowHandle();
  });

  afterEach(async () => {
    // Close the detached layout / picker windows so they don't leak into the
    // next spec, then return to the main window.
    try {
      await browser.execute(() => {
        const i: any = (window as any).__TAURI_INTERNALS__;
        i?.invoke?.('diag_close_windows_by_prefix', { prefix: 'layout' });
      });
    } catch { /* best effort */ }
    try { await browser.switchToWindow(mainHandle); } catch {}
  });

  it('[S-58] editing a layout opens a detached editor window targeting the item', async () => {
    await seedLayoutWidget({ id: 'lay', path: 'layout:', label: 'My Layout', layout: { kind: 'leaf', id: 'L0' } });
    await openLayoutWindow();
    await switchToWindowWith('layout');

    await $('.layout-window').waitForExist({ timeout: 5_000 });
    // The seeded item loaded: its name is in the header field and a single pane renders.
    const name = await $('.layout-window .layout-name-input').getValue();
    expect(name).to.equal('My Layout');
    expect(await $$('.applayouts-leaf').length).to.equal(1);
  });

  it('[S-59] splitting a pane in the editor window mutates the stored layout tree', async () => {
    await seedLayoutWidget({ id: 'lay', path: 'layout:', label: 'My Layout', layout: { kind: 'leaf', id: 'L0' } });
    await openLayoutWindow();
    await switchToWindowWith('layout');
    await $('.layout-window').waitForExist({ timeout: 5_000 });

    // Click the leaf's "split into left & right" button (in the hover toolbar;
    // present in the DOM regardless of hover opacity).
    await browser.execute(() => {
      const btn = Array.from(document.querySelectorAll('.applayouts-leaf-btn'))
        .find(b => b.getAttribute('title') === 'Split into left & right') as HTMLElement | undefined;
      btn?.click();
    });

    await browser.waitUntil(async () => {
      const it = await storedLayoutItem();
      return it?.layout?.kind === 'split';
    }, { timeout: 4_000, timeoutMsg: 'split did not persist to the stored tree' });

    const it = await storedLayoutItem();
    expect(it!.layout.kind).to.equal('split');
    expect(it!.layout.a?.id).to.equal('L0'); // original leaf preserved as child a
  });

  it('[S-59] editing the name in the editor window persists to the item label', async () => {
    await seedLayoutWidget({ id: 'lay', path: 'layout:', label: 'Old', layout: { kind: 'leaf', id: 'L0' } });
    await openLayoutWindow();
    await switchToWindowWith('layout');
    await $('.layout-window').waitForExist({ timeout: 5_000 });

    await browser.execute(() => {
      const el = document.querySelector('.layout-window .layout-name-input') as HTMLInputElement | null;
      if (!el) return;
      el.value = 'Renamed';
      el.dispatchEvent(new Event('input', { bubbles: true }));
    });

    await browser.waitUntil(async () => (await storedLayoutItem())?.label === 'Renamed',
      { timeout: 4_000, timeoutMsg: 'rename did not persist' });
  });

  it('[S-60] assigning a pane in-pane (source dropdown + URL) applies + persists', async () => {
    await seedLayoutWidget({ id: 'lay', path: 'layout:', label: 'My Layout', layout: { kind: 'leaf', id: 'L0' } });
    await openLayoutWindow();
    await switchToWindowWith('layout');
    await $('.layout-window').waitForExist({ timeout: 5_000 });
    await $('.applayouts-leaf').waitForExist({ timeout: 3_000 });

    // Open the pane's Source dropdown and switch it to URL.
    await browser.execute(() => {
      (document.querySelector('.leaf-assign-source') as HTMLElement | null)?.click();
    });
    await browser.waitUntil(async () => browser.execute(() =>
      Array.from(document.querySelectorAll('.dropdown-item')).some(i => (i.textContent ?? '').trim() === 'URL')),
      { timeout: 3_000, timeoutMsg: 'source options did not open' });
    await browser.execute(() => {
      const it = Array.from(document.querySelectorAll('.dropdown-item'))
        .find(i => (i.textContent ?? '').trim() === 'URL') as HTMLElement | undefined;
      it?.click();
    });

    // Type the URL into the in-pane input and commit (change = blur/Enter).
    await $('.leaf-assign-input').waitForExist({ timeout: 3_000 });
    await browser.execute(() => {
      const el = document.querySelector('.leaf-assign-input') as HTMLInputElement | null;
      if (!el) return;
      el.value = 'https://assigned.example';
      el.dispatchEvent(new Event('change', { bubbles: true }));
    });

    await browser.waitUntil(async () => {
      const it = await storedLayoutItem();
      return it?.layout?.assignment?.target === 'https://assigned.example';
    }, { timeout: 5_000, timeoutMsg: 'in-pane assignment did not reach the layout' });

    const it = await storedLayoutItem();
    expect(it!.layout.assignment.kind).to.equal('url');
  });
});
