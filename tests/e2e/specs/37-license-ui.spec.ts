/**
 * License tab UI: verifies Pro vs Free rendering.
 *
 * `src/license.ts` short-circuits to tier='pro' when `navigator.webdriver`
 * is true, which is always the case under tauri-driver. So the live
 * session is always Pro. This spec:
 *
 *  1. Asserts the WebDriver-forced Pro branch:
 *     - "Pro" badge shown on the License tab
 *     - Deactivate button present (Pro), Activate input absent (Free path)
 *     - Widget picker does NOT show `.picker-limit` pill when >= 10 widgets
 *
 *  2. Exercises deactivate → (simulated) Free state by *directly
 *     mutating* the license store via `license_deactivate`. The real
 *     behaviour is immediate because deactivateLicense() writes to the
 *     reactive store regardless of the invoke outcome. We stub the invoke
 *     so the test doesn't hit the network.
 *
 *  3. Skips the reverse navigator.webdriver override test: spoofing that
 *     property after initLicense() has already run doesn't reset the
 *     store, and reloading re-runs initLicense() which reads webdriver
 *     true again. So we cover the Free UI by driving the store directly
 *     via `license_activate` being stubbed after a forced deactivate.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
  stubTauriInvoke, openPicker, closePicker,
} from '../helpers/app.js';

async function licenseBadgeText(): Promise<string> {
  // The license hero card encodes the active tier via .license-hero.free
  // or .license-hero.pro. Read the modifier and return the human label.
  return browser.execute(() => {
    const el = document.querySelector('.config-panel .license-hero');
    if (!el) return '';
    if (el.classList.contains('pro')) return 'Pro';
    if (el.classList.contains('free')) return 'Free';
    return '';
  });
}

async function findSettingsBtnByText(text: string): Promise<boolean> {
  return browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    return btns.some(b => (b.textContent ?? '').trim() === t);
  }, text);
}

async function clickSettingsBtnByText(text: string): Promise<void> {
  const ok = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    for (const b of btns) {
      if ((b.textContent ?? '').trim() === t) { b.click(); return true; }
    }
    return false;
  }, text);
  if (!ok) throw new Error(`settings button "${text}" not found`);
}

describe('license tab UI', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  it('shows Pro badge when navigator.webdriver forces Pro', async () => {
    await openSettings();
    await (await settingsTab('License')).click();

    expect(await licenseBadgeText()).to.equal('Pro');

    // Free-tier UI bits must be hidden:
    //  - widget-limit progress bar (not rendered)
    //  - upgrade button (not rendered)
    const limitBar = await $$('.config-panel .license-usage-bar').length;
    expect(limitBar).to.equal(0);
    expect(await findSettingsBtnByText('Upgrade to Pro')).to.equal(false);

    // Pro-specific: Deactivate button is present.
    expect(await findSettingsBtnByText('Deactivate')).to.equal(true);

    await closeSettings();
  });

  it('widget picker does NOT show .picker-limit pill for Pro users', async () => {
    // We don't need to fill 10 widgets; the `.picker-limit` show-gate is
    // `!isPro() && widgets.length >= 10`. For Pro it's always off, even
    // if widgets.length is 0.
    await openPicker();
    const pill = await $$('.picker-limit').length;
    expect(pill).to.equal(0);
    await closePicker();
  });

  it('stubbed deactivate flips UI to Free tier', async () => {
    // The production `deactivateLicense()` invokes `license_deactivate` then
    // writes `tier: 'free'` into the store. Under webdriver, initLicense()
    // short-circuits to Pro and the invoke path isn't exercised unless we
    // drive it, and the stubbed invoke still doesn't run initLicense()
    // again. Instead, flip the reactive store directly via the test hook
    // exposed in `src/license.ts` (gated on navigator.webdriver).
    // Only stub the license-specific invoke; let everything else (notably
    // `open_config_window` for the settings window) pass through to the real
    // backend so the config window actually opens.
    await stubTauriInvoke({
      license_deactivate: null,
    });

    await openSettings();
    await (await settingsTab('License')).click();
    expect(await licenseBadgeText()).to.equal('Pro');

    // Mutate the store directly to simulate a completed deactivation.
    await browser.execute(() => {
      const h = (window as any).__license;
      h?.setLicenseState({
        tier: 'free', activated: false, key: null, instance_id: null,
        valid_until: null, error: null, loading: false,
      });
    });

    await browser.waitUntil(async () => (await licenseBadgeText()) === 'Free', {
      timeout: 3_000,
      interval: 100,
    });

    // Free-tier UI appears.
    expect(await findSettingsBtnByText('Upgrade to Pro')).to.equal(true);
    // Widget/view usage rows render (license-usage-bar exists).
    const bars = await $$('.config-panel .license-usage-bar').length;
    expect(bars).to.be.greaterThan(0);

    await closeSettings();
  });

  it('stubbed activate re-promotes UI back to Pro', async () => {
    await stubTauriInvoke({
      license_deactivate: null,
      license_activate: {
        tier: 'pro',
        activated: true,
        key: 'TEST-1234',
        instance_id: 'inst-abc',
        valid_until: null,
        error: null,
      },
    });

    await openSettings();
    await (await settingsTab('License')).click();

    // Force Free via direct store mutation (see previous test).
    await browser.execute(() => {
      const h = (window as any).__license;
      h?.setLicenseState({
        tier: 'free', activated: false, key: null, instance_id: null,
        valid_until: null, error: null, loading: false,
      });
    });
    await browser.waitUntil(async () => (await licenseBadgeText()) === 'Free', { timeout: 3_000 });

    // Now re-promote via direct store mutation to simulate successful activate.
    await browser.execute(() => {
      const h = (window as any).__license;
      h?.setLicenseState({
        tier: 'pro', activated: true, key: 'TEST-1234', instance_id: 'inst-abc',
        valid_until: null, error: null, loading: false,
      });
    });

    await browser.waitUntil(async () => (await licenseBadgeText()) === 'Pro', {
      timeout: 3_000,
      interval: 100,
    });

    await closeSettings();
  });
});
