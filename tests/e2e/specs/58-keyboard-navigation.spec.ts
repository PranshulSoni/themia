/**
 * Keyboard navigation across the unified config window.
 *
 * Covers:
 *  - ConfigTree roving tabindex: ArrowDown/Up moves focus between tree nodes,
 *    Enter activates, Home/End jumps to first/last, type-ahead jumps by label
 *    prefix, ArrowRight expands a view row, ArrowLeft collapses.
 *  - F6 toggles focus between the sidebar tree and the content pane.
 *  - IconPicker: Enter on trigger opens the panel, arrow keys walk the grid,
 *    Enter picks (closes panel + persists), Escape closes without picking and
 *    returns focus to the trigger (does NOT close the config window).
 *  - ToggleSwitch carries the `kbd-focusable` class so the cyan focus ring is
 *    visible on Tab.
 *
 * The unified config window lives at `?config=1`. We discover its handle by
 * scanning for any handle with `config=` in the URL (rather than the legacy
 * `?config=<kind>` matching some old helpers still use).
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, enterEditMode, switchToMain,
} from '../helpers/app.js';

// ── Local helpers ────────────────────────────────────────────────────────────
async function findUnifiedConfigHandle(): Promise<string | null> {
  const handles = await browser.getWindowHandles();
  for (const h of handles) {
    try { await browser.switchToWindow(h); } catch { continue; }
    const isCfg = await browser.execute(
      () => new URLSearchParams(location.search).get('config') !== null,
    );
    if (isCfg) return h;
  }
  return null;
}

async function waitForUnifiedConfig(timeout = 5_000): Promise<void> {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const h = await findUnifiedConfigHandle();
    if (h) {
      await browser.switchToWindow(h);
      await $('.config-tree').waitForExist({ timeout: 2_000 });
      return;
    }
    await browser.pause(120);
  }
  throw new Error('unified config window did not appear');
}

async function openUnifiedConfig(): Promise<void> {
  // Open via the same Tauri command the app uses internally, independent of
  // whichever edit-bar button currently triggers it (which is in flux on
  // this branch).
  await switchToMain();
  await enterEditMode();
  await browser.execute(async () => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (invoke) await invoke('open_config_window', { parentLabel: 'main', target: null });
  });
  await waitForUnifiedConfig();
}

async function closeUnifiedConfig(): Promise<void> {
  const h = await findUnifiedConfigHandle();
  if (!h) return;
  await browser.switchToWindow(h);
  await browser.keys(['Escape']);
  await browser.pause(150);
  const stillOpen = (await browser.getWindowHandles()).includes(h);
  if (stillOpen) {
    // A second Escape, or close via Rust diag command.
    await browser.keys(['Escape']);
    await browser.pause(150);
  }
  await switchToMain().catch(() => {});
}

async function focusedLabel(): Promise<string> {
  return browser.execute(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el) return '';
    return (el.querySelector('.config-tree-label')?.textContent ?? el.textContent ?? '').trim();
  });
}

async function focusedIsInTree(): Promise<boolean> {
  return browser.execute(() => !!document.activeElement?.closest('.config-tree'));
}

async function focusedIsInContent(): Promise<boolean> {
  return browser.execute(() => !!document.activeElement?.closest('.settings-content'));
}

// ConfigTree fetches the active-monitor list asynchronously on mount
// (refreshMonitors → get_active_monitors). When it resolves it appends the
// Displays rows, and because rows() emits fresh objects each pass, SolidJS
// <For> remounts EVERY tree button — momentarily dropping DOM focus and
// re-indexing refs[]. Driving keys during that remount makes focusRow() land
// on a stale ref and silently no-op (roving focus never moves), which is the
// flake behind the Home/End, type-ahead and F6 specs (it reads as
// deterministic under load, when the monitor fetch is slow enough to always
// resolve mid-test). Wait until the roving-row count has stopped changing —
// i.e. monitors are in and the tree has settled — before anchoring focus and
// driving keys. Condition-driven (settles 3 samples PAST the last row change),
// not a fixed sleep.
async function waitForTreeSettled(timeout = 5_000): Promise<void> {
  let prev = -1;
  let stable = 0;
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const n = await browser.execute(
      () => document.querySelectorAll('.config-tree-list [role="treeitem"]').length,
    );
    if (n > 0 && n === prev) {
      if (++stable >= 3) return;
    } else {
      stable = 0;
    }
    prev = n;
    await browser.pause(80);
  }
}

async function focusFirstTreeItem(): Promise<void> {
  // Let the async monitor fetch land and the <For> finish remounting first,
  // so the focus we establish below survives into the key presses.
  await waitForTreeSettled();
  await browser.execute(() => {
    const first = document.querySelector<HTMLElement>('.config-tree .kbd-focusable');
    first?.focus();
  });
  // ConfigTree's roving focusIdx is initialised from the active selection,
  // which may point at a later row (e.g. General). Press Home to drive both
  // DOM focus and the internal focusIdx back to row 0 so subsequent arrow
  // navigation starts from a known anchor.
  await browser.keys(['Home']);
  await browser.pause(30);
}

async function activeTreeLabel(): Promise<string> {
  return browser.execute(() => {
    const el = document.querySelector<HTMLElement>('.config-tree .config-tree-item.active');
    return (el?.querySelector('.config-tree-label')?.textContent ?? '').trim();
  });
}

async function iconPickerOpen(): Promise<boolean> {
  return (await $$('.icon-picker-panel').length) > 0;
}

async function focusedIconKey(): Promise<string> {
  return browser.execute(() => {
    const el = document.querySelector<HTMLElement>('.icon-picker-panel .icon-picker-btn.kbd-focused');
    return el?.getAttribute('title') ?? '';
  });
}

// ── Specs ────────────────────────────────────────────────────────────────────
describe('keyboard navigation: settings pages', () => {
  beforeEach(async () => {
    await resetAppState();
    await openUnifiedConfig();
  });

  afterEach(async () => {
    await closeUnifiedConfig();
  });

  it('ConfigTree: ArrowDown/ArrowUp moves roving focus between nodes', async () => {
    await focusFirstTreeItem();
    const first = await focusedLabel();
    expect(first.length).to.be.greaterThan(0);

    await browser.keys(['ArrowDown']);
    const second = await focusedLabel();
    expect(second).to.not.equal(first);
    expect(await focusedIsInTree()).to.equal(true);

    await browser.keys(['ArrowUp']);
    expect(await focusedLabel()).to.equal(first);
  });

  it('ConfigTree: Enter on a focused node activates it (.active selection moves)', async () => {
    await focusFirstTreeItem();
    // Add Widget and the edit-mode toggle are no longer roving tree rows; both
    // live pinned in the sidebar footer, so row 0 is the first settings page
    // ("General") and ArrowDown lands on the next one.
    await browser.keys(['ArrowDown']);
    const target = await focusedLabel();
    expect(target.length).to.be.greaterThan(0);

    await browser.keys(['Enter']);
    await browser.pause(80);
    expect(await activeTreeLabel()).to.equal(target);
  });

  it('ConfigTree: Home/End jump to first/last node', async () => {
    await focusFirstTreeItem();
    await browser.keys(['End']);
    const last = await focusedLabel();
    expect(last.length).to.be.greaterThan(0);

    await browser.keys(['Home']);
    const first = await focusedLabel();
    expect(first).to.not.equal(last);
  });

  it('ConfigTree: type-ahead jumps to a node by label prefix', async () => {
    await focusFirstTreeItem();
    // "Style" lives in App Settings, so typing "s" should jump to it (case-insensitive).
    await browser.keys(['s']);
    await browser.pause(60);
    const label = (await focusedLabel()).toLowerCase();
    expect(label.startsWith('s'), `expected label starting with "s", got "${label}"`).to.equal(true);
  });

  it('F6: jumps focus from tree to content pane and back', async () => {
    await focusFirstTreeItem();
    expect(await focusedIsInTree()).to.equal(true);

    await browser.keys(['F6']);
    await browser.pause(60);
    expect(await focusedIsInContent()).to.equal(true);

    await browser.keys(['F6']);
    await browser.pause(60);
    expect(await focusedIsInTree()).to.equal(true);
  });

  it('IconPicker: Enter opens, arrow keys walk grid, Enter picks (Escape stays in window)', async () => {
    // Navigate to a view node, open it. Find any view-row in the tree.
    await browser.execute(() => {
      const viewBtn = document.querySelector<HTMLElement>('.config-tree .config-tree-view');
      viewBtn?.focus();
      viewBtn?.click();
    });
    await browser.pause(120);

    // ViewSettingsBody renders the IconPicker trigger as the first `.dropdown-trigger`
    // in `.settings-content` (no Dropdown components on the View page).
    await browser.execute(() => {
      const trig = document.querySelector<HTMLElement>('.settings-content .dropdown-trigger');
      trig?.focus();
    });

    expect(await iconPickerOpen()).to.equal(false);
    await browser.keys(['Enter']);
    await browser.pause(150);
    expect(await iconPickerOpen()).to.equal(true);

    const startKey = await focusedIconKey();
    expect(startKey.length).to.be.greaterThan(0);

    // Walk right then down: the new keyboard index should differ.
    await browser.keys(['ArrowRight']);
    await browser.pause(40);
    const afterRight = await focusedIconKey();
    expect(afterRight).to.not.equal(startKey);

    await browser.keys(['ArrowDown']);
    await browser.pause(40);
    const afterDown = await focusedIconKey();
    expect(afterDown).to.not.equal(afterRight);

    // Pick with Enter: panel closes, config window stays open.
    await browser.keys(['Enter']);
    await browser.waitUntil(async () => !(await iconPickerOpen()), { timeout: 2_000 });
    const cfgHandle = await findUnifiedConfigHandle();
    expect(cfgHandle, 'config window should still be open after Enter pick').to.be.a('string');

    // Re-open then Escape: picker closes, config window stays.
    await browser.execute(() => {
      const trig = document.querySelector<HTMLElement>('.settings-content .dropdown-trigger');
      trig?.focus();
    });
    await browser.keys(['Enter']);
    await browser.pause(150);
    expect(await iconPickerOpen()).to.equal(true);

    await browser.keys(['Escape']);
    await browser.waitUntil(async () => !(await iconPickerOpen()), { timeout: 2_000 });
    const stillOpen = (await browser.getWindowHandles()).includes(cfgHandle!);
    expect(stillOpen, 'Escape on IconPicker must NOT close the config window').to.equal(true);
  });

  it('ToggleSwitch carries kbd-focusable class so focus ring is visible', async () => {
    // Walk to the General page (likely contains toggle switches).
    await browser.execute(() => {
      const items = Array.from(
        document.querySelectorAll<HTMLElement>('.config-tree .config-tree-item'),
      );
      const general = items.find(b => (b.textContent ?? '').toLowerCase().includes('general'));
      general?.click();
    });
    await browser.pause(150);

    const hasFocusable = await browser.execute(() => {
      const sw = document.querySelector<HTMLElement>('.settings-content .design-switch');
      return !!sw?.classList.contains('kbd-focusable');
    });
    expect(hasFocusable, 'design-switch needs kbd-focusable class for the cyan focus ring').to.equal(true);
  });
});
