/**
 * Folder widget: hidden-file handling ([S-22]).
 *
 * Verifies showHidden filters BOTH dot-prefixed names AND Windows-hidden
 * files (FILE_ATTRIBUTE_HIDDEN). The latter is the bug this spec guards:
 * before the fix, files marked hidden via `attrib +h` render regardless of
 * the toggle because the frontend only checked `name.startsWith('.')` and
 * Tauri's plugin-fs `readDir` doesn't surface Win32 attributes.
 *
 * Also verifies the toggle is reactive: flipping showHidden at runtime
 * must update the rendered list without waiting for a watcher/poll tick.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, sep } from 'node:path';
import { execSync } from 'node:child_process';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

function buildLayout(path: string, showHidden: boolean): string {
  return JSON.stringify({
    widgets: [{
      id: 'folder-1', type: 'folder', col: 1, row: 1, colSpan: 6, rowSpan: 6,
      config: {
        path, showHidden, viewMode: 'list', itemSize: 'medium',
        navigationMode: 'explorer', filter: '', showFileExtension: true,
        gridStretchHeight: false, gridItemSize: 'small',
        detailsColumns: { size: true, modified: true, type: true, cloudSync: false },
      },
    }],
    widgetGap: 12, bgImage: '', bgImageFit: 'cover',
  });
}

async function seedFolderWidget(path: string, showHidden: boolean): Promise<void> {
  await browser.execute((key: string, serialized: string) => {
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, buildLayout(path, showHidden));
  await $('.widget').waitForExist({ timeout: 5_000 });
}

async function setShowHidden(path: string, showHidden: boolean): Promise<void> {
  await browser.execute((key: string, serialized: string) => {
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, buildLayout(path, showHidden));
}

async function bodyText(): Promise<string> {
  return browser.execute(() => document.body.textContent ?? '');
}

describe('folder widget: hidden files ([S-22])', () => {
  let dir: string;

  before(function () {
    // The core bug is Win32 FILE_ATTRIBUTE_HIDDEN handling. `attrib +h` is
    // Windows-only; on other platforms there is no equivalent attribute
    // to exercise, so skip.
    if (process.platform !== 'win32') this.skip();
  });

  beforeEach(async () => {
    dir = mkdtempSync(join(tmpdir(), 'themia-fh-'));
    writeFileSync(join(dir, 'visible.txt'),    'v');
    writeFileSync(join(dir, '.dotfile.txt'),   'd');
    writeFileSync(join(dir, 'winhidden.txt'),  'h');
    // Mark winhidden.txt with FILE_ATTRIBUTE_HIDDEN. `attrib` requires a
    // Windows path with backslashes; dir from mkdtempSync already uses them.
    execSync(`attrib +h "${join(dir, 'winhidden.txt')}"`);

    await resetAppState();
    const widgetPath = dir.split(sep).join('/');
    await seedFolderWidget(widgetPath, /* showHidden */ false);

    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('visible.txt');
    }, { timeout: 10_000, timeoutMsg: `seeded visible.txt never rendered in ${dir}` });
  });

  afterEach(() => {
    try { rmSync(dir, { recursive: true, force: true }); } catch {}
  });

  it('dotfile is filtered when showHidden=false (regression guard)', async () => {
    // Give the widget a moment past the initial render so any async enrichment
    // settles, since the dotfile must never appear.
    await browser.pause(500);
    const t = await bodyText();
    expect(t).to.include('visible.txt');
    expect(t).to.not.include('.dotfile.txt');
  });

  it('Windows-hidden file is filtered when showHidden=false (core bug)', async () => {
    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('visible.txt') && !t.includes('winhidden.txt');
    }, {
      timeout: 4_000,
      timeoutMsg: 'winhidden.txt still visible despite showHidden=false: FILE_ATTRIBUTE_HIDDEN not honoured',
    });
  });

  it('toggling showHidden=true reveals both hidden files without reload', async () => {
    const widgetPath = dir.split(sep).join('/');
    await setShowHidden(widgetPath, true);

    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('visible.txt')
          && t.includes('.dotfile.txt')
          && t.includes('winhidden.txt');
    }, {
      timeout: 4_000,
      timeoutMsg: 'hidden entries did not appear after flipping showHidden=true: filter is not reactive',
    });
  });

  it('toggling showHidden=false again re-hides both', async () => {
    const widgetPath = dir.split(sep).join('/');
    await setShowHidden(widgetPath, true);
    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('.dotfile.txt') && t.includes('winhidden.txt');
    }, { timeout: 4_000 });

    await setShowHidden(widgetPath, false);
    await browser.waitUntil(async () => {
      const t = await bodyText();
      return t.includes('visible.txt')
          && !t.includes('.dotfile.txt')
          && !t.includes('winhidden.txt');
    }, {
      timeout: 4_000,
      timeoutMsg: 'hidden entries still visible after flipping showHidden=false',
    });
  });
});
