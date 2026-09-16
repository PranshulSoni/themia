/**
 * [ZC-01] A DWM-cloaked widget always comes back.
 *
 * Show Desktop and fullscreen exclusive apps hide windows by cloaking them, and
 * a cloaked overlay is invisible while still reporting visible, non-iconic and
 * above Progman — so nothing in the z-order machinery can detect or fix it.
 * That is the "widgets completely disappear with a game open" half of
 * nwalser/themia-releases#104.
 *
 * Scope note: this spec pins the INVARIANT (a cloak on a widget is always
 * undone), which both recovery paths satisfy — the `EVENT_OBJECT_CLOAKED` hook
 * and the `ensure_widget_cloak_tick` backstop. The backstop's own trigger is a
 * WinEvent that Windows *dropped* under load, which cannot be simulated from
 * here; that path needs manual verification with a real fullscreen game.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, waitForReady, pickWidget, clickGridCell } from '../helpers/app.js';

const PRIMARY_LABEL = 'monitor-0';

function invokeDiag<T>(cmd: string, args: Record<string, unknown> = {}): Promise<T> {
  return browser.execute(async (c: string, a: Record<string, unknown>) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    return await invoke(c, a);
  }, cmd, args) as Promise<T>;
}

interface Diag {
  is_visible: boolean;
  is_cloaked: boolean;
  cloak_reason: number;
}

const diagWindowState = (label: string) => invokeDiag<Diag>('diag_window_state', { label });
const cloakWidget = (label: string) => invokeDiag<boolean>('diag_cloak_widget', { label });

const runOnWindows = process.platform === 'win32' ? describe : describe.skip;

runOnWindows('[ZC-01] widget cloak recovery', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await $('.widget').waitForExist({ timeout: 5_000 });
  });

  it('uncloaks a widget that was cloaked behind its back', async function () {
    const landed = await cloakWidget(PRIMARY_LABEL);
    if (!landed) return this.skip(); // DWM refused the cloak; nothing to assert

    // Generous: the event path is immediate, the backstop cadence is ~1s.
    const deadline = Date.now() + 4000;
    let d = await diagWindowState(PRIMARY_LABEL);
    while (Date.now() < deadline && d.is_cloaked) {
      await browser.pause(150);
      d = await diagWindowState(PRIMARY_LABEL);
    }

    expect(d.is_cloaked, `widget still cloaked (reason ${d.cloak_reason})`).to.equal(false);
    expect(d.is_visible, 'widget not visible after uncloak').to.equal(true);
  });
});
