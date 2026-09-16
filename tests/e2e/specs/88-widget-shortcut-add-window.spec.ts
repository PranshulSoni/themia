/**
 * Shortcut widget config - the "Add shortcut" affordance.
 *
 * [S-61] The Items section header carries the add (+) button on its right.
 * [S-66] The + button opens the dedicated "Add shortcut" window
 *        (`open_shortcut_add_window`) instead of an inline chip row.
 *
 * The command is stubbed through the `__ipcInvoke` seam rather than letting it
 * build a real window: a leaked `shortcut-add` window would outlive the spec
 * (resetAppState only closes `config`-prefixed windows).
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, LS_KEYS, switchToMain, switchToConfigWindow,
  configModalIsOpen, closeConfigModal,
} from '../helpers/app.js';

const WIDGET_ID = 'shortcut-add-1';

/** Seed one shortcut widget and open its config window. */
async function seedAndOpenConfig(items: { id: string; path: string }[]): Promise<void> {
  await switchToMain();
  await browser.execute((key: string, id: string, its: unknown[]) => {
    const layout = {
      widgets: [{
        id, type: 'shortcut', x: 0.05, y: 0.05, w: 0.5, h: 0.6,
        config: { items: its, viewMode: 'list', label: '' },
      }],
      widgetGap: 12, bgImage: '', bgImageFit: 'cover',
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, WIDGET_ID, items);

  await browser.execute(async (id: string) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    await invoke('open_config_window', { parentLabel: 'main', target: `widget:${id}` });
  }, WIDGET_ID);
  await browser.waitUntil(() => configModalIsOpen(),
    { timeout: 5_000, timeoutMsg: 'widget config window did not open' });
  await switchToConfigWindow('widget');
  await browser.waitUntil(async () => browser.execute(() =>
    !!document.querySelector('.shortcut-items-head')),
    { timeout: 5_000, timeoutMsg: 'shortcut config never rendered its Items header' });
}

/** Record every command the config window sends, without performing it. */
async function recordInvokes(): Promise<void> {
  await browser.execute(() => {
    const w = window as any;
    const hook = w.__ipcInvoke;
    if (!hook?.setInvoke) throw new Error('__ipcInvoke.setInvoke missing (navigator.webdriver false?)');
    w.__recorded = [] as { cmd: string; args: unknown }[];
    const raw = w.__TAURI_INTERNALS__?.invoke;
    hook.setInvoke(async (cmd: string, args: unknown) => {
      w.__recorded.push({ cmd, args });
      if (cmd === 'open_shortcut_add_window') return null;
      return raw ? raw(cmd, args) : null;
    });
  });
}

async function recorded(): Promise<{ cmd: string; args: Record<string, string> }[]> {
  return browser.execute(() => (window as any).__recorded ?? []);
}

describe('shortcut config - add shortcut window', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  afterEach(async () => {
    try { await browser.execute(() => { (window as any).__ipcInvoke?.setInvoke(null); }); } catch {}
    if (await configModalIsOpen()) await closeConfigModal();
    await switchToMain();
  });

  it('[S-61] the Items header carries a labelled accent add button on its right', async () => {
    await seedAndOpenConfig([{ id: 'a', path: 'https://example.com' }]);
    const geom = await browser.execute(() => {
      const head = document.querySelector('.shortcut-items-head') as HTMLElement | null;
      const label = head?.querySelector('.section-label') as HTMLElement | null;
      const btn = head?.querySelector('.shortcut-add-btn') as HTMLElement | null;
      if (!head || !label || !btn) return null;
      const l = label.getBoundingClientRect();
      const b = btn.getBoundingClientRect();
      return {
        rightOfLabel: b.left >= l.right,
        insideHead: b.right <= head.getBoundingClientRect().right + 1,
        // The accent role, not a bare icon: it must carry a label and be
        // painted in the accent, which is what made it findable at all.
        text: (btn.textContent ?? '').trim(),
        accent: btn.classList.contains('btn-primary'),
      };
    });
    expect(geom, 'items header / add button missing').to.not.equal(null);
    expect(geom!.rightOfLabel).to.equal(true);
    expect(geom!.insideHead).to.equal(true);
    expect(geom!.accent, 'header add button must use the accent role').to.equal(true);
    expect(geom!.text.length, 'header add button must carry a text label').to.be.greaterThan(0);
  });

  it('[S-61] a second add button ends the list at the item rows own size', async () => {
    await seedAndOpenConfig([
      { id: 'a', path: 'https://example.com' },
      { id: 'b', path: 'https://other.example.com' },
    ]);
    const geom = await browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.item-row'));
      const list = document.querySelector('.shortcut-items-list') as HTMLElement | null;
      const btn = document.querySelector('.shortcut-add-row') as HTMLElement | null;
      if (!rows.length || !list || !btn) return null;
      const r = rows[0].getBoundingClientRect();
      const b = btn.getBoundingClientRect();
      return {
        text: (btn.textContent ?? '').trim(),
        belowList: b.top >= list.getBoundingClientRect().bottom - 1,
        dh: Math.abs(b.height - r.height),
        dw: Math.abs(b.width - r.width),
      };
    });
    expect(geom, 'end-of-list add button missing').to.not.equal(null);
    expect(geom!.belowList, 'add button must sit after the items').to.equal(true);
    expect(geom!.text.length).to.be.greaterThan(0);
    // Same block as the rows above it: a drifted `.item-row` metric fails here.
    expect(geom!.dh, `height differs from an item row by ${geom!.dh}px`).to.be.lessThan(3);
    expect(geom!.dw, `width differs from an item row by ${geom!.dw}px`).to.be.lessThan(2);
  });

  it('[S-66] clicking add opens the dedicated add-shortcut window', async () => {
    await seedAndOpenConfig([{ id: 'a', path: 'https://example.com' }]);
    await recordInvokes();
    await browser.execute(() => {
      (document.querySelector('.shortcut-add-btn') as HTMLElement | null)?.click();
    });
    // Both affordances open the same window.
    await browser.execute(() => {
      (document.querySelector('.shortcut-add-row') as HTMLElement | null)?.click();
    });
    await browser.waitUntil(async () =>
      (await recorded()).some(r => r.cmd === 'open_shortcut_add_window'),
      { timeout: 3_000, timeoutMsg: 'add button did not open the add-shortcut window' });

    const call = (await recorded()).find(r => r.cmd === 'open_shortcut_add_window')!;
    expect(call.args.widgetId).to.equal(WIDGET_ID);
    expect(call.args.viewId, 'view id must be passed so the window writes to the right view').to.be.a('string');
  });

  it('[S-61] an empty widget shows the empty state instead of an items list', async () => {
    await seedAndOpenConfig([]);
    const state = await browser.execute(() => ({
      hasEmpty: !!document.querySelector('.shortcut-items-empty'),
      hasList: !!document.querySelector('.shortcut-items-list'),
      hasHeaderAdd: !!document.querySelector('.shortcut-add-btn'),
      hasEndAdd: !!document.querySelector('.shortcut-add-row'),
    }));
    expect(state.hasEmpty).to.equal(true);
    expect(state.hasList).to.equal(false);
    expect(state.hasHeaderAdd, 'add button must stay reachable with no items').to.equal(true);
    expect(state.hasEndAdd, 'end-of-list add button must stay reachable with no items').to.equal(true);
  });
});
