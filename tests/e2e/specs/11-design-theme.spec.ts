/**
 * Design: Theme toggle.
 *
 * Clicks System/Dark/Light in the General tab and asserts:
 *  - `.theme-seg button.active` reflects selection
 *  - applyDesign() updates --foreground and --card CSS vars
 *  - localStorage `widget-global-design` persists choice (theme is global)
 *  - reload restores the theme
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, readCssVar,
  readDesign, waitForReady, switchToConfigWindow, switchToMain,
} from '../helpers/app.js';

/** Read active theme label from inside the app config window. */
async function activeThemeLabel(): Promise<string> {
  await switchToConfigWindow('app');
  return browser.execute(() => {
    const b = document.querySelector('.theme-seg button.active');
    return (b?.textContent ?? '').trim();
  });
}

/** Click a theme segment button inside the app config window. */
async function clickTheme(label: 'System' | 'Dark' | 'Light'): Promise<void> {
  await switchToConfigWindow('app');
  const ok = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.theme-seg button'));
    for (const b of btns) {
      if ((b.textContent ?? '').trim() === t) { b.click(); return true; }
    }
    return false;
  }, label);
  if (!ok) throw new Error(`theme button "${label}" not found`);
}

describe('design: theme', () => {
  beforeEach(async () => {
    await resetAppState();
    // Theme picker moved into the Style page (SettingsModal `style` match).
    await openSettings('Style');
  });

  afterEach(async () => { await closeSettings(); });

  it('default theme is dark with expected --foreground', async () => {
    await switchToMain();
    const fg = await readCssVar('--foreground');
    expect(fg).to.contain('255');   // rgba(255,255,255,0.886)
  });

  it('clicking Light flips --foreground to dark-on-light', async () => {
    await clickTheme('Light');
    expect(await activeThemeLabel()).to.contain('Light');
    await switchToMain();
    const fg = await readCssVar('--foreground');
    // Light theme: rgba(0,0,0,0.87)
    expect(fg.replace(/\s+/g, '')).to.match(/rgba\(0,0,0/);
  });

  it('clicking Dark restores dark --foreground', async () => {
    await clickTheme('Light');
    await clickTheme('Dark');
    expect(await activeThemeLabel()).to.contain('Dark');
    await switchToMain();
    const fg = await readCssVar('--foreground');
    expect(fg).to.contain('255');
  });

  it('System resolves based on prefers-color-scheme', async () => {
    await clickTheme('System');
    expect(await activeThemeLabel()).to.contain('System');
    await switchToMain();
    // We can't easily flip the OS theme, but we can assert *some* --foreground
    // is set (either rgba(255,255,255,...) or rgba(0,0,0,...)).
    const fg = await readCssVar('--foreground');
    expect(fg).to.match(/rgba\(/);
  });

  it('exposes an independent --header-bg that defaults to match --card', async () => {
    // The header surface is a separate CSS var driven by headerBgColor/Opacity.
    // By default (and via the v4 forward-migration) it mirrors the body so an
    // upgrade is visually a no-op; assert the var is wired and equal to --card.
    await switchToMain();
    const card = await readCssVar('--card');
    const headerBg = await readCssVar('--header-bg');
    expect(headerBg.replace(/\s+/g, '')).to.equal(card.replace(/\s+/g, ''));
    expect(headerBg).to.match(/rgba\(/);
  });

  it('persists through reload', async () => {
    await clickTheme('Light');
    await closeSettings();
    await browser.refresh();
    await waitForReady();

    const persisted = await readDesign();
    expect(persisted.theme).to.equal('light');
    const fg = await readCssVar('--foreground');
    expect(fg.replace(/\s+/g, '')).to.match(/rgba\(0,0,0/);
  });
});
