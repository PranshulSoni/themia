/**
 * Shortcut widget: comprehensive drag-and-drop coverage.
 *
 * Exercises every drop path the widget exposes via the OLE/native event bus
 * (the path real users hit, not the legacy HTML5 dispatch fallback used by
 * earlier specs). All drops are dispatched through `__nativeDragDispatch`,
 * the test hook in `initNativeDragListener`. Each test fires `enter` first
 * so the receiving zone is armed, then `drop` with the relevant payload at
 * coords inside the target widget (or item).
 *
 * Cases
 * ─────
 *  External drops onto widget:
 *    [A] empty widget receives plain file drop                  → file item
 *    [B] empty widget receives URL drop                          → URL item
 *    [C] non-empty widget receives multiple file drop            → all appended
 *
 *  Intra-widget reorder via OLE widget-data:
 *    [D] drop item C (idx 2) onto item A (idx 0) → [C, A, B]
 *    [E] drop item A (idx 0) onto item C (idx 2) → [B, A, C]    (insert-before)
 *    [F] drop item onto itself                    → no-op
 *
 *  Cross-widget receive via OLE widget-data:
 *    [G] drag from widget A onto widget B → B gains a fresh-id copy
 *    [H] cross-widget receive preserves label + path
 *
 *  Edge cases:
 *    [I] drop with malformed widgetData JSON falls through silently
 *    [J] external drop on widget while edit mode is on is ignored
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { resetAppState, LS_KEYS, stubTauriInvoke } from '../helpers/app.js';

type SeedItem = { id: string; path: string; label?: string };

async function seedOne(items: SeedItem[], extra: Record<string, any> = {}): Promise<void> {
  await browser.execute((key: string, its: SeedItem[], ex: Record<string, any>) => {
    const layout = {
      widgets: [{
        id: 'shortcut-1', type: 'shortcut', x: 0.05, y: 0.05, w: 0.5, h: 0.6,
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

async function seedTwo(itemsA: SeedItem[], itemsB: SeedItem[]): Promise<void> {
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
    { timeout: 5_000 });
}

async function storedItemsOf(widgetId: string): Promise<SeedItem[]> {
  return browser.execute((key: string, id: string) => {
    try {
      const layout = JSON.parse(localStorage.getItem(key) ?? '{}');
      const w = (layout.widgets ?? []).find((x: any) => x.id === id);
      return (w?.config?.items ?? []) as SeedItem[];
    } catch { return []; }
  }, LS_KEYS.layoutRoot, widgetId);
}

interface DropPayload {
  filePaths?: string[];
  urls?: string[];
  widgetData?: string;
  dataTypes?: string[];
}

/** Dispatch enter+drop at the centre of the element matched by `selector`.
 *  Supports a `[N]` suffix to pick the Nth document.querySelectorAll match
 *  (0-indexed), needed because two shortcut widgets aren't siblings in the
 *  DOM (they're flat children of `.grid` interleaved with other markup), so
 *  `nth-of-type` doesn't reliably select widget B. */
async function dispatchAt(selector: string, p: DropPayload): Promise<void> {
  await browser.execute((sel: string, payload: any) => {
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
      screenX: 0, screenY: 0,
      clientX: Math.round(r.left + r.width / 2),
      clientY: Math.round(r.top + r.height / 2),
      effect: 'copy',
      widgetData: null, filePaths: null, virtualFileNames: null, urls: null,
      ctrlHeld: false,
    };
    // Carry widgetData on `enter` too: the widget builds its live reorder preview
    // in updateLivePreview() off the enter/over event's widgetData (real OS `over`
    // events carry no payload, so the widget accepts a self-sourced shortcut-item
    // here). Without it the preview never builds and the drop commits no reorder.
    // External drops (filePaths/urls) pass widgetData:null, so they're unaffected.
    const enter = { ...base, eventType: 'enter', dataTypes: payload.dataTypes ?? [], widgetData: payload.widgetData ?? null };
    const drop = {
      ...base,
      eventType: 'drop',
      dataTypes: payload.dataTypes ?? [],
      filePaths: payload.filePaths ?? null,
      urls: payload.urls ?? null,
      widgetData: payload.widgetData ?? null,
    };
    const host = (window as any).__nativeDragDispatch;
    if (!host) throw new Error('no __nativeDragDispatch hook');
    host(enter); host(drop);
  }, selector, p);
}

function widgetItemSel(itemId: string): string {
  return `.shortcut-item[data-id="${itemId}"]`;
}

describe('shortcut widget: comprehensive DnD', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubTauriInvoke({
      open_url: null, open_in_explorer: null, open_file: null,
      get_file_icon: '', push_windows_to_desktop: null,
      resolve_shortcut: { target_path: '', is_directory: false },
      start_native_drag: { effect: 'none' },
      fs_exists: true,
      activate_widget_keyboard: null,
    });
  });

  // ── External drops ────────────────────────────────────────────────────────
  it('[A] empty widget: external file drop appends a file item', async () => {
    await seedOne([]);
    await dispatchAt('.shortcut-widget', {
      dataTypes: ['files'],
      filePaths: ['C:/tools/dropped.exe'],
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 1 && its[0]?.path === 'C:/tools/dropped.exe';
    }, { timeout: 3_000, timeoutMsg: 'file drop never persisted' });
  });

  it('[B] empty widget: external URL drop appends a URL item', async () => {
    await seedOne([]);
    await dispatchAt('.shortcut-widget', {
      dataTypes: ['url'],
      urls: ['https://hello.example.com'],
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 1 && its[0]?.path === 'https://hello.example.com';
    }, { timeout: 3_000, timeoutMsg: 'URL drop never persisted' });
  });

  it('[C] external file drop with N paths appends N items', async () => {
    await seedOne([{ id: 'pre', path: 'C:/pre.exe' }]);
    await dispatchAt('.shortcut-widget', {
      dataTypes: ['files'],
      filePaths: ['C:/x.txt', 'C:/y.png', 'C:/z.zip'],
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 4
        && its[0]?.path === 'C:/pre.exe'
        && its.some(i => i.path === 'C:/x.txt')
        && its.some(i => i.path === 'C:/y.png')
        && its.some(i => i.path === 'C:/z.zip');
    }, { timeout: 3_000, timeoutMsg: 'multi-file drop never produced 4 items' });
  });

  // ── Intra-widget reorder via OLE widget-data ─────────────────────────────
  function reorderPayload(srcWidgetId: string, item: SeedItem): string {
    return JSON.stringify({
      kind: 'shortcut-item',
      sourceWidgetId: srcWidgetId,
      dragId: `t-${item.id}-${Date.now()}`,
      item,
    });
  }

  it('[D] reorder: drop item C (idx 2) onto item A (idx 0) → [C, A, B]', async () => {
    const items: SeedItem[] = [
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ];
    await seedOne(items);
    await dispatchAt(widgetItemSel('a'), {
      dataTypes: ['widget'],
      widgetData: reorderPayload('shortcut-1', items[2]),
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its[0]?.id === 'c' && its[1]?.id === 'a' && its[2]?.id === 'b';
    }, { timeout: 3_000, timeoutMsg: 'reorder D did not produce [C, A, B]' });
  });

  it('[E] reorder: drop item A (idx 0) onto item C (idx 2) → [B, A, C]', async () => {
    const items: SeedItem[] = [
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ];
    await seedOne(items);
    await dispatchAt(widgetItemSel('c'), {
      dataTypes: ['widget'],
      widgetData: reorderPayload('shortcut-1', items[0]),
    });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its[0]?.id === 'b' && its[1]?.id === 'a' && its[2]?.id === 'c';
    }, { timeout: 3_000, timeoutMsg: 'reorder E did not produce [B, A, C]' });
  });

  it('[F] reorder: drop item onto itself is a no-op', async () => {
    const items: SeedItem[] = [
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ];
    await seedOne(items);
    await dispatchAt(widgetItemSel('a'), {
      dataTypes: ['widget'],
      widgetData: reorderPayload('shortcut-1', items[0]),
    });
    // Give async writes a beat to settle, then assert order unchanged.
    await browser.pause(200);
    const its = await storedItemsOf('shortcut-1');
    expect(its.map(i => i.id)).to.deep.equal(['a', 'b']);
  });

  it('[S-38] icon images are non-draggable (no native image drag racing OLE)', async () => {
    // Grabbing a URL item by its favicon must NOT start an HTML5 image drag,
    // otherwise the favicon `https://` src is captured as a URL drop on release
    // and appended as a spurious "weird link". Icons must be draggable={false}.
    await seedOne([{ id: 'a', path: 'https://a.example.com', label: 'A' }]);
    await $(widgetItemSel('a')).waitForExist({ timeout: 3_000 });
    const draggable = await browser.execute(() => {
      const img = document.querySelector('.shortcut-item .shortcut-icon-img') as HTMLImageElement | null;
      return img ? img.draggable : null;
    });
    expect(draggable).to.equal(false);
  });

  it('[F2] reorder: drop on empty space appends to the end (does not vanish)', async () => {
    const items: SeedItem[] = [
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ];
    await seedOne(items);
    // Drop item A on the list background BELOW all rows (no row under the
    // cursor). Regression guard: this used to match no row → no-op → the OLE
    // sender then removed the item on effect='move', so it disappeared. It must
    // now move to the end instead, and never be lost. Dispatch at an explicit
    // low point in the empty area below the last row (dispatchAt only targets
    // an element's centre, which would land on a row here).
    await browser.execute((payload: string) => {
      const list = document.querySelector('.shortcut-list') as HTMLElement | null;
      const last = document.querySelector('.shortcut-item[data-id="c"]') as HTMLElement | null;
      if (!list || !last) throw new Error('list/last row not found');
      const lr = list.getBoundingClientRect();
      const cr = last.getBoundingClientRect();
      // Midway between the last row's bottom and the list's bottom → empty space.
      const clientX = Math.round(lr.left + lr.width / 2);
      const clientY = Math.round((cr.bottom + lr.bottom) / 2);
      const base = {
        screenX: 0, screenY: 0, clientX, clientY, effect: 'move',
        dataTypes: ['widget'], filePaths: null, virtualFileNames: null,
        urls: null, ctrlHeld: false,
      };
      const host = (window as any).__nativeDragDispatch;
      // widgetData on enter too, so the widget builds its reorder preview before
      // the drop commits it (updateLivePreview reads it off enter/over).
      host({ ...base, eventType: 'enter', widgetData: payload });
      host({ ...base, eventType: 'drop', widgetData: payload });
    }, reorderPayload('shortcut-1', items[0]));
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its.length === 3 && its[0]?.id === 'b' && its[1]?.id === 'c' && its[2]?.id === 'a';
    }, { timeout: 3_000, timeoutMsg: 'empty-space drop did not append A to the end' });
  });

  it('[S-65] reorder previews live on `over` (before drop), commits on drop', async () => {
    const items: SeedItem[] = [
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
      { id: 'c', path: 'https://c.example.com', label: 'C' },
    ];
    await seedOne(items);
    const payload = reorderPayload('shortcut-1', items[0]); // dragging A

    // Fire enter + over (NO drop) over row C: inserting A before C yields the
    // live order [B, A, C] in the DOM, while the stored config stays [A, B, C].
    await browser.execute((p: string) => {
      const c = document.querySelector('.shortcut-item[data-id="c"]') as HTMLElement | null;
      if (!c) throw new Error('row c not found');
      const r = c.getBoundingClientRect();
      const base = {
        screenX: 0, screenY: 0,
        clientX: Math.round(r.left + r.width / 2),
        clientY: Math.round(r.top + r.height / 2),
        effect: 'move', dataTypes: ['widget'],
        filePaths: null, virtualFileNames: null, urls: null, ctrlHeld: false,
      };
      const host = (window as any).__nativeDragDispatch;
      host({ ...base, eventType: 'enter', widgetData: p });
      host({ ...base, eventType: 'over', widgetData: p });
    }, payload);

    const domOrder = () => browser.execute(() =>
      Array.from(document.querySelectorAll('.shortcut-item'))
        .map(el => (el as HTMLElement).dataset.id));

    await browser.waitUntil(async () => {
      const dom = await domOrder();
      return dom.join(',') === 'b,a,c';
    }, { timeout: 3_000, timeoutMsg: 'live preview did not reorder the DOM to [B, A, C]' });

    // Not yet committed: config still the seeded order.
    const beforeDrop = await storedItemsOf('shortcut-1');
    expect(beforeDrop.map(i => i.id)).to.deep.equal(['a', 'b', 'c']);

    // Drop at the same spot commits the previewed order.
    await dispatchAt(widgetItemSel('c'), { dataTypes: ['widget'], widgetData: payload });
    await browser.waitUntil(async () => {
      const its = await storedItemsOf('shortcut-1');
      return its[0]?.id === 'b' && its[1]?.id === 'a' && its[2]?.id === 'c';
    }, { timeout: 3_000, timeoutMsg: 'drop did not commit the previewed order [B, A, C]' });
  });

  // ── Cross-widget receive ─────────────────────────────────────────────────
  it('[G] cross-widget: A → B places a fresh-id copy in B', async () => {
    const aItem: SeedItem = { id: 'src', path: 'https://alpha.example.com', label: 'Alpha' };
    await seedTwo([aItem], []);
    await dispatchAt('.shortcut-widget[1]', {
      dataTypes: ['widget'],
      widgetData: JSON.stringify({
        kind: 'shortcut-item',
        sourceWidgetId: 'wA',
        sourceViewId: 'default',
        dragId: 'cross-1',
        item: aItem,
      }),
    });
    await browser.waitUntil(async () => {
      const b = await storedItemsOf('wB');
      // Must have one entry with the same path/label but a different id.
      return b.length === 1
        && b[0].path === 'https://alpha.example.com'
        && b[0].label === 'Alpha'
        && b[0].id !== 'src';
    }, { timeout: 3_000, timeoutMsg: 'cross-widget receive failed' });

    // [S-35] The RECEIVER removes the original: the sender used to do it when
    // `start_native_drag` resolved with effect='move', which raced this very
    // event and could delete an item that was never re-added.
    await browser.waitUntil(async () => (await storedItemsOf('wA')).length === 0,
      { timeout: 3_000, timeoutMsg: 'source kept the item: the move duplicated it' });
  });

  it('[S-35] a receive with no source view leaves the source alone (no half-move)', async () => {
    // An older sender (or a payload from outside) carries no `sourceViewId`.
    // The receiver cannot resolve the source store, so it must ADD only, never
    // guess — losing the item would be unrecoverable, a duplicate is not.
    const aItem: SeedItem = { id: 'keep', path: 'https://kept.example.com', label: 'Kept' };
    await seedTwo([aItem], []);
    await dispatchAt('.shortcut-widget[1]', {
      dataTypes: ['widget'],
      widgetData: JSON.stringify({
        kind: 'shortcut-item',
        sourceWidgetId: 'wA',
        dragId: 'legacy-1',
        item: aItem,
      }),
    });
    await browser.waitUntil(async () => (await storedItemsOf('wB')).length === 1,
      { timeout: 3_000, timeoutMsg: 'receive without a source view never landed' });
    expect((await storedItemsOf('wA')).map(i => i.id)).to.deep.equal(['keep']);
  });

  it('[H] cross-widget receive preserves the original label/path', async () => {
    const aItem: SeedItem = { id: 'p', path: 'C:/games/launch.exe', label: 'Launcher' };
    await seedTwo([aItem], [{ id: 'existing', path: 'C:/other.exe' }]);
    await dispatchAt('.shortcut-widget[1]', {
      dataTypes: ['widget'],
      widgetData: JSON.stringify({
        kind: 'shortcut-item',
        sourceWidgetId: 'wA',
        sourceViewId: 'default',
        dragId: 'cross-2',
        item: aItem,
      }),
    });
    await browser.waitUntil(async () => {
      const b = await storedItemsOf('wB');
      return b.length === 2
        && b[0].id === 'existing'
        && b[1].path === 'C:/games/launch.exe'
        && b[1].label === 'Launcher';
    }, { timeout: 3_000, timeoutMsg: 'cross-widget label/path not preserved' });
  });

  // ── .url shortcut drops (custom protocol) ────────────────────────────────
  it('[S-29][S-73] dropping a Steam .url adds a url item (label from filename, IconFile as iconPath)', async () => {
    await seedOne([]);
    // A real Steam game shortcut: custom steam:// protocol + an IconFile. The
    // widget reads the file off disk via readTextFile, so write a real temp one.
    const dir = mkdtempSync(join(tmpdir(), 'themia-url-'));
    const iconFile = 'C:\\Program Files (x86)\\Steam\\steam\\games\\abc.ico';
    const urlFile = join(dir, 'Unrailed 2 Back on Track.url');
    writeFileSync(urlFile,
      '[InternetShortcut]\r\nIconIndex=0\r\nURL=steam://rungameid/2211170\r\n'
      + `IconFile=${iconFile}\r\n`);
    try {
      await dispatchAt('.shortcut-widget', { dataTypes: ['files'], filePaths: [urlFile] });
      await browser.waitUntil(async () => {
        const its = await storedItemsOf('shortcut-1') as any[];
        return its.length === 1 && its[0]?.path === 'steam://rungameid/2211170';
      }, { timeout: 3_000, timeoutMsg: 'steam .url drop did not produce a url item' });
      const it = (await storedItemsOf('shortcut-1') as any[])[0];
      expect(it.label).to.equal('Unrailed 2 Back on Track');
      expect(it.iconPath).to.equal(iconFile);
      // It must NOT be treated as a broken local file.
      const cls = await browser.execute((id: string) =>
        (document.querySelector(`.shortcut-item[data-id="${id}"]`) as HTMLElement | null)?.className ?? '',
        it.id);
      expect(cls).to.not.match(/\bbroken\b/);
    } finally {
      try { rmSync(dir, { recursive: true, force: true }); } catch {}
    }
  });

  // ── Edge cases ───────────────────────────────────────────────────────────
  it('[I] malformed widgetData JSON is ignored: items unchanged', async () => {
    const seed: SeedItem[] = [{ id: 'a', path: 'https://a.example.com' }];
    await seedOne(seed);
    await dispatchAt('.shortcut-widget', {
      dataTypes: ['widget'],
      widgetData: '{this is not json',
    });
    await browser.pause(200);
    const its = await storedItemsOf('shortcut-1');
    expect(its.map(i => i.id)).to.deep.equal(['a']);
  });

  it('[J] in edit mode, external file drop is ignored', async () => {
    await seedOne([], { });
    // Flip the global edit-mode flag; the widget reads `_editMode` from props.
    await browser.execute((k: string) => {
      localStorage.setItem(k, 'true');
      window.dispatchEvent(new StorageEvent('storage', {
        key: k, newValue: 'true', storageArea: localStorage,
      }));
    }, LS_KEYS.editMode);
    await browser.pause(120);
    await dispatchAt('.shortcut-widget', {
      dataTypes: ['files'],
      filePaths: ['C:/should/not/appear.txt'],
    });
    await browser.pause(200);
    const its = await storedItemsOf('shortcut-1');
    expect(its.length).to.equal(0);
  });
});
