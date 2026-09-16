import { createSignal } from 'solid-js';
import {
  debug as pluginDebug,
  info as pluginInfo,
  warn as pluginWarn,
  error as pluginError,
} from '@tauri-apps/plugin-log';
import { listen } from '@tauri-apps/api/event';

export type LogLevel = 'error' | 'warn' | 'info' | 'debug';

export interface LogEntry {
  ts: number;
  level: LogLevel;
  source: string;
  msg: string;
}

const MAX_ENTRIES = 100_000;
const FLUSH_INTERVAL = 200;

const buffer: LogEntry[] = [];
let pending: LogEntry[] = [];
const sourceSet = new Set<string>();

const [logs, setLogs] = createSignal<LogEntry[]>([]);
const [sources, setSources] = createSignal<Set<string>>(new Set());
const [logVersion, setLogVersion] = createSignal(0);

export { logs, sources, logVersion };

let flushScheduled = false;

function flush() {
  flushScheduled = false;
  if (pending.length === 0) return;

  for (const e of pending) buffer.push(e);
  if (buffer.length > MAX_ENTRIES) buffer.splice(0, buffer.length - MAX_ENTRIES);
  pending = [];

  setLogs(buffer.slice());
  setLogVersion(v => v + 1);

  if (sourceSet.size !== sources().size) {
    setSources(new Set(sourceSet));
  }
}

function push(entry: LogEntry) {
  pending.push(entry);
  sourceSet.add(entry.source);
  if (!flushScheduled) {
    flushScheduled = true;
    setTimeout(flush, FLUSH_INTERVAL);
  }
}

// Map numeric level from tauri-plugin-log Webview event to our string level.
// @tauri-apps/plugin-log's LogLevel enum (NOT Rust's log::Level) is emitted:
// Trace=1, Debug=2, Info=3, Warn=4, Error=5
function mapLevel(n: number): LogLevel {
  switch (n) {
    case 1:
    case 2: return 'debug';
    case 3: return 'info';
    case 4: return 'warn';
    case 5: return 'error';
    default: return 'info';
  }
}

// Shorten noisy targets for display.
// - "webview:Object.debug@http://localhost:5173/src/foo.ts:10:3" → "foo.ts:10"
// - "tao::platform_impl::platform::event_loop::runner" → "event_loop::runner"
function cleanSource(raw: string): string {
  let s = raw.replace(/^webview:/, '');
  const stack = s.match(/@[^@\s]*\/([^/?#]+):(\d+):\d+$/);
  if (stack) return `${stack[1]}:${stack[2]}`;
  if (s.includes('::')) {
    const parts = s.split('::');
    if (parts.length > 2) return parts.slice(-2).join('::');
  }
  return s;
}

// Parse webview log message: strips any number of leading "[...]" groups (the
// plugin's default formatter prepends date/time/level/target), uses the last
// group as the target, and honours the "[source] msg" convention emitted by
// `logger.*` so app-level source names survive the round-trip.
function parseWebviewMessage(raw: string, fallbackTarget: string): { source: string; msg: string } {
  let rest = raw;
  const groups: string[] = [];
  while (true) {
    const m = rest.match(/^\[([^\]]*)\]/);
    if (!m) break;
    groups.push(m[1]);
    rest = rest.slice(m[0].length);
  }
  const msg = rest.replace(/^\s+/, '');
  const rawTarget = groups.length >= 3 ? groups[groups.length - 1] : fallbackTarget;
  let source = cleanSource(rawTarget);

  // Console wrapper sits in debugLog.ts, so every plain `console.*` call has
  // its stack-derived target point there. Relabel so the source column shows
  // where console output came from rather than our plumbing.
  if (/^debugLog\.ts:/.test(source)) source = 'console';

  // Logger convention: body starts with "[app-source] actual message".
  const tag = msg.match(/^\[([^\]]+)\]\s?(.*)$/s);
  if (tag) return { source: tag[1], msg: tag[2] };
  return { source, msg };
}

let attached = false;
let unlistenFn: (() => void) | null = null;
let restoreConsoleFn: (() => void) | null = null;

// Saved original console methods. The listener uses these to echo plugin
// records into DevTools without re-entering our wrapper (which would loop
// records straight back into the plugin).
interface OrigConsole {
  log: (...a: unknown[]) => void;
  info: (...a: unknown[]) => void;
  warn: (...a: unknown[]) => void;
  error: (...a: unknown[]) => void;
  debug: (...a: unknown[]) => void;
}
let origConsole: OrigConsole | null = null;

function fmtArgs(args: unknown[]): string {
  return args
    .map(a => {
      if (a instanceof Error) return a.stack || a.message;
      if (typeof a === 'string') return a;
      try { return JSON.stringify(a); } catch { return String(a); }
    })
    .join(' ');
}

// [LOG-03] Console-noise filter. After a page reload, orphaned
// tauri-plugin-fs watch Channels make @tauri-apps/api spam
// console.warn("[TAURI] Couldn't find callback id N…") on every fs event.
// Promoting those to persisted WARN amplified one orphaned watcher into
// unbounded log-file growth plus a WARN broadcast to every window. Matched
// records still echo to DevTools via the saved originals but never enter the
// plugin pipeline.
const CONSOLE_NOISE: RegExp[] = [
  /^\[TAURI\] Couldn't find callback id/,
];

export function isConsoleNoise(msg: string): boolean {
  return CONSOLE_NOISE.some(re => re.test(msg));
}

// [LOG-04] Forwarding master gate. Verbose logging is opt-in: while off (the
// release default) only ERROR is sent to tauri-plugin-log — the error floor,
// so a failure on a release machine is persisted without a prior opt-in.
// Everything below it costs nothing: no IPC round-trips, no file records, no
// webview broadcast. console.log/info/warn/debug fall through to the original
// console and logger.warn echoes to DevTools so warnings stay visible.
// Dev builds start ON (incl. records emitted during module init, before any
// effect runs). main.tsx seeds every window type once from the persisted
// blob; App.tsx/ConfigWindow.tsx re-push reactively on toggle changes.
let forwarding = import.meta.env.DEV;

export function setLogForwarding(enabled: boolean): void {
  forwarding = enabled;
}

const DESIGN_BLOB_KEY = 'widget-global-design';

// [LOG-04] One-shot seed for windows that never run the reactive design-store
// effect (connect / layout / picker windows): read `loggingEnabled` straight
// from the persisted design blob. Only a literal `true` enables; anything
// absent, corrupt or mistyped keeps the secure default. Returns the applied
// value (exported for tests; `dev` parameterizes `import.meta.env.DEV`).
export function seedLogForwardingFromStorage(
  storage: Pick<Storage, 'getItem'> = localStorage,
  dev: boolean = import.meta.env.DEV,
): boolean {
  let on = dev;
  if (!on) {
    try {
      const raw: unknown = JSON.parse(storage.getItem(DESIGN_BLOB_KEY) ?? '{}');
      on = typeof raw === 'object' && raw !== null
        && (raw as Record<string, unknown>).loggingEnabled === true;
    } catch { /* ignore: corrupt blob → keep the secure default (off) */ }
  }
  setLogForwarding(on);
  return on;
}

// Route console.* calls through tauri-plugin-log, while logging is enabled
// ([LOG-04]), so every sink (LogDir file, Stdout/VS Code, Settings > Logs
// buffer, DevTools) sees the same records. While logging is off, `console.error`
// still routes (the ERROR floor) and the rest fall through to the saved
// originals untouched. The plugin emits a `log://log`
// event which the listener (below) picks up to: (a) push into the in-memory
// buffer and (b) echo to DevTools via the saved originals, replacing the role
// of `attachConsole()`.
function patchConsole(): () => void {
  const orig: OrigConsole = {
    log: console.log.bind(console),
    info: console.info.bind(console),
    warn: console.warn.bind(console),
    error: console.error.bind(console),
    debug: console.debug.bind(console),
  };
  origConsole = orig;

  const route = (
    fn: (msg: string) => Promise<void>,
    echo: (...a: unknown[]) => void,
    // [LOG-04] Marks the ERROR floor: persisted whether or not verbose
    // logging is on.
    always = false,
  ) =>
    function (...args: unknown[]) {
      try {
        // [LOG-04] Logging off: plain console behavior, zero plugin IPC.
        if (!forwarding && !always) { echo(...args); return; }
        const msg = fmtArgs(args);
        // [LOG-03] Known-noise records: DevTools only, never persisted.
        if (isConsoleNoise(msg)) { echo(...args); return; }
        fn(msg).catch(() => {});
      }
      catch { /* never let logging break the app */ }
    };

  console.log = route(pluginInfo, orig.log);
  console.info = route(pluginInfo, orig.info);
  console.warn = route(pluginWarn, orig.warn);
  console.error = route(pluginError, orig.error, true); // [LOG-04] error floor
  console.debug = route(pluginDebug, orig.debug);

  return () => {
    console.log = orig.log;
    console.info = orig.info;
    console.warn = orig.warn;
    console.error = orig.error;
    console.debug = orig.debug;
    origConsole = null;
  };
}

/**
 * Wire up the frontend logging pipeline. Must be called once at app startup.
 * Single source of truth WHILE LOGGING IS ENABLED ([LOG-04]): every log
 * record (Rust `log::*!`, JS `logger.*`, and ad-hoc `console.*`) flows
 * through tauri-plugin-log. The plugin fans it out to LogDir (file), Stdout
 * (VS Code / debug builds), and the Webview target, which emits `log://log`.
 * We listen for that and (a) push into the Settings > Logs buffer and (b)
 * echo to DevTools via the saved originals. While verbose logging is off
 * (release default) the patch is still installed and passes through to the
 * originals for everything below the [LOG-04] ERROR floor.
 */
export async function attach(): Promise<void> {
  if (attached) return;
  attached = true;
  restoreConsoleFn = patchConsole();
  try {
    unlistenFn = await listen<{ message: string; level: number }>('log://log', evt => {
      const p = evt.payload;
      const level = mapLevel(p.level);
      const { source, msg } = parseWebviewMessage(p.message, 'webview');
      push({ ts: Date.now(), level, source, msg });

      // Echo to DevTools using the saved originals so the wrapper doesn't
      // re-enter the plugin. Mirrors what attachConsole() used to do.
      const o = origConsole;
      if (o) {
        const fn = level === 'error' ? o.error
                 : level === 'warn'  ? o.warn
                 : level === 'debug' ? o.debug
                 : o.info;
        fn(p.message);
      }
    });
  } catch {
    /* listen failures are non-fatal, UI just won't populate */
  }
}

export function detach(): void {
  attached = false;
  unlistenFn?.();
  restoreConsoleFn?.();
  unlistenFn = null;
  restoreConsoleFn = null;
}

// The @tauri-apps/plugin-log API does not expose a target override; the
// plugin derives the target from the JS call site, which yields noisy
// "webview:Object.debug@.../foo.ts:10:3" strings. To keep the app-level
// source visible in the log viewer, prefix the message with "[source] ";
// parseWebviewMessage picks it back up on the way in.
/**
 * Optional observer for ERROR records, set by analytics ([S-24]). Only the
 * SOURCE is handed over, never the message: a log line routinely carries paths,
 * addresses and server responses, and none of that may leave the machine.
 *
 * A sink rather than a direct call so this module keeps no dependency on
 * analytics (which depends on this one for its own logging).
 */
let errorSink: ((source: string) => void) | null = null;

export function setErrorSink(fn: ((source: string) => void) | null): void {
  errorSink = fn;
}

export const logger = {
  error(source: string, msg: string): void {
    // [LOG-04] ERROR is the floor: persisted with verbose logging off too, so
    // a failure on a release machine is diagnosable from the log file without
    // asking the user to opt in first and hit the bug again. The DevTools echo
    // comes from the `log://log` listener, same as when logging is on.
    pluginError(`[${source}] ${msg}`).catch(() => {});
    try {
      errorSink?.(source);
    } catch {
      // ignore: reporting an error must never turn into a second error.
    }
  },
  warn(source: string, msg: string): void {
    if (!forwarding) { (origConsole ?? console).warn(`[${source}] ${msg}`); return; }
    pluginWarn(`[${source}] ${msg}`).catch(() => {});
  },
  info(source: string, msg: string): void {
    if (!forwarding) return; // [LOG-04]
    pluginInfo(`[${source}] ${msg}`).catch(() => {});
  },
  debug(source: string, msg: string): void {
    if (!forwarding) return; // [LOG-04]
    pluginDebug(`[${source}] ${msg}`).catch(() => {});
  },
};

/** Backwards-compatible shim that emits at DEBUG level. */
export function debugLog(source: string, msg: string): void {
  logger.debug(source, msg);
}

export function clearLogs(): void {
  buffer.length = 0;
  pending = [];
  setLogs([]);
  setLogVersion(v => v + 1);
}

const LEVEL_LABEL: Record<LogLevel, string> = {
  error: 'ERROR',
  warn:  'WARN ',
  info:  'INFO ',
  debug: 'DEBUG',
};

export function exportLogsAsText(filter?: string, minLevel?: LogLevel): string {
  flush();
  const all = buffer;
  const levelRank: Record<LogLevel, number> = { error: 4, warn: 3, info: 2, debug: 1 };
  const min = minLevel ? levelRank[minLevel] : 0;
  const filtered = all.filter(e =>
    (!filter || e.source === filter) &&
    (!min || levelRank[e.level] >= min)
  );
  return filtered.map(e => {
    const d = new Date(e.ts);
    const t = d.toLocaleTimeString('en-GB', { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
    return `[${t}] [${LEVEL_LABEL[e.level]}] [${e.source}] ${e.msg}`;
  }).join('\n');
}
