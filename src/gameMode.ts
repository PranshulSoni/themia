// Game-mode detection (Windows fullscreen / presentation / busy).
//
// Exposes:
//  - `gameModeSuspend` / `setGameModeSuspend`: user preference (persisted)
//  - `gameModeActive`: current OS signal (only meaningful while enabled)
//  - `gameModeSuspended`: derived: true only when BOTH the toggle is on
//    AND the OS reports game/fullscreen. This is the flag widgets react to.
//  - `initGameMode()`: wires the Tauri listener and pushes the current
//    preference to the Rust watcher. Call once at startup.

import { createSignal, createMemo } from 'solid-js';
import { invoke } from './ipc.ts';
import { listen } from '@tauri-apps/api/event';
import { onStorageKey, sharedStorage } from './sync.ts';
import { logger } from './debugLog.ts';
import { loadPersisted, savePersistedBare, type PersistedSchema } from './persistedStore.ts';

const SUSPEND_KEY = 'themia-game-mode-suspend';

// Schema'd persistence (1.12). A scalar boolean. The legacy on-disk form was the
// bare string 'true'/'false'; JSON.parse('true') === true, so the loader reads it
// as a version-0 payload and migrates it to the enveloped form on the next save.
const SCHEMA: PersistedSchema<boolean> = {
  version: 1,
  defaults: () => false,
  parse: (raw: unknown): boolean => raw === true,
};

function loadSuspend(): boolean {
  return loadPersisted(SUSPEND_KEY, SCHEMA, localStorage, {
    onCorrupt: (m) => logger.error('game-mode', m),
  });
}

const [gameModeSuspend, setGameModeSuspend_] = createSignal(loadSuspend());
const [gameModeActive, setGameModeActive] = createSignal(false);

/** Drives the actual widget-suspension behavior. */
export const gameModeSuspended = createMemo(
  () => gameModeSuspend() && gameModeActive(),
);

export { gameModeSuspend, gameModeActive };

/** Flip the user preference, persist it, and (re)configure the Rust watcher. */
export function setGameModeSuspend(v: boolean) {
  setGameModeSuspend_(v);
  // Bare (no envelope): JSON.stringify(true) === "true", identical to the legacy
  // String(v) format the cross-window handler + e2e read raw.
  savePersistedBare(SUSPEND_KEY, v, sharedStorage, {
    onError: (m) => logger.error('game-mode', m),
  });
  invoke('set_game_mode_watcher', { enabled: v }).catch((e) =>
    logger.error('game-mode', `set_game_mode_watcher failed: ${e}`),
  );
  // When disabled, make sure the cached "active" flag doesn't keep widgets hidden.
  if (!v) setGameModeActive(false);
}

/** One-time init. Safe to call multiple times (the listener is attached once). */
let initialized = false;
export function initGameMode() {
  if (initialized) return;
  initialized = true;

  listen<{ active: boolean }>('themia:game-mode', (e) => {
    const active = !!e.payload?.active;
    setGameModeActive(active);
    const msg = active
      ? 'Fullscreen / game / presentation mode detected, suspending widgets'
      : 'Fullscreen / game / presentation mode ended, resuming widgets';
    logger.info('game-mode', msg);
  }).catch((e) => logger.error('game-mode', `listen failed: ${e}`));

  // Mirror preference changes from other widget windows.
  onStorageKey(SUSPEND_KEY, () => {
    const next = loadSuspend();
    setGameModeSuspend_(next);
    if (!next) setGameModeActive(false);
  });

  // Push initial state to the backend.
  invoke('set_game_mode_watcher', { enabled: gameModeSuspend() }).catch((e) =>
    logger.error('game-mode', `set_game_mode_watcher failed: ${e}`),
  );
}
