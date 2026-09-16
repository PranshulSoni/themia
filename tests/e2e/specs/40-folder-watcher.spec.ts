/**
 * Folder widget: does the OS fs watcher fire for external changes?
 *
 * Reproduces the "Ctrl+X in folder widget, paste in Windows Explorer, items
 * linger until next manual refresh" bug. The mechanism under test is the
 * `watch()` call in FolderView.tsx: when a file vanishes from the watched
 * directory by means other than an in-app action (Explorer cut-paste, Node
 * `fs.rm`, another process), the watcher callback must fire and `load()`
 * must re-read the directory so the widget's DOM stops showing the gone
 * entry.
 *
 * Strategy: create a real temp dir with a few files, point a folder widget
 * at it, delete one file externally via Node, and wait for that name to
 * disappear from the DOM. We don't stub Tauri here, since Tauri 2 makes
 * `__TAURI_INTERNALS__.invoke` non-writable, so real FS is both more correct
 * and more reliable than a stub.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, sep } from 'node:path';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

async function seedFolderWidget(path: string): Promise<void> {
  await browser.execute((key: string, pth: string) => {
    const layout = {
      widgets: [{
        id: 'folder-1', type: 'folder', col: 1, row: 1, colSpan: 6, rowSpan: 6,
        config: {
          path: pth, showHidden: false, viewMode: 'list', itemSize: 'medium',
          navigationMode: 'explorer', filter: '', showFileExtension: true,
          gridStretchHeight: false, gridItemSize: 'small',
          detailsColumns: { size: true, modified: true, type: true, cloudSync: false },
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
  }, LS_KEYS.layoutRoot, path);
  await $('.widget').waitForExist({ timeout: 5_000 });
}

async function bodyText(): Promise<string> {
  return browser.execute(() => document.body.textContent ?? '');
}

async function folderDebug(): Promise<any> {
  return browser.execute(() => (window as any).__folderDebug ?? null);
}

describe('folder widget: OS watcher fires on external fs changes', () => {
  let dir: string;

  beforeEach(async () => {
    dir = mkdtempSync(join(tmpdir(), 'themia-fw-'));
    writeFileSync(join(dir, 'alpha.txt'), 'a');
    writeFileSync(join(dir, 'beta.txt'),  'b');
    writeFileSync(join(dir, 'gamma.txt'), 'g');

    await resetAppState();
    // FolderView uses forward-slashes internally; normalise so the path
    // displayed in breadcrumb / watched matches what readDir gets back.
    const widgetPath = dir.split(sep).join('/');
    await seedFolderWidget(widgetPath);

    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('alpha.txt') && t.includes('beta.txt') && t.includes('gamma.txt');
    }, { timeout: 10_000, timeoutMsg: `seeded files never rendered in ${dir}` });
  });

  afterEach(() => {
    try { rmSync(dir, { recursive: true, force: true }); } catch {}
  });

  it('external deletion of a file disappears from the widget within 4s', async () => {
    // Regression bait: this is the exact mechanism that fails when the user
    // Ctrl+X in the widget and Ctrl+V in Explorer. Explorer removes the file
    // from the source dir on its own, so the widget has to rely on the
    // OS watcher. If the watcher doesn't fire (or the reload is suppressed
    // by a throttle), this test fails.
    rmSync(join(dir, 'beta.txt'));

    try {
      await browser.waitUntil(async () => {
        const t = await bodyText();
        return !t.includes('beta.txt') && t.includes('alpha.txt') && t.includes('gamma.txt');
      }, {
        timeout: 4_000,
        timeoutMsg: 'beta.txt still visible 4s after external removal: watcher did not fire or load was suppressed',
      });
    } catch (e) {
      const dbg = await folderDebug();
      throw new Error(`${(e as Error).message}\n  folder debug: ${JSON.stringify(dbg)}`);
    }
  });

  it('external creation of a file appears in the widget within 4s', async () => {
    writeFileSync(join(dir, 'delta.txt'), 'd');

    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('delta.txt');
    }, {
      timeout: 4_000,
      timeoutMsg: 'delta.txt never appeared 4s after external creation: watcher did not fire or load was suppressed',
    });
  });

  it('batch external removal: 3 files disappear after a single burst', async () => {
    // Simulates Explorer moving several cut files out at once.
    rmSync(join(dir, 'alpha.txt'));
    rmSync(join(dir, 'beta.txt'));
    rmSync(join(dir, 'gamma.txt'));

    await browser.waitUntil(async () => {
      const t = await bodyText();
      return !t.includes('alpha.txt') && !t.includes('beta.txt') && !t.includes('gamma.txt');
    }, {
      timeout: 5_000,
      timeoutMsg: 'batch removal not reflected 5s after external move',
    });
  });
});
