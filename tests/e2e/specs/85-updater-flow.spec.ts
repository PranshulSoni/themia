/**
 * Settings → Updates: the check/install flow.
 *
 * The app owns `updater_check` / `updater_install` rather than calling
 * tauri-plugin-updater's own IPC commands, so the update requests traverse the
 * Windows proxy (netproxy SPEC [PX-09]). A side effect worth having: both
 * commands now go through the typed `invoke` in `src/ipc.ts`, so they are
 * reachable from the webdriver seam - the plugin's JS API was not, which is why
 * the sibling autostart spec is skipped.
 *
 * Everything here is stubbed at that seam: the check never touches the network
 * and the install is recorded but NEVER forwarded, so running this spec cannot
 * launch a real installer.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, switchToMain, LS_KEYS,
} from '../helpers/app.js';

const STUB_VERSION = '99.99.99';

/**
 * Turn auto-check OFF before Settings mounts. ConfigWindow fires a check on
 * mount when the pref is on, which would race the recorder (and reach the
 * network) before the spec has said what the answer should be.
 */
async function disableAutoCheck(): Promise<void> {
  await switchToMain();
  await browser.execute((k: string) => localStorage.setItem(k, 'false'), LS_KEYS.autoUpdates);
}

/**
 * Record IPC via ipc.ts's webdriver seam in the CURRENT window (the config
 * window, where the Updates page lives). Both updater commands are answered
 * here and never delegated; every other command goes to the real backend.
 */
async function installUpdaterStub(version: string): Promise<void> {
  await browser.execute((v: string) => {
    const w = window as any;
    w.__invocations = [] as { cmd: string; args: any }[];

    const seam = w.__ipcInvoke;
    if (!seam?.setInvoke) {
      throw new Error('__ipcInvoke.setInvoke not exposed: ensure navigator.webdriver and a fresh debug build');
    }
    const real = w.__TAURI_INTERNALS__?.invoke;

    seam.setInvoke(async (cmd: string, args: any) => {
      w.__invocations.push({ cmd, args });
      if (cmd === 'updater_check') return { version: v };
      // Recorded, never forwarded: the real command downloads and runs an
      // installer, which would end the test session along with the app.
      if (cmd === 'updater_install') return null;
      return real ? real(cmd, args) : null;
    });
  }, version);
}

async function invoked(): Promise<string[]> {
  return browser.execute(() => ((window as any).__invocations ?? []).map((i: any) => i.cmd));
}

async function statusText(): Promise<string> {
  return browser.execute(() => document.querySelector('.update-status')?.textContent?.trim() ?? '');
}

/** Click the button in `.update-action` (either "Check for updates" or "Install"). */
async function clickUpdateAction(): Promise<void> {
  const ok = await browser.execute(() => {
    const b = document.querySelector<HTMLButtonElement>('.update-action button');
    if (!b) return false;
    b.click();
    return true;
  });
  if (!ok) throw new Error('no button in .update-action');
}

describe('[PX-09] updater check/install flow', () => {
  beforeEach(async () => {
    await resetAppState();
    await disableAutoCheck();
    await openSettings('Updates');
    await $('.update-row').waitForDisplayed({ timeout: 5_000 });
    await installUpdaterStub(STUB_VERSION);
  });

  afterEach(async () => { await closeSettings(); });

  it('"Check for updates" calls updater_check and shows the offered version', async () => {
    await clickUpdateAction();

    await browser.waitUntil(async () => (await statusText()).includes(STUB_VERSION), {
      timeout: 5_000,
      timeoutMsg: `update status never showed v${STUB_VERSION}`,
    });

    expect(await invoked()).to.include('updater_check');
  });

  it('the install button calls updater_install, not the plugin', async () => {
    await clickUpdateAction();                       // check → update offered
    await browser.waitUntil(async () => (await statusText()).includes(STUB_VERSION), { timeout: 5_000 });

    await clickUpdateAction();                       // now the install button

    await browser.waitUntil(async () => (await invoked()).includes('updater_install'), {
      timeout: 5_000,
      timeoutMsg: 'install button never invoked updater_install',
    });
  });
});
