/**
 * Settings → General → Application: "Quit Themia" button.
 *
 * The widget windows are borderless, skip the taskbar, and have no close
 * button, so the only in-app way to shut Themia down is this Settings button,
 * which invokes the `quit_app` command (AppHandle::exit under the hood). We
 * never let the real command run here (it would tear down the whole process,
 * including the WebDriver session); instead the recorder below stubs
 * `quit_app` and we assert the click reached it.
 *
 * The confirm step mirrors Reset: under WebDriver a `window.__settingsHooks
 * .askQuit` setter pre-decides accept/reject so no real dialog blocks the test.
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
} from '../helpers/app.js';

/**
 * Install an invoke recorder in the CURRENT window (must be the config window;
 * the Settings UI and its `quit_app` call live there, not on the main window).
 *
 * Tauri 2 freezes `__TAURI_INTERNALS__.invoke`, and Settings calls through the
 * typed `invoke` in `src/ipc.ts`, so we record via that module's webdriver seam
 * (`window.__ipcInvoke.setInvoke`), not by reassigning the frozen internal.
 * `quit_app` is stubbed to a no-op so the process survives the click; every other
 * command is delegated to the real Tauri invoke.
 */
async function installInvokeRecorder(): Promise<void> {
  await browser.execute(() => {
    const w = window as any;
    w.__invocations = [] as { cmd: string; args: any }[];

    const seam = w.__ipcInvoke;
    if (!seam?.setInvoke) {
      throw new Error('__ipcInvoke.setInvoke not exposed: ensure navigator.webdriver and a fresh debug build');
    }
    const real = w.__TAURI_INTERNALS__?.invoke;

    seam.setInvoke(async (cmd: string, args: any) => {
      w.__invocations.push({ cmd, args });
      if (cmd === 'quit_app') return null; // swallow: never actually exit
      return real ? real(cmd, args) : null; // delegate everything else
    });
  });
}

/** Pre-decide the quit confirmation dialog via the test hook. */
async function setAskQuit(answer: boolean): Promise<void> {
  await browser.execute((a: boolean) => {
    const w = window as any;
    w.__settingsHooks = { ...(w.__settingsHooks ?? {}), askQuit: async () => a };
  }, answer);
}

async function readInvocations(): Promise<string[]> {
  return browser.execute(() =>
    ((window as any).__invocations ?? []).map((i: { cmd: string }) => i.cmd),
  );
}

/** Click the General-page button whose trimmed text matches `label`. */
async function clickButtonByText(label: string): Promise<void> {
  const ok = await browser.execute((t: string) => {
    const btns = Array.from(document.querySelectorAll<HTMLButtonElement>('.config-panel button'));
    for (const b of btns) {
      if ((b.textContent ?? '').trim() === t) { b.click(); return true; }
    }
    return false;
  }, label);
  if (!ok) throw new Error(`button "${label}" not found in config panel`);
}

describe('Quit Themia button', () => {
  beforeEach(async () => {
    await resetAppState();
    await openSettings('General');           // switches into the config window
    await (await settingsTab('General')).click();
    await installInvokeRecorder();            // recorder lives in the config window
  });

  afterEach(async () => { await closeSettings(); });

  it('confirming the dialog invokes quit_app', async () => {
    await setAskQuit(true);
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickButtonByText('Quit');

    await browser.waitUntil(async () => (await readInvocations()).includes('quit_app'),
      { timeout: 2_000, timeoutMsg: 'expected quit_app to be invoked after confirming' });
  });

  it('cancelling the dialog does NOT invoke quit_app', async () => {
    await setAskQuit(false);
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickButtonByText('Quit');

    // Give any async handler a beat, then assert quit_app never fired.
    await browser.pause(300);
    const calls = await readInvocations();
    expect(calls).to.not.include('quit_app');
  });
});
