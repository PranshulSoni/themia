/**
 * Settings → General: Autostart + Auto-check-updates toggles.
 *
 * Autostart flips call `invoke('autostart_enable'|'autostart_disable')` and
 * the store is seeded from `invoke('autostart_is_enabled')` at mount. We
 * record every invoke name in a `window.__invocations` array so we can
 * assert *which* Tauri commands the click triggered (since tauri-driver
 * normally forwards to the real Rust implementation).
 *
 * Auto-check-updates purely writes `localStorage.themia-auto-check-updates`,
 * no invoke involved. We assert both storage and visual state survive
 * reload, and that each toggle is independent of the other.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, waitForReady, LS_KEYS,
} from '../helpers/app.js';

/**
 * Install an invoke recorder + minimal stub. Must run BEFORE the Settings
 * modal mounts autostart state. We also return sensible defaults for the
 * widget-mount-time commands so the app doesn't blow up.
 */
async function installInvokeRecorder(initiallyEnabled: boolean): Promise<void> {
  await browser.execute((startEnabled: boolean) => {
    const w = window as any;
    w.__invocations = [] as { cmd: string; args: any }[];
    // Track the current autostart state so toggling via the stub is realistic.
    w.__autostartEnabled = startEnabled;

    const internals = w.__TAURI_INTERNALS__ ?? (w.__TAURI_INTERNALS__ = {});
    const original = internals.__originalInvoke ?? internals.invoke;
    if (!internals.__originalInvoke) internals.__originalInvoke = original;

    internals.invoke = async (cmd: string, args: any) => {
      w.__invocations.push({ cmd, args });
      if (cmd === 'autostart_is_enabled') return w.__autostartEnabled;
      if (cmd === 'autostart_enable')  { w.__autostartEnabled = true;  return null; }
      if (cmd === 'autostart_disable') { w.__autostartEnabled = false; return null; }
      if (original) return original(cmd, args);
      throw new Error(`invoke stub miss: "${cmd}"`);
    };
  }, initiallyEnabled);
}

async function readInvocations(): Promise<string[]> {
  return browser.execute(() => {
    const arr = (window as any).__invocations as { cmd: string }[] | undefined;
    return (arr ?? []).map(i => i.cmd);
  });
}

async function autostartBtn() {
  // The Autostart switch is the first `.design-switch` in the General tab
  // (row labelled "Launch on startup").
  return browser.execute(() => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-modal .design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (/launch on startup|autostart/i.test(lbl)) {
        const b = row.querySelector<HTMLButtonElement>('.design-switch');
        return b ? b.className : null;
      }
    }
    return null;
  });
}

async function clickSwitchByLabel(labelRegex: RegExp): Promise<void> {
  const ok = await browser.execute((re: string) => {
    const pattern = new RegExp(re, 'i');
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-modal .design-row'));
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
  const cls = await browser.execute((re: string) => {
    const pattern = new RegExp(re, 'i');
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-modal .design-row'));
    for (const row of rows) {
      const lbl = row.querySelector('.design-label')?.textContent ?? '';
      if (!pattern.test(lbl)) continue;
      const b = row.querySelector<HTMLButtonElement>('.design-switch');
      return b?.className ?? '';
    }
    return '';
  }, labelRegex.source);
  return cls as string;
}

/**
 * The `autostart` signal is seeded by `invoke('autostart_is_enabled')` during
 * App's onMount, which runs BEFORE our per-test recorder is installed (the
 * beforeEach refresh happens inside resetAppState). That means the baseline
 * UI state reflects the REAL Rust value, not the stub's `startEnabled`.
 *
 * To get a deterministic starting state, we simply click the switch (through
 * the stub) until the UI matches `desiredOn`, then clear the invocations
 * buffer so subsequent assertions only see the click-under-test.
 */
async function ensureSwitch(labelRegex: RegExp, desiredOn: boolean): Promise<void> {
  const isOn = async () => (await switchClassFor(labelRegex)).includes(' on');
  if ((await isOn()) !== desiredOn) {
    await clickSwitchByLabel(labelRegex);
    await browser.waitUntil(async () => (await isOn()) === desiredOn, { timeout: 3_000 });
  }
}

// Skipped: autostart uses @tauri-apps/plugin-autostart which bypasses our invoke stub.
describe.skip('autostart + update-pref toggles', () => {
  beforeEach(async () => {
    await resetAppState();
    // Install recorder BEFORE opening Settings so autostart_is_enabled is stubbed.
    await installInvokeRecorder(false);
    await openSettings();
    await (await settingsTab('General')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('autostart toggle calls autostart_enable and reflects UI state', async () => {
    // Normalize initial state to OFF via the stubbed click, then clear the
    // invocation recorder so we only observe the click-under-test below.
    await ensureSwitch(/launch on startup|autostart/, false);
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickSwitchByLabel(/launch on startup|autostart/);

    // UI flips on.
    await browser.waitUntil(async () =>
      (await switchClassFor(/launch on startup|autostart/)).includes(' on'),
      { timeout: 2_000 });

    // Invoke recorded.
    const calls = await readInvocations();
    expect(calls).to.include('autostart_enable');
    expect(calls).to.not.include('autostart_disable');
  });

  it('autostart toggle from on → off calls autostart_disable', async () => {
    // Normalize to ON via the stub, then clear invocations and click once.
    await ensureSwitch(/launch on startup|autostart/, true);
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickSwitchByLabel(/launch on startup|autostart/);

    await browser.waitUntil(async () =>
      !(await switchClassFor(/launch on startup|autostart/)).includes(' on'),
      { timeout: 2_000 });

    const calls = await readInvocations();
    expect(calls).to.include('autostart_disable');
  });

  it('auto-check-updates toggle writes localStorage and persists through reload', async () => {
    // Default: unset → signal reads as `!== 'false'` i.e. true → switch has `.on`.
    await browser.waitUntil(async () => {
      const c = await switchClassFor(/auto-check updates|check updates/);
      return c.includes(' on');
    }, { timeout: 2_000 });

    // Click it off.
    await clickSwitchByLabel(/auto-check updates|check updates/);

    await browser.waitUntil(async () => {
      const v = await browser.execute((k: string) => localStorage.getItem(k), LS_KEYS.autoUpdates);
      return v === 'false';
    }, { timeout: 2_000 });

    // Close settings, reload, reopen: state stays off.
    await closeSettings();
    await browser.refresh();
    await waitForReady();

    const after = await browser.execute((k: string) => localStorage.getItem(k), LS_KEYS.autoUpdates);
    expect(after).to.equal('false');
  });

  it('auto-check-updates toggle does NOT invoke any autostart commands', async () => {
    // Clear the recorder, flip only the updates switch, assert no autostart calls.
    await browser.execute(() => { (window as any).__invocations = []; });

    await clickSwitchByLabel(/auto-check updates|check updates/);

    await browser.waitUntil(async () => {
      const v = await browser.execute((k: string) => localStorage.getItem(k), LS_KEYS.autoUpdates);
      return v === 'false';
    }, { timeout: 2_000 });

    const calls = await readInvocations();
    expect(calls.filter(c => c.startsWith('autostart'))).to.have.lengthOf(0);
  });

  it('autostart toggle does NOT touch the auto-updates storage key', async () => {
    // Normalize to OFF so the click triggers autostart_enable.
    await ensureSwitch(/launch on startup|autostart/, false);
    await browser.execute(() => { (window as any).__invocations = []; });

    // Remove the key so we can detect any writes.
    await browser.execute((k: string) => localStorage.removeItem(k), LS_KEYS.autoUpdates);

    await clickSwitchByLabel(/launch on startup|autostart/);

    await browser.waitUntil(async () => {
      const calls = await readInvocations();
      return calls.includes('autostart_enable');
    }, { timeout: 2_000 });

    const v = await browser.execute((k: string) => localStorage.getItem(k), LS_KEYS.autoUpdates);
    expect(v).to.equal(null);
  });
});

/**
 * [AS-05] / #69 + #79: a refused "Launch on startup" flip must say what to DO.
 *
 * Two users reported the same wall: the toggle would not stay on and the app
 * offered nothing actionable, so it read as a dead control. Both were unblocked
 * by relaunching Themia as administrator — advice the app never gave.
 *
 * Stubs go through `window.__ipcInvoke` (Tauri 2 makes
 * `__TAURI_INTERNALS__.invoke` non-writable, see the seam's comment in ipc.ts),
 * and reject BOTH directions so the test doesn't depend on the machine's real
 * autostart state: whichever way the switch was pointing, the flip fails and
 * must leave it exactly where it was.
 */
describe('[AS-05] a refused autostart flip explains the remedy', () => {
  const AUTOSTART_ROW = /launch on startup|autostart/;

  /** Reject autostart_enable AND autostart_disable with a typed payload. */
  async function rejectAutostart(payload: { kind: string; message: string }): Promise<void> {
    await browser.execute((p: { kind: string; message: string }) => {
      const hook = (window as any).__ipcInvoke;
      if (!hook?.setInvoke) throw new Error('__ipcInvoke.setInvoke missing (navigator.webdriver false?)');
      const raw = (window as any).__TAURI_INTERNALS__?.invoke;
      hook.setInvoke(async (cmd: string, args: any) => {
        if (cmd === 'autostart_enable' || cmd === 'autostart_disable') return Promise.reject(p);
        return raw ? raw(cmd, args) : null;
      });
    }, payload);
  }

  async function restoreInvoke(): Promise<void> {
    await browser.execute(() => { (window as any).__ipcInvoke?.setInvoke(null); });
  }

  async function calloutText(): Promise<string> {
    return browser.execute(() => {
      const el = document.querySelector<HTMLElement>('.settings-content.cfg .cfg-callout');
      return el ? (el.innerText || el.textContent || '') : '';
    });
  }

  /** Class of the autostart switch, used to prove the toggle didn't move. */
  async function autostartSwitchClass(): Promise<string> {
    return browser.execute((re: string) => {
      const pattern = new RegExp(re, 'i');
      for (const row of Array.from(document.querySelectorAll<HTMLElement>('.settings-content.cfg .design-row'))) {
        if (!pattern.test(row.querySelector('.design-label')?.textContent ?? '')) continue;
        return row.querySelector<HTMLButtonElement>('.design-switch')?.className ?? '';
      }
      return '';
    }, AUTOSTART_ROW.source);
  }

  async function clickAutostartSwitch(): Promise<void> {
    const ok = await browser.execute((re: string) => {
      const pattern = new RegExp(re, 'i');
      for (const row of Array.from(document.querySelectorAll<HTMLElement>('.settings-content.cfg .design-row'))) {
        if (!pattern.test(row.querySelector('.design-label')?.textContent ?? '')) continue;
        const b = row.querySelector<HTMLButtonElement>('.design-switch');
        if (!b) return false;
        b.click();
        return true;
      }
      return false;
    }, AUTOSTART_ROW.source);
    if (!ok) throw new Error('autostart switch not found in the General page');
  }

  beforeEach(async () => {
    await resetAppState();
    await openSettings('General');
  });

  afterEach(async () => {
    await restoreInvoke().catch(() => { /* ignore: window may already be gone */ });
    await closeSettings();
  });

  it('AS-05: a non-elevated failure names administrator rights and leaves the toggle alone', async () => {
    await rejectAutostart({
      kind: 'elevationRequired',
      message: 'schtasks /Create failed (exit code 1): ERROR: Access is denied.',
    });
    const before = await autostartSwitchClass();

    await clickAutostartSwitch();

    await browser.waitUntil(async () => (await calloutText()).length > 0, {
      timeout: 3_000,
      timeoutMsg: 'no inline warning appeared for a failed autostart flip',
    });
    expect((await calloutText()).toLowerCase()).to.contain('administrator');
    // The setting was NOT applied, so the switch must not claim it was.
    expect(await autostartSwitchClass()).to.equal(before);
  });

  it('AS-05: an already-elevated failure still offers administrator, plus the raw reason', async () => {
    await rejectAutostart({
      kind: 'other',
      message: 'schtasks /Create failed (exit code 1): ERROR: The service cannot be started.',
    });
    const before = await autostartSwitchClass();

    await clickAutostartSwitch();

    await browser.waitUntil(async () => (await calloutText()).length > 0, {
      timeout: 3_000,
      timeoutMsg: 'no inline warning appeared for a failed autostart flip',
    });
    const text = await calloutText();
    // The remedy that actually unblocked #69/#79 is offered on every failure...
    expect(text.toLowerCase()).to.contain('administrator');
    // ...and the raw diagnostic stays available for a bug report.
    expect(text).to.contain('The service cannot be started');
    expect(await autostartSwitchClass()).to.equal(before);
  });

  /** Force SettingsBody to re-run its `onMount` probe WITHOUT reloading the
   *  page. ConfigWindow renders the pages through a `<Switch>` on the target
   *  kind, so navigating onto another kind unmounts it and navigating back
   *  mounts it afresh. Closing and reopening the window would work too — except
   *  that it boots a new webview, which takes `__ipcInvoke` and its stub with
   *  it, which is exactly why this test used to observe the real probe. */
  async function remountSettingsGeneral(): Promise<void> {
    const goto = async (target: string) => {
      await browser.execute(async (t: string) => {
        const internals = (window as any).__TAURI_INTERNALS__;
        const invoke = internals?.__originalInvoke ?? internals?.invoke;
        // The config window is a singleton: this emits `config-navigate` at the
        // window we are already in rather than spawning a second one.
        await invoke('open_config_window', { parentLabel: 'config', target: t });
      }, target);
    };
    const settingsMounted = () =>
      browser.execute(() => !!document.querySelector('.settings-content.cfg'));

    await goto('add');
    await browser.waitUntil(async () => !(await settingsMounted()), {
      timeout: 3_000, timeoutMsg: 'the settings page never unmounted',
    });
    await goto('app:general');
    await browser.waitUntil(settingsMounted, {
      timeout: 3_000, timeoutMsg: 'the settings page never came back',
    });
  }

  it('AS-06: the warning is already there before any click, and the toggle stays clickable', async () => {
    // The probe runs on Settings mount, so the stub has to be installed in the
    // config window and Settings remounted for it to be observed.
    await browser.execute(() => {
      const hook = (window as any).__ipcInvoke;
      if (!hook?.setInvoke) throw new Error('__ipcInvoke.setInvoke missing (navigator.webdriver false?)');
      const raw = (window as any).__TAURI_INTERNALS__?.invoke;
      hook.setInvoke(async (cmd: string, args: any) => {
        if (cmd === 'autostart_can_configure') {
          return Promise.reject({ kind: 'elevationRequired', message: 'ERROR: Access is denied.' });
        }
        return raw ? raw(cmd, args) : null;
      });
    });
    await remountSettingsGeneral();

    await browser.waitUntil(async () => (await calloutText()).length > 0, {
      timeout: 3_000,
      timeoutMsg: 'no warning shown up front for a machine that cannot configure autostart',
    });
    expect((await calloutText()).toLowerCase()).to.contain('administrator');

    // NOT disabled: #69/#79 were both reported as a dead control, and the fix
    // (relaunch elevated) has to be retryable without reopening Settings.
    const disabled = await browser.execute((re: string) => {
      const pattern = new RegExp(re, 'i');
      for (const row of Array.from(document.querySelectorAll<HTMLElement>('.settings-content.cfg .design-row'))) {
        if (!pattern.test(row.querySelector('.design-label')?.textContent ?? '')) continue;
        const b = row.querySelector<HTMLButtonElement>('.design-switch');
        return b ? b.disabled : null;
      }
      return null;
    }, AUTOSTART_ROW.source);
    expect(disabled).to.equal(false);
  });

  it('AS-05: a later successful flip clears the warning', async () => {
    await rejectAutostart({ kind: 'elevationRequired', message: 'ERROR: Access is denied.' });
    await clickAutostartSwitch();
    await browser.waitUntil(async () => (await calloutText()).length > 0, { timeout: 3_000 });

    // Same click, now succeeding: the stale warning must not outlive the fix.
    await browser.execute(() => {
      const hook = (window as any).__ipcInvoke;
      const raw = (window as any).__TAURI_INTERNALS__?.invoke;
      hook.setInvoke(async (cmd: string, args: any) => {
        if (cmd === 'autostart_enable' || cmd === 'autostart_disable') return null;
        return raw ? raw(cmd, args) : null;
      });
    });
    await clickAutostartSwitch();

    await browser.waitUntil(async () => (await calloutText()) === '', {
      timeout: 3_000,
      timeoutMsg: 'inline warning survived a successful autostart flip',
    });
  });
});
