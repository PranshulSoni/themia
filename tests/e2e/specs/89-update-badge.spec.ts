/**
 * [S-35] The desktop "update available" badge.
 *
 * A check that happens in the CONFIG window has to reach the widget windows,
 * otherwise the desktop keeps claiming nothing is available until the next app
 * start. That hop is a real `update-available` BroadcastChannel message, so the
 * whole path is exercised here: stub the check in Settings, press "Check for
 * updates", then switch to the main window and look at the corner.
 *
 * The check is stubbed at ipc.ts's webdriver seam (same as the sibling updater
 * spec), so nothing touches the network and `updater_install` is never reached
 * — the badge deliberately opens Settings rather than installing.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, switchToMain, waitForReady, LS_KEYS,
} from '../helpers/app.js';

const STUB_VERSION = '99.99.99';
const DISMISS_KEY = 'themia-update-dismissed';
const BADGE_SEL = '.update-badge';

/** Answer `updater_check` in the CURRENT window; delegate everything else. */
async function installUpdaterStub(version: string): Promise<void> {
  await browser.execute((v: string) => {
    const w = window as any;
    const seam = w.__ipcInvoke;
    if (!seam?.setInvoke) {
      throw new Error('__ipcInvoke.setInvoke not exposed: ensure navigator.webdriver and a fresh debug build');
    }
    const real = w.__TAURI_INTERNALS__?.invoke;
    seam.setInvoke(async (cmd: string, args: any) => {
      if (cmd === 'updater_check') return { version: v };
      // Never forwarded: it downloads and runs an installer, ending the session.
      if (cmd === 'updater_install') return null;
      return real ? real(cmd, args) : null;
    });
  }, version);
}

/** Click the button in `.update-action` ("Check for updates" here). */
async function clickUpdateAction(): Promise<void> {
  const ok = await browser.execute(() => {
    const b = document.querySelector<HTMLButtonElement>('.update-action button');
    if (!b) return false;
    b.click();
    return true;
  });
  if (!ok) throw new Error('no button in .update-action');
}

/** Run a check in Settings that reports `STUB_VERSION`, then return to the desktop. */
async function findUpdateFromSettings(): Promise<void> {
  await switchToMain();
  await openSettings('Updates');
  await $('.update-row').waitForDisplayed({ timeout: 5_000 });
  await installUpdaterStub(STUB_VERSION);
  await clickUpdateAction();
  await browser.waitUntil(
    async () => browser.execute(
      () => document.querySelector('.update-status')?.textContent?.includes('99.99.99') ?? false,
    ),
    { timeout: 5_000, timeoutMsg: 'Settings never showed the offered version' },
  );
  await closeSettings();
  await switchToMain();
}

describe('[S-35] desktop update badge', () => {
  beforeEach(async () => {
    await resetAppState();
    await switchToMain();
    // Turn the auto-check off and reload BEFORE asserting anything: App fires a
    // real `updater_check` on mount, and a genuine hit would light the badge
    // out from under the test. After this reload the only check is the stubbed
    // one the test drives from Settings.
    await browser.execute((k: string, dk: string) => {
      localStorage.setItem(k, 'false');
      localStorage.removeItem(dk);
      location.reload();
    }, LS_KEYS.autoUpdates, DISMISS_KEY);
    await browser.pause(300);
    await switchToMain();
    await waitForReady();
  });

  afterEach(async () => { await closeSettings(); });

  it('a check made in Settings lights the badge on the desktop', async () => {
    expect(await $(BADGE_SEL).isExisting()).to.equal(false);

    await findUpdateFromSettings();

    await $(BADGE_SEL).waitForDisplayed({
      timeout: 5_000,
      timeoutMsg: 'the corner badge never appeared after Settings found an update',
    });
    expect(await $(BADGE_SEL).getText()).to.include(STUB_VERSION);
  });

  it('the badge sits in the bottom-right corner, above the other badges', async () => {
    await findUpdateFromSettings();
    await $(BADGE_SEL).waitForDisplayed({ timeout: 5_000 });

    const geom = await browser.execute(() => {
      const el = document.querySelector('.corner-badges .update-badge');
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { right: innerWidth - r.right, bottom: innerHeight - r.bottom, w: r.width, h: r.height };
    });
    expect(geom, 'badge is not inside .corner-badges').to.not.equal(null);
    expect(geom!.w).to.be.greaterThan(0);
    expect(geom!.right).to.be.lessThan(40);   // hugs the right edge
    expect(geom!.bottom).to.be.lessThan(80);  // hugs the bottom edge (may stack)
  });

  it('joins the pass-through mask so it stays clickable', async () => {
    await findUpdateFromSettings();
    await $(BADGE_SEL).waitForDisplayed({ timeout: 5_000 });
    // [S-17]: chrome only survives SetWindowRgn clipping when it is tagged.
    expect(await $(BADGE_SEL).getAttribute('data-themia-interactive')).to.equal('true');
  });

  it('dismissing hides it and remembers that exact version', async () => {
    await findUpdateFromSettings();
    await $(BADGE_SEL).waitForDisplayed({ timeout: 5_000 });

    await browser.execute(() => {
      document.querySelector<HTMLButtonElement>('.update-badge-dismiss')?.click();
    });

    await $(BADGE_SEL).waitForExist({
      timeout: 5_000, reverse: true,
      timeoutMsg: 'the badge survived its own dismiss button',
    });
    const stored = await browser.execute((k: string) => localStorage.getItem(k), DISMISS_KEY);
    expect(stored).to.equal(STUB_VERSION);
  });

  it('a dismissed version does not come back after a reload', async () => {
    await findUpdateFromSettings();
    await $(BADGE_SEL).waitForDisplayed({ timeout: 5_000 });
    await browser.execute(() => {
      document.querySelector<HTMLButtonElement>('.update-badge-dismiss')?.click();
    });
    await $(BADGE_SEL).waitForExist({ timeout: 5_000, reverse: true });

    await browser.refresh();
    await switchToMain();
    await waitForReady();
    await findUpdateFromSettings();

    // Same version, already dismissed: the corner stays quiet.
    await browser.pause(500);
    expect(await $(BADGE_SEL).isExisting()).to.equal(false);
  });
});
