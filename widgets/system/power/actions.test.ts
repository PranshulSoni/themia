// @vitest-environment jsdom
// `actions.ts` pulls in i18n for the labels, and the design store it reaches
// through registers a `storage` listener at import time.
import { describe, it, expect } from 'vitest';
import {
  ACTION_DEFS,
  POWER_ACTIONS,
  isPowerAction,
  needsConfirm,
  visibleActions,
  type PowerActionName,
} from './actions.ts';

describe('S-02 action set is closed at the config boundary', () => {
  it('every registry def is a known action', () => {
    for (const def of ACTION_DEFS) expect(isPowerAction(def.key)).toBe(true);
  });

  it('every action has a def, so nothing renders without an icon/label', () => {
    const keys = ACTION_DEFS.map((d) => d.key);
    for (const action of POWER_ACTIONS) expect(keys).toContain(action);
  });

  it('drops keys from an older build instead of passing them to invoke', () => {
    expect(visibleActions(['shutdown', 'sleepMode', 'lock', ''])).toEqual(['shutdown', 'lock']);
  });

  it('drops non-strings without throwing', () => {
    expect(visibleActions(['lock', 42, null, { key: 'shutdown' }])).toEqual(['lock']);
  });

  it('falls back to the defaults when nothing is persisted', () => {
    expect(visibleActions(undefined)).toEqual(['shutdown', 'restart', 'sleep', 'lock']);
  });

  it('honours an explicit empty selection rather than re-adding defaults', () => {
    expect(visibleActions([])).toEqual([]);
  });
});

describe('S-05 confirm covers exactly the session-ending actions', () => {
  const endsSession: PowerActionName[] = ['shutdown', 'restart', 'signOut'];
  const recoverable: PowerActionName[] = ['sleep', 'hibernate', 'lock', 'screenOff'];

  it('arms the actions that close every app', () => {
    for (const action of endsSession) expect(needsConfirm(action, true)).toBe(true);
  });

  it('never arms the actions that come back to the same desktop', () => {
    for (const action of recoverable) expect(needsConfirm(action, false)).toBe(false);
    for (const action of recoverable) expect(needsConfirm(action, true)).toBe(false);
  });

  it('runs everything on one click once the user turns confirm off', () => {
    for (const action of POWER_ACTIONS) expect(needsConfirm(action, false)).toBe(false);
  });

  it('covers the whole union — a new action is confirmed or explicitly not', () => {
    expect([...endsSession, ...recoverable].sort()).toEqual([...POWER_ACTIONS].sort());
  });
});
