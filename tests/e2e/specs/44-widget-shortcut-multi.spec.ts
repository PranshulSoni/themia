/**
 * Shortcut widget: multi-item collection.
 *
 * Covers the refactor where `config.path` (single target) became
 * `config.items[]` (ordered collection). The widget holds URL, file,
 * and `shell:` items, opens each on double-click, supports view-mode
 * switching, reorder, per-item ctx menu, and accepts native file drops
 * that append new items.
 *
 * SPEC tags mirrored in test titles.
 *
 * These specs stub `__TAURI_INTERNALS__.invoke` via `stubTauriInvoke`
 * so open_url / open_in_explorer / get_file_icon / resolve_shortcut
 * don't hit the real OS. Drops are simulated by emitting a `native-drag`
 * Tauri event payload through `window.__TAURI_INTERNALS__.metadata`;
 * instead we call `registerDropZone`'s handler via a test hook exposed
 * on the widget root.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS, stubTauriInvoke } from '../helpers/app.js';

type SeedItem = { id: string; path: string; label?: string };

async function seedShortcutWidget(items: SeedItem[], extra: Record<string, any> = {}): Promise<void> {
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
  await $('.widget').waitForExist({ timeout: 5_000 });
  await $('.shortcut-widget').waitForExist({ timeout: 5_000 });
}

async function setupInvokeRecorder(): Promise<void> {
  await browser.execute(() => { (window as any).__calls = []; });
  await stubTauriInvoke({
    open_url: null,
    open_in_explorer: null,
    open_file: null,
    get_file_icon: '',
    push_windows_to_desktop: null,
    resolve_shortcut: { target_path: 'C:/resolved/target.exe', is_directory: false },
    fs_exists: true,
    activate_widget_keyboard: null,
  });
  // Wrap invoke to also record calls.
  await browser.execute(() => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const inner = internals.invoke;
    internals.invoke = async (cmd: string, args: any) => {
      (window as any).__calls.push({ cmd, args });
      return inner(cmd, args);
    };
  });
}

async function recordedCalls(): Promise<Array<{ cmd: string; args: any }>> {
  return browser.execute(() => (window as any).__calls ?? []);
}

async function storedItems(): Promise<SeedItem[]> {
  return browser.execute((key: string) => {
    const raw = localStorage.getItem(key) ?? '{}';
    try {
      const parsed = JSON.parse(raw);
      const w = (parsed.widgets ?? []).find((x: any) => x.type === 'shortcut');
      return (w?.config?.items ?? []) as SeedItem[];
    } catch { return []; }
  }, LS_KEYS.layoutRoot);
}

describe('shortcut widget: multi-item collection', () => {
  beforeEach(async () => {
    await resetAppState();
    await setupInvokeRecorder();
  });

  it('[S-02] shows empty state when no items are configured', async () => {
    await seedShortcutWidget([]);
    const text = await $('.shortcut-widget').getText();
    expect(text.toLowerCase()).to.match(/drop|add|configure/);
  });

  it('[S-21] renders each item from config.items in order', async () => {
    await seedShortcutWidget([
      { id: 'a', path: 'https://example.com', label: 'Example' },
      { id: 'b', path: 'C:/tools/myapp.exe', label: 'My App' },
      { id: 'c', path: 'shell:RecycleBinFolder' },
    ]);
    const nodes = await $$('.shortcut-item');
    expect(await nodes.length).to.equal(3);

    const names: string[] = [];
    for await (const n of $$('.shortcut-item .shortcut-name')) {
      names.push((await n.getText()).trim());
    }
    expect(names).to.deep.equal(['Example', 'My App', 'Recycle Bin']);
  });

  it('[S-22] viewMode toggles root class between list / grid / details', async () => {
    await seedShortcutWidget(
      [{ id: 'a', path: 'https://example.com' }],
      { viewMode: 'grid' },
    );
    let root = await $('.shortcut-widget');
    expect(await root.getAttribute('class')).to.match(/view-grid/);

    // Flip viewMode via localStorage → storage event.
    await browser.execute((key: string) => {
      const raw = localStorage.getItem(key) ?? '{}';
      const parsed = JSON.parse(raw);
      const w = parsed.widgets[0];
      w.config.viewMode = 'details';
      const serialized = JSON.stringify(parsed);
      localStorage.setItem(key, serialized);
      window.dispatchEvent(new StorageEvent('storage', {
        key, newValue: serialized, oldValue: raw, storageArea: localStorage,
      }));
    }, LS_KEYS.layoutRoot);

    await browser.waitUntil(async () => {
      const cls = await $('.shortcut-widget').getAttribute('class');
      return /view-details/.test(cls);
    }, { timeout: 2_000, timeoutMsg: 'view-details class never applied' });
  });

  // Tauri 2 makes `__TAURI_INTERNALS__.invoke` non-writable, so the
  // recorder-wrapper from setupInvokeRecorder doesn't actually intercept
  // calls in a built debug build. The widget exposes a webdriver-only
  // `__shortcutLastOpen` global that mirrors the routing decision;
  // assert against that instead.
  async function lastOpen(): Promise<{ kind: string; path: string } | null> {
    return browser.execute(() => (window as any).__shortcutLastOpen ?? null) as any;
  }

  it('[S-12][S-15] URL item double-click routes to open_url (kind=url)', async () => {
    await seedShortcutWidget([{ id: 'a', path: 'https://example.com' }]);
    await browser.execute(() => { (window as any).__shortcutLastOpen = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    });
    await browser.waitUntil(async () => {
      const v = await lastOpen();
      return v?.kind === 'url' && v.path === 'https://example.com';
    }, { timeout: 3_000, timeoutMsg: 'URL open path not fired' });
  });

  it('[S-13][S-15] file item double-click routes to open_file (kind=file)', async () => {
    await seedShortcutWidget([{ id: 'a', path: 'C:/tools/myapp.exe' }]);
    await browser.execute(() => { (window as any).__shortcutLastOpen = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    });
    await browser.waitUntil(async () => {
      const v = await lastOpen();
      return v?.kind === 'file' && v.path.includes('myapp.exe');
    }, { timeout: 3_000, timeoutMsg: 'file open path not fired' });
  });

  it('[S-19] shell item double-click routes to open_in_explorer (kind=shell)', async () => {
    await seedShortcutWidget([{ id: 'a', path: 'shell:RecycleBinFolder' }]);
    await browser.execute(() => { (window as any).__shortcutLastOpen = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    });
    await browser.waitUntil(async () => {
      const v = await lastOpen();
      return v?.kind === 'shell' && v.path === 'shell:RecycleBinFolder';
    }, { timeout: 3_000, timeoutMsg: 'shell open path not fired' });
  });

  it('[S-33] clickMode=single opens on single click instead of double', async () => {
    await seedShortcutWidget(
      [{ id: 'a', path: 'https://example.com' }],
      { clickMode: 'single' },
    );
    await browser.execute(() => { (window as any).__shortcutLastOpen = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    });
    await browser.waitUntil(async () => {
      const v = await lastOpen();
      return v?.kind === 'url';
    }, { timeout: 3_000, timeoutMsg: 'single-click did not fire URL open' });
  });

  it('[S-28] native file drop appends file items to the widget', async () => {
    await seedShortcutWidget([]);

    // Simulate a native-drag drop via the Tauri window event that
    // `initNativeDragListener` subscribes to. The event listener
    // registered by registerDropZone on the widget inspects filePaths
    // and dispatches onDrop.
    await browser.execute(() => {
      const internals: any = (window as any).__TAURI_INTERNALS__ ?? {};
      // Get the widget root for hit-testing
      const el = document.querySelector('.shortcut-widget') as HTMLElement | null;
      if (!el) throw new Error('no .shortcut-widget');
      const r = el.getBoundingClientRect();
      const payload = {
        eventType: 'drop',
        screenX: 0, screenY: 0,
        clientX: Math.round(r.left + r.width / 2),
        clientY: Math.round(r.top + r.height / 2),
        dataTypes: ['files'],
        effect: 'copy',
        widgetData: null,
        filePaths: ['C:/dropped/one.txt', 'C:/dropped/two.png'],
        virtualFileNames: null,
        ctrlHeld: false,
      };
      // Prime an 'enter' to arm the zone.
      const enter = { ...payload, eventType: 'enter' };
      const host: any = (window as any).__nativeDragDispatch ?? null;
      if (host) {
        host(enter); host(payload);
      } else {
        // Fallback: post a CustomEvent the listener in app picks up.
        window.dispatchEvent(new CustomEvent('native-drag', { detail: enter }));
        window.dispatchEvent(new CustomEvent('native-drag', { detail: payload }));
      }
    });

    await browser.waitUntil(async () => {
      const its = await storedItems();
      return its.length === 2
        && its.some(i => i.path === 'C:/dropped/one.txt')
        && its.some(i => i.path === 'C:/dropped/two.png');
    }, { timeout: 3_000, timeoutMsg: 'dropped files did not become items' });
  });

  it('[S-31] reordering persists the new item order', async () => {
    await seedShortcutWidget([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ]);

    // Simulate HTML5 drag of item C (index 2) before item A (index 0).
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.shortcut-item')) as HTMLElement[];
      if (items.length < 3) throw new Error('expected 3 items');
      const src = items[2]; const tgt = items[0];
      const dt = new DataTransfer();
      const fire = (el: HTMLElement, type: string, evInit: any = {}) => {
        const ev = new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: dt, ...evInit });
        el.dispatchEvent(ev);
      };
      fire(src, 'dragstart');
      fire(tgt, 'dragenter');
      fire(tgt, 'dragover');
      fire(tgt, 'drop');
      fire(src, 'dragend');
    });

    await browser.waitUntil(async () => {
      const its = await storedItems();
      return its.length === 3 && its[0]?.id === 'c';
    }, { timeout: 3_000, timeoutMsg: 'reorder did not persist' });
  });

  it('[S-11][S-26] item context menu Remove deletes the item', async () => {
    await seedShortcutWidget([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);

    await browser.execute(() => {
      const el = document.querySelectorAll('.shortcut-item')[0] as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
    });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    // Click Remove.
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
      const rm = items.find(i => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Remove');
      rm?.click();
    });

    await browser.waitUntil(async () => {
      const its = await storedItems();
      return its.length === 1 && its[0]?.id === 'b';
    }, { timeout: 3_000, timeoutMsg: 'Remove did not persist' });
  });

  it('[S-24] hideHeader=true hides the widget header', async () => {
    await seedShortcutWidget(
      [{ id: 'a', path: 'https://example.com' }],
      { hideHeader: true },
    );
    const headers = await $$('.shortcut-widget .widget-header');
    expect(await headers.length).to.equal(0);
  });
});
