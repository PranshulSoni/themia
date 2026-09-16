/**
 * Clicking a widget dismisses the Windows desktop/Explorer context menu.
 *
 * Same root cause as the Start Menu spec (42): widget windows are
 * WS_EX_NOACTIVATE so they never become foreground, and the shell only
 * auto-closes its right-click menu when another window takes foreground.
 * Result: with the menu open, clicking a widget leaves it stuck open. Our
 * JS mousedown handler calls invoke('dismiss_context_menu_if_open'), which
 * detects the open shell menu and injects Escape to close it.
 *
 * Strategy (OS-level, via PowerShell P/Invoke):
 *   1. Place a clock widget.
 *   2. Right-click an empty desktop point to open the shell context menu.
 *   3. Confirm the menu is open (visible explorer-owned WinUI popup, or the
 *      classic "#32768" tracked popup).
 *   4. Dispatch a DOM mousedown on the widget: fires the dismiss command.
 *   5. Poll for up to 2s and assert the menu is gone.
 *
 * The Win11 desktop menu is a `Microsoft.UI.Content.PopupWindowSiteBridge`
 * window owned by explorer.exe; older builds use `Xaml_WindowedPopupClass`.
 * Detection matches both plus the classic `#32768`, mirroring the Rust
 * `find_context_menu_proc`.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { execSync } from 'node:child_process';
import { resetAppState, waitForReady, pickWidget, clickGridCell } from '../helpers/app.js';

// Run a PS expression with -NoProfile, return trimmed stdout.
function ps(expr: string): string {
  return execSync(
    `powershell -NoProfile -NonInteractive -Command "${expr.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8', windowsHide: true },
  ).trim();
}

// C# helper compiled into each PS process. Exposes:
//   OpenMenu(x,y)  : right-click at (x,y) to summon the shell menu.
//   MenuCount()    : number of visible shell-menu windows currently open.
//   CloseMenu()    : inject Escape (cleanup).
const CS = `
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
public static class Ctx {
  [DllImport(\\"user32.dll\\")] static extern bool SetCursorPos(int x,int y);
  [DllImport(\\"user32.dll\\")] static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
  [DllImport(\\"user32.dll\\")] static extern void keybd_event(byte vk,byte sc,uint f,IntPtr e);
  [DllImport(\\"user32.dll\\")] static extern bool EnumWindows(EnumProc cb,IntPtr l);
  [DllImport(\\"user32.dll\\")] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
  [DllImport(\\"user32.dll\\")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport(\\"user32.dll\\")] static extern int GetWindowThreadProcessId(IntPtr h,out int pid);
  delegate bool EnumProc(IntPtr h,IntPtr l);
  static int n;
  static bool Cb(IntPtr h,IntPtr l){
    if(!IsWindowVisible(h))return true;
    var sb=new StringBuilder(160);GetClassName(h,sb,160);string c=sb.ToString();
    if(c==\\"#32768\\"){n++;return true;}
    if(c==\\"Microsoft.UI.Content.PopupWindowSiteBridge\\"||c==\\"Xaml_WindowedPopupClass\\"){
      int pid;GetWindowThreadProcessId(h,out pid);string pn=\\"\\";try{pn=Process.GetProcessById(pid).ProcessName.ToLower();}catch{}
      if(pn==\\"explorer\\")n++;
    }
    return true;
  }
  public static int MenuCount(){ n=0; EnumWindows(Cb,IntPtr.Zero); return n; }
  public static void OpenMenu(int x,int y){ SetCursorPos(x,y); mouse_event(0x0008,0,0,0,IntPtr.Zero); mouse_event(0x0010,0,0,0,IntPtr.Zero); }
  public static void CloseMenu(){ keybd_event(0x1B,0,0,IntPtr.Zero); keybd_event(0x1B,0,2,IntPtr.Zero); }
}
'@`;

// Open the shell menu at an empty desktop point and report whether it opened.
function openMenuAndCount(x: number, y: number): number {
  const out = ps(`${CS}
    [Ctx]::OpenMenu(${x},${y})
    Start-Sleep -Milliseconds 400
    [Ctx]::MenuCount()`);
  return parseInt(out.split(/\r?\n/).pop() || '0', 10) || 0;
}

// Poll until the menu is gone (count 0) or the timeout elapses, in a single
// PS process to avoid per-poll compile cost.
function waitMenuGone(timeoutMs = 2000): number {
  const out = ps(`${CS}
    $deadline=(Get-Date).AddMilliseconds(${timeoutMs})
    $c=[Ctx]::MenuCount()
    while($c -gt 0 -and (Get-Date) -lt $deadline){ Start-Sleep -Milliseconds 80; $c=[Ctx]::MenuCount() }
    $c`);
  return parseInt(out.split(/\r?\n/).pop() || '0', 10) || 0;
}

function closeMenu(): void {
  try { ps(`${CS}\n[Ctx]::CloseMenu()`); } catch { /* best-effort cleanup */ }
}

const runOnWindows = process.platform === 'win32' ? describe : describe.skip;

runOnWindows('widget click dismisses Windows desktop context menu', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await $('.widget').waitForExist({ timeout: 5_000 });
  });

  afterEach(() => {
    try { if (openMenuCountSafe() > 0) closeMenu(); } catch { /* ignore */ }
  });

  function openMenuCountSafe(): number {
    try { return waitMenuGone(0); } catch { return 0; }
  }

  it('clicking a widget while the desktop context menu is open closes it', async function () {
    // Right-click an empty desktop point near the top-left, away from the
    // widget at grid cell (2,2). The transparent overlay is click-through
    // there, so the event reaches the real desktop and the shell menu opens.
    const opened = openMenuAndCount(180, 120);
    if (opened === 0) {
      // Headless / locked / no interactive desktop, can't summon the shell
      // menu. Skip rather than false-fail (mirrors the Start Menu spec).
      return this.skip();
    }

    // DOM mousedown on the widget → App-level handler invokes
    // dismiss_context_menu_if_open.
    await browser.execute(() => {
      const w = document.querySelector<HTMLElement>('.widget');
      w?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    });

    const remaining = waitMenuGone(2000);
    expect(remaining, `shell context menu still open after widget click: ${remaining} window(s)`).to.equal(0);
  });
});
