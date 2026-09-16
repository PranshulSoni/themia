/**
 * Default folder widget on default view.
 *
 * On first launch (no `widget-layout` key in localStorage), the default-view
 * seed in `defaultLayout()` (src/layout.ts) includes a folder widget with an
 * empty `path`. FolderView resolves the OS desktop directory via `desktopDir()`
 * at render and persists it into the widget config (1.26, no App-level seed
 * timer or sentinel), so `config.path` ends up a real path.
 *
 * User-created (non-default) views are NOT auto-seeded with the folder
 * widget: they keep the legacy clock + help + shortcut seed.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { attachToAppWindow, waitForReady } from '../helpers/app.js';

const SCREENS_KEY = 'widget-screens';

/** Fresh-launch reset: clear ALL localStorage (so `defaultLayout()` actually
 *  fires) and reload. Differs from `resetAppState`, which seeds widget-layout
 *  with `{ widgets: [] }` and thereby skips the default seed. */
async function freshLaunch(): Promise<void> {
  await attachToAppWindow();
  await browser.execute(() => {
    try { localStorage.clear(); } catch {}
    location.reload();
  });
  await browser.pause(300);
  await attachToAppWindow();
  await waitForReady();
}

async function readLayout(key = 'widget-layout'): Promise<any> {
  return browser.execute((k: string) => {
    try { return JSON.parse(localStorage.getItem(k) ?? 'null'); }
    catch { return null; }
  }, key);
}

describe('default folder widget seed', () => {
  beforeEach(async () => { await freshLaunch(); });

  it('seeds a folder widget on the default view', async () => {
    await browser.waitUntil(async () => {
      const l = await readLayout();
      return !!l && Array.isArray(l.widgets) && l.widgets.some((w: any) => w.type === 'folder');
    }, { timeout: 5_000, timeoutMsg: 'folder widget was never seeded on default view' });

    const layout = await readLayout();
    const folder = (layout.widgets as any[]).find(w => w.type === 'folder');
    expect(folder, 'folder widget present').to.exist;
    // Fractional layout schema (x/y/w/h as screen fractions; no cell col/row).
    expect(folder.x).to.be.a('number');
    expect(folder.y).to.be.a('number');
    expect(folder.w).to.be.above(0);
    expect(folder.h).to.be.above(0);
    expect(folder.config).to.be.an('object');
  });

  it('folder resolves the desktop path at render and persists it', async () => {
    // In a real Tauri environment desktopDir() resolves to the user's Desktop
    // folder. FolderView resolves + persists it on mount (no App-level timer or
    // sentinel). Allow up to 5s for the render + persist to land.
    await browser.waitUntil(async () => {
      const l = await readLayout();
      const f = l?.widgets?.find((w: any) => w.type === 'folder');
      return !!f && typeof f.config?.path === 'string' && f.config.path.length > 0;
    }, { timeout: 5_000, timeoutMsg: 'folder desktop path never resolved/persisted' });

    const layout = await readLayout();
    const folder = (layout.widgets as any[]).find(w => w.type === 'folder');
    expect(folder.config.__seedDesktop, 'no sentinel is written anymore').to.equal(undefined);
    expect(folder.config.path, 'path resolved').to.be.a('string').and.have.length.greaterThan(0);
    // Loose check that it looks like a Desktop path.
    expect(/desktop/i.test(folder.config.path), `path was: ${folder.config.path}`).to.equal(true);
  });

  it('does NOT seed a folder widget on user-created views', async () => {
    // Make sure the default-view seed has landed before we add a new view.
    await browser.waitUntil(async () => {
      const l = await readLayout();
      return !!l && Array.isArray(l.widgets) && l.widgets.some((w: any) => w.type === 'folder');
    }, { timeout: 5_000 });

    // Add a user-created view. The add-view UI moved out of the view bar into
    // the config window, so seed it directly via widget-screens + a storage
    // event (views.ts reloads the list on it) rather than clicking the removed
    // .view-add-btn.
    await browser.execute((k: string) => {
      const list = JSON.parse(localStorage.getItem(k) ?? '[]');
      list.push({ id: 'view-work', name: 'Work', icon: 'home' });
      const next = JSON.stringify(list);
      localStorage.setItem(k, next);
      window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: next }));
    }, SCREENS_KEY);

    // Wait for the new view to appear in widget-screens.
    let newId: string | undefined;
    await browser.waitUntil(async () => {
      const list: any[] = await browser.execute((k: string) => {
        try { return JSON.parse(localStorage.getItem(k) ?? '[]'); }
        catch { return []; }
      }, SCREENS_KEY);
      const s = list.find(x => x.name === 'Work');
      if (s) { newId = s.id; return true; }
      return false;
    }, { timeout: 3_000, timeoutMsg: 'new view was never created' });

    expect(newId, 'new view id').to.be.a('string');

    // Only the active ViewRoot is mounted (see App.tsx 666–684), so we can't
    // compare DOM counts across views. Inspect each view's persisted layout
    // instead. A brand-new view with zero widgets isn't written to
    // localStorage until the first widget is added (lazy/debounced save in
    // createViewLayoutStore.persistNow), so the per-view key may legitimately
    // be missing, so treat that as "no folder widget" rather than failing.
    const newViewLayout     = await readLayout(`widget-layout-${newId}`);
    const defaultViewLayout = await readLayout('widget-layout');
    const newFolders     = ((newViewLayout?.widgets    ?? []) as any[]).filter(w => w.type === 'folder');
    const defaultFolders = ((defaultViewLayout?.widgets ?? []) as any[]).filter(w => w.type === 'folder');

    expect(newFolders.length,     'no folder widget on user-created view').to.equal(0);
    expect(defaultFolders.length, 'folder widget still present on default view').to.equal(1);
  });

  it('default seed does not duplicate on relaunch', async () => {
    // Wait for the initial seed.
    await browser.waitUntil(async () => {
      const l = await readLayout();
      return !!l && Array.isArray(l.widgets) && l.widgets.some((w: any) => w.type === 'folder');
    }, { timeout: 5_000 });

    // Soft-reload without clearing localStorage.
    await browser.execute(() => location.reload());
    await browser.pause(300);
    await attachToAppWindow();
    await waitForReady();

    const layout = await readLayout();
    const folders = (layout.widgets as any[]).filter(w => w.type === 'folder');
    expect(folders, 'exactly one folder widget after relaunch').to.have.length(1);
  });
});
