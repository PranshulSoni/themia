/**
 * Folder widget: icons render after navigating into a subfolder.
 *
 * Covers [S-60] in widgets/office/folder/SPEC.md.
 *
 * Regression bait: loadIcons() in FolderView.tsx serialises batch requests
 * via an `iconsInFlight` promise. On navigation the icon createEffect fires
 * twice in quick succession: first with stale `entries()` (old folder's
 * list, new currentPath) and again once load() has populated entries for
 * the new path. If the second call is dropped because the first is still
 * in flight, the new folder's icons never request and rows show the
 * fallback emoji rather than the rendered shell icon.
 *
 * Strategy: use a real temp dir with a known subfolder, point a folder
 * widget at the parent, navigate into the subfolder via double-click in
 * `navigationMode: 'navigate'`, then wait for `<img class="item-icon-img">`
 * elements with data-URL `src` attributes to appear for the subfolder's
 * entries. We don't stub Tauri, since the watcher test (40) established that
 * real FS is more reliable than stubbing for this widget.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { mkdtempSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, sep } from 'node:path';
import { rmSync } from 'node:fs';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

async function seedFolderWidget(path: string): Promise<void> {
  await browser.execute((key: string, pth: string) => {
    const layout = {
      widgets: [{
        id: 'folder-1', type: 'folder', col: 1, row: 1, colSpan: 6, rowSpan: 6,
        config: {
          path: pth, showHidden: false, viewMode: 'list', itemSize: 'medium',
          navigationMode: 'navigate', filter: '', showFileExtension: true,
          gridStretchHeight: false, gridItemSize: 'small',
          detailsColumns: { size: true, modified: true, type: true, cloudSync: false },
          clickMode: 'double',
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

// Count rendered icon <img> elements with a data-URL src inside the widget.
// Fallback emoji spans don't count; those are what shows when loadIcons
// silently drops the batch.
async function countIconImgs(): Promise<number> {
  return browser.execute(() =>
    document.querySelectorAll<HTMLImageElement>(
      '.folder-view img.item-icon-img'
    ).length
  );
}

// Double-click a row by visible filename. Matches FolderView's item DOM
// (rows carry `data-fp`, child text nodes hold the filename).
async function dblClickEntry(name: string): Promise<boolean> {
  return browser.execute((n: string) => {
    const nodes = Array.from(document.querySelectorAll<HTMLElement>('.folder-view *'));
    const target = nodes.find(e =>
      e.children.length === 0 && (e.textContent ?? '').trim() === n,
    );
    if (!target) return false;
    const row = (target.closest('[data-fp], .item, .grid-item, .details-row') as HTMLElement) ?? target;
    row.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    row.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    return true;
  }, name);
}

describe('folder widget: icons render after navigation [S-60]', () => {
  let rootDir: string;
  let subDir: string;

  beforeEach(async () => {
    rootDir = mkdtempSync(join(tmpdir(), 'themia-icons-nav-'));
    // Parent folder entries; their icons will populate iconCache first.
    writeFileSync(join(rootDir, 'parent-a.txt'), 'a');
    writeFileSync(join(rootDir, 'parent-b.txt'), 'b');
    // Subfolder with a distinct set of files.
    subDir = join(rootDir, 'child');
    mkdirSync(subDir);
    writeFileSync(join(subDir, 'child-alpha.txt'),   'aa');
    writeFileSync(join(subDir, 'child-beta.md'),     'bb');
    writeFileSync(join(subDir, 'child-gamma.json'),  'gg');

    await resetAppState();
    const widgetPath = rootDir.split(sep).join('/');
    await seedFolderWidget(widgetPath);

    // Parent entries must render and have icons before we navigate; this
    // guarantees the first-path iconCache run is the one that can race with
    // the navigation-triggered effect.
    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('parent-a.txt') && t.includes('child');
    }, { timeout: 10_000, timeoutMsg: `parent files never rendered in ${rootDir}` });

    await browser.waitUntil(async () => (await countIconImgs()) >= 2, {
      timeout: 10_000,
      timeoutMsg: 'parent-folder icons never loaded: test precondition failed',
    });
  });

  afterEach(() => {
    try { rmSync(rootDir, { recursive: true, force: true }); } catch {}
  });

  it('navigating into a subfolder loads icons for the new entries', async () => {
    const ok = await dblClickEntry('child');
    expect(ok).to.equal(true);

    // After navigation the widget shows the subfolder's filenames.
    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('child-alpha.txt')
          && t.includes('child-beta.md')
          && t.includes('child-gamma.json');
    }, { timeout: 5_000, timeoutMsg: 'subfolder entries never rendered after navigation' });

    // And every visible row should have a rendered icon <img>, not a
    // fallback emoji. Three files → at least three icon imgs. We tolerate
    // the exact count being higher (grid view could show additional
    // decorative imgs) but require at minimum one per entry.
    await browser.waitUntil(async () => (await countIconImgs()) >= 3, {
      timeout: 5_000,
      timeoutMsg: 'subfolder entries never got icons after navigation: loadIcons batch was likely dropped by in-flight guard',
    });

    // Sanity: no parent-folder filenames should be visible post-nav (so the
    // icon-img count above is measuring the subfolder, not leftover parent
    // rows).
    const t = await bodyText();
    expect(t.includes('parent-a.txt')).to.equal(false);
    expect(t.includes('parent-b.txt')).to.equal(false);
  });
});
