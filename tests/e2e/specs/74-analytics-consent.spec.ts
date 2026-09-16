/**
 * Settings → General → Data: "Share anonymous usage data" toggle.
 *
 * The switch IS the consent decision, and it calls the same command the
 * first-run prompt does ([S-03]):
 *   share ON  → invoke('analytics_set_consent', { granted: true })
 *   share OFF → invoke('analytics_set_consent', { granted: false })
 *
 * The analytics commands go through the typed `invoke` in `src/ipc.ts`. Tauri 2
 * freezes `__TAURI_INTERNALS__.invoke`, so we record via that module's webdriver
 * seam (`window.__ipcInvoke.setInvoke`) rather than the frozen internal. The
 * toggle's INITIAL state is driven by ConfigWindow's `analytics_consent` read on
 * mount, so `setBackendConsent(...)` normalizes the real backend before Settings
 * opens (this also heals a decision left on disk by a prior run).
 *
 * NOT covered here: the first-run consent card itself, which only appears while
 * the decision is still `unset`. A running app has always answered by the time a
 * spec can look, so that path is unit-tested in Rust ([S-26], [S-27]) and
 * verified manually.
 *
 * Spec: docs/ANALYTICS.md [S-03] (consent required) and the toggle wiring.
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, switchToMain,
} from '../helpers/app.js';

const LABEL = /share anonymous usage data|usage data/;

/**
 * Normalize the REAL backend consent before Settings opens, so the toggle's
 * initial state (read by ConfigWindow on mount) is deterministic. Runs on the
 * main window via the real Tauri invoke, never the recorder.
 */
async function setBackendConsent(granted: boolean): Promise<void> {
  await switchToMain();
  await browser.execute(async (v: boolean) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (invoke) await invoke('analytics_set_consent', { granted: v });
  }, granted);
}

/**
 * Record IPC via ipc.ts's webdriver seam in the CURRENT window (the config
 * window, where the Settings toggle lives). `analytics_set_consent` is recorded
 * but NOT forwarded, so toggling never mutates the on-disk decision; every other
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
      // Record only. The null is load-bearing twice over: it keeps the on-disk
    // decision untouched, and the real command returns a plan holding one
    // app_opt_out signal when consent is withdrawn, so null also keeps this
    // spec off the network.
    if (cmd === 'analytics_set_consent') return null;
      return real ? real(cmd, args) : null; // delegate everything else
    });
  });
}

async function readInvocations(): Promise<{ cmd: string; args: any }[]> {
  return browser.execute(() => (window as any).__invocations ?? []);
}

async function clickSwitchByLabel(labelRegex: RegExp): Promise<void> {
  const ok = await browser.execute((re: string) => {
    const pattern = new RegExp(re, 'i');
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (!pattern.test(lbl)) continue;
      const b = row.querySelector<HTMLButtonElement>('.design-switch');
      if (!b) return false;
      b.click();
      return true;
    }
    return false;
  }, labelRegex.source);
  if (!ok) throw new Error(`no .design-switch for row ${labelRegex}`);
}

async function switchClassFor(labelRegex: RegExp): Promise<string> {
  return browser.execute((re: string) => {
    const pattern = new RegExp(re, 'i');
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (!pattern.test(lbl)) continue;
      const b = row.querySelector<HTMLButtonElement>('.design-switch');
      return b?.className ?? '';
    }
    return '';
  }, labelRegex.source);
}

async function isOn(): Promise<boolean> {
  return (await switchClassFor(LABEL)).includes(' on');
}

describe('analytics consent toggle', () => {
  beforeEach(async () => {
    await resetAppState();
    await setBackendConsent(true);    // consent granted → share ON (normalizes disk)
    await openSettings();
    await (await settingsTab('General')).click();
    await installInvokeRecorder();    // record + stub set in the config window
  });

  afterEach(async () => { await closeSettings(); });

  it('shows ON while consent is granted', async () => {
    await browser.waitUntil(isOn, { timeout: 3_000 });
    expect(await isOn()).to.equal(true);
  });

  // [S-03] A refusal must read as OFF when Settings is reopened: the toggle
  // renders the stored decision, never an optimistic default.
  it('shows OFF after consent was refused', async () => {
    await closeSettings();
    await setBackendConsent(false);
    await openSettings();
    await (await settingsTab('General')).click();
    await browser.waitUntil(async () => !(await isOn()), { timeout: 3_000 });
    expect(await isOn()).to.equal(false);
    await installInvokeRecorder();  // afterEach closes Settings; keep the seam
  });

  it('turning it OFF calls analytics_set_consent with granted=false', async () => {
    await browser.waitUntil(isOn, { timeout: 3_000 });
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickSwitchByLabel(LABEL);

    await browser.waitUntil(async () => !(await isOn()), { timeout: 2_000 });

    const calls = await readInvocations();
    const setCall = calls.find(c => c.cmd === 'analytics_set_consent');
    expect(setCall, 'analytics_set_consent was invoked').to.exist;
    expect(setCall!.args?.granted).to.equal(false);
  });

  it('turning it back ON calls analytics_set_consent with granted=true', async () => {
    await browser.waitUntil(isOn, { timeout: 3_000 });
    // off first
    await clickSwitchByLabel(LABEL);
    await browser.waitUntil(async () => !(await isOn()), { timeout: 2_000 });
    await browser.execute(() => { (window as any).__invocations = []; });

    // back on
    await clickSwitchByLabel(LABEL);
    await browser.waitUntil(isOn, { timeout: 2_000 });

    const calls = await readInvocations();
    const setCall = calls.find(c => c.cmd === 'analytics_set_consent');
    expect(setCall, 'analytics_set_consent was invoked').to.exist;
    expect(setCall!.args?.granted).to.equal(true);
  });
});
