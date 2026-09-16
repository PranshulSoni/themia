/**
 * Clicking a widget dismisses the Windows Start Menu.
 *
 * Widget windows are WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW so they never
 * become foreground. Side effect: when the Start Menu is open and the user
 * clicks a widget, the Start Menu does NOT auto-close (Windows dismisses
 * Start only when another window becomes foreground). This spec reproduces
 * the bug at OS level using PowerShell to drive Start + query foreground.
 *
 * Strategy:
 *   1. Place a clock widget.
 *   2. Open Start via Ctrl+Esc (WScript.Shell.SendKeys).
 *   3. Verify foreground process is StartMenuExperienceHost.exe.
 *   4. Click the widget via webdriver: our JS mousedown handler fires
 *      invoke('dismiss_start_menu_if_open') which calls SetForegroundWindow
 *      on Progman, causing the shell to close Start.
 *   5. Poll foreground for up to 2s and assert Start is gone.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { execSync } from 'node:child_process';
import { resetAppState, waitForReady, pickWidget, clickGridCell } from '../helpers/app.js';

// ── PowerShell helpers ──────────────────────────────────────────────────────
// Run a PS expression with -NoProfile and return stdout as a trimmed string.
function ps(expr: string): string {
  return execSync(
    `powershell -NoProfile -NonInteractive -Command "${expr.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8', windowsHide: true },
  ).trim();
}

// Open Windows Start Menu. Ctrl+Esc is the keyboard shortcut for Start and
// works reliably from non-foreground processes (Win-key SendKeys sometimes
// does not). WScript.Shell is always present on Win10/11 desktops.
function openStartMenu(): void {
  ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('^{ESC}')`);
}

// Force-close Start (used for cleanup). ESC is the native dismiss.
function closeStartMenu(): void {
  ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('{ESC}')`);
}

// Query the foreground window via P/Invoke. Returns lower-case process
// basename (e.g. 'startmenuexperiencehost.exe', 'explorer.exe').
function foregroundProcess(): string {
  const script = `
    Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
public static class FG {
  [DllImport(\\"user32.dll\\")] public static extern IntPtr GetForegroundWindow();
  [DllImport(\\"user32.dll\\")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  public static string ProcName() {
    var h = GetForegroundWindow();
    if (h == IntPtr.Zero) return \\"\\";
    int pid; GetWindowThreadProcessId(h, out pid);
    try { return Process.GetProcessById(pid).ProcessName + \\".exe\\"; }
    catch { return \\"\\"; }
  }
}
'@
    [FG]::ProcName()
  `;
  return ps(script).toLowerCase();
}

const START_HOSTS = new Set(['startmenuexperiencehost.exe', 'searchhost.exe']);
const isStart = (p: string) => START_HOSTS.has(p);

async function waitForStart(timeoutMs = 1500): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (isStart(foregroundProcess())) return true;
    await new Promise(r => setTimeout(r, 80));
  }
  return false;
}

async function waitForStartGone(timeoutMs = 2000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let last = '';
  while (Date.now() < deadline) {
    last = foregroundProcess();
    if (!isStart(last)) return last;
    await new Promise(r => setTimeout(r, 80));
  }
  return last;
}

const runOnWindows = process.platform === 'win32' ? describe : describe.skip;

runOnWindows('widget click dismisses Windows Start Menu', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await $('.widget').waitForExist({ timeout: 5_000 });
  });

  afterEach(() => {
    // Best-effort cleanup: if the test left Start open, close it so it
    // doesn't sit over the next spec's webdriver interactions.
    try { if (isStart(foregroundProcess())) closeStartMenu(); } catch {}
  });

  it('clicking a widget while Start Menu is open closes Start', async () => {
    openStartMenu();
    const opened = await waitForStart(1500);
    if (!opened) {
      // Environment can't open Start (locked session, no interactive
      // desktop), so skip rather than false-fail.
      return this?.skip?.();
    }
    expect(isStart(foregroundProcess())).to.equal(true);

    // Click the widget. WDIO .click() dispatches a native-ish DOM mousedown
    // inside the webview; our App-level mousedown listener invokes the
    // dismiss_start_menu_if_open Tauri command.
    await browser.execute(() => {
      const w = document.querySelector<HTMLElement>('.widget');
      w?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    });

    const after = await waitForStartGone(2000);
    expect(isStart(after), `Start still foreground after widget click: ${after}`).to.equal(false);
  });
});
