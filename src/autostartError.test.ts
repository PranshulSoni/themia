/**
 * #79 / [AS-05]: the "Launch on startup" toggle must be able to tell the user
 * that Themia needs administrator rights. That hinges on the rejection payload
 * being classified, not guessed — so an unrecognised shape must never be
 * promoted into an `elevationRequired` claim.
 */
import { describe, it, expect } from 'vitest';
import { parseAutostartError } from './autostartError.ts';

describe('parseAutostartError', () => {
  it('s79: keeps the backend kind and message', () => {
    const e = parseAutostartError({
      kind: 'elevationRequired',
      message: 'schtasks /Create failed (exit code 1): ERROR: Access is denied.',
    });
    expect(e.kind).toBe('elevationRequired');
    expect(e.message).toContain('Access is denied');
  });

  it('s79: passes through the "other" kind', () => {
    const e = parseAutostartError({ kind: 'other', message: 'policy blocked' });
    expect(e).toEqual({ kind: 'other', message: 'policy blocked' });
  });

  it('s79: an unknown kind degrades to other, never to elevationRequired', () => {
    // A value outside the closed set must not be trusted into the union: the
    // admin advice is only shown when the backend actually classified it.
    const e = parseAutostartError({ kind: 'needsAdmin', message: 'x' });
    expect(e.kind).toBe('other');
  });

  it('s79: a bare string rejection becomes an other-kind error carrying the text', () => {
    const e = parseAutostartError('schtasks /Delete failed (exit code 1)');
    expect(e.kind).toBe('other');
    expect(e.message).toBe('schtasks /Delete failed (exit code 1)');
  });

  it('s79: an Error is unwrapped to its message', () => {
    const e = parseAutostartError(new Error('invoke failed'));
    expect(e).toEqual({ kind: 'other', message: 'invoke failed' });
  });

  it('s79: a typed payload with a non-string message reads as empty, not undefined', () => {
    const e = parseAutostartError({ kind: 'elevationRequired', message: 42 });
    expect(e).toEqual({ kind: 'elevationRequired', message: '' });
  });

  it('s79: null/undefined never throw', () => {
    expect(parseAutostartError(null)).toEqual({ kind: 'other', message: '' });
    expect(parseAutostartError(undefined)).toEqual({ kind: 'other', message: '' });
  });
});
