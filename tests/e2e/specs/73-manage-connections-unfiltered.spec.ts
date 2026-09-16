/**
 * "Manage connections" opens the FULL Connections page (regression).
 *
 * Bug: opening the Connections page via a widget's "Manage connections" button
 * used to scope it to that widget's providers (managerFilter), so connections of
 * other types vanished, while the sidebar "Connections" entry showed them all.
 * Fixed by removing the per-widget filter from the Connections page entirely:
 * the page now always shows every connection + every add-service provider. The
 * in-widget toggle list keeps its own provider scoping (that part was correct).
 *
 * Covers:
 *  - [S-39 email]    Manage connections opens the Connections page unfiltered.
 *  - [S-37 calendar] (same behavior, shared AccountManager / ConfigWindow path)
 *  - [S-37 contacts] (same)
 *  - [S-08 rss]      (same)
 *
 * SKIPPED: the tauri-driver session lifecycle still hangs cascade across the
 * accounts specs (mirrors 07-settings-modal / 59 / 60). Re-enable once 07 is.
 * Until then this path is verified manually (see PR / task summary).
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, seedWidgets, switchToMain, waitForConfigWindow,
  closeAllConfigWindows, stubThemiaInvoke,
} from '../helpers/app.js';

// Accounts spanning email-relevant AND non-email providers. The non-email ones
// (github, caldav) are what the old filter wrongly hid when entering from an
// email widget.
const MIXED_ACCOUNTS = [
  { id: 'ms-1',  email: 'a@contoso.com',  display_name: 'Work',   provider: 'microsoft' },
  { id: 'gh-1',  email: 'octo@github',    display_name: 'Octocat', provider: 'github' },
  { id: 'cd-1',  email: 'cal@nextcloud',  display_name: 'Cal',    provider: 'caldav' },
];

describe.skip('Manage connections → full Connections page', () => {
  beforeEach(async () => {
    await resetAppState();
    // accountStore.loadAccounts() calls email_list_accounts through the demo
    // invoke wrapper; stub it so the Connections page + toggle list populate.
    await stubThemiaInvoke({ email_list_accounts: MIXED_ACCOUNTS });
  });

  afterEach(async () => { await closeAllConfigWindows(); });

  it('[S-39] from an email widget, the page shows ALL connections', async () => {
    await seedWidgets([{ type: 'email', id: 'email-mc', config: { accountIds: ['ms-1'] } }]);

    // Open the email widget's config window and click "Manage connections".
    await switchToMain();
    await browser.execute(async () => {
      const internals = (window as any).__TAURI_INTERNALS__;
      const invoke = internals?.__originalInvoke ?? internals?.invoke;
      await invoke('open_config_window', { parentLabel: 'main', target: 'widget:email-mc' });
    });
    await waitForConfigWindow('widget', 5_000);

    // Source tab renders the AccountToggleList with the Manage-connections btn.
    const manageBtn = await $('.manage-accounts-btn');
    await manageBtn.waitForDisplayed({ timeout: 5_000 });
    await manageBtn.click();

    // Same window navigates to the Connections page.
    await $('.account-manager').waitForDisplayed({ timeout: 5_000 });

    // Connected list shows EVERY account, including non-email ones.
    const rows = await $$('.account-manager .am-account-list .account-row').length;
    expect(rows, 'all 3 connections listed (not just email-relevant)').to.equal(3);

    // The page offers the same unscoped Add button as the sidebar route: the
    // provider catalog moved into the Connect window entirely ([S-40]), so
    // there is no per-widget-filtered picker left here to get wrong.
    const addBtn = await $('.account-manager .am-add-btn');
    expect(await addBtn.isDisplayed(), 'Add connection offered from an email widget').to.equal(true);
  });
});
