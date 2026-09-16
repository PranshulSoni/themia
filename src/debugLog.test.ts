// [LOG-03]/[LOG-04] Console-noise filter + forwarding master gate (see
// src-tauri/SPEC.md, Logging pipeline).
//
// [LOG-03]: Orphaned tauri-plugin-fs watch Channels after a page reload make
// @tauri-apps/api spam `console.warn("[TAURI] Couldn't find callback id N…")`
// on every fs event. The console.warn → persisted-WARN promotion must drop
// these records (they'd otherwise amplify one orphaned watcher into unbounded
// log-file growth + a WARN broadcast to every window), while real warnings
// keep flowing into the plugin pipeline.
//
// [LOG-04]: Verbose logging is opt-in. In release the bridge starts OFF (the
// module initializer is `import.meta.env.DEV`, so dev builds (and vitest)
// start on); while off, only ERROR reaches tauri-plugin-log (the error floor,
// so a release failure is persisted without a prior opt-in), the rest of
// console.* passes through to the original console and logger.warn still
// echoes to DevTools. Windows without a reactive design-store effect seed the
// bridge once via seedLogForwardingFromStorage (main.tsx, every window).
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('@tauri-apps/plugin-log', () => ({
  debug: vi.fn(() => Promise.resolve()),
  info: vi.fn(() => Promise.resolve()),
  warn: vi.fn(() => Promise.resolve()),
  error: vi.fn(() => Promise.resolve()),
}));
vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(() => Promise.resolve(() => {})),
}));

import {
  warn as pluginWarn,
  info as pluginInfo,
  error as pluginError,
  debug as pluginDebug,
} from '@tauri-apps/plugin-log';
import {
  attach, detach, isConsoleNoise, logger, debugLog,
  setLogForwarding, seedLogForwardingFromStorage,
} from './debugLog.ts';

function clearPluginMocks() {
  vi.mocked(pluginWarn).mockClear();
  vi.mocked(pluginInfo).mockClear();
  vi.mocked(pluginError).mockClear();
  vi.mocked(pluginDebug).mockClear();
}

const ORPHAN_SPAM =
  "[TAURI] Couldn't find callback id 7066143687173241. This might happen when the app is reloaded while Rust is running an asynchronous operation.";

describe('[LOG-03] isConsoleNoise', () => {
  it('matches the orphaned-callback watch spam', () => {
    expect(isConsoleNoise(ORPHAN_SPAM)).toBe(true);
  });

  it('does not match ordinary warnings', () => {
    expect(isConsoleNoise('disk almost full')).toBe(false);
    expect(isConsoleNoise('[folder] watch failed: access denied')).toBe(false);
    // Mentioning the phrase mid-message is not the spam record itself.
    expect(isConsoleNoise("saw [TAURI] Couldn't find callback id in logs")).toBe(false);
  });
});

describe('[LOG-03] console.warn promotion (forwarding on)', () => {
  beforeEach(async () => {
    clearPluginMocks();
    setLogForwarding(true); // [LOG-04] promotion only exists while logging is on
    await attach();
  });

  afterEach(() => {
    detach();
    setLogForwarding(false);
  });

  it('drops noise records from the plugin pipeline', () => {
    console.warn(ORPHAN_SPAM);
    expect(pluginWarn).not.toHaveBeenCalled();
  });

  it('still promotes real warnings to persisted WARN', () => {
    console.warn('disk almost full');
    expect(pluginWarn).toHaveBeenCalledWith('disk almost full');
  });

  it('still promotes console.log to INFO (filter is warn-spam scoped, not a blanket gag)', () => {
    console.log('hello');
    expect(pluginInfo).toHaveBeenCalledWith('hello');
  });
});

describe('[LOG-04] forwarding master gate', () => {
  beforeEach(() => {
    clearPluginMocks();
    setLogForwarding(false);
  });

  afterEach(() => {
    detach();
    setLogForwarding(false);
  });

  it('logger.info/debug are dropped while forwarding is off (no plugin IPC)', () => {
    logger.info('src', 'hello');
    logger.debug('src', 'tick');
    debugLog('poll', 'tick #1');
    expect(pluginInfo).not.toHaveBeenCalled();
    expect(pluginDebug).not.toHaveBeenCalled();
  });

  it('logger.warn echoes to the console while forwarding is off (no plugin call)', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    try {
      logger.warn('email', 'careful');
      expect(pluginWarn).not.toHaveBeenCalled();
      expect(warnSpy).toHaveBeenCalledWith('[email] careful');
    } finally {
      warnSpy.mockRestore();
    }
  });

  it('logger.error is persisted while forwarding is off (the ERROR floor)', () => {
    logger.error('email', 'boom');
    expect(pluginError).toHaveBeenCalledWith('[email] boom');
  });

  it('console.error is persisted while forwarding is off, console.warn/log are not', async () => {
    await attach();
    console.error('render crashed');
    console.warn('disk almost full');
    console.log('hello');
    expect(pluginError).toHaveBeenCalledWith('render crashed');
    expect(pluginWarn).not.toHaveBeenCalled();
    expect(pluginInfo).not.toHaveBeenCalled();
  });

  it('forwarding on routes logger.* into the plugin', () => {
    setLogForwarding(true);
    logger.info('src', 'hello');
    logger.error('src', 'boom');
    expect(pluginInfo).toHaveBeenCalledWith('[src] hello');
    expect(pluginError).toHaveBeenCalledWith('[src] boom');
  });

  it('seedLogForwardingFromStorage applies the persisted toggle (release semantics)', () => {
    const store = (v: string | null) => ({ getItem: () => v });

    // wrong state beforehand, seeding must override it
    setLogForwarding(true);
    expect(seedLogForwardingFromStorage(store(null), false)).toBe(false);
    logger.info('seed', 'dropped');
    expect(pluginInfo).not.toHaveBeenCalled();

    expect(seedLogForwardingFromStorage(store(JSON.stringify({ loggingEnabled: true })), false)).toBe(true);
    logger.info('seed', 'flows');
    expect(pluginInfo).toHaveBeenCalledWith('[seed] flows');

    // corrupt blob / wrong type → default off, never throws
    expect(seedLogForwardingFromStorage(store('{corrupt'), false)).toBe(false);
    expect(seedLogForwardingFromStorage(store(JSON.stringify({ loggingEnabled: 'yes' })), false)).toBe(false);
    expect(seedLogForwardingFromStorage(store(JSON.stringify(null)), false)).toBe(false);

    // dev builds force the bridge on regardless of the persisted value
    expect(seedLogForwardingFromStorage(store(null), true)).toBe(true);
  });

  it('patched console passes through to the original while off, routes to plugin when on', async () => {
    const origLog = vi.fn();
    const saved = console.log;
    console.log = origLog;
    try {
      await attach(); // patches console, captures origLog as the original

      console.log('offline');
      expect(origLog).toHaveBeenCalledTimes(1);
      expect(origLog).toHaveBeenCalledWith('offline');
      expect(pluginInfo).not.toHaveBeenCalled();

      setLogForwarding(true);
      console.log('online');
      // Routed into the plugin; the DevTools echo happens via the log://log
      // listener, not a direct original call.
      expect(origLog).toHaveBeenCalledTimes(1);
      expect(pluginInfo).toHaveBeenCalledWith('online');
    } finally {
      detach();
      console.log = saved;
    }
  });
});
