/**
 * Back/forward navigation across the unified config window.
 *
 * The window keeps a browser-style history of the nodes the user visits. Two
 * gestures step through it:
 *   - Alt+ArrowLeft / Alt+ArrowRight (Windows back/forward convention).
 *   - Mouse side buttons (XButton1=button 3 back, XButton2=button 4 forward).
 *
 * Both feed the same `goBack()`/`goForward()` path in ConfigWindow, so the
 * mouse case is exercised by dispatching a synthetic `mouseup` with the side
 * button, since wdio can't press real X-buttons reliably.
 *
 * The unified config window lives at `?config=1`; we discover its handle by
 * scanning for any handle whose URL carries a `config=` param.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, enterEditMode, switchToMain } from '../helpers/app.js';

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
  if ((await browser.getWindowHandles()).includes(h)) {
    await browser.keys(['Escape']);
    await browser.pause(150);
  }
  await switchToMain().catch(() => {});
}

// Click the i-th App-Settings row in the sidebar tree; returns its label.
// App pages render before displays/views, so indices 0..2 are stable
// (General / Style / Accounts).
async function clickAppRow(i: number): Promise<string> {
  return browser.execute((idx: number) => {
    const items = Array.from(
      document.querySelectorAll<HTMLElement>('.config-tree-list .config-tree-item'),
    );
    const btn = items[idx];
    if (!btn) return '';
    btn.click();
    return (btn.querySelector('.config-tree-label')?.textContent ?? '').trim();
  }, i);
}

async function activeTreeLabel(): Promise<string> {
  return browser.execute(() => {
    const el = document.querySelector<HTMLElement>('.config-tree .config-tree-item.active');
    return (el?.querySelector('.config-tree-label')?.textContent ?? '').trim();
  });
}

async function dispatchSideButton(button: 3 | 4): Promise<void> {
  await browser.execute((b: number) => {
    window.dispatchEvent(new MouseEvent('mouseup', { button: b, bubbles: true }));
  }, button);
  await browser.pause(50);
}

// Select + expand + focus the first real view row; returns its label.
async function selectFirstViewRow(): Promise<string> {
  return browser.execute(() => {
    const btn = document.querySelector<HTMLElement>(
      '.config-tree-view:not(.config-tree-add-view)',
    );
    if (!btn) return '';
    btn.click();
    btn.focus();
    return (btn.querySelector('.config-tree-label')?.textContent ?? '').trim();
  });
}

async function firstViewExpanded(): Promise<boolean> {
  return browser.execute(() => {
    const btn = document.querySelector<HTMLElement>(
      '.config-tree-view:not(.config-tree-add-view)',
    );
    return btn?.getAttribute('aria-expanded') === 'true';
  });
}

// ── Specs ────────────────────────────────────────────────────────────────────
describe('config window: back/forward history', () => {
  beforeEach(async () => {
    await resetAppState();
    await openUnifiedConfig();
  });

  afterEach(async () => {
    await closeUnifiedConfig();
  });

  it('Alt+ArrowLeft/Right step back and forward through visited pages', async () => {
    const general = await clickAppRow(0);
    const style = await clickAppRow(1);
    const accounts = await clickAppRow(2);
    expect(accounts).to.not.equal(style);
    expect(await activeTreeLabel()).to.equal(accounts);

    await browser.keys(['Alt', 'ArrowLeft']);
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(style);

    await browser.keys(['Alt', 'ArrowLeft']);
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(general);

    await browser.keys(['Alt', 'ArrowRight']);
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(style);
  });

  it('back is clamped at the first entry (no underflow)', async () => {
    const general = await clickAppRow(0);
    const style = await clickAppRow(1);

    await browser.keys(['Alt', 'ArrowLeft']); // → general
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(general);

    // Already at the start: further back does nothing.
    await browser.keys(['Alt', 'ArrowLeft']);
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(general);

    await browser.keys(['Alt', 'ArrowRight']); // forward still works
    await browser.pause(50);
    expect(await activeTreeLabel()).to.equal(style);
  });

  it('mouse side buttons (3=back, 4=forward) navigate history', async () => {
    const style = await clickAppRow(1);
    const accounts = await clickAppRow(2);
    expect(await activeTreeLabel()).to.equal(accounts);

    await dispatchSideButton(3); // back
    expect(await activeTreeLabel()).to.equal(style);

    await dispatchSideButton(4); // forward
    expect(await activeTreeLabel()).to.equal(accounts);
  });

  it('Alt+ArrowLeft on a focused view row goes back without collapsing the row', async () => {
    const general = await activeTreeLabel(); // active on open
    const view = await selectFirstViewRow(); // selects + expands + focuses; history: [general, view]
    expect(view.length).to.be.greaterThan(0);
    await browser.pause(80);
    expect(await firstViewExpanded()).to.equal(true);

    // Alt+ArrowLeft must drive window history (→ general) and NOT be consumed by
    // the tree's bare-ArrowLeft collapse handler.
    await browser.keys(['Alt', 'ArrowLeft']);
    await browser.pause(80);
    expect(await activeTreeLabel()).to.equal(general);
    expect(
      await firstViewExpanded(),
      'Alt+ArrowLeft must not collapse the focused view row',
    ).to.equal(true);
  });
});
