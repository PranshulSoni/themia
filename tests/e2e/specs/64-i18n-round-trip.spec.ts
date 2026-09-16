/**
 * i18n round-trip: pick a non-default language and assert the settings UI
 * actually translates. Catches regressions where a key was added in en.ts
 * but forgotten in the other locales (renders as the key path, e.g.
 * "settings.nav.general") OR where the language picker writes the design
 * state but the reactive `t()` calls don't pick it up.
 *
 * We assert against the ConfigTree's "General" entry: en = "General",
 * de = "Allgemein". Both labels are sourced from `settings.nav.general` in
 * the locale files.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, openSettings, closeSettings } from '../helpers/app.js';

async function configTreeLabels(): Promise<string[]> {
  return browser.execute(() => Array.from(
    document.querySelectorAll<HTMLElement>('.config-tree .config-tree-label'),
  ).map(el => (el.textContent ?? '').trim()));
}

async function setLanguage(code: 'en' | 'de'): Promise<void> {
  // The language picker is a Dropdown in Settings → General. Driving it via
  // click would test the Dropdown rather than i18n; write directly to the
  // shared design state and dispatch the storage event so the config window
  // re-renders.
  await browser.execute((c: string) => {
    const key = 'widget-global-design';
    const cur = (() => {
      try { return JSON.parse(localStorage.getItem(key) ?? '{}'); } catch { return {}; }
    })();
    const next = JSON.stringify({ ...cur, language: c });
    localStorage.setItem(key, next);
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: next }));
  }, code);
  // Solid reactivity needs a tick for t() consumers to flush.
  await browser.pause(80);
}

describe('i18n round-trip', () => {
  beforeEach(async () => { await resetAppState(); });
  afterEach(async () => {
    try { await setLanguage('en'); } catch { /* best-effort */ }
    try { await closeSettings(); } catch { /* best-effort */ }
  });

  it('settings ConfigTree translates "General" when language flips en → de → en', async () => {
    await setLanguage('en');
    await openSettings();
    await $('.config-tree .settings-nav-item').waitForExist({ timeout: 5_000 });

    let labels = await configTreeLabels();
    expect(labels, `expected "General" in EN labels, got ${JSON.stringify(labels)}`)
      .to.include('General');
    expect(labels, 'EN should not include the DE label').to.not.include('Allgemein');

    await setLanguage('de');
    await browser.waitUntil(async () => (await configTreeLabels()).includes('Allgemein'),
      { timeout: 3_000, timeoutMsg: 'expected ConfigTree to translate to DE after language flip' });
    labels = await configTreeLabels();
    expect(labels, 'DE labels should not include EN "General"').to.not.include('General');

    await setLanguage('en');
    await browser.waitUntil(async () => (await configTreeLabels()).includes('General'),
      { timeout: 3_000, timeoutMsg: 'expected ConfigTree to translate back to EN' });
  });

  it('every shipped locale exposes a settings.nav.general translation', async () => {
    // Failsafe against forgotten keys in non-default locales: a missing
    // translation would render the raw key path ("settings.nav.general")
    // rather than a localized label.
    const codes = ['en', 'de', 'es', 'fr', 'pt', 'it', 'pl', 'ja'] as const;
    for (const code of codes) {
      await setLanguage(code);
      if (!await $('.config-tree .settings-nav-item').isExisting()) {
        await openSettings();
        await $('.config-tree .settings-nav-item').waitForExist({ timeout: 5_000 });
      }
      const labels = await configTreeLabels();
      const hasKeyPath = labels.some(l => l.includes('settings.') || l.includes('nav.'));
      expect(hasKeyPath,
        `locale "${code}" rendered raw key path(s) in ConfigTree: ${JSON.stringify(labels)}`)
        .to.equal(false);
    }
  });
});
