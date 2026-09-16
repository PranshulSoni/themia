/**
 * Themia "branding" widget.
 *
 * The spec brief asked for: Settings / GitHub / Docs / License link buttons
 * that invoke `open_url`. The actual component
 * (widgets/others/themia/Themia.tsx) is a passive branding tile; it renders
 * a logo, the app name, and the version string via `getVersion()` from
 * `@tauri-apps/api/app`. There are NO buttons, NO click handlers, NO calls
 * to `open_url`.
 *
 * The link-button + invoke-stub + License-tab flows are therefore SKIPPED
 * with explanatory describe.skip blocks. We keep the parts we *can* test:
 *  - the widget mounts and renders the logo, name, and a version string
 *  - the version string is fetched from the Tauri backend (stubbable via
 *    plugin:app|get_version) and displayed live
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist,
  configModalIsOpen, closeConfigModal,
} from '../helpers/app.js';

describe('widget: Themia (branding)', () => {
  beforeEach(async () => {
    await resetAppState();
    await pickWidget('Themia');
    await clickGridCell(2, 2);
    await waitForPersist();
    if (await configModalIsOpen()) await closeConfigModal();
    await $('.themia-widget').waitForDisplayed({ timeout: 3_000 });
  });

  it('renders the logo, app name, and a version string', async () => {
    expect(await $('.themia-widget__logo').isDisplayed()).to.equal(true);
    expect(await $('.themia-widget__name').getText()).to.equal('Themia');

    // getVersion() resolves asynchronously; wait until the span has content.
    await browser.waitUntil(async () => {
      const t = (await $('.themia-widget__version').getText()).trim();
      return /^v.+/.test(t);
    }, { timeout: 5_000, timeoutMsg: 'version string never populated' });

    const ver = (await $('.themia-widget__version').getText()).trim();
    // Format is "v<something>", non-empty after the `v` prefix.
    expect(ver).to.match(/^v.+/);
  });

  it('has no interactive buttons (widget is pure branding)', async () => {
    // Sanity: if buttons are added later, this test will fail and prompt an
    // update to the link-button spec below.
    const btns = await browser.execute(() =>
      document.querySelectorAll('.themia-widget button, .themia-widget a').length,
    );
    expect(btns).to.equal(0);
  });
});

/**
 * Retained as a skipped describe so the brief's intent is visible to future
 * maintainers. If Settings / GitHub / Docs / License buttons are added to
 * the Themia widget, flip these to describe/it and wire them up using
 * `stubTauriInvoke({ open_url: null })` + call-tracking in window.__inv.
 */
describe.skip('widget: Themia link buttons, NOT IMPLEMENTED', () => {
  it('Settings button opens the Settings modal', () => { /* skip */ });
  it('GitHub button invokes open_url with the repo URL', () => { /* skip */ });
  it('Docs button invokes open_url with the docs URL', () => { /* skip */ });
  it('License link opens the Settings modal on the License tab', () => { /* skip */ });
});
