/**
 * Connect window: provider picker + protocol presets.
 *
 * Covers:
 *  - [S-40 email]    Settings' Add button opens the Connect window, and every
 *                    provider lives there (IMAP / CalDAV / CardDAV included —
 *                    formerly asserted in Settings by spec 59).
 *  - [S-38 email]    IMAP is reachable from the picker.
 *  - [S-36 calendar] CalDAV is reachable from the picker.
 *  - [S-36 contacts] CardDAV is reachable from the picker.
 *  - [S-47 email]    Picking IMAP offers presets; picking "Gmail" fills the
 *                    server fields, so nobody has to know `imap.gmail.com:993`.
 *  - [S-47 email]    Typing a Gmail address auto-selects the Gmail preset.
 *  - [S-47 email]    Hand-editing a server field clears the preset, so
 *                    detection can never overwrite deliberate input.
 *  - [S-47 email]    A self-hosted preset leaves a {token} and Connect stays
 *                    disabled until it is replaced.
 *
 * The reachability badge itself ([S-46]/[S-47]) is deliberately NOT asserted
 * here: it depends on live third-party endpoints. Its logic is covered offline
 * (src/connect/probe.test.ts, the Rust `s46_*` tests) and against real servers
 * by `npm run test:presets`.
 *
 * SKIPPED: the tauri-driver session lifecycle hangs cascade across the accounts
 * specs (mirrors 07-settings-modal / 59 / 73). Re-enable once 07 is.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, switchToMain } from '../helpers/app.js';

const CONNECT_TIMEOUT = 6_000;

describe.skip('connect window → presets', () => {
  beforeEach(async () => { await resetAppState(); });

  afterEach(async () => {
    await switchToMain().catch(() => {});
    await browser.execute(async () => {
      const internals = (window as never as { __TAURI_INTERNALS__?: Record<string, unknown> }).__TAURI_INTERNALS__;
      const invoke = (internals?.__originalInvoke ?? internals?.invoke) as
        | ((cmd: string, args?: unknown) => Promise<unknown>) | undefined;
      if (invoke) await invoke('diag_close_windows_by_prefix', { prefix: 'connect' });
    }).catch(() => {});
  });

  /** Open the Connect window (label "connect") and switch into it. */
  async function openConnect(provider?: string): Promise<void> {
    await switchToMain();
    await browser.execute(async (p: string | null) => {
      const internals = (window as never as { __TAURI_INTERNALS__?: Record<string, unknown> }).__TAURI_INTERNALS__;
      const invoke = (internals?.__originalInvoke ?? internals?.invoke) as
        (cmd: string, args?: unknown) => Promise<unknown>;
      await invoke('open_connect_window', { parentLabel: 'main', provider: p });
    }, provider ?? null);

    const deadline = Date.now() + CONNECT_TIMEOUT;
    while (Date.now() < deadline) {
      for (const h of await browser.getWindowHandles()) {
        try { await browser.switchToWindow(h); } catch { continue; }
        const isConnect = await browser
          .execute(() => !!document.querySelector('.connect-window'))
          .catch(() => false);
        if (isConnect) return;
      }
      await browser.pause(100);
    }
    throw new Error('connect window did not appear');
  }

  async function textsOf(selector: string): Promise<string[]> {
    const out: string[] = [];
    for await (const el of $$(selector)) out.push((await el.getText()).trim());
    return out;
  }

  const fieldInput = (label: string) =>
    $(`//label[contains(., "${label}")]/following-sibling::input`);

  it('[S-40] the picker lists every provider, protocols included', async () => {
    await openConnect();
    await $('.connect-provider-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    const names = await textsOf('.connect-provider-name');
    expect(names).to.include.members([
      'Microsoft',
      'IMAP',     // [S-38 email]
      'CalDAV',   // [S-36 calendar]
      'CardDAV',  // [S-36 contacts]
    ]);

    // [S-49] Each card lists what connecting it unlocks, in the same words the
    // Add-Widget picker uses — and nothing else: no marketing blurb.
    const microsoft = await $('.connect-provider-btn*=Microsoft');
    const uses: string[] = [];
    for await (const item of microsoft.$$('.connect-use')) uses.push((await item.getText()).trim());
    expect(uses).to.include.members(['Email', 'Calendar', 'Contacts']);
    expect(await (await $('.connect-provider-desc')).isExisting(), 'no blurb on the card').to.equal(false);
  });

  it('[S-49] protocol cards list their widgets the same way', async () => {
    await openConnect();
    await $('.connect-provider-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    // Protocols share the card component, so the treatment must match exactly.
    for (const [label, widget] of [['IMAP', 'Email'], ['CalDAV', 'Calendar'], ['CardDAV', 'Contacts']]) {
      const card = await $(`.connect-provider-btn*=${label}`);
      const items: string[] = [];
      for await (const item of card.$$('.connect-use')) items.push((await item.getText()).trim());
      expect(items, `${label} lists ${widget}`).to.deep.equal([widget]);
    }
  });

  it('[S-49] lays both groups out in three columns', async () => {
    await openConnect();
    await $('.connect-provider-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    for await (const grid of $$('.connect-provider-grid')) {
      const lefts = new Set<number>();
      for await (const card of grid.$$('.connect-provider-btn')) lefts.add((await card.getLocation()).x);
      expect(lefts.size, 'three distinct columns').to.equal(3);
    }
  });

  it('[S-50] has no in-app header — the OS title bar is the header', async () => {
    await openConnect();
    await $('.connect-body').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    for (const sel of ['.connect-head', '.connect-title', '.connect-close']) {
      expect(await (await $(sel)).isExisting(), `${sel} removed`).to.equal(false);
    }
    // The step heading moved to the real title bar instead of being dropped.
    expect(await browser.getTitle()).to.match(/add a connection/i);
  });

  it('[S-47] a protocol asks for the provider on its own page first', async () => {
    await openConnect('imap');
    await $('.connect-preset-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    // Page one asks one question and shows no credential fields at all.
    expect(await $$('.connect-panel input').length, 'no fields on page one').to.equal(0);

    const tiles = await textsOf('.connect-preset-tile-name');
    expect(tiles, 'top providers offered').to.include.members(['Gmail', 'iCloud Mail', 'Fastmail']);
    // Outlook.com is deliberately absent: Microsoft sign-in is the recommended
    // path, so an IMAP tile competing with it would be the worse option.
    expect(tiles.join(' '), 'no Outlook IMAP tile').to.not.contain('Outlook');
    expect(tiles, 'always an escape hatch').to.include('Enter it myself');
    expect(tiles.length, 'ten IMAP providers plus the manual tile').to.be.at.least(11);
  });

  it('[S-47] picking a provider advances to the form with its settings filled', async () => {
    await openConnect('imap');
    await $('.connect-preset-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    await (await $('.connect-preset-tile-name=Gmail')).click();

    await $('.connect-panel').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    expect(await (await $('.connect-preset-grid')).isExisting(), 'left the provider page').to.equal(false);
    expect(await (await $('.connect-chosen-name')).getText()).to.equal('Gmail');
    expect(await (await fieldInput('IMAP server')).getValue()).to.equal('imap.gmail.com');
    expect(await (await fieldInput('Port')).getValue()).to.equal('993');
    // Gmail rejects the account password, so the app-password requirement is
    // stated up front rather than after a failed sign-in.
    expect(await (await $('.connect-preset-note.app-password')).isDisplayed()).to.equal(true);
  });

  it('[S-47] Back returns to the provider page, not out of the flow', async () => {
    await openConnect('imap');
    await $('.connect-preset-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    await (await $('.connect-preset-tile-name=Gmail')).click();
    await $('.connect-panel').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    await (await $('.connect-back')).click();
    expect(await (await $('.connect-preset-grid')).isDisplayed(), 'one step back').to.equal(true);
  });

  it('[S-47] "Enter it myself" opens a blank form and stops auto-detection', async () => {
    await openConnect('imap');
    await $('.connect-preset-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    // Choose a preset first, so the manual path has stale values to clear —
    // the regression that made "enter it myself" land on Gmail's server.
    await (await $('.connect-preset-tile-name=Gmail')).click();
    await $('.connect-panel').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    await (await $('.connect-back')).click();
    await (await $('.connect-preset-tile-name=Enter it myself')).click();

    expect(await (await fieldInput('IMAP server')).getValue(), 'blank server').to.equal('');
    expect(await (await fieldInput('Port')).getValue(), 'port back to its default').to.equal('993');

    // Declining the list must survive typing an address the list would match.
    await (await fieldInput('Username or email')).setValue('someone@gmail.com');
    expect(await (await fieldInput('IMAP server')).getValue(), 'no auto-fill after opting out').to.equal('');
  });

  it('[S-49] protocol cards show the services they reach', async () => {
    await openConnect();
    await $('.connect-provider-grid').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    // "IMAP" means nothing to most people; the brand marks say what it is for.
    for (const label of ['IMAP', 'CalDAV', 'CardDAV']) {
      const card = await $(`.connect-provider-btn*=${label}`);
      expect(await card.$$('.connect-provider-brands .preset-logo').length,
        `${label} shows brand marks`).to.be.greaterThan(0);
    }
    // Providers without presets show no strip at all.
    const rss = await $('.connect-provider-btn*=RSS');
    expect(await (await rss.$('.connect-provider-brands')).isExisting()).to.equal(false);
  });

  it('[S-47] typing an address auto-selects its provider', async () => {
    await openConnect('imap');
    await $('.connect-preset-chips').waitForDisplayed({ timeout: CONNECT_TIMEOUT });

    await (await fieldInput('Username or email')).setValue('someone@gmail.com');

    const selected = await $('.connect-preset-chip.selected');
    expect(await selected.getText()).to.equal('Gmail');
    expect(await (await fieldInput('IMAP server')).getValue()).to.equal('imap.gmail.com');
  });

  it('[S-47] hand-editing the server field clears the preset', async () => {
    await openConnect('imap');
    await $('.connect-preset-chips').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    await (await $('.connect-preset-chip=Gmail')).click();

    await (await fieldInput('IMAP server')).setValue('mail.example.com');

    expect(await (await $('.connect-preset-chip.selected')).isExisting(),
      'preset released once the user takes over').to.equal(false);

    // Detection must not now overwrite what was just typed by hand.
    await (await fieldInput('Username or email')).setValue('someone@gmail.com');
    expect(await (await fieldInput('IMAP server')).getValue()).to.equal('mail.example.com');
  });

  it('[S-47] a self-hosted preset blocks Connect until its token is replaced', async () => {
    await openConnect('caldav');
    await $('.connect-preset-chips').waitForDisplayed({ timeout: CONNECT_TIMEOUT });
    await (await $('.connect-preset-chip=Nextcloud / ownCloud')).click();

    await (await fieldInput('Username or email')).setValue('me@example.com');
    await (await fieldInput('Password')).setValue('hunter2');

    const hint = await $('.connect-placeholder-hint');
    expect(await hint.isDisplayed(), 'says exactly what to replace').to.equal(true);
    expect(await hint.getText()).to.contain('{your-server}');

    const connectBtn = await $('.connect-footer .btn-primary');
    expect(await connectBtn.isEnabled(), 'blocked while a token remains').to.equal(false);

    await (await fieldInput('Server or CalDAV URL')).setValue('https://cloud.example.com/remote.php/dav');
    expect(await connectBtn.isEnabled(), 'enabled once the address is real').to.equal(true);
  });
});
