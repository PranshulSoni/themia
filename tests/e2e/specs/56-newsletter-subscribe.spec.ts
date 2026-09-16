/**
 * Newsletter subscription row: lives on the Updates settings page.
 *
 * The real backend POSTs to Lemon Squeezy `/v1/customers` from Rust. We stub
 * the Tauri invoke so the test stays offline. Behaviour exercised:
 *
 *  1. Row visible on Updates tab; Subscribe button disabled until '@' typed.
 *  2. Successful subscribe flips to "Subscribed" state with the email shown.
 *  3. Subscribed state survives a reload (localStorage hydration).
 *  4. Error path: stubbed reject surfaces the error inline.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings,
  stubTauriInvoke, switchToConfigWindow,
} from '../helpers/app.js';

/** Stub the wrapped `invoke` used by src/license.ts inside the config
 *  window. Tauri 2 makes `__TAURI_INTERNALS__.invoke` non-writable, so
 *  patching `internals.invoke` silently fails; license.ts exposes a
 *  `window.__licenseInvoke.setInvoke` hook for tests instead. */
async function stubInConfigWindow(mappings: Record<string, any>): Promise<void> {
  await switchToConfigWindow('app');
  await browser.execute((m: Record<string, any>) => {
    const hook = (window as any).__licenseInvoke;
    if (!hook?.setInvoke) throw new Error('__licenseInvoke.setInvoke not exposed: ensure webdriver flag and rebuild');
    hook.setInvoke(async (cmd: string, _args: any) => {
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      if (Object.prototype.hasOwnProperty.call(m, '*')) return m['*'];
      return null;
    });
  }, mappings);
}

async function findBtnByText(text: string): Promise<boolean> {
  return browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    return btns.some(b => (b.textContent ?? '').trim().includes(t));
  }, text);
}

async function clickBtnByText(text: string): Promise<void> {
  const ok = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    for (const b of btns) {
      if ((b.textContent ?? '').trim().includes(t)) { b.click(); return true; }
    }
    return false;
  }, text);
  if (!ok) throw new Error(`button matching "${text}" not found`);
}

async function newsletterRowExists(): Promise<boolean> {
  return (await $$('.config-panel .newsletter-row').length) > 0;
}

async function newsletterShowsSubscribed(): Promise<boolean> {
  return browser.execute(() => !!document.querySelector('.config-panel .newsletter-ok'));
}

describe('newsletter subscribe', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  it('row renders on Updates tab and Subscribe is gated on @-containing email', async () => {
    await openSettings('Updates');
    expect(await newsletterRowExists()).to.equal(true);

    const input = await $('.config-panel .newsletter-input');
    await input.waitForExist({ timeout: 3_000 });

    // Empty → button disabled.
    const subBtnDisabledEmpty = await browser.execute(() => {
      const b = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel .newsletter-activate button'));
      return b[0]?.disabled === true;
    });
    expect(subBtnDisabledEmpty).to.equal(true);

    // No-@ → still disabled.
    await input.setValue('notanemail');
    const noAt = await browser.execute(() => {
      const b = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel .newsletter-activate button'));
      return b[0]?.disabled === true;
    });
    expect(noAt).to.equal(true);

    // Valid email → enabled.
    await input.setValue('user@example.com');
    const enabled = await browser.execute(() => {
      const b = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel .newsletter-activate button'));
      return b[0]?.disabled === false;
    });
    expect(enabled).to.equal(true);

    await closeSettings();
  });

  it('successful subscribe flips row to "Subscribed" state', async () => {
    await stubTauriInvoke({ newsletter_subscribe: null, '*': null });

    await openSettings('Updates');
    // The Subscribe button fires invoke('newsletter_subscribe') from the
    // config window, so also stub there (main-window stub doesn't propagate).
    await stubInConfigWindow({ newsletter_subscribe: null, '*': null });
    expect(await newsletterRowExists()).to.equal(true);

    const input = await $('.config-panel .newsletter-input');
    await input.setValue('user@example.com');
    await clickBtnByText('Subscribe');

    await browser.waitUntil(() => newsletterShowsSubscribed(), { timeout: 3_000 });
    expect(await findBtnByText('Change email')).to.equal(true);

    await closeSettings();
  });

  it('subscribed state persists across reload via localStorage', async () => {
    await stubTauriInvoke({ newsletter_subscribe: null, '*': null });

    await openSettings('Updates');
    await stubInConfigWindow({ newsletter_subscribe: null, '*': null });
    const input = await $('.config-panel .newsletter-input');
    await input.setValue('persist@example.com');
    await clickBtnByText('Subscribe');
    await browser.waitUntil(() => newsletterShowsSubscribed(), { timeout: 3_000 });
    await closeSettings();

    // Reload main window; newsletter state should rehydrate from localStorage.
    await browser.execute(() => location.reload());
    await browser.pause(400);
    await openSettings('Updates');
    expect(await newsletterShowsSubscribed()).to.equal(true);
    await closeSettings();
  });

  it('rejected invoke surfaces an inline error', async () => {
    await openSettings('Updates');
    // Stub the wrapped license-invoke inside the config window so the
    // click handler's invoke('newsletter_subscribe') throws.
    await switchToConfigWindow('app');
    await browser.execute(() => {
      const hook = (window as any).__licenseInvoke;
      if (!hook?.setInvoke) throw new Error('__licenseInvoke.setInvoke not exposed');
      hook.setInvoke(async (cmd: string, _args: any) => {
        if (cmd === 'newsletter_subscribe') throw 'Network down';
        return null;
      });
    });
    const input = await $('.config-panel .newsletter-input');
    await input.setValue('err@example.com');
    await clickBtnByText('Subscribe');

    await browser.waitUntil(async () => {
      return !!(await browser.execute(() => document.querySelector('.config-panel .newsletter-error')));
    }, { timeout: 3_000 });

    const errText = await browser.execute(() => {
      return (document.querySelector('.config-panel .newsletter-error')?.textContent ?? '').trim();
    });
    expect(errText).to.match(/network down/i);

    await closeSettings();
  });
});
