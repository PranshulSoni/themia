/**
 * Shared helpers for Tauri e2e specs.
 *
 * Conventions:
 *  - All selectors rely on existing CSS classes in the app (see App.tsx, WidgetGrid.tsx).
 *  - Storage keys used by the app live in localStorage; we reset them between specs.
 *  - Keyboard: use browser.keys() which dispatches through the window, mimicking the
 *    JS keydown listener in hotkeys.ts. (The Rust WH_KEYBOARD_LL bridge is not
 *    exercised by webdriver; that path is tested via Rust unit tests instead.)
 */
import { browser, $, $$ } from '@wdio/globals';
import { settle, TIMEOUT } from './wait.ts';

// ── localStorage keys the app owns ───────────────────────────────────────────
// Actual keys per src/layout.ts + src/views.ts:
//   'widget-layout'              default view layout
//   'widget-layout-<viewId>'     per-view layout
//   'widget-global-design'       design state
//   'themia-edit-mode'           edit-mode flag (App.tsx)
//   'themia-auto-check-updates'  updater pref
export const LS_KEYS = {
  editMode:    'themia-edit-mode',
  layoutRoot:  'widget-layout',
  layoutPrefix: 'widget-layout',       // prefix match catches per-view keys too
  design:      'widget-global-design',
  autoUpdates: 'themia-auto-check-updates',
} as const;

/**
 * Switch the webdriver session to the actual Tauri app window.
 *
 * tauri-driver creates an extra `about:blank` context alongside the real
 * Themia window (at http://tauri.localhost/). Without this step the session
 * is attached to the blank page and every selector misses.
 *
 * Idempotent: once attached to tauri.localhost, no-op.
 */
export async function attachToAppWindow(): Promise<void> {
  // The Tauri app window is either at http://tauri.localhost/ (built exe)
  // or http://localhost:5173/ (dev mode). tauri-driver also exposes an
  // about:blank window we want to avoid. When the webview has failed to
  // load (e.g. dev URL with no server), `.grid` will never appear, so we
  // detect that and force-navigate to the built app URL.
  const isAppUrl = (u: string) => !!u && u !== 'about:blank' && !u.startsWith('data:');

  async function isMainWindow(): Promise<boolean> {
    // The widget window carries `body.widget-window` (main.tsx) for its
    // whole lifetime; the `.grid` element disappears while game-mode is
    // suspended. Match on the body class so a leftover suspension from a
    // prior spec doesn't leave us spinning for a non-existent grid.
    // Wrap in try/catch: a freshly-spawned tauri-driver session can
    // briefly attach to a Bidi context whose JS execution scope was already
    // torn down by the prior spec's window churn ("Cannot find context with
    // specified id" / "no such frame"). Treat that as "no main here" so the
    // loop falls through to the next handle / next poll.
    try {
      return await browser.execute(() =>
        document.body?.classList.contains('widget-window')
          || !!document.querySelector('.grid'),
      );
    } catch { return false; }
  }

  const deadline = Date.now() + 15_000;
  let lastSeen: Record<string, string> = {};
  while (Date.now() < deadline) {
    const handles = await browser.getWindowHandles().catch(() => [] as string[]);
    lastSeen = {};
    for (const h of handles) {
      try { await browser.switchToWindow(h); } catch { continue; }
      const url = await browser.getUrl().catch(() => '');
      lastSeen[h] = url;
      if (isAppUrl(url) && (await isMainWindow())) {
        // If we're on the main window but the grid is hidden (the prior
        // spec left game-mode suspended), fire `themia:game-mode` { active:
        // false } so the grid remounts before downstream `waitForReady`
        // calls.
        try {
          const gridMissing = await browser.execute(() =>
            !document.querySelector('.grid'),
          );
          if (gridMissing) {
            await browser.execute(async () => {
              const internals = (window as any).__TAURI_INTERNALS__;
              const real = internals?.__originalInvoke ?? internals?.invoke;
              if (real) await real('plugin:event|emit', {
                event: 'themia:game-mode', payload: { active: false },
              });
            });
            await browser.pause(150);
          }
        } catch { /* ignore */ }
        return;
      }
    }

    // No handle has a rendered .grid yet. Pick the non-blank handle and wait.
    for (const h of handles) {
      try { await browser.switchToWindow(h); } catch { continue; }
      const url = await browser.getUrl().catch(() => '');
      if (isAppUrl(url)) break;
    }
    await browser.pause(200);
  }

  // Last resort: force-navigate to the Tauri built-app URL. Works when the
  // exe tried to load a dev URL that isn't listening.
  try {
    await browser.url('http://tauri.localhost/');
    await browser.waitUntil(() => isMainWindow(), { timeout: 5_000 });
    return;
  } catch {/* fall through to error */}

  throw new Error(`No app window with .grid after 15s. Seen: ${JSON.stringify(lastSeen)}`);
}

/**
 * Reset all app state in localStorage and seed an empty layout so tests
 * start from a blank grid instead of the default clock+help widgets
 * (which occupy col 1 and collide with placement tests).
 *
 * Uses `location.reload()` via `browser.execute` rather than `browser.refresh()`,
 * since the latter hangs in Tauri+wdio sessions when vite HMR holds a long poll.
 */
// Cached main-app window handle (the one that renders the widget grid).
// Reload invalidates the handle on the WebDriver side, so we clear this in
// `resetAppState`. Declared up here so resetAppState can reset it before the
// declaration-order of the rest of the config-window helpers.
let _mainHandle: string | null = null;

export async function resetAppState(): Promise<void> {
  await attachToAppWindow();
  // Close any detached config windows leaked from a prior spec: they share
  // the same Tauri process and would otherwise still be open across the
  // upcoming localStorage clear + reload.
  try {
    await browser.execute(async () => {
      const internals = (window as any).__TAURI_INTERNALS__;
      const invoke = internals?.__originalInvoke ?? internals?.invoke;
      if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'config' });
    });
  } catch {/* command may not exist on older builds */}
  await browser.execute(() => {
    try {
      // Drop every live layout store to empty and cancel any pending
      // debounced save BEFORE we touch localStorage. Without this, the default
      // Folder widget's on-mount Desktop-path resolve+persist had already
      // scheduled a save with the freshly-loaded default widgets, and
      // `beforeunload` flushSave would land that save *after* our clear,
      // restoring the defaults in the new page.
      const reset = (window as any).__themiaResetLayouts;
      if (typeof reset === 'function') reset();
    } catch {}
    try {
      localStorage.clear();
      localStorage.setItem('widget-layout', JSON.stringify({
        widgets: [], widgetGap: 12, bgImage: '', bgImageFit: 'cover',
      }));
    } catch {}
    // Fire a soft reload that doesn't tear down the webdriver session.
    location.reload();
  });
  // Reload may invalidate the cached main-window handle.
  _mainHandle = null;
  // Give the reload a moment to kick in before we poll.
  await browser.pause(300);
  await attachToAppWindow();
  await waitForReady();
}

/** Wait for the widget grid to render. */
export async function waitForReady(): Promise<void> {
  const grid = await $('.grid');
  await grid.waitForExist({ timeout: 15_000 });
}

// ── Edit-mode helpers ────────────────────────────────────────────────────────
export async function isEditMode(): Promise<boolean> {
  return browser.execute(
    (k: string) => localStorage.getItem(k) === 'true',
    LS_KEYS.editMode,
  );
}

export async function enterEditMode(): Promise<void> {
  if (await isEditMode()) return;
  await switchToMain();
  // Ctrl+E hotkey.
  await browser.keys(['Control', 'e']);
  try {
    await browser.waitUntil(() => isEditMode(), { timeout: 1_500 });
  } catch {
    // Fallback: hotkey didn't fire (OS focus is on a detached config window
    // that just popped open, e.g. post-placement). Set the flag directly so
    // the test can proceed; specs that specifically assert the *hotkey*
    // dispatch use `browser.keys` directly without this helper.
    await browser.execute(() => {
      const key = 'themia-edit-mode';
      localStorage.setItem(key, 'true');
      window.dispatchEvent(new StorageEvent('storage', { key, newValue: 'true' }));
    });
    await browser.waitUntil(() => isEditMode(), { timeout: 1_500 });
  }
}

export async function leaveEditMode(): Promise<void> {
  if (!(await isEditMode())) return;
  await switchToMain();
  await browser.keys(['Control', 'e']);
  try {
    await browser.waitUntil(async () => !(await isEditMode()), { timeout: 1_500 });
  } catch {
    await browser.execute(() => {
      const key = 'themia-edit-mode';
      localStorage.setItem(key, 'false');
      window.dispatchEvent(new StorageEvent('storage', { key, newValue: 'false' }));
    });
    await browser.waitUntil(async () => !(await isEditMode()), { timeout: 1_500 });
  }
}

// ── Picker helpers ───────────────────────────────────────────────────────────
// The picker (Add Widget page) is rendered inside the detached config
// window when its target is the bare `add` kind. Ctrl+W from the main
// window invokes openConfigWindow({ kind: 'add' }).

/** Send Ctrl+W from the main window and switch into the picker config
 *  window once it appears. Idempotent: if the picker is already open we
 *  just focus it. */
export async function openPicker(): Promise<void> {
  if (await pickerIsOpen()) {
    await switchToConfigWindow('add');
    await $('.picker-search').waitForDisplayed({ timeout: 3_000 });
    return;
  }
  await switchToMain();
  await browser.keys(['Control', 'w']);
  await waitForConfigWindow('add', 5_000);
  await $('.picker-search').waitForDisplayed({ timeout: 3_000 });
}

export async function pickerIsOpen(): Promise<boolean> {
  return (await findConfigHandle('add')) !== null;
}

export async function closePicker(): Promise<void> {
  const h = await findConfigHandle('add');
  if (!h) return;
  await browser.switchToWindow(h);
  // Picker search auto-focuses. escapeStack is LIFO: 1st Esc blurs the
  // input, 2nd closes the window. A third is a safety net for any extra
  // layer (filter-clear, dropdown, etc).
  for (let i = 0; i < 3; i++) {
    await browser.keys(['Escape']);
    await browser.pause(80);
    if (!(await browser.getWindowHandles()).includes(h)) break;
  }
  if ((await browser.getWindowHandles()).includes(h)) {
    try {
      await browser.execute(async () => {
        const internals = (window as any).__TAURI_INTERNALS__;
        const invoke = internals?.__originalInvoke ?? internals?.invoke;
        if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'config' });
      });
    } catch { /* ignore */ }
  }
  await browser.waitUntil(async () => !(await browser.getWindowHandles()).includes(h), { timeout: 3_000 });
  await switchToMain();
}

/** saveLayout debounces persistence by 50ms with no observable "saved" signal
 *  yet; this is an intrinsically time-based wait (replace with a flush / dev
 *  read-API condition when Phase 1 lands `createPersistedStore`). */
export async function waitForPersist(): Promise<void> {
  await settle(120, 'saveLayout 50ms debounce + localStorage write');
}

/** Search + click first widget card matching label (case-insensitive).
 *  Runs inside the picker config window; returns to the main window so the
 *  caller can place via clickGridCell. */
export async function pickWidget(label: string): Promise<void> {
  await openPicker();
  // We're now in the picker config window.
  const search = await $('.picker-search');
  await search.setValue(label);
  const labels: string[] = [];
  let clicked = false;
  for await (const c of $$('.widget-card')) {
    const txt = await c.$('.widget-card-label').getText();
    labels.push(txt);
    if (txt.toLowerCase() === label.toLowerCase()) {
      await c.click();
      clicked = true;
      break;
    }
  }
  if (!clicked) {
    throw new Error(`No widget card labelled "${label}", got: ${JSON.stringify(labels)}`);
  }
  // After a pick, ConfigWindow puts itself into "placing" mode and minimizes
  // so the main window can receive the placement click. Return there.
  await switchToMain();
}

// ── Grid / placing ───────────────────────────────────────────────────────────
/** Click a grid cell (1-indexed col/row). Used after pickWidget to place.
 *  Auto-dismisses the widget-config window that App.tsx opens after a fresh
 *  placement so subsequent main-grid interactions aren't blocked by a
 *  newly-focused detached window. */
export async function clickGridCell(col: number, row: number): Promise<void> {
  await switchToMain();
  const grid = await $('.grid');
  const size = await grid.getSize();
  // The app's snap grid is now a FINE fractional grid (gridGeometry.gridDims() =
  // round(screen / 10px)) and `.grid` no longer uses CSS-grid tracks, so
  // `getComputedStyle(.grid).gridTemplateColumns` reads `none`, which collapses
  // the column count to 1 and pushes the click offset (≈ full width) outside the
  // element ("move target out of bounds"). Place by a NOMINAL coarse 12×8 grid,
  // the grid these specs were written against, so widget positions match the
  // pre-fractional behaviour and the offset stays inside the element.
  const cols = 12;
  const rows = 8;
  const cellW = size.width / cols;
  const cellH = size.height / rows;
  const cx = Math.round((col - 0.5) * cellW);
  const cy = Math.round((row - 0.5) * cellH);
  await grid.click({ x: Math.round(cx - size.width / 2), y: Math.round(cy - size.height / 2) });
  // Placement triggers App.tsx → openConfigWindow({ kind: 'widget' }) so the
  // user can configure the fresh widget. Close it for the test; callers
  // that want it open can use pickWidget+clickGridCell+rightClick→Configure.
  // Placement spawns the widget-config window asynchronously; wait for its
  // handle to appear (up to a bound) instead of a fixed sleep. If it never
  // opens (no-op placement), proceed with h undefined.
  let h: string | undefined;
  await browser
    .waitUntil(async () => { h = await findConfigHandle('widget'); return !!h; }, { timeout: TIMEOUT.quick })
    .catch(() => { /* no config window opened, fine */ });
  if (h) {
    await browser.switchToWindow(h);
    await browser.execute(() => {
      const internals = (window as any).__TAURI_INTERNALS__;
      const invoke = internals?.__originalInvoke ?? internals?.invoke;
      if (invoke) invoke('diag_close_windows_by_prefix', { prefix: 'config' }).catch(() => {});
    });
    await browser.waitUntil(async () => !(await browser.getWindowHandles()).includes(h), { timeout: 3_000 }).catch(() => {});
    await switchToMain();
  }
}

/** Count widgets currently rendered in the active grid. Scoped to direct
 *  children of the visible `.grid` so we don't count `.widget`-classed
 *  descendants of widget config bodies (some widgets render their own
 *  `.widget-header` / `.widget-title` subtrees that, on certain Tauri / WV2
 *  builds, surface a stray ancestor matching the bare `.widget` selector). */
export async function widgetCount(): Promise<number> {
  return browser.execute(() =>
    document.querySelectorAll('.grid > .widget').length,
  );
}

export interface SeedWidget {
  type: string;
  col?: number;
  row?: number;
  colSpan?: number;
  rowSpan?: number;
  config?: Record<string, any>;
  /** Optional explicit id; auto-generated when absent. */
  id?: string;
  label?: string;
}

/**
 * Place widgets directly into the active view's layout via the
 * `window.__themiaSeedWidgets` hook exposed by `src/layout.ts`. Skips the
 * picker + click-to-place flow, saving ~2–3 s per widget. Use for specs
 * that only need a rendered widget; specs that exercise the picker /
 * placement code paths should keep using `pickWidget` + `clickGridCell`.
 */
export async function seedWidgets(widgets: SeedWidget[]): Promise<void> {
  await switchToMain();
  await browser.execute((ws: SeedWidget[]) => {
    const seed = (window as any).__themiaSeedWidgets;
    if (typeof seed !== 'function') {
      throw new Error('__themiaSeedWidgets not exposed; ensure navigator.webdriver is true');
    }
    const expanded = ws.map((w, i) => ({
      id: w.id ?? `${w.type}-seed-${i}-${Date.now()}`,
      type: w.type,
      col: w.col ?? 1,
      row: w.row ?? 1,
      colSpan: w.colSpan ?? 3,
      rowSpan: w.rowSpan ?? 2,
      config: w.config ?? {},
      label: w.label,
    }));
    seed(expanded);
  }, widgets);
  // Solid re-renders synchronously after setLayout, but give the
  // microtask queue a tick so the `.widget` DOM is in place when the
  // caller queries it.
  await browser.pause(50);
}

// ── Views ────────────────────────────────────────────────────────────────────
export async function activeViewName(): Promise<string> {
  const el = await $('.view-chip.active .view-name-text');
  return el.getText();
}

export async function clickView(name: string): Promise<void> {
  for await (const chip of $$('.view-chip')) {
    const txt = await chip.$('.view-name-text').getText();
    if (txt === name) {
      await chip.$('.view-name-btn').click();
      return;
    }
  }
  throw new Error(`No view chip named "${name}"`);
}

// ── Context menu ─────────────────────────────────────────────────────────────
// Classes per src/ContextMenu.tsx:
//   .ctx-menu       menu root
//   .ctx-item       clickable item (with .ctx-label inside)
//   .ctx-sep        separator
//   .ctx-header     section header (non-clickable)
//   .ctx-backdrop   backdrop
export async function rightClick(selector: string): Promise<void> {
  const el = await $(selector);
  await el.click({ button: 'right' });
  await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
}

export async function ctxMenuIsOpen(): Promise<boolean> {
  return (await $$('.ctx-menu').length) > 0;
}

export async function ctxMenuItem(label: string): Promise<WebdriverIO.Element> {
  const labels: string[] = [];
  for await (const it of $$('.ctx-menu .ctx-item')) {
    const txt = (await it.$('.ctx-label').getText()).trim();
    labels.push(txt);
    if (txt === label) return it;
  }
  throw new Error(`ctx menu item "${label}" not found, got: ${JSON.stringify(labels)}`);
}

/**
 * Override window.confirm to return `accept`. WidgetGrid's Remove action
 * uses window.confirm which blocks tests; call this before clicking.
 */
export async function stubConfirm(accept = true): Promise<void> {
  await browser.execute((a: boolean) => {
    (window as any).confirm = () => a;
  }, accept);
}

// ── Focus ────────────────────────────────────────────────────────────────────
export async function activeElementTag(): Promise<string> {
  // Wrap in try/catch: when polling around a closing window, the JS
  // execution context may have been torn down already, which would otherwise
  // surface as an unhandled "no such frame" Bidi error inside waitUntil.
  try {
    return await browser.execute(() => document.activeElement?.tagName.toLowerCase() ?? '');
  } catch { return ''; }
}

// ── Detached config-window switching ─────────────────────────────────────────
// The settings / picker / widget-config / view-settings / display-settings
// surfaces all live inside a SINGLE detached Tauri window (Rust label
// `"config"`). The window URL is `index.html?config=1&target=<encoded>`,
// where the encoded target follows ConfigTarget (`app:<page>`, `view:<id>`,
// `display:<id>`, `widget:<id>`, or bare `add` for the picker).
//
// `findConfigHandle(kind)` matches by the target's kind prefix. To ease the
// migration from the pre-refactor helpers, a few legacy aliases are mapped:
//   'settings'        → 'app'
//   'view-bar'        → 'display'
//   'picker'          → 'add'

const LEGACY_KIND_ALIAS: Record<string, string> = {
  settings: 'app',
  'view-bar': 'display',
  picker: 'add',
};

/** Read the target kind prefix (e.g. 'app', 'add', 'widget') of the current
 *  window. Returns null if the current window is not a config window. */
async function configKindOfCurrent(): Promise<string | null> {
  // Ignore stale/torn-down execution contexts so a single broken handle
  // doesn't abort the whole window scan.
  try {
    return await browser.execute(() => {
      const sp = new URLSearchParams(location.search);
      if (sp.get('config') == null) return null;
      const raw = sp.get('target') ?? '';
      if (!raw) return null;
      const colon = raw.indexOf(':');
      return colon < 0 ? raw : raw.slice(0, colon);
    });
  } catch { return null; }
}

/** Find the OS window handle whose target kind matches `kind` (or any
 *  config window if `kind` is undefined). Restores the previously-active
 *  window before returning so probe calls don't have a switch side effect. */
export async function findConfigHandle(kind?: string): Promise<string | null> {
  const want = kind ? (LEGACY_KIND_ALIAS[kind] ?? kind) : undefined;
  const handles = await browser.getWindowHandles();
  const prev = await browser.getWindowHandle().catch(() => null as string | null);
  let found: string | null = null;
  for (const h of handles) {
    try { await browser.switchToWindow(h); } catch { continue; }
    const k = await configKindOfCurrent();
    if (k && (want === undefined || k === want)) { found = h; break; }
  }
  if (prev) { try { await browser.switchToWindow(prev); } catch {} }
  return found;
}

/**
 * Find the main app window handle (the one rendering the widget grid). Cached
 * across calls; invalidated on `resetAppState()` (page reload changes handle).
 */
async function findMainHandle(): Promise<string> {
  if (_mainHandle) {
    const handles = await browser.getWindowHandles();
    if (handles.includes(_mainHandle)) return _mainHandle;
    _mainHandle = null;
  }
  const handles = await browser.getWindowHandles();
  for (const h of handles) {
    try { await browser.switchToWindow(h); } catch { continue; }
    let isMain = false;
    try {
      // Identify the main widget window: it carries `body.widget-window`
      // (main.tsx) regardless of whether the grid is currently mounted
      // (game-mode suspends the grid but keeps the body class + game-mode
      // badge). Fallback to `.grid` or `.game-mode-badge` for older builds.
      isMain = await browser.execute(() =>
        document.body.classList.contains('widget-window')
          || !!document.querySelector('.grid')
          || !!document.querySelector('.game-mode-badge'),
      );
    } catch { isMain = false; }
    if (isMain) { _mainHandle = h; return h; }
  }
  throw new Error('main app window (widget-window) not found');
}

export async function switchToMain(): Promise<void> {
  await browser.switchToWindow(await findMainHandle());
}

/** Switch to a config window of the given kind, throwing if not open. */
export async function switchToConfigWindow(kind: string): Promise<void> {
  const h = await findConfigHandle(kind);
  if (!h) throw new Error(`no config window of kind "${kind}" is open`);
  await browser.switchToWindow(h);
}

/** Wait for a config window of the given kind to appear, then switch into it. */
export async function waitForConfigWindow(kind: string, timeout = 5_000): Promise<void> {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const h = await findConfigHandle(kind);
    if (h) { await browser.switchToWindow(h); return; }
    await browser.pause(100);
  }
  throw new Error(`config window kind="${kind}" did not appear within ${timeout}ms`);
}

/** Run `fn` inside the config-window context; restore main window afterward. */
async function inConfigWindow<T>(kind: string, fn: () => Promise<T>): Promise<T> {
  const main = await findMainHandle();
  await switchToConfigWindow(kind);
  try { return await fn(); }
  finally {
    try {
      const handles = await browser.getWindowHandles();
      if (handles.includes(main)) await browser.switchToWindow(main);
    } catch {/* ignore */}
  }
}

/** Close every spawned config window via the Rust diag command. */
export async function closeAllConfigWindows(): Promise<void> {
  await switchToMain().catch(() => {});
  await browser.execute(async () => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'config' });
  });
  await browser.pause(150);
}

// ── Settings (unified config window, target prefix "app") ────────────────────
// After the refactor, application settings (General / Style / License /
// Accounts / Updates / Logs) are pages of the single config window. Each
// page is a sidebar entry in ConfigTree (`.settings-nav-item.config-tree-child`)
// whose `.config-tree-label` text matches the page name.

/** Map a user-facing settings tab label to its ConfigTarget page. */
const SETTINGS_TAB_PAGES: Record<string, string> = {
  General:  'general',
  Style:    'style',
  License:  'license',
  Accounts: 'accounts',
  Updates:  'updates',
  Logs:     'logs',
};

async function invokeOpenConfigWindow(target: string | null): Promise<void> {
  await switchToMain();
  await browser.execute(async (t: string | null) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (!invoke) throw new Error('Tauri invoke missing on main window');
    await invoke('open_config_window', { parentLabel: 'main', target: t });
  }, target);
}

export async function settingsIsOpen(): Promise<boolean> {
  return (await findConfigHandle('app')) !== null;
}

export async function openSettings(page: keyof typeof SETTINGS_TAB_PAGES | 'General' = 'General'): Promise<void> {
  const target = `app:${SETTINGS_TAB_PAGES[page] ?? 'general'}`;
  if (await settingsIsOpen()) {
    await switchToConfigWindow('app');
  } else {
    await invokeOpenConfigWindow(target);
    await waitForConfigWindow('app', 5_000);
  }
  // Always wait for the shell + nav-tree to be live: when the window was
  // open from a prior step it might still be mid-reload, with no nav items
  // rendered yet.
  await $('.config-shell').waitForDisplayed({ timeout: 5_000 });
  await $('.config-tree .settings-nav-item').waitForExist({ timeout: 5_000 });
}

export async function closeSettings(): Promise<void> {
  const h = await findConfigHandle('app');
  if (!h) return;
  await browser.switchToWindow(h);
  for (let i = 0; i < 3; i++) {
    await browser.keys(['Escape']);
    await browser.pause(80);
    if (!(await browser.getWindowHandles()).includes(h)) break;
  }
  if ((await browser.getWindowHandles()).includes(h)) {
    try {
      await browser.execute(async () => {
        const internals = (window as any).__TAURI_INTERNALS__;
        const invoke = internals?.__originalInvoke ?? internals?.invoke;
        if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'config' });
      });
    } catch { /* ignore */ }
  }
  await browser.waitUntil(async () => !(await browser.getWindowHandles()).includes(h), { timeout: 3_000 });
  await switchToMain();
}

/** Find the ConfigTree row that corresponds to a settings page. Switches
 *  into the app config window. */
export async function settingsTab(label: string): Promise<WebdriverIO.Element> {
  await switchToConfigWindow('app');
  for await (const btn of $$('.settings-nav-item')) {
    const t = (await btn.$('.config-tree-label').getText().catch(async () => btn.getText())).trim();
    if (t === label) return btn;
  }
  throw new Error(`Settings tab "${label}" not found`);
}

export async function activeSettingsTab(): Promise<string> {
  return inConfigWindow('app', async () => {
    const el = await $('.settings-nav-item.config-tree-child.active .config-tree-label');
    return (await el.getText()).trim();
  });
}

/**
 * Enable Developer Mode via the General tab switch, required to reveal
 * the Logs tab + the Demo mode row.
 */
export async function enableDevMode(): Promise<void> {
  await browser.execute(() => {
    const key = 'widget-global-design';
    const cur = JSON.parse(localStorage.getItem(key) ?? '{}');
    if (cur.devMode === true) return;
    const next = { ...cur, devMode: true };
    localStorage.setItem(key, JSON.stringify(next));
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: JSON.stringify(next),
    }));
  });
  // Small settle for Solid reactivity to re-render nav items.
  await browser.pause(60);
}

/** [LOG-04] Logging is opt-in (master switch, default off, dev setting).
 *  Seed the design blob so logger/console records actually reach the log
 *  pipeline; the App effect pushes the change to the Rust gate live. */
export async function enableLogging(): Promise<void> {
  await browser.execute(() => {
    const key = 'widget-global-design';
    const cur = JSON.parse(localStorage.getItem(key) ?? '{}');
    if (cur.loggingEnabled === true) return;
    const next = { ...cur, loggingEnabled: true };
    localStorage.setItem(key, JSON.stringify(next));
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: JSON.stringify(next),
    }));
  });
  await browser.pause(60);
}

// ── View Settings (unified config window, target prefix "view") ──────────────
/** Open the view-settings page of the config window. Pass `viewId` to target
 *  a specific view; defaults to the active view. */
export async function openViewSettings(viewId?: string): Promise<void> {
  if ((await findConfigHandle('view')) !== null) {
    await switchToConfigWindow('view');
    return;
  }
  const id = viewId ?? await browser.execute(() => {
    try {
      const raw = localStorage.getItem('widget-views');
      const v = raw ? JSON.parse(raw) : null;
      return v?.activeId ?? 'default';
    } catch { return 'default'; }
  });
  await invokeOpenConfigWindow(`view:${id}`);
  await waitForConfigWindow('view', 5_000);
  await $('.config-shell').waitForDisplayed({ timeout: 3_000 });
}

export async function viewSettingsTab(label: 'General' | 'Layout' | 'Appearance'): Promise<WebdriverIO.Element> {
  await switchToConfigWindow('view');
  for await (const btn of $$('.config-panel .settings-nav-item')) {
    const t = (await btn.getText()).trim();
    if (t === label) return btn;
  }
  throw new Error(`View-settings tab "${label}" not found`);
}

// ── View Bar Settings → now part of Display Settings ─────────────────────────
// The dedicated view-bar modal was merged into DisplaySettingsModal, which
// renders for target `display:<monitorId>`. We open it for the current
// monitor so the size + show-labels controls remain reachable by name.
export async function openViewBarSettings(): Promise<void> {
  if ((await findConfigHandle('display')) !== null) {
    await switchToConfigWindow('display');
    return;
  }
  await switchToMain();
  const monitorId = await browser.execute(async () => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    try {
      const winLabel = (window as any).__TAURI_INTERNALS__?.metadata?.currentWindow?.label ?? 'main';
      const id = invoke ? await invoke('get_monitor_id', { label: winLabel }) : null;
      return id ?? 'default';
    } catch { return 'default'; }
  }) as string;
  await invokeOpenConfigWindow(`display:${monitorId}`);
  await waitForConfigWindow('display', 5_000);
  await $('.config-shell').waitForDisplayed({ timeout: 3_000 });
}

// ── Widget config modal (detached, kind="widget") ────────────────────────────
export async function configModalIsOpen(): Promise<boolean> {
  return (await findConfigHandle('widget')) !== null;
}

export async function closeConfigModal(): Promise<void> {
  const h = await findConfigHandle('widget');
  if (!h) return;
  await browser.switchToWindow(h);
  // Try up to three escapes: an open dropdown / focused input can absorb
  // the first one or two (input-blur escapeStack layer + dropdown layer +
  // window-close layer). Stop early once the window is gone.
  for (let i = 0; i < 3; i++) {
    await browser.keys(['Escape']);
    await browser.pause(80);
    if (!(await browser.getWindowHandles()).includes(h)) break;
  }
  // Fallback: forcibly close via the diag command if Esc still hasn't
  // landed (some widget configs leave a focused element that intercepts).
  if ((await browser.getWindowHandles()).includes(h)) {
    try {
      await browser.execute(async () => {
        const internals = (window as any).__TAURI_INTERNALS__;
        const invoke = internals?.__originalInvoke ?? internals?.invoke;
        if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'config' });
      });
    } catch { /* ignore */ }
  }
  await browser.waitUntil(async () => !(await browser.getWindowHandles()).includes(h), { timeout: 3_000 });
  await switchToMain();
}

/** Set the widget-config Header input. Switches into the detached config
 *  window for the duration, then returns to the main window. */
export async function setConfigHeader(value: string): Promise<void> {
  await inConfigWindow('widget', async () => {
    // Header-surface refactor renamed this input (ConfigTabs.tsx: .header-mode-field).
    const input = await $('.header-mode-field');
    await input.setValue(value);
  });
}

// ── Widget toolbar (edit mode) ───────────────────────────────────────────────
// Classes per WidgetGrid.tsx. Each button has a `title` attribute.
export function widgetToolbarBtn(title: 'Configure' | 'Copy' | 'Cut' | 'Remove') {
  return $(`.widget.edit .widget-toolbar .toolbar-btn[title="${title}"]`);
}

/**
 * Click a toolbar button via DOM `.click()`, since the inline lucide-solid SVG
 * intercepts WebDriver's geometric click and causes "element click
 * intercepted" errors. DOM click bypasses hit-testing.
 */
export async function clickToolbarBtn(title: 'Configure' | 'Copy' | 'Cut' | 'Remove'): Promise<void> {
  await browser.execute((t: string) => {
    const btn = document.querySelector<HTMLButtonElement>(
      `.widget.edit .widget-toolbar .toolbar-btn[title="${t}"]`,
    );
    btn?.click();
  }, title);
}

// ── CSS / design helpers ─────────────────────────────────────────────────────
/** Read a CSS custom property from :root (document.documentElement). */
export async function readCssVar(name: string): Promise<string> {
  return browser.execute((n: string) => {
    return getComputedStyle(document.documentElement).getPropertyValue(n).trim();
  }, name);
}

/** Read the global design state (theme, uiScale, demoMode). */
export async function readDesign(): Promise<Record<string, any>> {
  return browser.execute(() => {
    try { return JSON.parse(localStorage.getItem('widget-global-design') ?? '{}'); }
    catch { return {}; }
  });
}

/**
 * Set a NumberStepper's inner <input type="number"> value and dispatch
 * input+change+blur so Solid listeners (onInput / onBlur) and Stepper's
 * commit-on-blur fire. WebDriver's setValue doesn't play well with the
 * stepper's draft buffer, so we drive the native setter directly.
 *
 * `selector` should match the inner number input (e.g. `.number-stepper input[type="number"]`)
 * or any element selector; pass the stepper wrapper and we'll find the input.
 */
export async function setStepperValue(selector: string, value: number): Promise<void> {
  await browser.execute((sel: string, v: number) => {
    const root = document.querySelector<HTMLElement>(sel);
    if (!root) throw new Error(`stepper "${sel}" not found`);
    const el = root.matches('input[type="number"]')
      ? (root as HTMLInputElement)
      : root.querySelector<HTMLInputElement>('input[type="number"]');
    if (!el) throw new Error(`stepper input not found inside "${sel}"`);
    const setter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype, 'value',
    )?.set;
    setter?.call(el, String(v));
    el.dispatchEvent(new Event('input',  { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('blur',   { bubbles: true }));
  }, selector, value);
}

/**
 * Click a button inside the config window's content panel by visible text.
 * Avoids the SVG-intercepts-click issue by using DOM `.click()`.
 */
export async function clickSettingsBtn(text: string): Promise<void> {
  const clicked = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    for (const b of btns) {
      if ((b.textContent ?? '').trim() === t) { b.click(); return true; }
    }
    return false;
  }, text);
  if (!clicked) throw new Error(`settings button "${text}" not found`);
}

// ── Tauri invoke stubbing ────────────────────────────────────────────────────
/**
 * Install a Tauri-invoke stub in the page. Mappings are (command name) →
 * (return value). Missing commands throw; pass a wildcard `"*"` entry for
 * a default value.
 *
 * Call in `beforeEach` AFTER resetAppState but BEFORE triggering widget
 * mounts that might invoke commands. Because many widgets fire invokes in
 * `onMount`, the cleanest place is *after* the app window is attached but
 * *before* the widget is picked.
 *
 * The stub targets `window.__TAURI_INTERNALS__.invoke` (Tauri 2's IPC
 * transport) so it intercepts all `invoke()` calls from @tauri-apps/api.
 */
export async function stubTauriInvoke(mappings: Record<string, any>): Promise<void> {
  await browser.execute((m: Record<string, any>) => {
    const internals = (window as any).__TAURI_INTERNALS__ ?? ((window as any).__TAURI_INTERNALS__ = {});
    const original = internals.__originalInvoke ?? internals.invoke;
    if (!internals.__originalInvoke) internals.__originalInvoke = original;
    internals.invoke = async (cmd: string, _args: any) => {
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      if (Object.prototype.hasOwnProperty.call(m, '*')) return m['*'];
      // Fall through to the real invoke for unmapped commands; keeps the
      // app functioning (set_interactive, get_monitor_id, etc.).
      if (original) return original(cmd, _args);
      throw new Error(`invoke stub miss: "${cmd}"`);
    };
  }, mappings);
}

/** Restore the original invoke. */
export async function unstubTauriInvoke(): Promise<void> {
  await browser.execute(() => {
    const internals = (window as any).__TAURI_INTERNALS__;
    if (internals?.__originalInvoke) internals.invoke = internals.__originalInvoke;
  });
}

/**
 * Stub the wrapped invoke exposed by `src/demo/invoke.ts` via the
 * `window.__themiaInvoke.setInvoke` hook. Every widget that imports invoke
 * from that wrapper (system widgets, online widgets, office widgets, ...)
 * goes through it, so this is the reliable path for stubbing under
 * Tauri 2, since `__TAURI_INTERNALS__.invoke` itself is non-writable and a raw
 * assignment silently no-ops.
 *
 * Pass `'*'` for a catch-all return value.
 */
export async function stubThemiaInvoke(mappings: Record<string, any>): Promise<void> {
  await browser.execute((m: Record<string, any>) => {
    const hook = (window as any).__themiaInvoke;
    if (!hook?.setInvoke) {
      throw new Error('__themiaInvoke.setInvoke not exposed; ensure navigator.webdriver is true');
    }
    hook.setInvoke(async (cmd: string, _args: any) => {
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      if (Object.prototype.hasOwnProperty.call(m, '*')) return m['*'];
      return null;
    });
  }, mappings);
}

// ---------------------------------------------------------------------------
// Notes editor
//
// The Notes widget is a contenteditable markdown editor, not a textarea, so
// `getValue`/`setValue` don't apply. It reads its source back out of the DOM on
// `input` (see widgets/others/notes/editorDom.ts), which is why writing means
// "replace the children, then fire input".
// ---------------------------------------------------------------------------

/** Read the markdown source out of a Notes editor. */
export async function readNote(selector = '.notes-widget__editor'): Promise<string> {
  return browser.execute((sel: string) => {
    const el = document.querySelector<HTMLElement>(sel);
    if (!el) return '';
    return Array.from(el.childNodes)
      .map(n => (n as HTMLElement).innerText ?? n.textContent ?? '')
      .join('\n');
  }, selector);
}

/** Replace a Notes editor's content and let the widget re-project + persist. */
export async function writeNote(text: string, selector = '.notes-widget__editor'): Promise<void> {
  await browser.execute((sel: string, value: string) => {
    const el = document.querySelector<HTMLElement>(sel);
    if (!el) throw new Error(`notes editor not found: ${sel}`);
    el.textContent = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }, selector, text);
}
