// @vitest-environment jsdom
//
// Characterization of the op-log persistence schema: the guard that keeps a
// structurally-broken persisted entry from reaching the undo path (where
// kind-specific fields are dereferenced). Also pins the "drop orphaned pending
// entries on load" behavior: their resolving Promise lived only in the window
// that started the op, so after a reload they can never settle.

import { describe, it, expect, vi } from 'vitest';
import { opLogSchema } from './operationLog.ts';
import { loadPersisted, type ReadableStorage } from './persistedStore.ts';

function fakeStorage(initial: Record<string, string> = {}): ReadableStorage {
  const map = new Map<string, string>(Object.entries(initial));
  return { getItem: (k: string) => (map.has(k) ? map.get(k)! : null) };
}

const valid = { id: 'a', ts: 100, kind: 'rename', dir: '/d', fromPath: '/d/new', originalPath: '/d/old' };

describe('operationLog schema: undo-path guard', () => {
  it('absent key → empty log, no corruption log', () => {
    const onCorrupt = vi.fn();
    expect(loadPersisted('k', opLogSchema, fakeStorage(), { onCorrupt })).toEqual({ entries: [] });
    expect(onCorrupt).not.toHaveBeenCalled();
  });

  it('corrupt JSON → empty log + loud log', () => {
    const onCorrupt = vi.fn();
    expect(loadPersisted('k', opLogSchema, fakeStorage({ k: '{bad' }), { onCorrupt })).toEqual({ entries: [] });
    expect(onCorrupt).toHaveBeenCalledTimes(1);
  });

  it('non-object payload or non-array entries → empty log', () => {
    expect(opLogSchema.parse(42)).toEqual({ entries: [] });
    expect(opLogSchema.parse(null)).toEqual({ entries: [] });
    expect(opLogSchema.parse({ entries: 'nope' })).toEqual({ entries: [] });
  });

  it('keeps a structurally-valid entry verbatim', () => {
    expect(opLogSchema.parse({ entries: [valid] })).toEqual({ entries: [valid] });
  });

  it('drops entries missing id/ts, with an unknown kind, or still pending', () => {
    const noId = { ts: 1, kind: 'rename' };
    const noTs = { id: 'x', kind: 'rename' };
    const badKind = { id: 'y', ts: 1, kind: 'frobnicate' };
    const pending = { id: 'z', ts: 1, kind: 'delete', pending: true };
    const got = opLogSchema.parse({ entries: [valid, noId, noTs, badKind, pending, null, 'str'] });
    expect(got.entries.map((e) => e.id)).toEqual(['a']);
  });
});
