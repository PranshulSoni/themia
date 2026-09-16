/**
 * Shortcut widget: URL drops, cross-widget drag, and Ctrl+Z undo.
 *
 * Exercises three behaviours layered on top of the multi-item base widget:
 *
 *  1. Native URL drops (Rust CFSTR_INETURLW / CF_UNICODETEXT fallback) create
 *     new URL items.
 *  2. Dropping a shortcut-item payload on a different shortcut widget adds
 *     the item to the receiver (cross-widget via OLE widget-data JSON).
 *  3. Every user-initiated mutation records a `widget-config` op-log entry,
 *     and `undoLast()` restores the previous state.
 *
 * Drops are driven from the frontend via `window.__nativeDragDispatch`,
 * the test hook added to `initNativeDragListener` so webdriver specs can
 * bypass the Tauri event bus (which isn't available under webdriver).
 *
 * SPEC tags mirrored in test titles.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS, stubTauriInvoke } from '../helpers/app.js';

type SeedItem = { id: string; path: string; label?: string };

async function seedOneWidget(items: SeedItem[], extra: Record<string, any> = {}): Promise<void> {
  await browser.execute((key: string, its: SeedItem[], ex: Record<string, any>) => {
    const layout = {
      widgets: [{
        id: 'shortcut-1', type: 'shortcut', col: 1, row: 1, colSpan: 4, rowSpan: 4,
        config: {
          items: its, viewMode: 'list', itemSize: 'medium', clickMode: 'double',
          hideHeader: false, label: '', ...ex,
        },
      }],
      widgetGap: 12, bgImage: '', bgImageFit: 'cover',
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, items, extra);
  await $('.shortcut-widget').waitForExist({ timeout: 5_000 });
}

async function seedTwoWidgets(itemsA: SeedItem[], itemsB: SeedItem[]): Promise<void> {
  await browser.execute((key: string, a: SeedItem[], b: SeedItem[]) => {
    const mk = (id: string, col: number, its: SeedItem[]) => ({
      id, type: 'shortcut', col, row: 1, colSpan: 3, rowSpan: 3,
      config: {
        items: its, viewMode: 'list', itemSize: 'medium', clickMode: 'double',
        hideHeader: false, label: '',      },
    });
    const layout = {
      widgets: [mk('wA', 1, a), mk('wB', 5, b)],
      widgetGap: 12, bgImage: '', bgImageFit: 'cover',
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, itemsA, itemsB);
  await browser.waitUntil(async () => (await $$('.shortcut-widget').length) === 2,
    { timeout: 5_000, timeoutMsg: 'two shortcut widgets never rendered' });
}

async function storedItemsOf(widgetId: string): Promise<SeedItem[]> {
  return browser.execute((key: string, id: string) => {
    const raw = localStorage.getItem(key) ?? '{}';
    try {
      const parsed = JSON.parse(raw);
      const w = (parsed.widgets ?? []).find((x: any) => x.id === id);
      return (w?.config?.items ?? []) as SeedItem[];
    } catch { return []; }
  }, LS_KEYS.layoutRoot, widgetId);
}

async function dispatchNativeDrop(
  widgetSelector: string,
  payload: Partial<{
    filePaths: string[];
    urls: string[];
    widgetData: string;
    dataTypes: string[];
  }>,
): Promise<void> {
  await browser.execute((sel: string, p: any) => {
    let el: HTMLElement | null = null;
    const m = sel.match(/^(.*)\[(\d+)\]$/);
    if (m) {
      const matches = document.querySelectorAll(m[1]);
      el = matches[Number(m[2])] as HTMLElement | null;
    } else {
      el = document.querySelector(sel) as HTMLElement | null;
    }
    if (!el) throw new Error(`no element ${sel}`);
    const r = el.getBoundingClientRect();
    const base = {
      viewX: 0, viewY: 0,
      clientX: Math.round(r.left + r.width / 2),
      clientY: Math.round(r.top + r.height / 2),
      effect: 'copy',
      widgetData: null,
      filePaths: null,
      virtualFileNames: null,
      urls: null,
      ctrlHeld: false,
    };
    const enter = { ...base, eventType: 'enter', dataTypes: p.dataTypes ?? [] };
    const drop = {
      ...base,
      eventType: 'drop',
      dataTypes: p.dataTypes ?? [],
      filePaths: p.filePaths ?? null,
      urls: p.urls ?? null,
      widgetData: p.widgetData ?? null,
    };
    const host = (window as any).__nativeDragDispatch;
    if (!host) throw new Error('no __nativeDragDispatch');
    host(enter);
    host(drop);
  }, widgetSelector, payload);
}

describe('shortcut widget: URL drops, cross-widget DnD, Ctrl+Z undo', () => {
  beforeEach(async () => {
    await resetAppState();
    await browser.execute(() => { (window as any).__calls = []; });
    await stubTauriInvoke({
      open_url: null,
      open_in_explorer: null,
      get_file_icon: '',
      push_windows_to_desktop: null,
      resolve_shortcut: { target_path: '', is_directory: false },
      start_native_drag: { effect: 'none' },
    });
  });

  it('[S-34] native URL drop creates a URL item', async () => {
    await seedOneWidget([]);
    await dispatchNativeDrop('.shortcut-widget', {
      dataTypes: ['url'],
      urls: ['https://hello.example.com'],
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 1 && its[0]?.path === 'https://hello.example.com';
    }, { timeout: 3_000, timeoutMsg: 'url drop did not produce an item' });
  });

  it('[S-35] cross-widget drop of a shortcut-item payload adds it to the receiver', async () => {
    await seedTwoWidgets(
      [{ id: 'src-item', path: 'https://alpha.example.com', label: 'Alpha' }],
      [],
    );

    const payload = JSON.stringify({
      kind: 'shortcut-item',
      sourceWidgetId: 'wA',
      dragId: 'test-drag-1',
      item: { id: 'src-item', path: 'https://alpha.example.com', label: 'Alpha' },
    });
    await dispatchNativeDrop('.shortcut-widget[1]', {
      dataTypes: ['widget'],
      widgetData: payload,
    });

    await browser.waitUntil(async () => {
      const bItems = await storedItemsOf('wB');
      return bItems.length === 1 && bItems[0]?.path === 'https://alpha.example.com';
    }, { timeout: 3_000, timeoutMsg: 'cross-widget receive never applied' });
  });

  it('[S-36] Ctrl+Z undoes a remove performed via the item context menu', async () => {
    await seedOneWidget([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);

    // Open ctx on first item → click Remove.
    await browser.execute(() => {
      const el = document.querySelectorAll('.shortcut-item')[0] as HTMLElement;
      el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
    });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
      const rm = items.find(i => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Remove');
      rm?.click();
    });

    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 1 && its[0]?.id === 'b';
    }, { timeout: 3_000 });

    // Now Ctrl+Z via the op-log test hook.
    const result = await browser.execute(async () => {
      const h = (window as any).__opLog;
      if (!h) return { error: 'no __opLog' };
      const r = await h.undo();
      return r;
    });
    expect((result as any).ok).to.equal(true);

    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 2
        && its.some(x => x.id === 'a')
        && its.some(x => x.id === 'b');
    }, { timeout: 3_000, timeoutMsg: 'undo did not restore removed item' });
  });

  it('[S-36] Ctrl+Z undoes a reorder performed via drag', async () => {
    await seedOneWidget([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ]);

    // Reorder: drop C before A (HTML5 drag path, same one covered by spec 44).
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.shortcut-item')) as HTMLElement[];
      const src = items[2]; const tgt = items[0];
      const dt = new DataTransfer();
      const fire = (el: HTMLElement, type: string) => {
        el.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: dt }));
      };
      fire(src, 'dragstart'); fire(tgt, 'dragenter'); fire(tgt, 'dragover'); fire(tgt, 'drop'); fire(src, 'dragend');
    });

    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its[0]?.id === 'c';
    }, { timeout: 3_000 });

    const r = await browser.execute(async () => (window as any).__opLog?.undo());
    expect((r as any)?.ok).to.equal(true);

    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its[0]?.id === 'a' && its[1]?.id === 'b' && its[2]?.id === 'c';
    }, { timeout: 3_000, timeoutMsg: 'undo did not restore original order' });
  });
});
