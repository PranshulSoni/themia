/**
 * Help & Getting Started widget.
 *
 * Registry label is "Help & Getting Started" (widgets/others/help/index.tsx).
 * Renders a four-tab segmented control (Basics, Widgets, Views, Customize) over
 * a shared scrollable body. Basics is the default active tab. Content is written
 * for non-technical users; it teaches actions via menus/buttons, not keyboard
 * shortcuts, so there is no Shortcuts tab and no `<kbd>` shortcut list ([S-05],
 * [S-06]).
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist,
  configModalIsOpen, closeConfigModal, leaveEditMode,
} from '../helpers/app.js';

describe('widget: Help & Getting Started', () => {
  beforeEach(async () => {
    await resetAppState();
    await pickWidget('Help & Getting Started');
    await clickGridCell(2, 2);
    await waitForPersist();
    if (await configModalIsOpen()) await closeConfigModal();
    await leaveEditMode();
    // Belt-and-braces: force-clear edit mode in case Ctrl+E was eaten.
    await browser.execute(() => {
      const k = 'themia-edit-mode';
      localStorage.setItem(k, 'false');
      window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: 'false' }));
    });
    await browser.waitUntil(async () => !(await $('.widget.edit').isExisting()), {
      timeout: 3_000, timeoutMsg: '.widget.edit overlay still present',
    });
    await $('.help-widget').waitForDisplayed({ timeout: 3_000 });
  });

  it('Basics tab is the default active tab', async () => {
    const basicsTab = await $('.help-widget__tab[data-tab="basics"]');
    expect(await basicsTab.getAttribute('aria-selected')).to.equal('true');
  });

  it('renders exactly the four non-technical tabs, with no Shortcuts tab ([S-06])', async () => {
    const tabs = await $$('.help-widget__tab');
    const ids: string[] = [];
    for await (const el of tabs) ids.push((await el.getAttribute('data-tab')) ?? '');
    expect(ids).to.deep.equal(['basics', 'widgets', 'views', 'customize']);
    expect(await $('.help-widget__tab[data-tab="shortcuts"]').isExisting()).to.equal(false);
  });

  it('teaches no keyboard shortcuts, no kbd list on any tab ([S-05])', async () => {
    for (const tab of ['basics', 'widgets', 'views', 'customize']) {
      await $(`.help-widget__tab[data-tab="${tab}"]`).click();
      await browser.pause(50);
      // No shortcut-list kbd chips anywhere in the body.
      expect(await $$('.help-widget__kbd').length).to.equal(0);
      expect(await $$('.help-widget__row').length).to.equal(0);
    }
  });

  it('switches tabs when a tab is clicked', async () => {
    // Start on Basics (default) → switch to Widgets and confirm aria state.
    const widgetsTab = await $('.help-widget__tab[data-tab="widgets"]');
    await widgetsTab.click();
    await browser.pause(50);
    expect(await widgetsTab.getAttribute('aria-selected')).to.equal('true');

    const basicsTab = await $('.help-widget__tab[data-tab="basics"]');
    expect(await basicsTab.getAttribute('aria-selected')).to.equal('false');
  });

  // issue #91: the whole guide used to be English literals in the component, so
  // a non-English user read the onboarding text in English no matter what the
  // language was set to. The prose now comes from `help.*` locale keys ([S-14]),
  // and every shipped locale translates them.
  async function setLanguage(code: string): Promise<void> {
    await browser.execute((c: string) => {
      const key = 'widget-global-design';
      const cur = (() => {
        try { return JSON.parse(localStorage.getItem(key) ?? '{}'); } catch { return {}; }
      })();
      const next = JSON.stringify({ ...cur, language: c });
      localStorage.setItem(key, next);
      window.dispatchEvent(new StorageEvent('storage', { key, newValue: next }));
    }, code);
  }

  const introText = async () => (await $('.help-widget__intro').getText()).trim();

  // zh is the locale the reporter uses; de is a second, independent locale —
  // together they prove the guide follows the language rather than one
  // hand-patched translation.
  for (const [lang, marker] of [['zh', '欢迎使用 Themia'], ['de', 'Willkommen bei Themia']]) {
    it(`issue_91 guide prose translates when the language flips to ${lang} ([S-14])`, async () => {
      expect(await introText()).to.contain('Welcome to Themia');
      await setLanguage(lang);
      await browser.waitUntil(async () => (await introText()).includes(marker), {
        timeout: 3_000,
        timeoutMsg: `expected the Basics intro to render in ${lang} after the flip`,
      });

      // The bullets are prose keys too, and their *term* / _ui element_ markers
      // must render as real emphasis rather than leaking a literal asterisk.
      const bullets = await $('.help-widget__bullets').getText();
      expect(bullets).to.not.contain('*');
      expect(await $$('.help-widget__bullets strong').length).to.be.greaterThan(0);
    });
  }

  it('scrolling the body does not crash the widget', async () => {
    await browser.execute(() => {
      const body = document.querySelector<HTMLElement>('.help-widget__body');
      if (body) body.scrollTop = 200;
    });
    await browser.pause(100);
    expect(await $('.help-widget').isDisplayed()).to.equal(true);
  });
});
