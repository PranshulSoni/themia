/**
 * Widget "Open in window" pop-out (generic mechanism).
 *
 * Poppable widgets (registry `popOut`) can be popped out into their own OS
 * window (`open_widget_window` → `index.html?widgetwindow=1`). The window
 * renders the target widget bound to the shared per-view layout store, so edits
 * made there persist back to the grid tile and vice-versa — presentation config
 * via localStorage, a note's body via the note store it points at ([S-08]).
 * Singleton: re-invoking retargets the one window.
 *
 * SPEC tags: notes [S-11]/[S-12]/[S-13], todo [S-39].
 *
 * NOTE: `__TAURI_INTERNALS__.invoke` is non-writable in the built debug binary,
 * so `invoke('open_widget_window', …)` reaches real Rust and actually spawns the
 * window, exactly what we want to exercise.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, LS_KEYS, readNote, writeNote } from '../helpers/app.js';

async function seedWidget(w: Record<string, unknown>): Promise<string> {
  const rootSel = (w.type === 'notes') ? '.notes-widget' : '.todo-widget';
  await browser.execute((key: string, widget: Record<string, unknown>) => {
    const layout = { widgets: [widget], widgetGap: 12 };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: serialized, oldValue: prior, storageArea: localStorage }));
  }, LS_KEYS.layoutRoot, w);
  await $(rootSel).waitForExist({ timeout: 5_000 });
  return rootSel;
}

async function openWidgetWindow(type: string, id: string): Promise<void> {
  await browser.execute((widgetType: string, widgetId: string) => {
    const internals: any = (window as any).__TAURI_INTERNALS__;
    const label = internals?.metadata?.currentWindow?.label ?? 'main';
    internals.invoke('open_widget_window', {
      parentLabel: label, widgetType, widgetId, viewId: 'default', title: 'Test',
    });
  }, type, id);
}

async function switchToWindowWith(param: string, timeoutMs = 8_000): Promise<string> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    for (const h of await browser.getWindowHandles()) {
      try {
        await browser.switchToWindow(h);
        if (await browser.execute((p: string) => new URLSearchParams(location.search).has(p), param)) return h;
      } catch { /* stale handle */ }
    }
    await browser.pause(150);
  }
  throw new Error(`no window with ?${param} appeared`);
}

async function storedConfig(id: string): Promise<Record<string, any> | undefined> {
  return browser.execute((key: string, widgetId: string) => {
    try {
      const parsed = JSON.parse(localStorage.getItem(key) ?? '{}');
      const w = (parsed.widgets ?? []).find((x: any) => x.id === widgetId);
      return w?.config;
    } catch { return undefined; }
  }, LS_KEYS.layoutRoot, id);
}

/** The body a Notes widget actually persisted. [S-08]/[S-32]: the text lives in
 *  the note STORE; the config only carries the `noteId` pointing at it. */
async function storedNoteSource(widgetId: string): Promise<string | null> {
  const noteId = (await storedConfig(widgetId))?.noteId as string | undefined;
  if (!noteId) return null;
  return browser.execute(async (id: string) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (!invoke) return null;
    return (await invoke('notes_read', { id })) as string | null;
  }, noteId);
}

describe('widget pop-out window (generic)', () => {
  let mainHandle: string;

  beforeEach(async () => {
    await resetAppState();
    mainHandle = await browser.getWindowHandle();
  });

  afterEach(async () => {
    try {
      await browser.execute(() => {
        const i: any = (window as any).__TAURI_INTERNALS__;
        i?.invoke?.('diag_close_windows_by_prefix', { prefix: 'widget-window' });
      });
    } catch { /* best effort */ }
    try { await browser.switchToWindow(mainHandle); } catch {}
  });

  it('[S-11] popping a note out opens a window loaded with its content', async () => {
    await seedWidget({ id: 'notes-1', type: 'notes', x: 0.1, y: 0.1, w: 0.3, h: 0.4, config: { content: 'hello from the grid', fontSize: 13 } });
    await openWidgetWindow('notes', 'notes-1');
    await switchToWindowWith('widgetwindow');

    await $('.widget-window .notes-widget').waitForExist({ timeout: 5_000 });
    const value = await readNote('.widget-window .notes-widget__editor');
    expect(value).to.equal('hello from the grid');
  });

  it('[S-12] editing in the window persists back to the shared note', async () => {
    await seedWidget({ id: 'notes-1', type: 'notes', x: 0.1, y: 0.1, w: 0.3, h: 0.4, config: { content: 'start', fontSize: 13 } });
    await openWidgetWindow('notes', 'notes-1');
    await switchToWindowWith('widgetwindow');
    await $('.widget-window .notes-widget__editor').waitForExist({ timeout: 5_000 });

    await writeNote('edited in the window', '.widget-window .notes-widget__editor');

    await browser.waitUntil(async () => (await storedNoteSource('notes-1')) === 'edited in the window',
      { timeout: 4_000, timeoutMsg: 'window edit did not persist to the shared note' });
  });

  it('[S-17] the popped-out widget renders no header (the window title bar is it)', async () => {
    await seedWidget({ id: 'notes-1', type: 'notes', x: 0.1, y: 0.1, w: 0.3, h: 0.4, config: { content: 'no double header', fontSize: 13, headerMode: 'normal' } });
    await openWidgetWindow('notes', 'notes-1');
    await switchToWindowWith('widgetwindow');
    await $('.widget-window .notes-widget').waitForExist({ timeout: 5_000 });

    expect(await $('.widget-window .widget-header').isExisting(),
      'widget header duplicates the OS title bar').to.equal(false);
    // The body is still there — only the header is suppressed.
    expect(await readNote('.widget-window .notes-widget__editor')).to.equal('no double header');
    // notes [S-29] With no header to host it, the formatting bar falls back to
    // its own row rather than disappearing with the header.
    expect(await $('.widget-window .widget-body > .notes-toolbar').isDisplayed()).to.equal(true);
  });

  it('[S-30] the popped-out widget is squared off, not rounded', async () => {
    await seedWidget({ id: 'notes-1', type: 'notes', x: 0.1, y: 0.1, w: 0.3, h: 0.4, config: { content: 'corners', fontSize: 13 } });
    await openWidgetWindow('notes', 'notes-1');
    await switchToWindowWith('widgetwindow');
    await $('.widget-window .notes-widget').waitForExist({ timeout: 5_000 });

    const radius = await browser.execute(() => {
      const el = document.querySelector('.widget-window .notes-widget');
      return el ? getComputedStyle(el).borderRadius : 'missing';
    });
    expect(radius, 'grid corner radius leaks into the square OS window').to.equal('0px');
  });

  it('[S-39] a todo widget renders inside the pop-out window', async () => {
    await seedWidget({ id: 'todo-1', type: 'todo', x: 0.1, y: 0.1, w: 0.3, h: 0.5, config: { accountIds: [], viewMode: 'list', showCompleted: false, refreshInterval: 60 } });
    await openWidgetWindow('todo', 'todo-1');
    await switchToWindowWith('widgetwindow');

    // The todo widget mounts (empty "select account" state is fine — the point
    // is the generic host rendered the right component for the type).
    await $('.widget-window .todo-widget').waitForExist({ timeout: 5_000 });
  });

  it('[S-13] re-invoking pop-out retargets the single existing window', async () => {
    await seedWidget({ id: 'notes-1', type: 'notes', x: 0.1, y: 0.1, w: 0.3, h: 0.4, config: { content: 'note text', fontSize: 13 } });
    await openWidgetWindow('notes', 'notes-1');
    const handle = await switchToWindowWith('widgetwindow');
    await $('.widget-window').waitForExist({ timeout: 5_000 });

    await browser.switchToWindow(mainHandle);
    await openWidgetWindow('notes', 'notes-1');
    await browser.pause(500);

    const wins: string[] = [];
    for (const h of await browser.getWindowHandles()) {
      try {
        await browser.switchToWindow(h);
        if (await browser.execute(() => new URLSearchParams(location.search).has('widgetwindow'))) wins.push(h);
      } catch { /* stale */ }
    }
    expect(wins).to.deep.equal([handle]);
  });
});
