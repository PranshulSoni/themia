/**
 * Logs tab: debug log viewer in Settings > Logs.
 *
 * Covers:
 *  - Tab renders with container + header + filter dropdowns
 *  - Logger-emitted entries appear after a short pause
 *  - Source filter dropdown narrows the visible list
 *  - Clear button empties the list
 *
 * [LOG-08] The three actions are copy / open log folder / clear. Copy and
 * open-folder are not asserted here: the first needs clipboard permission the
 * webdriver session doesn't grant, the second spawns Explorer. The dropdowns +
 * clear path already exercise the non-trivial filtering logic.
 *
 * Selectors per SettingsModal.tsx:
 *  - root:       `.logs-page`
 *  - actions:    `.logs-actions`
 *  - filter dd:  `.dropdown-trigger.logs-filter` (source, then level)
 *  - count:      `.logs-count`
 *  - list:       `.logs-container`
 *  - row:        `.log-line`, source cell `.log-source`
 *  - clear btn:  `.logs-btn.logs-btn-danger`
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, enableDevMode, enableLogging,
} from '../helpers/app.js';

async function countRows(): Promise<number> {
  return $$('.config-panel .logs-container .log-line').length;
}

async function sourceSet(): Promise<string[]> {
  return browser.execute(() => {
    return Array.from(document.querySelectorAll<HTMLElement>(
      '.config-panel .logs-container .log-source',
    )).map(el => (el.textContent ?? '').trim());
  });
}

async function emitLog(source: string, msg: string): Promise<void> {
  // Use @tauri-apps/plugin-log directly via a window bridge the app already
  // wires. Simpler: call the page's `logger.info` via a direct dynamic
  // import is awkward under webdriver, so push through console.info,
  // which debugLog.ts patches to route through the plugin.
  // parseWebviewMessage picks up "[source] msg" back into { source, msg }.
  await browser.execute((s: string, m: string) => {
    console.info(`[${s}] ${m}`);
  }, source, msg);
}

async function openSourceFilter(): Promise<void> {
  await browser.execute(() => {
    const triggers = document.querySelectorAll<HTMLElement>(
      '.config-panel .dropdown-trigger.logs-filter',
    );
    // First logs-filter dropdown is the source filter.
    (triggers[0] as HTMLElement)?.click();
  });
  await $('.dropdown-panel').waitForDisplayed({ timeout: 2_000 });
}

async function pickDropdownOption(label: string): Promise<void> {
  await browser.execute((t: string) => {
    const items = Array.from(document.querySelectorAll<HTMLElement>(
      '.dropdown-panel .dropdown-item',
    ));
    items.find(el => (el.textContent ?? '').trim() === t)?.click();
  }, label);
}

describe('logs panel', () => {
  beforeEach(async () => {
    await resetAppState();
    // Logs tab is only rendered while devMode is on.
    await enableDevMode();
    // [LOG-04] logging is opt-in; records only flow while the toggle is on.
    await enableLogging();
    await openSettings();
    await (await settingsTab('Logs')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('renders the logs page with header + container', async () => {
    expect(await $('.config-panel .logs-page').isDisplayed()).to.equal(true);
    expect(await $('.config-panel .logs-container').isExisting()).to.equal(true);
    // Two filter dropdowns + copy/export/clear buttons.
    const filters = await $$('.config-panel .dropdown-trigger.logs-filter');
    expect(filters.length).to.equal(2);
    expect(await $('.config-panel .logs-btn.logs-btn-danger').isExisting()).to.equal(true);
  });

  it('emitted log entries appear in the list', async () => {
    await emitLog('spec-logs', 'hello from test');
    // debugLog buffers and flushes every 200ms, so wait a bit longer.
    await browser.waitUntil(async () => {
      const sources = await sourceSet();
      return sources.includes('spec-logs');
    }, { timeout: 5_000, interval: 200 });
  });

  it('source dropdown filter narrows the list', async () => {
    await emitLog('alpha-src', 'a1');
    await emitLog('alpha-src', 'a2');
    await emitLog('beta-src',  'b1');

    await browser.waitUntil(async () => {
      const s = await sourceSet();
      return s.includes('alpha-src') && s.includes('beta-src');
    }, { timeout: 5_000, interval: 200 });

    const before = await countRows();
    expect(before).to.be.greaterThan(1);

    // Pick "alpha-src" in the source dropdown.
    await openSourceFilter();
    await pickDropdownOption('alpha-src');
    await browser.pause(150);

    const after = await sourceSet();
    // Every visible row must now be alpha-src.
    expect(after.length).to.be.greaterThan(0);
    for (const s of after) expect(s).to.equal('alpha-src');
    expect(after.length).to.be.lessThan(before);
  });

  it('[LOG-05] turning Developer Mode off hides the Logging toggle and disables logging', async () => {
    // Move to the General page where the Developer section lives.
    await (await settingsTab('General')).click();
    await browser.pause(100);

    const rowState = () => browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.config-panel .design-row'));
      const byLabel = (label: string) =>
        rows.find(r => r.querySelector('.design-label')?.textContent?.trim() === label) ?? null;
      return {
        hasLoggingRow: !!byLabel('Logging'),
        hasDevModeRow: !!byLabel('Developer Mode'),
      };
    });

    // Dev mode is on (beforeEach) → the Logging row is visible.
    expect((await rowState()).hasLoggingRow).to.equal(true);

    // Flip Developer Mode off via its real toggle.
    await browser.execute(() => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.config-panel .design-row'));
      const row = rows.find(r => r.querySelector('.design-label')?.textContent?.trim() === 'Developer Mode');
      row?.querySelector<HTMLButtonElement>('button[role="switch"]')?.click();
    });
    await browser.pause(150);

    // [LOG-05] cascade: the row is gone AND the persisted toggle is forced off.
    expect((await rowState()).hasLoggingRow).to.equal(false);
    const blob = await browser.execute(() =>
      JSON.parse(localStorage.getItem('widget-global-design') ?? '{}'));
    expect(blob.devMode).to.equal(false);
    expect(blob.loggingEnabled).to.equal(false);
  });

  it('clear button empties the list', async () => {
    await emitLog('clear-spec', 'pre-clear');
    await browser.waitUntil(async () => (await countRows()) > 0, { timeout: 5_000, interval: 200 });

    await browser.execute(() => {
      const btn = document.querySelector<HTMLButtonElement>(
        '.config-panel .logs-btn.logs-btn-danger',
      );
      btn?.click();
    });

    await browser.waitUntil(async () => (await countRows()) === 0, { timeout: 2_000 });
    expect(await countRows()).to.equal(0);
  });
});
