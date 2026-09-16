/**
 * Settings → Accounts: adding a connection.
 *
 * Covers (specs may be skipped if tauri-driver session is unstable; pattern
 * mirrors 07-settings-modal):
 *  - [S-40 email] The Accounts page shows no provider list — the catalog lives
 *    in the Connect window, not in Settings.
 *  - [S-40 email] "Add connection" is offered top-right of the section header
 *    AND as a ghost row closing the connection list.
 *  - [S-40 email] Clicking it renders no inline form and no inline picker: the
 *    add flow is the dedicated Connect window (src/ConnectWindow.tsx).
 *
 * The providers themselves (IMAP / CalDAV / CardDAV, [S-38 email], [S-36
 * calendar], [S-36 contacts]) are asserted where they now live, in
 * 87-connect-window-presets.spec.ts.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, stubThemiaInvoke,
} from '../helpers/app.js';

const ONE_ACCOUNT = [
  { id: 'ms-1', email: 'a@contoso.com', display_name: 'Work', nickname: null, provider: 'microsoft' },
];

// Mirrors 07-settings-modal: tauri-driver session lifecycle hangs cascade
// across specs. Re-enable once 07 is re-enabled.
describe.skip('settings → accounts: add connection', () => {
  beforeEach(async () => { await resetAppState(); });

  async function gotoAccountsTab(): Promise<void> {
    await openSettings('Accounts');
    const tab = await settingsTab('Accounts');
    await tab.click();
    await $('.account-manager .am-add-btn').waitForDisplayed({ timeout: 5_000 });
  }

  it('[S-40] offers Add connection, not a provider list', async () => {
    await gotoAccountsTab();

    const addBtn = await $('.account-manager .am-add-btn');
    expect(await addBtn.getText()).to.match(/add connection/i);

    // The inline provider grid was removed: duplicating the catalog here made
    // Settings grow with every provider and bypassed the guided flow.
    const grid = await $('.account-manager .add-provider-row');
    expect(await grid.isExisting(), 'no inline provider grid').to.equal(false);

    await closeSettings();
  });

  it('[S-40] pins the header button top-right, above the connection list', async () => {
    await stubThemiaInvoke({ email_list_accounts: ONE_ACCOUNT });
    await gotoAccountsTab();

    const head = await $('.account-manager .am-section-head');
    const btn = await $('.account-manager .am-section-head .am-add-btn');
    const headBox = await head.getLocation();
    const headSize = await head.getSize();
    const btnBox = await btn.getLocation();
    const btnSize = await btn.getSize();

    // Right edge of the button hugs the right edge of the header (within the
    // header's own padding), and it sits on the heading's top line.
    expect(btnBox.x + btnSize.width, 'button is right-aligned in the header')
      .to.be.closeTo(headBox.x + headSize.width, 2);
    expect(btnBox.y, 'button rides the top of the header').to.be.closeTo(headBox.y, 4);

    // …and above the list it introduces.
    const list = await $('.account-manager .am-account-list');
    expect(btnBox.y).to.be.below((await list.getLocation()).y);

    await closeSettings();
  });

  it('[S-40] closes the connection list with a ghost row that also adds', async () => {
    await stubThemiaInvoke({ email_list_accounts: ONE_ACCOUNT });
    await gotoAccountsTab();

    const classes: string[] = [];
    for await (const row of $$('.account-manager .am-account-list > *')) {
      classes.push((await row.getAttribute('class')) ?? '');
    }
    expect(classes.length, 'the account plus the ghost').to.equal(2);
    expect(classes[classes.length - 1], 'ghost is the final entry').to.contain('am-add-ghost');

    const ghost = await $('.account-manager .am-add-ghost');
    expect(await ghost.getText()).to.match(/add connection/i);
    // It is an outline, not a filled row: that is what makes it read as the
    // next empty slot rather than a fourth account.
    expect(await ghost.getCSSProperty('border-top-style').then(p => p.value)).to.equal('dashed');

    await closeSettings();
  });

  it('[S-40] clicking Add renders no inline form or picker in Settings', async () => {
    await gotoAccountsTab();
    await (await $('.account-manager .am-add-btn')).click();

    // The add flow is a separate OS window, so nothing new should appear in
    // the manager itself.
    await browser.pause(500);
    for (const sel of ['.account-manager .form-panel', '.account-manager .connect-provider-grid']) {
      expect(await (await $(sel)).isExisting(), `no ${sel} in the manager`).to.equal(false);
    }

    await closeSettings();
  });
});
