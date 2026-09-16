/**
 * Apps widget: Start-menu app listing, launch, search filter.
 *
 * Apps calls:
 *   - `list_start_menu_apps` → { name, path, category }[] on mount.
 *   - `get_file_icon`        → base64 PNG per app path (lazy, queue).
 *   - `open_file`            → launches the app via the shell "Open" verb.
 *   - `open_in_explorer`     → "Open location" only, on the parent directory.
 *
 * Tauri 2 makes `__TAURI_INTERNALS__.invoke` non-writable, so stubbing at the
 * window level silently fails. `src/demo/invoke.ts` exposes a `setInvoke` hook
 * via `window.__themiaInvoke` (only under webdriver); we install the recorder
 * through that.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, waitForPersist, LS_KEYS } from '../helpers/app.js';

async function stubWithRecorder(mapping: Record<string, any>): Promise<void> {
  await browser.execute((m: Record<string, any>) => {
    const w = window as any;
    w.__calls = [];
    const hook = w.__themiaInvoke;
    if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed; ensure webdriver flag and rebuild debug binary');
    hook.setInvoke(async (cmd: string, args: any) => {
      // Deep-clone to strip SolidJS store proxies, otherwise array fields
      // serialise as `undefined` when read back via webdriver.
      const plain = args == null ? args : JSON.parse(JSON.stringify(args));
      w.__calls.push({ cmd, args: plain });
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      if (Object.prototype.hasOwnProperty.call(m, '*')) return m['*'];
      return null;
    });
  }, mapping);
}

async function recordedCalls(): Promise<Array<{ cmd: string; args: any }>> {
  return browser.execute(() => (window as any).__calls ?? []);
}

// Seed via storage-event dispatch rather than refresh so the invoke stub
// installed in beforeEach stays alive when Apps's onMount calls
// `list_start_menu_apps`.
async function seedApps(viewMode = 'list', extraConfig: Record<string, any> = {}): Promise<void> {
  await browser.execute((key: string, vm: string, extra: Record<string, any>) => {
    const raw = localStorage.getItem(key) ?? '{}';
    let layout: any = {};
    try { layout = JSON.parse(raw); } catch {}
    layout.widgets = [{
      id: 'apps-1', type: 'apps', col: 1, row: 1, colSpan: 4, rowSpan: 5,
      config: { viewMode: vm, hiddenCategories: [], hiddenApps: [], ...extra },
    }];
    layout.widgetGap = layout.widgetGap ?? 12;
    layout.bgImage = layout.bgImage ?? '';
    layout.bgImageFit = layout.bgImageFit ?? 'cover';
    const serialized = JSON.stringify(layout);
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: raw, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, viewMode, extraConfig);
  await $('.widget').waitForExist({ timeout: 5_000 });
}

describe('apps widget', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubWithRecorder({
      'list_start_menu_apps': [
        { name: 'Notepad',    path: 'C:/apps/notepad.lnk',    category: 'Accessories' },
        { name: 'Calculator', path: 'C:/apps/calculator.lnk', category: 'Accessories' },
        { name: 'Firefox',    path: 'C:/apps/firefox.lnk',    category: 'Internet' },
      ],
      'get_file_icon':    '',
      'open_file':        null,
      'open_in_explorer': null,
    });
    await seedApps('list');
  });

  it('renders the stubbed app names [S-01] [S-16]', async () => {
    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Notepad')
          && text.includes('Calculator')
          && text.includes('Firefox');
    }, { timeout: 5_000, timeoutMsg: 'app names never rendered' });
  });

  it('clicking an app fires open_file with its path [S-18] [S-20]', async () => {
    // Wait for list to paint.
    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Notepad');
    }, { timeout: 5_000 });

    await browser.execute(() => { (window as any).__calls = []; });

    // Click the Notepad list-item (class may be .list-item, .grid-item, or
    // .compact-item depending on viewMode).
    const clicked = await browser.execute((name: string) => {
      const candidates = Array.from(document.querySelectorAll<HTMLElement>(
        '.list-item, .grid-item, .compact-item',
      ));
      for (const c of candidates) {
        if ((c.textContent ?? '').trim().includes(name)) { c.click(); return true; }
      }
      return false;
    }, 'Notepad');
    expect(clicked, 'failed to find Notepad list-item').to.equal(true);

    // Launch must run the app via `open_file`, not `open_in_explorer`, which
    // would only select the .lnk in its containing folder.
    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c =>
        c.cmd === 'open_file' && String(c.args?.path ?? '').includes('notepad.lnk'),
      );
    }, { timeout: 3_000, timeoutMsg: 'open_file never fired with notepad path' });

    const calls = await recordedCalls();
    expect(
      calls.some(c => c.cmd === 'open_in_explorer'),
      'click should not fire open_in_explorer',
    ).to.equal(false);
  });

  it('hiddenCategories config excludes whole categories [S-22]', async () => {
    await resetAppState();
    await stubWithRecorder({
      'list_start_menu_apps': [
        { name: 'Notepad',    path: 'C:/apps/notepad.lnk',    category: 'Accessories' },
        { name: 'Calculator', path: 'C:/apps/calculator.lnk', category: 'Accessories' },
        { name: 'Firefox',    path: 'C:/apps/firefox.lnk',    category: 'Internet' },
      ],
      'get_file_icon': '', 'open_file': null, 'open_in_explorer': null,
    });
    await seedApps('list', { hiddenCategories: ['Internet'] });

    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Notepad') && text.includes('Calculator') && !text.includes('Firefox');
    }, { timeout: 5_000, timeoutMsg: 'hiddenCategories never filtered Internet out' });
  });

  it('hiddenApps config excludes specific apps [S-23]', async () => {
    await resetAppState();
    await stubWithRecorder({
      'list_start_menu_apps': [
        { name: 'Notepad',    path: 'C:/apps/notepad.lnk',    category: 'Accessories' },
        { name: 'Calculator', path: 'C:/apps/calculator.lnk', category: 'Accessories' },
        { name: 'Firefox',    path: 'C:/apps/firefox.lnk',    category: 'Internet' },
      ],
      'get_file_icon': '', 'open_file': null, 'open_in_explorer': null,
    });
    await seedApps('list', { hiddenApps: ['C:/apps/notepad.lnk'] });

    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Calculator') && text.includes('Firefox') && !text.includes('Notepad');
    }, { timeout: 5_000, timeoutMsg: 'hiddenApps never filtered Notepad out' });
  });

  it('right-click Hide from list appends path to hiddenApps [S-24]', async () => {
    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Notepad');
    }, { timeout: 5_000 });

    // Dispatch contextmenu on the Notepad row.
    const opened = await browser.execute((name: string) => {
      const candidates = Array.from(document.querySelectorAll<HTMLElement>(
        '.list-item, .grid-item, .compact-item',
      ));
      for (const c of candidates) {
        if ((c.textContent ?? '').trim().includes(name)) {
          const r = c.getBoundingClientRect();
          c.dispatchEvent(new MouseEvent('contextmenu', {
            bubbles: true, cancelable: true, view: window,
            clientX: r.left + 4, clientY: r.top + 4, button: 2,
          }));
          return true;
        }
      }
      return false;
    }, 'Notepad');
    expect(opened, 'failed to dispatch contextmenu on Notepad').to.equal(true);

    // Wait for the ctx menu to render: Solid's reactive store may not have
    // flushed by the time we query the DOM.
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    // Click "Hide from list", targeting the `.ctx-item` directly so we hit the
    // SolidJS onClick handler instead of an inner span that doesn't bubble.
    const clicked = await browser.execute(() => {
      const items = Array.from(document.querySelectorAll<HTMLElement>('.ctx-menu .ctx-item'));
      for (const item of items) {
        const lbl = item.querySelector('.ctx-label')?.textContent?.trim() ?? '';
        if (lbl === 'Hide from list') { item.click(); return true; }
      }
      return false;
    });
    expect(clicked, 'failed to click Hide from list in menu').to.equal(true);

    // Notepad gone; check rendered list-item names (not body text, which
    // can pick up transient menu text or category headers).
    await browser.waitUntil(async () => {
      return browser.execute(() => {
        const names = Array.from(document.querySelectorAll<HTMLElement>(
          '.list-item .list-name, .grid-item .grid-name, .compact-item',
        )).map(el => (el.textContent ?? '').trim());
        return names.some(n => n.includes('Firefox'))
            && !names.some(n => n.includes('Notepad'));
      });
    }, { timeout: 5_000, timeoutMsg: 'Notepad never disappeared after Hide from list' });

    // hiddenApps persisted to localStorage (debounced save; wait for flush).
    await waitForPersist();
    const hidden = await browser.execute((key: string) => {
      try {
        const raw = localStorage.getItem(key) ?? '{}';
        const layout = JSON.parse(raw);
        const w = (layout.widgets ?? []).find((x: any) => x.id === 'apps-1');
        return w?.config?.hiddenApps ?? [];
      } catch { return []; }
    }, LS_KEYS.layoutRoot);
    expect(hidden, 'hiddenApps should contain notepad path').to.include('C:/apps/notepad.lnk');
  });

  it('search input narrows the list [S-08]', async () => {
    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Firefox');
    }, { timeout: 5_000 });

    // Drive the native setter + dispatch a SolidJS-compatible input event.
    // `setValue` on Solid inputs sometimes skips the synthetic input handler.
    await browser.execute(() => {
      const el = document.querySelector<HTMLInputElement>('.search-input');
      if (!el) return;
      const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype, 'value',
      )?.set;
      setter?.call(el, 'fire');
      el.dispatchEvent(new Event('input', { bubbles: true }));
    });

    // After filter, the .list-item rows for Notepad/Calculator should be gone
    // and only Firefox remains. We check rendered list items rather than the
    // whole document body (the search input itself reflects 'fire', and other
    // chrome may transiently mention category names).
    await browser.waitUntil(async () => {
      return browser.execute(() => {
        const names = Array.from(document.querySelectorAll<HTMLElement>(
          '.list-item .list-name, .grid-item .grid-name, .compact-item',
        )).map(el => (el.textContent ?? '').trim());
        return names.length > 0
            && names.some(n => n.includes('Firefox'))
            && !names.some(n => n.includes('Notepad'));
      });
    }, { timeout: 3_000, timeoutMsg: 'filter never narrowed list' });
  });
});
