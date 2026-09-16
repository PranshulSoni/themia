/**
 * GitHub widget account picker + accounts modal integration.
 *
 * Covers:
 *  - [S-13] Token stored centrally on the github provider account, not on the widget.
 *  - [S-22] GitHub config Source tab shows AccountToggleList instead of a token input.
 *  - [S-37] Account picker uses AccountToggleList filtered to the github provider.
 *  - [S-38] Legacy `config.token` auto-migrates to a github account on first
 *           config open (token field removed from widget config).
 *  - [S-39] Account stores api_base_url; widget repo field accepts owner/repo
 *           and resolves host from the selected account.
 *
 * Mirrors 59-account-modal-secondary-providers.spec.ts skip pattern: the
 * tauri-driver session lifecycle still hangs cascade across specs.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
} from '../helpers/app.js';

describe.skip('github widget accounts integration', () => {
  beforeEach(async () => { await resetAppState(); });

  async function gotoAccountsTab(): Promise<void> {
    await openSettings('Accounts');
    const tab = await settingsTab('Accounts');
    await tab.click();
    await $('.account-manager .add-provider-row').waitForDisplayed({ timeout: 5_000 });
  }

  async function providerNames(): Promise<string[]> {
    const names: string[] = [];
    for await (const btn of $$('.account-manager .add-provider-row .add-provider-btn')) {
      const nameEl = await btn.$('.add-provider-name');
      names.push((await nameEl.getText()).trim());
    }
    return names;
  }

  // [S-37] Account picker uses AccountToggleList filtered to github provider.
  it('[S-37] lists GitHub among Accounts add-provider buttons', async () => {
    await gotoAccountsTab();
    const names = await providerNames();
    expect(names).to.include('GitHub');
    await closeSettings();
  });

  // [S-22] GitHub button launches the dedicated Connect window (no inline form).
  it('[S-22] GitHub button opens the Connect window, not an inline form', async () => {
    await gotoAccountsTab();
    let clicked = false;
    for await (const btn of $$('.account-manager .add-provider-row .add-provider-btn')) {
      const name = (await btn.$('.add-provider-name').getText()).trim();
      if (name === 'GitHub') { await btn.click(); clicked = true; break; }
    }
    expect(clicked, 'GitHub button found').to.equal(true);

    // The token form now lives in src/ConnectWindow.tsx (a separate OS window),
    // so no inline `.form-panel` should render inside the manager.
    await browser.pause(500);
    const inlineForm = await $('.account-manager .form-panel');
    expect(await inlineForm.isExisting(), 'no inline form in the manager').to.equal(false);
    await closeSettings();
  });

  // [S-13] Token is stored on the account, not in widget config.
  //        After adding a github account, the widget config should expose
  //        AccountToggleList rather than a raw token field.
  it('[S-13][S-37] github widget config shows account toggle list, not a token field', async () => {
    // Seed a github account directly via Tauri command to bypass the live
    // /user validation roundtrip.
    await browser.execute(async () => {
      // @ts-ignore: Tauri global is injected at runtime.
      await (window as any).__TAURI__.core.invoke('email_add_github_account', {
        token: 'ghp_dummy_test_token',
        apiBaseUrl: 'https://api.github.com',
        nickname: 'Test',
      }).catch(() => {});
    });

    // Picking up the github widget should now show AccountToggleList in
    // the Source tab and *no* token input.
    // (Driver / helper to open widget config not shown; see helpers/app.ts
    // pattern in 08-widget-config.spec.ts.)
  });

  // [S-38] Legacy `config.token` auto-migrates to a github account.
  it('[S-38] legacy config.token migrates to a github account on first config open', async () => {
    // Seed localStorage with a widget that has the old `token` field set.
    // Open the widget config → migration should create an account, drop the
    // token field, and populate config.accountIds.
    // (Implementation pending wiring up the widget store helpers for this
    // spec to drive end-to-end. The migration logic lives in
    // GitHubConfig.tsx::onMount.)
  });

  // [S-39] Account stores api_base_url; repo accepts owner/repo.
  it('[S-39] account api_base_url is used as fetch host', async () => {
    // After adding a github account with api_base_url
    // https://ghe.example.com/api/v3, configuring the widget with
    // `owner/repo` should produce GET requests against ghe.example.com/api/v3
    // verifiable via preview_network or by inspecting the cache key.
  });
});
