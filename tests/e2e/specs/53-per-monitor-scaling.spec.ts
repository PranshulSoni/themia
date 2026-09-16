/**
 * Per-monitor widget scaling.
 *
 * Two independent scale knobs:
 *  - General scale (`uiScale` in `widget-global-design`) → `:root --ui-scale`,
 *    scales popups + the config window only.
 *  - Per-monitor scale (`widget-monitor-scale-map`, keyed by monitor
 *    device_name) → `:root --monitor-scale`, scales the widget window's
 *    content (widgets + chrome) on that monitor.
 *
 * The widget window applies `--monitor-scale` to `#app.app-layer` and
 * `--ui-scale` to `.popup-layer`; the two never bleed into each other.
 */
import { expect } from 'chai';
import { browser, $$ } from '@wdio/globals';
import {
  resetAppState, waitForReady, readCssVar, openSettings, closeSettings,
  settingsTab, switchToMain, seedWidgets,
} from '../helpers/app.js';

const SCALE_KEY  = 'widget-monitor-scale-map';
const GLOBAL_KEY = 'widget-global-design';

/** The current widget window's monitor device_name; initForMonitor() seeds
 *  it into `widget-monitor-screen-map` on load. */
async function currentMonitorId(): Promise<string> {
  return browser.execute(() => {
    try {
      const m = JSON.parse(localStorage.getItem('widget-monitor-screen-map') ?? '{}');
      return Object.keys(m)[0] ?? '';
    } catch { return ''; }
  });
}

/** Write `widget-monitor-scale-map` and fire a same-window storage event. */
async function setMonitorScaleMap(map: Record<string, number>): Promise<void> {
  await browser.execute((k: string, v: string) => {
    localStorage.setItem(k, v);
    window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: v }));
  }, SCALE_KEY, JSON.stringify(map));
}

/** Write `widget-global-design` and fire a same-window storage event. */
async function setGlobalDesign(partial: Record<string, any>): Promise<void> {
  await browser.execute((k: string, p: Record<string, any>) => {
    const cur = JSON.parse(localStorage.getItem(k) ?? '{}');
    const next = { ...cur, ...p };
    localStorage.setItem(k, JSON.stringify(next));
    window.dispatchEvent(new StorageEvent('storage', {
      key: k, newValue: JSON.stringify(next),
    }));
  }, GLOBAL_KEY, partial);
}

async function readScaleMap(): Promise<Record<string, number>> {
  return browser.execute((k: string) => {
    try { return JSON.parse(localStorage.getItem(k) ?? '{}'); }
    catch { return {}; }
  }, SCALE_KEY);
}

describe('per-monitor scaling', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  afterEach(async () => {
    await closeSettings();
    await switchToMain();
  });

  it('per-monitor scale drives :root --monitor-scale on the widget window', async () => {
    const id = await currentMonitorId();
    expect(id, 'monitor id seeded by initForMonitor').to.not.equal('');

    await setMonitorScaleMap({ [id]: 1.5 });
    await browser.waitUntil(
      async () => (await readCssVar('--monitor-scale')) === '1.5',
      { timeout: 2_000, timeoutMsg: '--monitor-scale did not update to 1.5' },
    );
  });

  it('changing per-monitor scale leaves --ui-scale untouched', async () => {
    const id = await currentMonitorId();
    await setMonitorScaleMap({ [id]: 1.7 });
    await browser.waitUntil(
      async () => (await readCssVar('--monitor-scale')) === '1.7',
      { timeout: 2_000 },
    );
    // General scale knob is independent, still at its default.
    expect(await readCssVar('--ui-scale')).to.equal('1');
  });

  it('changing general uiScale drives --ui-scale but not --monitor-scale', async () => {
    await setGlobalDesign({ uiScale: 1.3 });
    await browser.waitUntil(
      async () => (await readCssVar('--ui-scale')) === '1.3',
      { timeout: 2_000, timeoutMsg: '--ui-scale did not update to 1.3' },
    );
    // Widget content scale is independent, untouched by the general knob.
    expect(await readCssVar('--monitor-scale')).to.equal('1');
  });

  // [S-14] The scale magnifies widget CONTENT, not geometry: a widget's
  // on-screen position and footprint must stay fixed across scale changes.
  // Before the fix the renderer laid rects out against the raw viewport while
  // the layer applied `transform: scale()`, double-counting the scale so
  // widgets drifted toward the bottom-right AND grew by the scale factor.
  // We measure `.corner-overlay` (the full logical rect, no gap inset) so the
  // widgetGap (which legitimately scales with content) doesn't add noise.
  it('[S-14] per-monitor scale leaves widget position + footprint fixed on screen', async () => {
    const id = await currentMonitorId();
    // Seed away from the origin so any scale-induced drift is unmistakable.
    await seedWidgets([{ type: 'clock', col: 6, row: 4, colSpan: 4, rowSpan: 3 }]);

    const read = () => browser.execute(() => {
      const el = document.querySelector('.grid > .corner-overlay') as HTMLElement | null;
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { left: r.left, top: r.top, width: r.width, height: r.height };
    });

    const before = await read();
    expect(before, 'seeded widget rendered a corner-overlay').to.not.equal(null);

    await setMonitorScaleMap({ [id]: 1.5 });
    await browser.waitUntil(
      async () => (await readCssVar('--monitor-scale')) === '1.5',
      { timeout: 2_000, timeoutMsg: '--monitor-scale did not update to 1.5' },
    );

    const after = await read();
    // 2px tolerance for per-edge pixel rounding magnified by the transform.
    expect(Math.abs(after!.left   - before!.left),   'left stable').to.be.lessThan(2);
    expect(Math.abs(after!.top    - before!.top),    'top stable').to.be.lessThan(2);
    expect(Math.abs(after!.width  - before!.width),  'width stable').to.be.lessThan(2);
    expect(Math.abs(after!.height - before!.height), 'height stable').to.be.lessThan(2);
  });

  it('per-monitor scale survives reload', async () => {
    const id = await currentMonitorId();
    await setMonitorScaleMap({ [id]: 1.4 });
    await browser.waitUntil(
      async () => (await readCssVar('--monitor-scale')) === '1.4',
      { timeout: 2_000 },
    );

    await browser.refresh();
    await waitForReady();

    await browser.waitUntil(
      async () => (await readCssVar('--monitor-scale')) === '1.4',
      { timeout: 3_000, timeoutMsg: '--monitor-scale lost after reload' },
    );
  });

  // Skipped: driving the detached settings config window through the UI is
  // unreliable in the e2e harness during the in-flight config-window refactor
  // (see 07-settings-modal.spec.ts, all skipped for the same reason). The
  // map-driven tests above cover the scaling behaviour; this documents the
  // Displays control contract for when the harness can open config windows.
  it.skip('Displays settings exposes a per-monitor scale control that writes the map', async () => {
    await openSettings();
    await (await settingsTab('Displays')).click();

    // The scale control is the only NumberStepper in the Displays page
    // (min=50, max=200; % display, stored as 0.5–2.0 in the scale map).
    const set = await browser.execute(() => {
      const inputs = Array.from(document.querySelectorAll<HTMLInputElement>(
        '.settings-content .number-stepper input[type="number"]',
      ));
      const target = inputs.find(r => parseFloat(r.max) === 200 && parseFloat(r.min) === 50);
      if (!target) return false;
      const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype, 'value',
      )?.set;
      setter?.call(target, '140');
      target.dispatchEvent(new Event('input',  { bubbles: true }));
      target.dispatchEvent(new Event('change', { bubbles: true }));
      target.dispatchEvent(new Event('blur',   { bubbles: true }));
      return true;
    });
    expect(set, 'per-monitor scale stepper found in Displays settings').to.equal(true);

    await browser.waitUntil(async () => {
      const map = await readScaleMap();
      return Object.values(map).includes(1.4);
    }, { timeout: 2_000, timeoutMsg: 'widget-monitor-scale-map not written by Displays control' });
  });
});
