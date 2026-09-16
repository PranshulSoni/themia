/**
 * Shortcut widget - multi-select, search, and broken-target detection.
 *
 * Covers:
 *  - Click modifiers (ctrl-toggle, shift-range) and selection state.
 *  - Delete removes every selected item, recorded as one undoable op.
 *  - Ctrl+A selects all, Escape clears.
 *  - Search header filters items by label / path substring.
 *  - File items whose path doesn't exist get a `broken` class.
 *
 * SPEC tags mirrored in test titles.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, sep } from 'node:path';
import { resetAppState, LS_KEYS, stubTauriInvoke } from '../helpers/app.js';

type SeedItem = { id: string; path: string; label?: string };

async function seed(items: SeedItem[], extra: Record<string, any> = {}): Promise<void> {
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

async function itemClassForId(id: string): Promise<string> {
  return browser.execute((iid: string) => {
    const el = document.querySelector(`.shortcut-item[data-id="${iid}"]`) as HTMLElement | null;
    return el?.className ?? '';
  }, id);
}

async function clickItem(id: string, mod: 'none' | 'ctrl' | 'shift' = 'none'): Promise<void> {
  await browser.execute((iid: string, m: string) => {
    const el = document.querySelector(`.shortcut-item[data-id="${iid}"]`) as HTMLElement | null;
    if (!el) throw new Error(`no item ${iid}`);
    el.dispatchEvent(new MouseEvent('click', {
      bubbles: true, cancelable: true, button: 0,
      ctrlKey: m === 'ctrl', shiftKey: m === 'shift',
    }));
  }, id, mod);
}

async function pressKey(key: string, opts: { ctrl?: boolean; shift?: boolean } = {}): Promise<void> {
  await browser.execute((k: string, o: any) => {
    document.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true, cancelable: true,
      key: k, ctrlKey: !!o.ctrl, shiftKey: !!o.shift,
    }));
  }, key, opts);
}

describe('shortcut widget - polish: select, search, broken', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubTauriInvoke({
      open_url: null,
      open_in_explorer: null,
      get_file_icon: '',
      push_windows_to_desktop: null,
      // File existence check: default every path exists unless a test overrides.
      fs_exists: true,
      activate_widget_keyboard: null,
    });
  });

  // ── Multi-select ────────────────────────────────────────────────────────────
  it('[S-40] ctrl-click toggles selection of an item', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);
    await clickItem('a', 'ctrl');
    expect(await itemClassForId('a')).to.match(/\bselected\b/);
    expect(await itemClassForId('b')).to.not.match(/\bselected\b/);
    await clickItem('a', 'ctrl');
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
  });

  it('[S-40] shift-click selects a contiguous range', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com' },
      { id: 'b', path: 'https://b.example.com' },
      { id: 'c', path: 'https://c.example.com' },
    ]);
    await clickItem('a', 'ctrl');   // anchor
    await clickItem('c', 'shift');  // range a..c
    for (const id of ['a', 'b', 'c']) {
      expect(await itemClassForId(id)).to.match(/\bselected\b/);
    }
  });

  it('[S-41] Delete removes every selected item as one undoable op', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com' },
      { id: 'b', path: 'https://b.example.com' },
      { id: 'c', path: 'https://c.example.com' },
    ]);
    await clickItem('a', 'ctrl');
    await clickItem('b', 'ctrl');
    await pressKey('Delete');

    await browser.waitUntil(async () => {
      const its = await storedItems();
      return its.length === 1 && its[0]?.id === 'c';
    }, { timeout: 3_000, timeoutMsg: 'Delete did not remove selection' });

    // One undo brings them all back.
    const r = await browser.execute(async () => (window as any).__opLog?.undo());
    expect((r as any)?.ok).to.equal(true);
    await browser.waitUntil(async () => (await storedItems()).length === 3,
      { timeout: 3_000, timeoutMsg: 'undo did not restore deleted items' });
  });

  it('[S-42] Ctrl+A selects all items, Escape clears', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com' },
      { id: 'b', path: 'https://b.example.com' },
    ]);
    // Ensure widget owns the keyboard.
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-widget') as HTMLElement;
      el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    });
    await pressKey('a', { ctrl: true });
    expect(await itemClassForId('a')).to.match(/\bselected\b/);
    expect(await itemClassForId('b')).to.match(/\bselected\b/);
    await pressKey('Escape');
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
  });

  // ── Search header ──────────────────────────────────────────────────────────
  it('[S-43] header search filters items by label / path substring', async () => {
    await seed([
      { id: 'a', path: 'https://alpha.example.com', label: 'Alpha' },
      { id: 'b', path: 'https://beta.example.com',  label: 'Beta' },
      { id: 'c', path: 'https://gamma.example.com', label: 'Gamma' },
    ]);

    // Click the search button in the header.
    await browser.execute(() => {
      const btn = document.querySelector('.shortcut-widget .search-toggle-btn') as HTMLElement | null;
      btn?.click();
    });
    const input = await $('.shortcut-widget .search-input');
    await input.waitForDisplayed({ timeout: 2_000 });
    await input.setValue('bet');

    await browser.waitUntil(async () => {
      const ids = await browser.execute(() =>
        Array.from(document.querySelectorAll('.shortcut-item'))
          .map(e => (e as HTMLElement).dataset.id ?? ''));
      return (ids as string[]).length === 1 && (ids as string[])[0] === 'b';
    }, { timeout: 3_000, timeoutMsg: 'search did not filter down to "Beta"' });

    // Escape clears + closes.
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-widget .search-input') as HTMLInputElement;
      el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    });
    await browser.waitUntil(async () => (await $$('.shortcut-item').length) === 3,
      { timeout: 2_000 });
  });

  // ── Preselection / keyboard ownership ───────────────────────────────────────
  it('[S-62] freshly placed widget ignores global keys until interacted with', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);
    // No mousedown / click anywhere in the widget, since the user has not yet
    // interacted with it. A stray global keystroke must NOT create a selection
    // (regression: the widget claimed the keyboard hook on mount and grabbed
    // global keys, leaving an item permanently "preselected").
    await browser.execute(() => { delete (window as any).__shortcutLastOpen; });
    await pressKey('ArrowDown');
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
    expect(await itemClassForId('b')).to.not.match(/\bselected\b/);

    // ...and Enter must not open anything when nothing was deliberately selected.
    await pressKey('Enter');
    const opened = await browser.execute(() => (window as any).__shortcutLastOpen ?? null);
    expect(opened).to.equal(null);
  });

  it('[S-62] clicking the widget chrome (outside the list) clears the selection', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);
    await clickItem('a');
    expect(await itemClassForId('a')).to.match(/\bselected\b/);

    // Mousedown on the header (inside the widget but outside .shortcut-list)
    // deselects, mirroring the folder widget's background-click behavior.
    await browser.execute(() => {
      const hdr = document.querySelector('.shortcut-widget .widget-header') as HTMLElement;
      hdr.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    });
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
  });

  it('[S-62] mousedown on the empty list background clears the selection', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);
    await clickItem('a');
    expect(await itemClassForId('a')).to.match(/\bselected\b/);

    // Mousedown on the list container itself (empty area, not on a row)
    // deselects, same as clicking a folder widget's background.
    await browser.execute(() => {
      const list = document.querySelector('.shortcut-widget .shortcut-list') as HTMLElement;
      list.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    });
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
  });

  it('[S-64] losing app focus (window blur) clears the selection', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'A' },
      { id: 'b', path: 'https://b.example.com', label: 'B' },
    ]);
    await clickItem('a');
    expect(await itemClassForId('a')).to.match(/\bselected\b/);

    // Clicking outside the Themia app entirely fires a window `blur` (no DOM
    // mousedown lands in the webview), so the selection must reset so nothing
    // stays highlighted/armed for Enter when the user clicks back in.
    await browser.execute(() => window.dispatchEvent(new Event('blur')));
    expect(await itemClassForId('a')).to.not.match(/\bselected\b/);
  });

  // ── Icon-only grid ───────────────────────────────────────────────────────────
  it('[S-71] gridIconOnly hides name labels in grid view', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'Alpha' },
      { id: 'b', path: 'https://b.example.com', label: 'Beta' },
    ], { viewMode: 'grid', gridIconOnly: true });

    // Root carries the grid-icon-only class and the name labels are display:none.
    const rootCls = await browser.execute(() =>
      (document.querySelector('.shortcut-widget') as HTMLElement)?.className ?? '');
    expect(rootCls).to.match(/\bgrid-icon-only\b/);

    const nameDisplay = await browser.execute(() => {
      const el = document.querySelector('.shortcut-item .shortcut-name') as HTMLElement | null;
      return el ? getComputedStyle(el).display : 'missing';
    });
    expect(nameDisplay).to.equal('none');
  });

  it('[S-71] gridIconOnly is ignored in list view (names still shown)', async () => {
    await seed([
      { id: 'a', path: 'https://a.example.com', label: 'Alpha' },
    ], { viewMode: 'list', gridIconOnly: true });

    const nameDisplay = await browser.execute(() => {
      const el = document.querySelector('.shortcut-item .shortcut-name') as HTMLElement | null;
      return el ? getComputedStyle(el).display : 'missing';
    });
    expect(nameDisplay).to.not.equal('none');
  });

  // ── Broken-target detection ─────────────────────────────────────────────────
  it('[S-44] file items whose path does not exist get a `broken` class', async () => {
    // `__TAURI_INTERNALS__.invoke` is non-writable in Tauri 2 so we can't
    // stub `fs_exists` from JS. Use real on-disk paths instead: create one
    // file that exists and one that doesn't.
    const dir = mkdtempSync(join(tmpdir(), 'themia-broken-'));
    const okFile  = join(dir, 'present.exe');
    const gonePath = join(dir, 'missing.exe');
    writeFileSync(okFile, 'a');

    const okPath   = okFile.split(sep).join('/');
    const goneStr  = gonePath.split(sep).join('/');

    await seed([
      { id: 'ok',   path: okPath },
      { id: 'gone', path: goneStr },
    ]);

    try {
      await browser.waitUntil(async () => {
        const okCls   = await itemClassForId('ok');
        const goneCls = await itemClassForId('gone');
        return !/\bbroken\b/.test(okCls) && /\bbroken\b/.test(goneCls);
      }, { timeout: 3_000, timeoutMsg: 'broken class never applied to missing item' });
    } finally {
      try { rmSync(dir, { recursive: true, force: true }); } catch {}
    }
  });

  // ── Recycle Bin fill indicator (issue #94) ─────────────────────────────────
  // The real bin state is whatever this machine happens to hold, so stub the
  // command through the `__ipcInvoke` seam (Tauri 2 makes
  // `__TAURI_INTERNALS__.invoke` non-writable, see ipc.ts) and make both the
  // full and the empty case deterministic.
  async function stubBinStatus(status: { items: number; bytes: number }): Promise<void> {
    await browser.execute((s: { items: number; bytes: number }) => {
      const hook = (window as any).__ipcInvoke;
      if (!hook?.setInvoke) throw new Error('__ipcInvoke.setInvoke missing (navigator.webdriver false?)');
      const raw = (window as any).__TAURI_INTERNALS__?.invoke;
      hook.setInvoke(async (cmd: string, args: any) => {
        if (cmd === 'recycle_bin_status') return s;
        return raw ? raw(cmd, args) : null;
      });
    }, status);
  }

  /** Badge text on an item's icon, or '' when it carries no badge. */
  async function binBadge(id: string): Promise<string> {
    return browser.execute((iid: string) => {
      const el = document.querySelector(`.shortcut-item[data-id="${iid}"] .shortcut-bin-badge`);
      return el ? (el.textContent ?? '') : '';
    }, id);
  }

  afterEach(async () => {
    await browser.execute(() => { (window as any).__ipcInvoke?.setInvoke(null); });
  });

  it('[S-75] a Recycle Bin item badges its icon with the file count', async () => {
    await stubBinStatus({ items: 4, bytes: 2048 });
    await seed([
      { id: 'bin',   path: 'shell:RecycleBinFolder' },
      { id: 'other', path: 'shell:MyComputerFolder' },
    ]);
    await browser.waitUntil(async () => (await binBadge('bin')) === '4',
      { timeout: 5_000, timeoutMsg: 'no count badge on a non-empty Recycle Bin' });
    // Only the bin is badged; other shell items are untouched.
    expect(await binBadge('other')).to.equal('');
  });

  it('[S-75] a huge bin caps the badge at 99+', async () => {
    await stubBinStatus({ items: 1234, bytes: 5 * 1024 * 1024 });
    await seed([{ id: 'bin', path: 'shell:RecycleBinFolder' }]);
    await browser.waitUntil(async () => (await binBadge('bin')) === '99+',
      { timeout: 5_000, timeoutMsg: 'badge did not cap at 99+' });
  });

  it('[S-75] tooltip reports what the bin holds', async () => {
    await stubBinStatus({ items: 4, bytes: 2048 });
    await seed([{ id: 'bin', path: 'shell:RecycleBinFolder' }]);
    await browser.waitUntil(async () => !!(await binBadge('bin')), { timeout: 5_000 });
    const title = await browser.execute(() => {
      const el = document.querySelector('.shortcut-item[data-id="bin"]') as HTMLElement | null;
      return el?.getAttribute('title') ?? '';
    });
    expect(title).to.match(/Recycle Bin — 4 items \(2\.0 KB\)/);
  });

  it('[S-75] an empty bin shows no badge', async () => {
    await stubBinStatus({ items: 0, bytes: 0 });
    await seed([{ id: 'bin', path: 'shell:RecycleBinFolder' }]);
    // Give the mount-time query time to land before asserting the absence.
    await browser.pause(1_000);
    expect(await binBadge('bin')).to.equal('');
  });
});
