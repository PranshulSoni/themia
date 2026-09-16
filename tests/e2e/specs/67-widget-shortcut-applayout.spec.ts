/**
 * Shortcut widget: app-layout items.
 *
 * The standalone "App Layouts" widget was folded into the Shortcut widget as a
 * 4th item kind ('layout', alongside url/file/shell). A layout item carries an
 * app-layout tree in `item.layout`; activating it walks the tree into launch
 * entries and fires `launch_app_layout` on the display the click happened on.
 *
 * SPEC tags mirrored in test titles ([S-50]..[S-53]).
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS, stubTauriInvoke } from '../helpers/app.js';

type AnyItem = { id: string; path: string; label?: string; layout?: any };

// A 50/50 vertical split: left = URL, right = custom path.
const TREE = {
  kind: 'split', id: 's1', direction: 'vertical', ratio: 0.5,
  a: { kind: 'leaf', id: 'l1', assignment: { kind: 'url', target: 'https://left.example', reuseIfRunning: false } },
  b: { kind: 'leaf', id: 'l2', assignment: { kind: 'path', target: 'C:/tools/right.exe', label: 'Right', matchHint: 'right.exe', reuseIfRunning: true } },
};

async function seedShortcutWidget(items: AnyItem[], extra: Record<string, any> = {}): Promise<void> {
  await browser.execute((key: string, its: AnyItem[], ex: Record<string, any>) => {
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

async function setupInvoke(): Promise<void> {
  await stubTauriInvoke({
    open_url: null,
    open_in_explorer: null,
    open_file: null,
    launch_app_layout: [],
    get_file_icon: '',
    push_windows_to_desktop: null,
    resolve_shortcut: { target_path: 'C:/resolved/target.exe', is_directory: false },
    fs_exists: true,
    activate_widget_keyboard: null,
  });
}

async function lastOpen(): Promise<{ kind: string; path: string } | null> {
  return browser.execute(() => (window as any).__shortcutLastOpen ?? null) as any;
}

async function lastLaunch(): Promise<{ entries: any[] } | null> {
  return browser.execute(() => (window as any).__shortcutLastLaunch ?? null) as any;
}

describe('shortcut widget: app-layout items', () => {
  beforeEach(async () => {
    await resetAppState();
    await setupInvoke();
  });

  it('[S-50] renders a layout item with its label', async () => {
    await seedShortcutWidget([{ id: 'lay', path: 'layout:', label: 'Office', layout: TREE }]);
    const nodes = await $$('.shortcut-item');
    expect(await nodes.length).to.equal(1);
    expect((await $('.shortcut-item .shortcut-name').getText()).trim()).to.equal('Office');
  });

  it('[S-72] an assigned layout shows a stack of its panes\' app icons', async () => {
    await seedShortcutWidget([{ id: 'lay', path: 'layout:', label: 'Office', layout: TREE }]);
    // TREE has 2 assigned panes (url + path) → a 2-icon stack, not the generic glyph.
    expect(await $$('.shortcut-item .shortcut-layout-stack').length).to.equal(1);
    expect(await $$('.shortcut-item .shortcut-layout-stack-item').length).to.equal(2);
    expect(await $$('.shortcut-item .shortcut-icon-lucide').length).to.equal(0);
  });

  it('[S-72] an unassigned layout falls back to the generic layout glyph', async () => {
    const empty = { kind: 'leaf', id: 'l1' };
    await seedShortcutWidget([{ id: 'lay', path: 'layout:', label: 'Empty', layout: empty }]);
    expect(await $$('.shortcut-item .shortcut-layout-stack').length).to.equal(0);
    expect(await $$('.shortcut-item .shortcut-icon-lucide').length).to.equal(1);
  });

  it('[S-51][S-52] activating a layout item fires launch_app_layout with one entry per assigned leaf', async () => {
    await seedShortcutWidget([{ id: 'lay', path: 'layout:', label: 'Office', layout: TREE }]);
    await browser.execute(() => { (window as any).__shortcutLastOpen = null; (window as any).__shortcutLastLaunch = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    });
    await browser.waitUntil(async () => (await lastOpen())?.kind === 'layout',
      { timeout: 3_000, timeoutMsg: 'layout open not fired' });

    const launch = await lastLaunch();
    expect(launch, 'launch mirror').to.not.equal(null);
    const entries = launch!.entries;
    expect(entries.length).to.equal(2);
    const left = entries.find(e => e.target === 'https://left.example');
    const right = entries.find(e => e.target === 'C:/tools/right.exe');
    expect(left, 'left entry').to.not.equal(undefined);
    expect(right, 'right entry').to.not.equal(undefined);
    // 50/50 vertical split → left occupies x 0..50, right 50..100, both full height.
    expect(left.xPct).to.equal(0);
    expect(left.wPct).to.equal(50);
    expect(right.xPct).to.equal(50);
    expect(right.wPct).to.equal(50);
    expect(left.hPct).to.equal(100);
  });

  it('[S-53] an unassigned-only layout produces no launch entries (no-op)', async () => {
    const empty = { kind: 'leaf', id: 'l1' };
    await seedShortcutWidget([{ id: 'lay', path: 'layout:', label: 'Empty', layout: empty }]);
    await browser.execute(() => { (window as any).__shortcutLastLaunch = null; });
    await browser.execute(() => {
      const el = document.querySelector('.shortcut-item') as HTMLElement | null;
      el?.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0 }));
    });
    // Give the handler a tick; nothing should be launched.
    await browser.pause(300);
    expect(await lastLaunch()).to.equal(null);
  });

});
