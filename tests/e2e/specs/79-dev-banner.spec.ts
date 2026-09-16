/**
 * Developer-mode disclaimer banner: config window.
 *
 * Users who flip Developer Mode on were connecting account types they
 * shouldn't and filing bug reports for not-yet-tested features. A primary-
 * accented banner spans the full width at the very top of the config window,
 * above the whole shell (sidebar tree + content panel), warning that the
 * unlocked features are experimental. Gated on `globalDesign.devMode`.
 *
 * Selector per ConfigWindow.tsx: `.config-root > .config-dev-banner` (a sibling
 * of `.config-shell`, NOT inside `.config-panel`).
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, enableDevMode, settingsTab,
} from '../helpers/app.js';

describe('developer mode banner [D-10]', () => {
  beforeEach(async () => { await resetAppState(); });
  afterEach(async () => { await closeSettings(); });

  it('[D-10] shows a not-fully-tested disclaimer when Developer Mode is on', async () => {
    // devMode must be set before the config window boots; it reads the design
    // blob from localStorage at construction.
    await enableDevMode();
    await openSettings();

    const banner = await $('.config-dev-banner');
    expect(await banner.isExisting()).to.equal(true);
    // It sits above the shell, not inside the scrollable panel.
    expect(await $('.config-panel .config-dev-banner').isExisting()).to.equal(false);

    const text = (await banner.getText()).trim();
    expect(text.length, 'banner must carry disclaimer text').to.be.greaterThan(0);
    // A missing i18n key renders the raw path ("settings.devBanner.text").
    expect(text, 'banner must not render a raw i18n key path').to.not.include('settings.');
  });

  it('[D-10] banner spans the whole window: stays visible across config pages', async () => {
    await enableDevMode();
    await openSettings();
    // It lives outside the page Switch, so navigating pages never removes it.
    await (await settingsTab('Logs')).click();
    await browser.pause(120);
    expect(await $('.config-dev-banner').isExisting()).to.equal(true);
  });

  it('[D-10] hides the banner when Developer Mode is off', async () => {
    await openSettings();
    expect(await $('.config-dev-banner').isExisting()).to.equal(false);
  });
});
