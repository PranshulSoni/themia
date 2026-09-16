/**
 * [ZS-01] Opening the Start Menu during a Show Desktop must not hide widgets.
 *
 * Regression for nwalser/themia-releases#104. During a Show Desktop the shell
 * elevates its desktop layer past the normal z-order, so the `WS_EX_TOPMOST`
 * pin is the only thing keeping widgets visible. The foreground hook used to
 * drop that pin for any foreground window that wasn't the desktop or one of our
 * own overlays — and the Start Menu is exactly that. Every widget vanished on
 * the Windows key, and stayed gone, because dismissing Start hands the
 * foreground to the taskbar rather than to the desktop.
 *
 * The spec drives the real shell: ToggleDesktop (the same API the taskbar's
 * Show Desktop button calls), then Ctrl+Esc to open Start, then reads the
 * z-state back through `diag_z_counters` / `diag_window_state`. It waits well
 * past the 80 ms fast poll before asserting, so a re-introduced drop lands
 * inside the observation window rather than racing it.
 *
 * Skips (rather than fails) when the environment can't produce the Show Desktop
 * pin or can't open Start — a locked session or a desktop with no shell.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { execSync } from 'node:child_process';
import { resetAppState, waitForReady, pickWidget, clickGridCell } from '../helpers/app.js';

const PRIMARY_LABEL = 'monitor-0';

// ── Tauri diag bridge ───────────────────────────────────────────────────────

function invokeDiag<T>(cmd: string, args: Record<string, unknown> = {}): Promise<T> {
  return browser.execute(async (c: string, a: Record<string, unknown>) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    return await invoke(c, a);
  }, cmd, args) as Promise<T>;
}

interface ZCounters {
  topmost_transitions: number;
  currently_topmost: boolean;
  foreground_class: string;
  shell_surface_foreground: boolean;
}

interface Diag {
  is_visible: boolean;
  is_iconic: boolean;
  is_cloaked: boolean;
  topmost_class_at_center: string;
  above_progman: boolean;
  widgets_pinned_topmost: boolean;
}

const zCounters = () => invokeDiag<ZCounters>('diag_z_counters');
const diagWindowState = (label: string) => invokeDiag<Diag>('diag_window_state', { label });

/** ToggleDesktop via IShellDispatch4 — the taskbar's own Show Desktop path. */
async function toggleDesktop(): Promise<void> {
  const code = await invokeDiag<number>('diag_toggle_desktop');
  if (code !== 0) throw new Error(`ToggleDesktop powershell exited ${code}`);
  await browser.pause(700);
}

const SHELL_DESKTOP_CLASSES = ['Progman', 'WorkerW', 'SHELLDLL_DefView', 'SysListView32'];

/** Undo Show Desktop until the widget is unobscured again. Idempotent. */
async function ensureDesktopNotShown(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    const d = await diagWindowState(PRIMARY_LABEL);
    if (d.above_progman && !SHELL_DESKTOP_CLASSES.includes(d.topmost_class_at_center)) return;
    try { await toggleDesktop(); } catch { /* ignore: best-effort restore */ }
  }
}

/** Show Desktop until the topmost pin is actually held. False if it never takes. */
async function showDesktopAndWaitForPin(timeoutMs = 3000): Promise<boolean> {
  await toggleDesktop();
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if ((await zCounters()).currently_topmost) return true;
    await browser.pause(100);
  }
  return false;
}

// ── Shell drivers ───────────────────────────────────────────────────────────

function ps(expr: string): string {
  return execSync(
    `powershell -NoProfile -NonInteractive -Command "${expr.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8', windowsHide: true },
  ).trim();
}

// Ctrl+Esc is the Start shortcut that works from a non-foreground process
// (SendKeys of the Win key itself does not, reliably).
const openStartMenu = () => ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('^{ESC}')`);
const closeStartMenu = () => ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('{ESC}')`);

/** Poll the backend's own foreground classification until Start shows up. */
async function waitForStartForeground(timeoutMs = 2000): Promise<ZCounters | null> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const c = await zCounters();
    if (c.shell_surface_foreground) return c;
    await browser.pause(100);
  }
  return null;
}

const runOnWindows = process.platform === 'win32' ? describe : describe.skip;

runOnWindows('[ZS-01] Start Menu does not hide widgets during Show Desktop', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await $('.widget').waitForExist({ timeout: 5_000 });
    await ensureDesktopNotShown();
  });

  afterEach(async () => {
    try { closeStartMenu(); } catch { /* ignore: best-effort cleanup */ }
    await browser.pause(300);
    await ensureDesktopNotShown();
  });

  it('holds the topmost pin while the Start Menu owns the foreground', async function () {
    const pinned = await showDesktopAndWaitForPin();
    if (!pinned) return this.skip(); // env can't reproduce a genuine Show Desktop

    openStartMenu();
    const withStart = await waitForStartForeground();
    if (!withStart) return this.skip(); // env can't open Start

    // Well past the 80 ms fast poll: a re-introduced drop lands here.
    await browser.pause(600);

    const after = await zCounters();
    expect(
      after.currently_topmost,
      `pin dropped while "${after.foreground_class}" owned the foreground`,
    ).to.equal(true);

    const d = await diagWindowState(PRIMARY_LABEL);
    expect(d.is_visible, 'widget stopped being visible').to.equal(true);
    expect(d.is_cloaked, 'widget got cloaked').to.equal(false);
    expect(d.above_progman, 'widget fell below the desktop layer').to.equal(true);
    expect(
      SHELL_DESKTOP_CLASSES.includes(d.topmost_class_at_center),
      `widget covered by the desktop layer ("${d.topmost_class_at_center}")`,
    ).to.equal(false);
  });

  it('still drops the pin when a real application takes the foreground', async function () {
    const pinned = await showDesktopAndWaitForPin();
    if (!pinned) return this.skip();

    // Notepad is a plain top-level app window: the hold must not extend to it,
    // or widgets would paint over ordinary windows.
    ps('Start-Process notepad.exe');
    try {
      const deadline = Date.now() + 5000;
      let dropped = false;
      while (Date.now() < deadline) {
        if (!(await zCounters()).currently_topmost) { dropped = true; break; }
        await browser.pause(100);
      }
      const c = await zCounters();
      if (!dropped && c.shell_surface_foreground) return this.skip(); // notepad never came up
      expect(dropped, `pin still held with "${c.foreground_class}" foreground`).to.equal(true);
    } finally {
      try { ps('Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force'); } catch { /* ignore */ }
      await browser.pause(300);
    }
  });
});
