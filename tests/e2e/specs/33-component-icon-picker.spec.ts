/**
 * IconPicker: view icon selector in View Settings.
 *
 * Covers:
 *  - Clicking the trigger opens the portal panel (.icon-picker-panel)
 *  - Clicking an icon writes the chosen key to view.icon and persists to
 *    localStorage `widget-screens`
 *  - Panel closes on outside click
 *  - Icon is non-clearable: no "None"/X button exists; every view always
 *    has an icon set (default 'home' on seed and new views)
 *
 * Architecture note: ViewSettings lives inside the detached config window
 * (target=`view:<id>`). All inner selectors run there. The picker panel
 * (`.icon-picker-panel`) is portalled to the same document body.
 *
 * Selectors per IconPicker.tsx:
 *  - trigger: `.dropdown-trigger` (shared CSS with Dropdown)
 *  - panel:   `.icon-picker-panel` (Portal → body)
 *  - option:  `.icon-picker-btn` (title attr = capitalised key, e.g. "Home")
 *
 * The View settings page renders no Dropdown components, so the first
 * `.dropdown-trigger` inside `.config-panel` is the IconPicker trigger.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openViewSettings, closeAllConfigWindows, switchToMain,
} from '../helpers/app.js';

async function iconPickerOpen(): Promise<boolean> {
  return (await $$('.icon-picker-panel').length) > 0;
}

async function readViews(): Promise<Array<{ id: string; name: string; icon: string }>> {
  return browser.execute(() => {
    try { return JSON.parse(localStorage.getItem('widget-screens') ?? '[]'); }
    catch { return []; }
  });
}

async function openIconPicker(): Promise<void> {
  await browser.execute(() => {
    const t = document.querySelector<HTMLElement>('.config-panel .dropdown-trigger');
    t?.click();
  });
  await $('.icon-picker-panel').waitForDisplayed({ timeout: 2_000 });
}

describe('component: icon picker', () => {
  beforeEach(async () => {
    await resetAppState();
    // IconPicker lives in the View settings page of the unified config window.
    // openViewSettings switches the webdriver session into that window.
    await openViewSettings();
  });

  afterEach(async () => {
    try { await closeAllConfigWindows(); } catch {}
    try { await switchToMain(); } catch {}
  });

  it('click trigger opens the picker panel', async () => {
    expect(await iconPickerOpen()).to.equal(false);
    await openIconPicker();
    expect(await iconPickerOpen()).to.equal(true);
  });

  it('clicking an icon updates view.icon in widget-screens', async () => {
    await openIconPicker();

    // Click the icon with title "Home".
    const clicked = await browser.execute(() => {
      const btns = Array.from(document.querySelectorAll<HTMLButtonElement>(
        '.icon-picker-panel .icon-picker-btn',
      ));
      const hit = btns.find(b => b.title === 'Home');
      if (!hit) return false;
      hit.click();
      return true;
    });
    expect(clicked).to.equal(true);

    // Picker closes after a pick.
    await browser.waitUntil(async () => !(await iconPickerOpen()), { timeout: 2_000 });

    // Assert persisted state (widget-screens is shared across windows).
    await browser.pause(120);
    const views = await readViews();
    const def = views.find(s => s.id === 'default');
    expect(def?.icon).to.equal('home');
  });

  it('closes on outside click', async () => {
    await openIconPicker();
    expect(await iconPickerOpen()).to.equal(true);

    // mousedown outside the panel (on the settings content area inside the
    // config panel).
    await browser.execute(() => {
      const content = document.querySelector('.config-panel .settings-content') as HTMLElement | null;
      content?.dispatchEvent(new MouseEvent('mousedown', {
        bubbles: true, cancelable: true, clientX: 0, clientY: 0, button: 0,
      }));
    });

    await browser.waitUntil(async () => !(await iconPickerOpen()), { timeout: 2_000 });
  });

  it('icon cannot be cleared: no "None" (X) button exists', async () => {
    // Set an icon first.
    await openIconPicker();
    await browser.execute(() => {
      const btns = Array.from(document.querySelectorAll<HTMLButtonElement>(
        '.icon-picker-panel .icon-picker-btn',
      ));
      btns.find(b => b.title === 'Star')?.click();
    });
    await browser.waitUntil(async () => !(await iconPickerOpen()), { timeout: 2_000 });
    await browser.pause(120);

    let views = await readViews();
    expect(views.find(s => s.id === 'default')?.icon).to.equal('star');

    // Reopen: "None" button must not exist (every view always has an icon).
    await openIconPicker();
    const hasNone = await browser.execute(() => {
      return !!document.querySelector('.icon-picker-panel .icon-picker-btn[title="None"]');
    });
    expect(hasNone).to.equal(false);

    // Icon stays as previously picked; cannot be unset.
    views = await readViews();
    expect(views.find(s => s.id === 'default')?.icon).to.equal('star');
  });

  it('fresh seed views ship with an icon (no missing/undefined)', async () => {
    // DEFAULT_VIEWS are seeded in-memory only and not persisted to
    // localStorage until something mutates them, so `widget-screens` may be
    // empty on a fresh install. Read the rendered view-bar chips in the main
    // window instead; every chip has either an SVG icon inside `.view-icon`
    // (resolved via VIEW_ICON_MAP) or the fallback `.view-icon-letters`.
    // "Has an icon" means the SVG path was rendered, not the fallback.
    await switchToMain();
    const info = await browser.execute(() => {
      const chips = Array.from(document.querySelectorAll<HTMLElement>('.view-chip'));
      return chips.map((c) => ({
        name: c.querySelector('.view-name-text')?.textContent?.trim() ?? '',
        hasSvg: !!c.querySelector('.view-icon svg'),
        hasFallback: !!c.querySelector('.view-icon-letters'),
      }));
    });
    expect(info.length, 'expected at least one view chip rendered').to.be.greaterThan(0);
    for (const v of info) {
      expect(
        v.hasSvg,
        `view "${v.name}" should render an icon SVG (not the letter fallback)`,
      ).to.equal(true);
      expect(v.hasFallback, `view "${v.name}" should not render the fallback initials`).to.equal(false);
    }
  });
});
