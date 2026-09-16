/**
 * WebdriverIO config for Tauri e2e.
 *
 * Drives the built debug binary via tauri-driver (which proxies to
 * msedgedriver, since Tauri on Windows uses WebView2 = Edge).
 *
 * Prereqs (one-time):
 *   cargo install tauri-driver --locked
 *   Install Microsoft Edge Driver matching your Edge version:
 *     https://developer.microsoft.com/microsoft-edge/tools/webdriver/
 *   Place msedgedriver.exe on PATH (or set MSEDGEDRIVER env var).
 *
 * Build once:   npm run test:e2e:build
 * Run tests:    npm run test:e2e
 */
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { request } from 'node:http';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { browser } from '@wdio/globals';

// package.json uses "type": "module"; derive __dirname in ESM.
const __filename = fileURLToPath(import.meta.url);
const __dirname  = dirname(__filename);

const ROOT = resolve(__dirname, '..', '..');

// Debug build path (workspace target dir; Cargo workspace shares it).
// Switch to 'release' + productName if running `tauri build` (no --debug).
const APP_EXE = resolve(ROOT, 'target', 'debug', 'themia.exe');

let tauriDriver: ChildProcess | undefined;

// ── Vite dev server lifecycle ───────────────────────────────────────────────
// The debug binary loads its frontend from `devUrl` (http://localhost:5173),
// so the harness must keep a vite dev server up for the duration of the run.
const VITE_PORT = 5173;
const VITE_BIN = resolve(ROOT, 'node_modules', 'vite', 'bin', 'vite.js');
// Records the PID of a harness-started vite so a later run (or a crashed run's
// successor) can reclaim and kill it instead of leaking it on :5173.
const VITE_PID_FILE = resolve(__dirname, '.vite-harness.pid');

// PID of the vite the harness is RESPONSIBLE for. Only set when we started it
// (or adopted a leaked harness vite). Never set for a user's own `npm run dev`
// server: that one we must leave strictly alone.
let ownVitePid: number | undefined;

function readVitePidFile(): number | undefined {
  try {
    const n = Number(readFileSync(VITE_PID_FILE, 'utf8').trim());
    return Number.isFinite(n) && n > 0 ? n : undefined;
  } catch { return undefined; }
}
function writeVitePidFile(pid: number): void {
  try { writeFileSync(VITE_PID_FILE, String(pid), 'utf8'); } catch {}
}
function removeVitePidFile(): void {
  try { unlinkSync(VITE_PID_FILE); } catch {}
}

// The PID actually LISTENING on a TCP port (Windows `netstat`). Lets us tell a
// leaked harness vite (pidfile PID == port owner) apart from a user's dev
// server (no/!matching pidfile) so we never kill the latter.
function pidOnPort(port: number): number | undefined {
  try {
    const out = spawnSync('netstat', ['-ano', '-p', 'tcp'], { encoding: 'utf8' }).stdout ?? '';
    for (const line of out.split(/\r?\n/)) {
      if (!line.includes('LISTENING')) continue;
      const cols = line.trim().split(/\s+/);
      if ((cols[1] ?? '').endsWith(`:${port}`)) {
        const pid = Number(cols[cols.length - 1]);
        if (Number.isFinite(pid) && pid > 0) return pid;
      }
    }
  } catch {}
  return undefined;
}

function killPidTreeSync(pid: number): void {
  try { spawnSync('taskkill', ['/F', '/T', '/PID', String(pid)], { stdio: 'ignore' }); } catch {}
}

// Test-only child processes (by image name) that must never outlive the runner.
const TEST_PROC_NAMES = ['themia.exe', 'tauri-driver.exe', 'msedgedriver.exe'];
function killTestProcessesSync(): void {
  for (const name of TEST_PROC_NAMES) {
    try { spawnSync('taskkill', ['/F', '/T', '/IM', name], { stdio: 'ignore' }); } catch {}
  }
}

// Single shutdown path used by onComplete AND by signal/exit handlers.
//
// Why this exists: wdio's onComplete previously only `.kill()`-ed the spawn()
// handles. On Windows those handles are shell wrappers, so killing them
// orphaned the REAL children: tauri-driver.exe, msedgedriver.exe, and the
// vite `node` process squatting on :5173 that then collided with `npm run dev`.
// We now (a) tree-kill the vite we own by its real PID and (b) kill the driver
// processes by image name, on EVERY exit path: normal completion, Ctrl+C,
// SIGTERM, or a throw, so nothing leaks. A user's own dev server is never
// touched (ownVitePid stays undefined for it).
let shutdownDone = false;
function shutdownHarness(): void {
  if (shutdownDone) return;
  shutdownDone = true;
  try { tauriDriver?.kill('SIGKILL'); } catch {}
  if (ownVitePid !== undefined) { killPidTreeSync(ownVitePid); removeVitePidFile(); }
  killTestProcessesSync();
}

process.once('SIGINT',  () => { shutdownHarness(); process.exit(130); });
process.once('SIGTERM', () => { shutdownHarness(); process.exit(143); });
process.once('exit',    () => { shutdownHarness(); });

async function pingViteDev(): Promise<boolean> {
  return new Promise((resolve) => {
    // Probe `localhost` (not a hard-coded 127.0.0.1): vite binds `localhost`,
    // which on Windows often resolves to IPv6 ::1, so an IPv4-only probe never
    // connects and we'd wrongly conclude vite never came up.
    const req = request(
      { host: 'localhost', port: VITE_PORT, path: '/', method: 'GET', timeout: 1500 },
      (res) => { res.resume(); resolve(true); },
    );
    req.on('error', () => resolve(false));
    req.on('timeout', () => { req.destroy(); resolve(false); });
    req.end();
  });
}

async function ensureViteDev(): Promise<void> {
  if (await pingViteDev()) {
    // Port already taken. Is it a leaked harness vite (reclaim + clean up) or a
    // user's own dev server (leave alone)? Adopt ONLY when the pidfile's PID is
    // exactly the process listening on the port.
    const filePid = readVitePidFile();
    const portPid = pidOnPort(VITE_PORT);
    if (filePid !== undefined && portPid !== undefined && filePid === portPid) {
      ownVitePid = portPid; // leaked from a prior run, so we'll kill it at shutdown
    } else {
      console.warn(`[e2e] :${VITE_PORT} already in use by pid ${portPid ?? '?'}; reusing without ownership.`);
    }
    return;
  }
  // Nothing listening; a pidfile here is stale.
  removeVitePidFile();

  // Spawn vite DIRECTLY via node (not `npm run` through a shell) so the handle
  // PID is the real server PID. A shell wrapper would orphan the node child on
  // kill: the exact bug that used to leak :5173.
  const proc = spawn(process.execPath, [VITE_BIN], { cwd: ROOT, stdio: 'ignore' });
  proc.on('error', (e) => console.error('vite:dev failed to start:', e));
  ownVitePid = proc.pid;
  if (ownVitePid !== undefined) writeVitePidFile(ownVitePid);

  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (await pingViteDev()) return;
    await new Promise((r) => setTimeout(r, 300));
  }
  // Came up never; don't leave a half-started vite behind.
  if (ownVitePid !== undefined) { killPidTreeSync(ownVitePid); removeVitePidFile(); ownVitePid = undefined; }
  throw new Error('vite:dev did not come up within 30s');
}

function spawnTauriDriver(): ChildProcess {
  const proc = spawn('tauri-driver', [], {
    stdio: [null, process.stdout, process.stderr],
    shell: true,
  });
  proc.on('error', (e) => console.error('tauri-driver failed to start:', e));
  return proc;
}

async function pingTauriDriver(): Promise<boolean> {
  return new Promise((resolve) => {
    const req = request(
      { host: '127.0.0.1', port: 4444, path: '/status', method: 'GET', timeout: 1500 },
      (res) => { res.resume(); resolve(true); },
    );
    req.on('error', () => resolve(false));
    req.on('timeout', () => { req.destroy(); resolve(false); });
    req.end();
  });
}

/**
 * Kill specific process names with taskkill /T. Pass `['themia.exe']` to drop
 * only the app under test between specs; pass the full list when we need a
 * forced reset (e.g. after a wedge or at shutdown).
 */
function killProcesses(names: string[]): Promise<void> {
  return new Promise((resolve) => {
    if (names.length === 0) { resolve(); return; }
    let pending = names.length;
    for (const t of names) {
      const p = spawn('taskkill', ['/F', '/IM', t, '/T'], { stdio: 'ignore', shell: true });
      p.on('close', () => { if (--pending === 0) resolve(); });
      p.on('error', () => { if (--pending === 0) resolve(); });
    }
  });
}

/**
 * Forced respawn: always runs in `beforeSession` for cascade resilience.
 * Trimmed from the original 1.5s+800ms+700ms sleep dance down to a single
 * 300ms settle. The cumulative saving across a 73-spec run is ~1.5 min.
 * If wedge regressions reappear, dial the settle back up.
 */
async function respawnTauriDriver(): Promise<void> {
  if (tauriDriver) { try { tauriDriver.kill('SIGKILL'); } catch {} }
  await killProcesses(['themia.exe', 'tauri-driver.exe', 'msedgedriver.exe']);
  await new Promise((r) => setTimeout(r, 300));
  tauriDriver = spawnTauriDriver();
  const deadline = Date.now() + 8_000;
  while (Date.now() < deadline) {
    if (await pingTauriDriver()) return;
    await new Promise((r) => setTimeout(r, 80));
  }
  throw new Error('tauri-driver did not come up after restart');
}

// WDIO v9 auto-detects `tsx` for .ts config + specs. Set TSCONFIG_PATH so
// spec files use the test tsconfig (with wdio types included).
process.env.TSCONFIG_PATH = resolve(__dirname, 'tsconfig.json');

export const config: WebdriverIO.Config = {
  runner: 'local',
  specs: [resolve(__dirname, 'specs', '**', '*.spec.ts')],
  maxInstances: 1,          // Tauri windows don't parallelise cleanly
  capabilities: [{
    // WebView2 via tauri-driver. `tauri:options.application` is the key bit.
    browserName: 'wry',
    // Vendor-specific capability for tauri-driver; not in wdio types.
    //
    // `--autostart` is themia's own argv flag (src-tauri/src/lib.rs
    // `AUTOSTART_FLAG`). When set, the setup() hook skips the
    // `show_config_window` call that normally fires on a manual launch, so
    // the test process never spawns the settings window unprompted.
    //
    // `--no-activate` ([WA-02]) covers the windows the specs DO open. Every
    // window creation activates on its own — wry calls `MoveFocus` while
    // building the WebView2 controller, which takes the foreground even for a
    // `.visible(false)` window — and `open_config_window` & friends then
    // `set_focus()` on top of that. With ~90 specs each respawning the app,
    // that was a foreground steal every second or so for the whole run. Under
    // the flag those windows are shown with `SW_SHOWNOACTIVATE` instead.
    // Nothing about *what* is created changes, so specs are unaffected; the
    // driver reaches windows by handle, not by OS focus.
    //
    // What remains: WebDriver's own "switch to window" activates its target, so
    // a spec that drives a second window still raises it once. That one is not
    // reachable from the app side.
    'tauri:options': { application: APP_EXE, args: ['--autostart', '--no-activate'] },
  } as WebdriverIO.Capabilities],
  logLevel: 'warn',
  bail: 0,
  baseUrl: '',
  waitforTimeout: 10_000,
  connectionRetryTimeout: 120_000,
  connectionRetryCount: 3,
  framework: 'mocha',
  reporters: ['spec'],
  mochaOpts: {
    ui: 'bdd',
    timeout: 60_000,
  },

  // tauri-driver listens on 4444 by default; msedgedriver spawned as its native driver.
  port: 4444,
  hostname: '127.0.0.1',

  async onPrepare(): Promise<void> {
    if (!existsSync(APP_EXE)) {
      throw new Error(`App binary not found: ${APP_EXE}\nRun 'npm run test:e2e:build' first.`);
    }
    // The debug binary loads its frontend from `devUrl` (http://localhost:5173),
    // so vite must be up for the run. ensureViteDev() starts it (tracking the
    // real PID for guaranteed cleanup) or reuses one already running.
    await ensureViteDev();
    tauriDriver = spawnTauriDriver();
  },

  // Edge-148 / WebView2 `getText()` intermittently returns '' for elements that
  // are demonstrably rendered (verified via diagnostics: the same element's
  // `textContent` holds the value, with display:block + visible + non-zero box).
  // Fall back to `textContent` when `getText()` is empty so specs read the real
  // content. A genuinely-empty element returns '' from textContent too, so this
  // only recovers the driver quirk; it never masks a real empty element.
  before() {
    browser.overwriteCommand('getText', async function (this: WebdriverIO.Element, origGetText: () => Promise<string>) {
      const t = await origGetText();
      if (t && t.trim() !== '') return t;
      const tc = await this.getProperty('textContent');
      return typeof tc === 'string' ? tc : t;
    }, true);
  },

  // Always-respawn before each spec: cheaper here (~1s) than recovering
  // from a wedged msedgedriver child mid-suite, and immune to cascade
  // failures we saw when reusing the driver across specs.
  async beforeSession() {
    await respawnTauriDriver();
  },

  // Aggressive teardown: when wdio asks tauri-driver to DELETE the session,
  // a wedged msedgedriver can take long enough that the inner undici fetch
  // times out (UND_ERR_HEADERS_TIMEOUT) and the spec is marked failed even
  // though every test passed. Pre-empt by force-killing themia.exe + the
  // msedgedriver child; DELETE then sees no live browser and returns
  // quickly. tauri-driver itself stays alive so the next beforeSession
  // doesn't pay the ~2s respawn cost.
  async afterSession() {
    // Force-kill themia.exe so the wdio session DELETE doesn't sit waiting
    // for a slow shutdown (we've seen UND_ERR_HEADERS_TIMEOUT when a
    // wedged WebView2 stalls DELETE for minutes). The next beforeSession
    // does the rest of the cleanup via respawnTauriDriver.
    await killProcesses(['themia.exe']).catch(() => {});
  },

  onComplete(): void {
    // Tree-kill the harness-owned vite by its real PID and kill tauri-driver /
    // msedgedriver / the app under test by image name. (The same path runs from
    // the SIGINT/SIGTERM/exit handlers above, so Ctrl+C and crashes clean up too;
    // a vite that still slips through is reclaimed via the pidfile next run.)
    shutdownHarness();
  },
};
