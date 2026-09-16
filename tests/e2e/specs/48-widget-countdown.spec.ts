/**
 * Countdown widget: list of countdown timers.
 *
 * Covers the behaviors tagged in widgets/others/countdown/SPEC.md.
 *
 * Notable regressions guarded here:
 *  - [C-54] label input keeps focus across keystrokes (the previous `<For>`
 *    implementation re-mounted rows on every patch, blowing away focus).
 *  - [C-52] reorder is via up/down chevron buttons. HTML5 drag is intentionally
 *    not used in the config because the window-level Win32 IDropTarget
 *    swallows in-modal HTML5 drags.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, LS_KEYS, waitForConfigWindow, switchToMain,
} from '../helpers/app.js';

type Format = 'auto' | 'days' | 'dhm' | 'dhms';
interface SeedTimer { id: string; label: string; target: string; format: Format }

async function seedCountdown(timers: SeedTimer[], extra: Record<string, any> = {}): Promise<void> {
  await browser.execute((key: string, ts: SeedTimer[], ex: Record<string, any>) => {
    const layout = {
      widgets: [{
        id: 'countdown-1', type: 'countdown', col: 1, row: 1, colSpan: 4, rowSpan: 4,
        config: { timers: ts, hideHeader: false, label: '', ...ex },
      }],
      widgetGap: 12, bgImage: '', bgImageFit: 'cover',
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, timers, extra);
  await $('.widget').waitForExist({ timeout: 5_000 });
  await $('.countdown-widget').waitForExist({ timeout: 5_000 });
}

async function storedTimers(): Promise<SeedTimer[]> {
  return browser.execute((key: string) => {
    const raw = localStorage.getItem(key) ?? '{}';
    try {
      const parsed = JSON.parse(raw);
      const w = (parsed.widgets ?? []).find((x: any) => x.type === 'countdown');
      return (w?.config?.timers ?? []) as SeedTimer[];
    } catch { return []; }
  }, LS_KEYS.layoutRoot);
}

async function openCountdownConfig(): Promise<void> {
  // Configure now opens the widget config in a DETACHED Tauri window
  // (target prefix "widget"). Right-click the widget to open the ctx menu
  // on the main window, then switch into the config window.
  await switchToMain();
  await browser.execute(() => {
    const w = document.querySelector('.widget') as HTMLElement | null;
    w?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
  });
  await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
  await browser.execute(() => {
    const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
    const cfg = items.find(i => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Configure');
    cfg?.click();
  });
  await waitForConfigWindow('widget', 8_000);
  // Inside the config window, the countdown config renders timer rows and an
  // "Add timer" button, so wait for either to appear.
  await $('.timer-row, .btn-add').waitForExist({ timeout: 3_000 });
}

function isoFromNow(deltaMs: number): string {
  return new Date(Date.now() + deltaMs).toISOString();
}

describe('widget: Countdown', () => {
  beforeEach(async () => { await resetAppState(); });

  // ── Display ────────────────────────────────────────────────────────────────

  it('[C-02] empty state shows when there are no timers', async () => {
    await seedCountdown([]);
    const text = (await $('.countdown-widget').getText()).toLowerCase();
    expect(text).to.match(/no timers/);
  });

  it('[C-03] renders one row per timer in config order with label + remaining', async () => {
    await seedCountdown([
      { id: 'a', label: 'Birthday',  target: isoFromNow(3 * 24 * 60 * 60 * 1000),  format: 'auto' },
      { id: 'b', label: 'Conference', target: isoFromNow(10 * 24 * 60 * 60 * 1000), format: 'days' },
    ]);
    const rows = await $$('.countdown-item');
    expect(await rows.length).to.equal(2);

    const labels: string[] = [];
    for await (const r of $$('.countdown-item .countdown-label')) {
      labels.push((await r.getText()).trim());
    }
    expect(labels).to.deep.equal(['Birthday', 'Conference']);

    // Remaining cells should be non-empty strings.
    for await (const r of $$('.countdown-item .countdown-remaining')) {
      expect((await r.getText()).trim().length).to.be.greaterThan(0);
    }
  });

  it('[C-30] auto format picks day-grain for far futures', async () => {
    // Seed +30d 12h so a few-ms render delay can't push us under 30 days.
    await seedCountdown([
      { id: 'a', label: 'Future', target: isoFromNow(30 * 24 * 60 * 60 * 1000 + 12 * 3600 * 1000), format: 'auto' },
    ]);
    const txt = (await $('.countdown-item .countdown-remaining').getText()).trim();
    expect(txt).to.match(/^30 days$/);
  });

  it('[C-11] days-only format renders "N day(s)" exactly', async () => {
    // +5d 12h for the same anti-flake reason.
    await seedCountdown([
      { id: 'a', label: 'X', target: isoFromNow(5 * 24 * 60 * 60 * 1000 + 12 * 3600 * 1000), format: 'days' },
    ]);
    const txt = (await $('.countdown-item .countdown-remaining').getText()).trim();
    expect(txt).to.match(/^5 days$/);
  });

  it('[C-11] dhm format renders "Nd Nh Nm"', async () => {
    // 2 days, 3 hours, 15 minutes.
    const ms = (2 * 24 * 60 + 3 * 60 + 15) * 60 * 1000;
    await seedCountdown([
      { id: 'a', label: 'X', target: isoFromNow(ms), format: 'dhm' },
    ]);
    const txt = (await $('.countdown-item .countdown-remaining').getText()).trim();
    expect(txt).to.match(/^\d+d \d+h \d+m$/);
  });

  it('[C-40] elapsed timer shows "<duration> ago" + adds elapsed class', async () => {
    await seedCountdown([
      { id: 'a', label: 'Past', target: isoFromNow(-2 * 24 * 60 * 60 * 1000), format: 'days' },
    ]);
    const item = await $('.countdown-item');
    const cls = await item.getAttribute('class');
    expect(cls).to.match(/elapsed/);
    const txt = (await item.$('.countdown-remaining').getText()).trim();
    expect(txt).to.match(/\bago\b/);
  });

  it('[C-06] hideHeader=true hides the widget header', async () => {
    await seedCountdown(
      [{ id: 'a', label: 'X', target: isoFromNow(60_000), format: 'auto' }],
      { hideHeader: true },
    );
    const headers = await $$('.countdown-widget .widget-header');
    expect(await headers.length).to.equal(0);
  });

  // ── Configuration ────────────────────────────────────────────────────────

  it('[C-51] "Add timer" appends a new timer with auto format and target ~24h out', async () => {
    await seedCountdown([]);
    await openCountdownConfig();

    const before = (await storedTimers()).length;
    await browser.execute(() => {
      const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.btn-add'));
      const add = btns.find(b => (b.textContent ?? '').trim().toLowerCase().startsWith('add timer'));
      add?.click();
    });
    await browser.waitUntil(async () => (await storedTimers()).length === before + 1, {
      timeout: 3_000, timeoutMsg: 'Add timer did not append',
    });
    const ts = await storedTimers();
    const fresh = ts[ts.length - 1];
    expect(fresh.format).to.equal('auto');
    expect(fresh.label).to.equal('');
    const deltaH = (new Date(fresh.target).getTime() - Date.now()) / 3_600_000;
    expect(deltaH).to.be.greaterThan(23).and.lessThan(25);
  });

  it('[C-54] label input keeps focus across keystrokes (regression)', async () => {
    // Seed empty so the assertion is independent of the seed value.
    await seedCountdown([
      { id: 'a', label: '', target: isoFromNow(60_000), format: 'auto' },
    ]);
    await openCountdownConfig();

    // Focus the label input, type one char at a time, assert focus survives
    // every keystroke. The previous bug used <For> which re-mounted rows on
    // each patch and blew away focus on the very first keystroke.
    const focusReport = await browser.execute(() => {
      const input = document.querySelector<HTMLInputElement>('.timer-label');
      if (!input) return { ok: false, reason: 'input not found', focusedAfter: [] as boolean[] };
      input.focus();
      const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype, 'value',
      )?.set;
      const focusedAfter: boolean[] = [];
      let buf = '';
      for (const ch of ['H', 'e', 'l', 'l', 'o']) {
        buf += ch;
        setter?.call(input, buf);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        focusedAfter.push(document.activeElement === input);
      }
      return { ok: true, reason: '', focusedAfter };
    });
    expect(focusReport.ok, focusReport.reason).to.equal(true);
    expect(
      focusReport.focusedAfter.every(Boolean),
      `input lost focus mid-typing: focusedAfter=${JSON.stringify(focusReport.focusedAfter)}`,
    ).to.equal(true);
  });

  it('[C-50] removing a timer drops it from config.timers', async () => {
    await seedCountdown([
      { id: 'a', label: 'Keep',   target: isoFromNow(60_000), format: 'auto' },
      { id: 'b', label: 'Remove', target: isoFromNow(60_000), format: 'auto' },
    ]);
    await openCountdownConfig();

    await browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.timer-row'));
      const target = rows.find(r => r.getAttribute('data-timer-id') === 'b');
      target?.querySelector<HTMLButtonElement>('.item-remove')?.click();
    });

    await browser.waitUntil(async () => {
      const ts = await storedTimers();
      return ts.length === 1 && ts[0]?.id === 'a';
    }, { timeout: 3_000, timeoutMsg: 'remove did not persist' });
  });

  // ── Reorder (up/down chevrons) ────────────────────────────────────────────

  it('[C-52] up/down chevrons swap adjacent timers and persist new order', async () => {
    await seedCountdown([
      { id: 'a', label: 'A', target: isoFromNow(60_000), format: 'auto' },
      { id: 'b', label: 'B', target: isoFromNow(60_000), format: 'auto' },
      { id: 'c', label: 'C', target: isoFromNow(60_000), format: 'auto' },
    ]);
    await openCountdownConfig();

    // Click "Move down" on row 'a' → expect order [b, a, c].
    await browser.execute(() => {
      const row = document.querySelector<HTMLElement>('.timer-row[data-timer-id="a"]');
      const btn = Array.from(row?.querySelectorAll<HTMLButtonElement>('.reorder-btn') ?? [])
        .find(b => b.title === 'Move down');
      btn?.click();
    });
    await browser.waitUntil(async () => {
      const ids = (await storedTimers()).map(t => t.id);
      return ids.join(',') === 'b,a,c';
    }, { timeout: 3_000, timeoutMsg: 'down-chevron did not reorder a→down' });

    // Click "Move up" on row 'c' → expect order [b, c, a].
    await browser.execute(() => {
      const row = document.querySelector<HTMLElement>('.timer-row[data-timer-id="c"]');
      const btn = Array.from(row?.querySelectorAll<HTMLButtonElement>('.reorder-btn') ?? [])
        .find(b => b.title === 'Move up');
      btn?.click();
    });
    await browser.waitUntil(async () => {
      const ids = (await storedTimers()).map(t => t.id);
      return ids.join(',') === 'b,c,a';
    }, { timeout: 3_000, timeoutMsg: 'up-chevron did not reorder c→up' });
  });

  it('[C-52] first row up button + last row down button are disabled', async () => {
    await seedCountdown([
      { id: 'a', label: 'A', target: isoFromNow(60_000), format: 'auto' },
      { id: 'b', label: 'B', target: isoFromNow(60_000), format: 'auto' },
    ]);
    await openCountdownConfig();

    const result = await browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.timer-row'));
      const first = rows[0]?.querySelectorAll<HTMLButtonElement>('.reorder-btn');
      const last  = rows[rows.length - 1]?.querySelectorAll<HTMLButtonElement>('.reorder-btn');
      return {
        firstUpDisabled:  first?.[0]?.disabled === true,
        firstDownEnabled: first?.[1]?.disabled === false,
        lastUpEnabled:    last?.[0]?.disabled === false,
        lastDownDisabled: last?.[1]?.disabled === true,
      };
    });
    expect(result).to.deep.equal({
      firstUpDisabled: true,  firstDownEnabled: true,
      lastUpEnabled:   true,  lastDownDisabled: true,
    });
  });

  // ── Per-timer ctx menu ────────────────────────────────────────────────────

  it('[C-60] right-click row → Display submenu switches format and persists', async () => {
    await seedCountdown([
      { id: 'a', label: 'X', target: isoFromNow(2 * 24 * 60 * 60 * 1000), format: 'auto' },
    ]);
    await browser.execute(() => {
      const row = document.querySelector('.countdown-item') as HTMLElement | null;
      row?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
    });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });

    // Click "Days only" item.
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
      const tgt = items.find(i => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Days only');
      tgt?.click();
    });
    await browser.waitUntil(async () => {
      const ts = await storedTimers();
      return ts[0]?.format === 'days';
    }, { timeout: 3_000, timeoutMsg: 'format switch did not persist' });
  });

  it('[C-60] right-click row → Remove deletes the timer', async () => {
    await seedCountdown([
      { id: 'a', label: 'A', target: isoFromNow(60_000), format: 'auto' },
      { id: 'b', label: 'B', target: isoFromNow(60_000), format: 'auto' },
    ]);
    await browser.execute(() => {
      const rows = document.querySelectorAll('.countdown-item');
      const second = rows[1] as HTMLElement | null;
      second?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
    });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
      const rm = items.find(i => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Remove');
      rm?.click();
    });
    await browser.waitUntil(async () => {
      const ts = await storedTimers();
      return ts.length === 1 && ts[0]?.id === 'a';
    }, { timeout: 3_000, timeoutMsg: 'ctx menu Remove did not persist' });
  });
});
