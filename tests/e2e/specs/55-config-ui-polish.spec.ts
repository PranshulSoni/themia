/**
 * Config / settings UI polish.
 *
 * Covers three changes to the settings surface:
 *  - Language selector is now a searchable Dropdown (was a .theme-seg toggle).
 *    Opening it shows a .dropdown-search input; typing filters the options;
 *    selecting persists `language` to widget-global-design.
 *  - Config/settings scroll containers use an always-visible styled scrollbar
 *    (`overflow-y: scroll`, not `auto` / hidden).
 *  - `.btn-secondary` buttons (Contact support etc.) flex-align their icon +
 *    text so the icon is vertically centred.
 *
 * Selectors per Dropdown.tsx / config-base.css / App.css.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, readDesign,
} from '../helpers/app.js';

async function openLanguageDropdown(): Promise<void> {
  await browser.execute(() => {
    const t = document.querySelector<HTMLElement>('.settings-content .dropdown-trigger');
    t?.click();
  });
  await $('.dropdown-panel').waitForDisplayed({ timeout: 2_000 });
}

async function dropdownOpen(): Promise<boolean> {
  return (await $$('.dropdown-panel').length) > 0;
}

async function optionLabels(): Promise<string[]> {
  return browser.execute(() =>
    Array.from(document.querySelectorAll<HTMLElement>('.dropdown-panel .dropdown-item'))
      .map(el => (el.textContent ?? '').trim()),
  );
}

describe('config: ui polish', () => {
  beforeEach(async () => {
    await resetAppState();
    await openSettings();
    await (await settingsTab('General')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('language selector is a searchable dropdown', async () => {
    const trigger = await $('.settings-content .dropdown-trigger');
    expect(await trigger.isExisting()).to.equal(true);
    expect((await trigger.getText()).trim()).to.contain('English');

    await openLanguageDropdown();
    expect(await dropdownOpen()).to.equal(true);

    const search = await $('.dropdown-panel .dropdown-search');
    expect(await search.isExisting()).to.equal(true);

    // Locale labels per src/locales/en.ts settings.language.*
    expect(await optionLabels()).to.have.members([
      'English', 'Deutsch', 'Español', 'Français',
      'Português', 'Italiano', 'Polski', '日本語', '简体中文',
    ]);
  });

  it('typing in the search filters the language options', async () => {
    await openLanguageDropdown();
    await $('.dropdown-panel .dropdown-search').setValue('deu');
    await browser.waitUntil(async () => (await optionLabels()).length === 1, { timeout: 2_000 });
    expect(await optionLabels()).to.deep.equal(['Deutsch']);
  });

  it('selecting a language persists it and closes the panel', async () => {
    await openLanguageDropdown();
    await $('.dropdown-panel .dropdown-search').setValue('deu');
    await browser.waitUntil(async () => (await optionLabels()).length === 1, { timeout: 2_000 });
    await browser.execute(() => {
      document.querySelector<HTMLElement>('.dropdown-panel .dropdown-item')?.click();
    });
    await browser.waitUntil(async () => !(await dropdownOpen()), { timeout: 2_000 });

    expect((await readDesign()).language).to.equal('de');
    const trigger = await $('.settings-content .dropdown-trigger');
    expect((await trigger.getText()).trim()).to.contain('Deutsch');
  });

  it('settings scroll container has an always-visible styled scrollbar', async () => {
    const overflowY = await browser.execute(() => {
      const el = document.querySelector('.settings-content');
      return el ? getComputedStyle(el).overflowY : '';
    });
    expect(overflowY).to.equal('scroll');
  });

  it('.btn-secondary buttons flex-align their icon and text', async () => {
    const styles = await browser.execute(() => {
      const el = document.querySelector('.btn-secondary');
      if (!el) return null;
      const s = getComputedStyle(el);
      return { display: s.display, alignItems: s.alignItems };
    });
    expect(styles).to.not.equal(null);
    expect(styles!.display).to.equal('flex');
    expect(styles!.alignItems).to.equal('center');
  });
});
