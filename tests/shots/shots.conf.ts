/**
 * WebdriverIO config for the documentation screenshot exporter.
 *
 * Separate from `tests/e2e/wdio.conf.ts` on purpose: that harness drives the
 * built Tauri binary through tauri-driver, which needs `tauri build --debug`
 * (minutes) plus a matching msedgedriver. The exporter doesn't need any of it —
 * `src/main.tsx` already ships a DEV-only `?pickeronly` harness that mounts the
 * Add-Widget picker in a plain browser with the Tauri IPC stubbed and demo
 * fixtures supplying the data, which is exactly the surface we want to capture.
 *
 * So this drives Chrome directly against the vite dev server. The app itself
 * renders in WebView2, which is Chromium too, so layout and text metrics match;
 * a standalone WebView2 runtime isn't drivable on its own, and the Edge browser
 * is not necessarily installed (it isn't on this machine).
 *
 *   npm run shots
 *
 * Output lands in `dist-shots/` (gitignored). See tests/shots/README.md.
 */
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { request } from 'node:http';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const ROOT = resolve(__dirname, '..', '..');

// Its own port so a running `npm run dev` (1420/1421) is never disturbed.
const VITE_PORT = 1425;
const VITE_BIN = resolve(ROOT, 'node_modules', 'vite', 'bin', 'vite.js');
const VITE_PID_FILE = resolve(__dirname, '.vite-shots.pid');

/** Device pixel ratio for the captured PNGs. The docs render these at roughly
 *  half their pixel size, so 2× keeps them crisp on HiDPI displays. */
export const SHOT_SCALE = 2;

let ownVitePid: number | undefined;

function readVitePidFile(): number | undefined {
  try {
    const n = Number(readFileSync(VITE_PID_FILE, 'utf8').trim());
    return Number.isFinite(n) && n > 0 ? n : undefined;
  } catch { return undefined; }
}
function removeVitePidFile(): void {
  try { unlinkSync(VITE_PID_FILE); } catch { /* already gone */ }
}

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
  } catch { /* netstat unavailable; fall through */ }
  return undefined;
}

function killPidTreeSync(pid: number): void {
  try { spawnSync('taskkill', ['/F', '/T', '/PID', String(pid)], { stdio: 'ignore' }); } catch { /* already dead */ }
}

// Single shutdown path, shared by onComplete and the signal handlers, so a
// Ctrl+C mid-run doesn't leak a vite squatting on the port (the e2e harness
// learned this the hard way — see tests/e2e/wdio.conf.ts).
let shutdownDone = false;
function shutdownHarness(): void {
  if (shutdownDone) return;
  shutdownDone = true;
  if (ownVitePid !== undefined) { killPidTreeSync(ownVitePid); removeVitePidFile(); }
}
process.once('SIGINT', () => { shutdownHarness(); process.exit(130); });
process.once('SIGTERM', () => { shutdownHarness(); process.exit(143); });
process.once('exit', () => { shutdownHarness(); });

async function pingVite(): Promise<boolean> {
  return new Promise((res) => {
    // Probe `localhost`, not 127.0.0.1: vite binds localhost, which resolves to
    // ::1 on Windows, so an IPv4-only probe never connects.
    const req = request(
      { host: 'localhost', port: VITE_PORT, path: '/', method: 'GET', timeout: 1500 },
      (r) => { r.resume(); res(true); },
    );
    req.on('error', () => res(false));
    req.on('timeout', () => { req.destroy(); res(false); });
    req.end();
  });
}

async function ensureVite(): Promise<void> {
  if (await pingVite()) {
    const filePid = readVitePidFile();
    const portPid = pidOnPort(VITE_PORT);
    // Only adopt a vite this harness leaked previously; never touch a server
    // the user started themselves.
    if (filePid !== undefined && portPid !== undefined && filePid === portPid) ownVitePid = portPid;
    return;
  }
  removeVitePidFile();

  // Spawn node directly rather than through `npm run`, so the handle PID is the
  // real server PID and the tree-kill on shutdown actually reaches it.
  const proc: ChildProcess = spawn(
    process.execPath,
    [VITE_BIN, '--port', String(VITE_PORT), '--strictPort'],
    { cwd: ROOT, stdio: 'ignore' },
  );
  proc.on('error', (e) => console.error('[shots] vite failed to start:', e));
  ownVitePid = proc.pid;
  if (ownVitePid !== undefined) writeFileSync(VITE_PID_FILE, String(ownVitePid), 'utf8');

  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (await pingVite()) return;
    await new Promise((r) => setTimeout(r, 300));
  }
  shutdownHarness();
  throw new Error('[shots] vite did not come up within 30s');
}

// WDIO v9 auto-detects `tsx` for .ts config + specs; point it at this folder's
// tsconfig so the specs get the wdio/mocha globals.
process.env.TSCONFIG_PATH = resolve(__dirname, 'tsconfig.json');

export const config: WebdriverIO.Config = {
  runner: 'local',
  specs: [resolve(__dirname, '*.spec.ts')],
  maxInstances: 1,
  capabilities: [{
    browserName: 'chrome',
    'goog:chromeOptions': {
      args: [
        'headless=new',
        'disable-gpu',
        // Captures at 2× so the docs images stay sharp when scaled down.
        `force-device-scale-factor=${SHOT_SCALE}`,
        // The harness pins its own 720×680 box; a generous window means the
        // preview pane is never the constraint.
        'window-size=1280,1000',
        // No wallpaper or OS chrome behind the widget: the preview sits on the
        // config surface, which is what the docs frame expects.
        'hide-scrollbars',
      ],
    },
  } as WebdriverIO.Capabilities],
  logLevel: 'warn',
  bail: 0,
  baseUrl: `http://localhost:${VITE_PORT}`,
  waitforTimeout: 10_000,
  connectionRetryTimeout: 120_000,
  connectionRetryCount: 3,
  framework: 'mocha',
  reporters: ['spec'],
  mochaOpts: {
    ui: 'bdd',
    // 49 widgets × 2 themes, each with a settle pause; the whole capture runs
    // inside one `it`, so the timeout covers the full sweep.
    timeout: 600_000,
  },

  async onPrepare(): Promise<void> {
    await ensureVite();
  },

  onComplete(): void {
    shutdownHarness();
  },
};
