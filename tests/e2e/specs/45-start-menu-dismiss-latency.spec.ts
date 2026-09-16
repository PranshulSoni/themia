/**
 * Start-menu dismiss latency.
 *
 * Companion to 42-start-menu-dismiss.spec.ts. That spec only proves Start
 * eventually closes on a widget click. This one proves it happens *fast*
 * enough to feel native, matching the sub-100ms dismiss you get when
 * clicking any normal app with Start open.
 *
 * Latency path exercised (real OS click, not synthetic DOM mousedown):
 *   real mouse click on widget HWND
 *     → WH_MOUSE_LL hook in Rust (installed at startup)
 *     → start_is_foreground() + point_over_widget_window() fast-path
 *     → inject_escape() via SendInput
 *     → Start receives ESC → closes
 *
 * The old JS-mousedown → invoke IPC path was ~150-400ms end-to-end on a
 * warm box, noticeably laggier than a native click. The mouse hook cuts
 * it to ~50-100ms because there is no JS, no webview, no IPC between the
 * click and the ESC injection.
 *
 * Measurement is done entirely in Rust (diag_click_and_measure_start_close)
 * so the Node-side clock is never on the critical path.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { execSync } from 'node:child_process';
import { resetAppState, waitForReady, pickWidget, clickGridCell } from '../helpers/app.js';

function ps(expr: string): string {
  return execSync(
    `powershell -NoProfile -NonInteractive -Command "${expr.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8', windowsHide: true },
  ).trim();
}

function openStartMenu(): void {
  ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('^{ESC}')`);
}

function closeStartMenu(): void {
  ps(`$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys('{ESC}')`);
}

function foregroundProcess(): string {
  const script = `
    Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

// Budget: sub-100ms is the native feel. Give some headroom for CI noise
// and slower hardware. The JS-IPC path it replaces measured 150-400ms,
// so this threshold still fails loudly if the mouse hook regresses.
const LATENCY_BUDGET_MS = 150;

const runOnWindows = process.platform === 'win32' ? describe : describe.skip;

runOnWindows('Start menu dismiss latency', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
    await pickWidget('Clock');
    await clickGridCell(2, 2);
    await $('.widget').waitForExist({ timeout: 5_000 });
  });

  afterEach(() => {
    try { if (isStart(foregroundProcess())) closeStartMenu(); } catch {}
  });

  it(`dismisses Start within ${LATENCY_BUDGET_MS}ms of a native widget click`, async () => {
    // Widget view coords = viewport coords × devicePixelRatio. Each Tauri
    // widget window spans a monitor starting at that monitor's top-left, so
    // viewport (0,0) corresponds to the monitor's view origin. On the
    // primary monitor that's (0,0); on secondary monitors we'd need outer
    // position, but the test always places on the primary, so this holds.
    const pt = await browser.execute(() => {
      const el = document.querySelector<HTMLElement>('.widget');
      if (!el) return null;
      const r = el.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      return {
        x: Math.round(((r.left + r.right) / 2) * dpr),
        y: Math.round(((r.top + r.bottom) / 2) * dpr),
      };
    });
    expect(pt, 'widget bounds').to.not.equal(null);

    openStartMenu();
    const opened = await waitForStart(1500);
    if (!opened) return this?.skip?.();

    // All timing is measured in Rust; Node is not on the critical path.
    const elapsed: number | null = await browser.execute(async (x: number, y: number, budgetMs: number) => {
      const internals = (window as any).__TAURI_INTERNALS__;
      const invoke = internals?.__originalInvoke ?? internals?.invoke;
      return await invoke('diag_click_and_measure_start_close', {
        x, y,
        timeoutMs: budgetMs * 10, // generous upper bound; we assert on the return value
      });
    }, pt!.x, pt!.y, LATENCY_BUDGET_MS);

    expect(elapsed, 'Start did not close within timeout').to.be.a('number');
    expect(elapsed!,
      `dismiss latency ${elapsed}ms exceeded ${LATENCY_BUDGET_MS}ms budget, ` +
      `WH_MOUSE_LL hook may have regressed, check src-tauri/src/lib.rs`,
    ).to.be.lessThan(LATENCY_BUDGET_MS);
  });
});
