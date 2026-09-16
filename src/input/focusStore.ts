/**
 * Focus store.
 *
 * Tracks the currently-focused text input element so that other input
 * subsystems (hotkeys, escapeStack, isInteractive memo) can reason about
 * "is the user typing" without each re-implementing focusin/focusout.
 *
 * Input-like means <input>, <textarea>, or [contenteditable="true"].
 *
 * Also tracks `hoverFocusable`: whether the cursor is currently over such
 * an element. This is wired as an interactivity reason so the widget
 * window is already flipped out of WS_EX_NOACTIVATE by the time the user
 * clicks, otherwise the first click into a textarea paints a caret but
 * the window cannot receive keyboard input until a second click, because
 * the Rust bridge round-trip only completes after focusin fires.
 */

import { createSignal, type Accessor } from 'solid-js';
import { debugLog } from '../debugLog.ts';

// ---------------------------------------------------------------------------
// Signals
// ---------------------------------------------------------------------------

const [activeInput_, setActiveInput] = createSignal<HTMLElement | null>(null);
const [hoverFocusable_, setHoverFocusable] = createSignal(false);

/** The currently-focused input/textarea element, or null. */
export const activeInput: Accessor<HTMLElement | null> = activeInput_;

/** True when an input or textarea is focused. */
export const inputFocused: Accessor<boolean> = () => activeInput_() !== null;

/**
 * True while the cursor is hovering a focusable text element. Drives an
 * eager interactivity reason so WS_EX_NOACTIVATE is already removed by
 * the time the user clicks.
 */
export const hoverFocusable: Accessor<boolean> = hoverFocusable_;

// ---------------------------------------------------------------------------
// Detection
// ---------------------------------------------------------------------------

function isInputEl(el: EventTarget | null): el is HTMLElement {
  if (!el || !(el instanceof HTMLElement)) return false;
  // contenteditable counts: the Notes markdown editor is one, and without this
  // the WH_KEYBOARD_LL hook keeps eating its keys and hotkeys still fire while
  // the user types (the same failure textareas hit before `isInputEl` existed).
  return el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable;
}

/** True when `el` (or any ancestor) is a focusable text surface. */
function isFocusableText(el: EventTarget | null): boolean {
  if (!(el instanceof HTMLElement)) return false;
  return !!el.closest('input, textarea, [contenteditable="true"], [data-focusable="true"]');
}

function describeEl(el: HTMLElement | null): string {
  if (!el) return 'null';
  const tag = el.tagName.toLowerCase();
  const id = el.id ? `#${el.id}` : '';
  const cls = typeof el.className === 'string' && el.className
    ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.')
    : '';
  return `${tag}${id}${cls}`;
}

function onFocusIn(e: FocusEvent) {
  if (isInputEl(e.target)) {
    debugLog('focus', `focusin ${describeEl(e.target as HTMLElement)}`);
    setActiveInput(e.target);
  }
}

function onFocusOut(e: FocusEvent) {
  // Only clear when focus moves away from input-land. If focus transitions
  // between two inputs, focusin fires next and overwrites with the new one.
  if (!isInputEl(e.relatedTarget)) {
    const prev = activeInput_();
    if (prev) debugLog('focus', `focusout ${describeEl(prev)}`);
    setActiveInput(null);
  }
}

function onWindowFocus() {
  debugLog('focus', 'window focus');
}

function onWindowBlur() {
  debugLog('focus', 'window blur');
  // Safety: if the window loses focus while we believed the cursor was
  // over an input, clear the flag, otherwise the interactivity reason
  // stays pinned on across app-switches.
  if (hoverFocusable_()) setHoverFocusable(false);
}

function onMouseOver(e: MouseEvent) {
  if (isFocusableText(e.target) && !hoverFocusable_()) {
    debugLog('focus', `hover-focusable enter ${describeEl(e.target as HTMLElement)}`);
    setHoverFocusable(true);
  }
}

function onMouseOut(e: MouseEvent) {
  // `mouseout` fires for every boundary crossing; only flip off when the
  // cursor is genuinely leaving focusable-text territory.
  if (!isFocusableText(e.relatedTarget) && hoverFocusable_()) {
    debugLog('focus', `hover-focusable leave`);
    setHoverFocusable(false);
  }
}

// ---------------------------------------------------------------------------
// Install
// ---------------------------------------------------------------------------

let installed = false;

/** Install the global focus + hover listeners. Idempotent. */
export function initFocusStore(): void {
  if (installed) return;
  installed = true;
  window.addEventListener('focusin', onFocusIn);
  window.addEventListener('focusout', onFocusOut);
  window.addEventListener('focus', onWindowFocus);
  window.addEventListener('blur', onWindowBlur);
  window.addEventListener('mouseover', onMouseOver);
  window.addEventListener('mouseout', onMouseOut);
}
