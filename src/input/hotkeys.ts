/**
 * Hotkey registry.
 *
 * Single keydown + single Tauri `app-key` listener dispatch to registered
 * handlers keyed by a normalised chord string. Replaces the scattered
 * ctrl-shortcut branches in App.tsx and the `app-key` duplicate path.
 *
 * Chord format: "mod+mod+key", modifiers in any order, key last.
 *   Modifiers: ctrl, shift, alt, meta (meta = cmd / win).
 *   Keys: single char/digit (a-z, 0-9) or a named key (lowercased e.key),
 *         e.g. "arrowup", "enter", "delete", "f2", "escape".
 *
 * Strict modifier matching by default: "ctrl+e" will NOT fire if shift is
 * also held. Pass `loose: true` to permit extra modifiers.
 *
 * The Rust WH_KEYBOARD_LL hook (see src-tauri) forwards ctrl+<letter> and
 * ctrl+<digit> combos as `app-key` Tauri events with the bare key as payload
 * (bypassing WS_EX_NOACTIVATE which blocks JS keydown on passive windows).
 * Those are dispatched here as if the user pressed ctrl+<key>.
 *
 * Escape is reserved, handled by escapeStack, not this module.
 */

import { onCleanup } from 'solid-js';
import { listen } from '@tauri-apps/api/event';
import { inputFocused } from './focusStore.ts';
import { debugLog } from '../debugLog.ts';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface HotkeyOptions {
  /** When false (default), handler is skipped while an input/textarea is focused. */
  whenInput?: boolean;
  /** When true, extra modifiers are allowed. Default false (strict match). */
  loose?: boolean;
  /** Call preventDefault on the event. Default true. */
  preventDefault?: boolean;
}

interface Entry {
  chord: string;          // normalised, no modifier bits; split from mods for loose matching
  mods: number;           // bitmask: ctrl=1, shift=2, alt=4, meta=8
  key: string;            // final key name, lowercased
  handler: (e: KeyboardEvent) => void;
  opts: Required<HotkeyOptions>;
}

const CTRL = 1, SHIFT = 2, ALT = 4, META = 8;

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

const entries: Entry[] = [];

// ---------------------------------------------------------------------------
// Chord parsing
// ---------------------------------------------------------------------------

function parseChord(chord: string): { mods: number; key: string } {
  const parts = chord.toLowerCase().split('+').map(s => s.trim()).filter(Boolean);
  let mods = 0;
  let key = '';
  for (const p of parts) {
    if (p === 'ctrl' || p === 'control') mods |= CTRL;
    else if (p === 'shift') mods |= SHIFT;
    else if (p === 'alt' || p === 'option') mods |= ALT;
    else if (p === 'meta' || p === 'cmd' || p === 'win') mods |= META;
    else key = p;
  }
  if (!key) throw new Error(`hotkey: no key in chord "${chord}"`);
  return { mods, key };
}

function eventMods(e: KeyboardEvent): number {
  return (e.ctrlKey ? CTRL : 0) | (e.shiftKey ? SHIFT : 0) | (e.altKey ? ALT : 0) | (e.metaKey ? META : 0);
}

function eventKey(e: KeyboardEvent): string {
  // Normalise to lowercase; e.key is case-sensitive for letters.
  return e.key.toLowerCase();
}

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

/**
 * Register a hotkey. Returns a cleanup function that removes it.
 */
export function registerHotkey(
  chord: string,
  handler: (e: KeyboardEvent) => void,
  opts: HotkeyOptions = {},
): () => void {
  const { mods, key } = parseChord(chord);
  const entry: Entry = {
    chord,
    mods,
    key,
    handler,
    opts: {
      whenInput: opts.whenInput ?? false,
      loose: opts.loose ?? false,
      preventDefault: opts.preventDefault ?? true,
    },
  };
  entries.push(entry);
  debugLog('hotkeys', `register ${chord} (total=${entries.length})`);
  return () => {
    const idx = entries.indexOf(entry);
    if (idx >= 0) {
      entries.splice(idx, 1);
      debugLog('hotkeys', `unregister ${chord} (total=${entries.length})`);
    }
  };
}

/**
 * Solid wrapper: registers for the current owner, auto-cleans up.
 */
export function useHotkey(
  chord: string,
  handler: (e: KeyboardEvent) => void,
  opts: HotkeyOptions = {},
): void {
  onCleanup(registerHotkey(chord, handler, opts));
}

// ---------------------------------------------------------------------------
// Dispatch
// ---------------------------------------------------------------------------

function formatEventChord(mods: number, key: string): string {
  const parts: string[] = [];
  if (mods & CTRL) parts.push('ctrl');
  if (mods & SHIFT) parts.push('shift');
  if (mods & ALT) parts.push('alt');
  if (mods & META) parts.push('meta');
  parts.push(key);
  return parts.join('+');
}

function tryDispatch(mods: number, key: string, e: KeyboardEvent | null, source: string): boolean {
  const inInput = inputFocused();
  const evChord = formatEventChord(mods, key);
  let skipReason: string | null = null;
  for (const entry of entries) {
    if (entry.key !== key) continue;
    const modMatch = entry.opts.loose
      ? (mods & entry.mods) === entry.mods
      : mods === entry.mods;
    if (!modMatch) {
      if (mods !== 0) skipReason = `${entry.chord}: mod mismatch`;
      continue;
    }
    if (inInput && !entry.opts.whenInput) {
      if (mods !== 0) skipReason = `${entry.chord}: input focused`;
      continue;
    }

    if (e && entry.opts.preventDefault) e.preventDefault();
    // Use a synthetic event-like object when dispatched from app-key.
    debugLog('hotkeys', `fire ${entry.chord} (event=${evChord}, src=${source})`);
    entry.handler(e ?? ({} as KeyboardEvent));
    return true;
  }
  if (skipReason) {
    debugLog('hotkeys', `skip ${evChord}: ${skipReason} (src=${source})`);
  }
  return false;
}

function onKeyDown(e: KeyboardEvent) {
  // Escape is owned by escapeStack.
  if (e.key === 'Escape') return;
  tryDispatch(eventMods(e), eventKey(e), e, 'keydown');
}

// ---------------------------------------------------------------------------
// Tauri app-key bridge
// ---------------------------------------------------------------------------

/**
 * Map a bare `app-key` payload to a chord.
 * The Rust hook only forwards ctrl+<letter> / ctrl+<digit>, so we synthesise
 * ctrl as the modifier.
 */
function appKeyToChord(payload: string): { mods: number; key: string } | null {
  if (!payload) return null;
  const k = payload.toLowerCase();
  if (k.length === 1 && ((k >= 'a' && k <= 'z') || (k >= '0' && k <= '9'))) {
    return { mods: CTRL, key: k };
  }
  return null;
}

// ---------------------------------------------------------------------------
// Install
// ---------------------------------------------------------------------------

let installed = false;

/**
 * Install the global keydown listener and the Tauri app-key bridge.
 * Idempotent.
 */
export function initHotkeys(): void {
  if (installed) return;
  installed = true;
  debugLog('hotkeys', 'init: listeners installed');
  window.addEventListener('keydown', onKeyDown);
  listen<string>('app-key', (event) => {
    const chord = appKeyToChord(event.payload);
    if (!chord) {
      debugLog('hotkeys', `app-key ignored: "${event.payload}"`);
      return;
    }
    debugLog('hotkeys', `app-key "${event.payload}" → ctrl+${chord.key}`);
    tryDispatch(chord.mods, chord.key, null, 'app-key');
  });
}
