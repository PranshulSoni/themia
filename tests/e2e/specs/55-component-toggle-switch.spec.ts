/**
 * ToggleSwitch component: the sliding pill switch that replaced the old
 * text "On/Off" buttons (`.toggle-btn`) across every widget config page and
 * the settings modals.
 *
 * We drive the Clock widget's "Show seconds" row because:
 *  - ClockConfig has two plain boolean rows (showSeconds, showDate) with no
 *    async handlers, the cleanest place to prove click + keyboard + persist.
 *  - The widget config window auto-opens after placement (kind="widget").
 *  - `updateWidgetConfig` writes through to localStorage `widget-layout`,
 *    shared across Tauri webview windows, so we can read it back to prove
 *    the value committed.
 *
 * Selectors per ToggleSwitch.tsx:
 *  - the switch: `button.design-switch` with `role="switch"`
 *  - on state:  `.design-switch.on` + `aria-checked="true"`
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist,
  configModalIsOpen, closeConfigModal, switchToConfigWindow, switchToMain,
  LS_KEYS,
} from '../helpers/app.js';

/** Read state of the switch in the config row whose label === `label`. */
async function switchState(label: string): Promise<{ exists: boolean; role: string; checked: string; hasOn: boolean }> {
  return browser.execute((lbl: string) => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    const row = rows.find(r => (r.querySelector('.design-label')?.textContent ?? '').trim() === lbl);
    const sw = row?.querySelector<HTMLElement>('.design-switch');
    return {
      exists: !!sw,
      role: sw?.getAttribute('role') ?? '',
      checked: sw?.getAttribute('aria-checked') ?? '',
      hasOn: !!sw?.classList.contains('on'),
    };
  }, label);
}

/** Click the switch in the config row whose label === `label`. */
async function clickSwitch(label: string): Promise<void> {
  await browser.execute((lbl: string) => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    const row = rows.find(r => (r.querySelector('.design-label')?.textContent ?? '').trim() === lbl);
    row?.querySelector<HTMLButtonElement>('.design-switch')?.click();
  }, label);
}

/** Focus the switch in the config row whose label === `label`. */
async function focusSwitch(label: string): Promise<void> {
  await browser.execute((lbl: string) => {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
    const row = rows.find(r => (r.querySelector('.design-label')?.textContent ?? '').trim() === lbl);
    row?.querySelector<HTMLButtonElement>('.design-switch')?.focus();
  }, label);
}

/** Read the clock widget's persisted config from the main window's localStorage. */
async function clockConfig(): Promise<Record<string, any>> {
  await switchToMain();
  return browser.execute((key: string) => {
    try {
      const layout = JSON.parse(localStorage.getItem(key) ?? '{}');
      const w = (layout.widgets ?? []).find((x: any) => x.type === 'clock');
      return w?.config ?? {};
    } catch { return {}; }
  }, LS_KEYS.layoutRoot);
}

describe('component: toggle switch', () => {
  beforeEach(async () => {
    await resetAppState();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await waitForPersist();
    // clickGridCell auto-dismisses the placement config window. Re-open it
    // for the just-placed widget so the toggle assertions have something to
    // bind to. We read the placed widget id from the main window's layout
    // and invoke open_config_window directly.
    await switchToMain();
    const widgetId = await browser.execute((key: string) => {
      try {
        const layout = JSON.parse(localStorage.getItem(key) ?? '{}');
        const w = (layout.widgets ?? []).find((x: any) => x.type === 'clock');
        return w?.id ?? null;
      } catch { return null; }
    }, LS_KEYS.layoutRoot) as string | null;
    if (!widgetId) throw new Error('clock widget was not placed');
    await browser.execute(async (id: string) => {
      const internals = (window as any).__TAURI_INTERNALS__;
      const invoke = internals?.__originalInvoke ?? internals?.invoke;
      await invoke('open_config_window', { parentLabel: 'main', target: `widget:${id}` });
    }, widgetId);
    await browser.waitUntil(async () => configModalIsOpen(), {
      timeout: 5_000, timeoutMsg: 'widget config window did not reopen',
    });
    await switchToConfigWindow('widget');
    // ClockConfig renders `.design-row` rows; wait for them to land so the
    // first test doesn't query before Solid mounts the panel.
    await browser.waitUntil(async () => {
      return browser.execute(() => {
        const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
        return rows.some(r => (r.querySelector('.design-label')?.textContent ?? '').trim() === 'Show seconds');
      });
    }, { timeout: 5_000, timeoutMsg: 'Show seconds row never rendered in widget config' });
  });

  afterEach(async () => {
    if (await configModalIsOpen()) await closeConfigModal();
  });

  // Clock defaults to showSeconds=true / showDate=true (widgets/others/clock/
  // index.tsx). The switches therefore render in the ON state; test that the
  // user toggling them OFF flips the aria + class state and persists.
  it('renders as a role="switch" button matching the persisted value', async () => {
    const s = await switchState('Show seconds');
    expect(s.exists).to.equal(true);
    expect(s.role).to.equal('switch');
    expect(s.checked).to.equal('true');
    expect(s.hasOn).to.equal(true);
  });

  it('clicking flips aria-checked + .on and persists the value', async () => {
    await clickSwitch('Show seconds');

    const s = await switchState('Show seconds');
    expect(s.checked).to.equal('false');
    expect(s.hasOn).to.equal(false);

    await closeConfigModal();
    await waitForPersist();
    const cfg = await clockConfig();
    expect(cfg.showSeconds).to.equal(false);
  });

  it('Space key toggles the focused switch', async () => {
    await focusSwitch('Show date');
    expect((await switchState('Show date')).checked).to.equal('true');

    await browser.keys([' ']);
    expect((await switchState('Show date')).checked).to.equal('false');

    await closeConfigModal();
    await waitForPersist();
    const cfg = await clockConfig();
    expect(cfg.showDate).to.equal(false);
  });
});
