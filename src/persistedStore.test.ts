import { describe, it, expect, vi } from 'vitest';
import {
  loadPersisted,
  savePersisted,
  savePersistedBare,
  BAK_PREFIX,
  objectSchema,
  isString,
  isBoolean,
  isNumber,
  isOneOf,
  isArrayOf,
  type PersistedSchema,
} from './persistedStore.ts';

// A minimal in-memory Storage stand-in (the real localStorage isn't in node env).
function fakeStorage(initial: Record<string, string> = {}) {
  const map = new Map<string, string>(Object.entries(initial));
  return {
    getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
    setItem: (k: string, v: string) => void map.set(k, v),
    _dump: () => Object.fromEntries(map),
    _raw: (k: string) => map.get(k) ?? null,
  };
}

interface Settings {
  theme: 'dark' | 'light';
  scale: number;
  enabled: boolean;
}

function settingsSchema(version = 1, migrate?: PersistedSchema<Settings>['migrate']): PersistedSchema<Settings> {
  return objectSchema<Settings>({
    version,
    defaults: () => ({ theme: 'dark', scale: 1, enabled: true }),
    fields: {
      theme: isOneOf('dark', 'light'),
      scale: isNumber,
      enabled: isBoolean,
    },
    migrate,
  });
}

describe('loadPersisted: absent vs corrupt', () => {
  it('returns defaults silently when the key is absent', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', settingsSchema(), fakeStorage(), { onCorrupt });
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
    expect(onCorrupt).not.toHaveBeenCalled();
  });

  it('returns defaults AND logs loudly when the JSON is corrupt', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', settingsSchema(), fakeStorage({ k: '{not json' }), { onCorrupt });
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
    expect(onCorrupt).toHaveBeenCalledTimes(1);
    expect(onCorrupt.mock.calls[0][0]).toContain('corrupt JSON');
  });

  it('treats an empty-string value as absent (silent defaults)', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', settingsSchema(), fakeStorage({ k: '' }), { onCorrupt });
    expect(got.theme).toBe('dark');
    expect(onCorrupt).not.toHaveBeenCalled();
  });
});

describe('loadPersisted: field validation', () => {
  it('keeps valid fields and falls back per-field on wrong types', () => {
    // legacy bare blob: scale is a string, theme is invalid, enabled is fine.
    const raw = JSON.stringify({ theme: 'neon', scale: 'big', enabled: false });
    const got = loadPersisted('k', settingsSchema(), fakeStorage({ k: raw }));
    expect(got.theme).toBe('dark'); // invalid enum → default
    expect(got.scale).toBe(1); // wrong type → default
    expect(got.enabled).toBe(false); // valid → kept
  });

  it('ignores unknown/extra fields', () => {
    const raw = JSON.stringify({ theme: 'light', scale: 2, enabled: true, bogus: 99 });
    const got = loadPersisted('k', settingsSchema(), fakeStorage({ k: raw }));
    expect(got).toEqual({ theme: 'light', scale: 2, enabled: true });
    expect(Object.keys(got)).not.toContain('bogus');
  });

  it('rejects a non-object payload, returning full defaults', () => {
    const got = loadPersisted('k', settingsSchema(), fakeStorage({ k: '42' }));
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
  });
});

describe('save/load round-trip + envelope', () => {
  it('round-trips a value and writes a versioned envelope', () => {
    const store = fakeStorage();
    const schema = settingsSchema(3);
    savePersisted('k', schema, { theme: 'light', scale: 2, enabled: false }, store);
    const rawOnDisk = JSON.parse(store._raw('k')!);
    expect(rawOnDisk.__sv).toBe(3);
    expect(rawOnDisk.data).toEqual({ theme: 'light', scale: 2, enabled: false });
    expect(loadPersisted('k', schema, store)).toEqual({ theme: 'light', scale: 2, enabled: false });
  });
});

describe('savePersistedBare: external-reader-compatible format', () => {
  it('writes the bare value (no envelope) and loadPersisted still validates it', () => {
    const store = fakeStorage();
    const schema = settingsSchema(2);
    savePersistedBare('k', { theme: 'light', scale: 2, enabled: false }, store);
    // On disk it's the plain object, so an external reader sees the real fields.
    expect(JSON.parse(store._raw('k')!)).toEqual({ theme: 'light', scale: 2, enabled: false });
    // And loadPersisted reads it back (as a legacy v0 payload) + validates.
    expect(loadPersisted('k', schema, store)).toEqual({ theme: 'light', scale: 2, enabled: false });
  });

  it('still validates a bare value that is partially corrupt', () => {
    const store = fakeStorage();
    savePersistedBare('k', { theme: 'neon', scale: 'x', enabled: true }, store);
    const got = loadPersisted('k', settingsSchema(), store);
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
  });
});

describe('migration', () => {
  it('migrates a legacy bare blob (version 0) via migrate()', () => {
    // Legacy stored `mode` instead of `theme`; migrate maps it across.
    const legacy = JSON.stringify({ mode: 'light', scale: 2, enabled: true });
    const schema = settingsSchema(2, (raw, from) => {
      expect(from).toBe(0); // bare blob → version 0
      const r = raw as Record<string, unknown>;
      return { theme: r.mode, scale: r.scale, enabled: r.enabled };
    });
    const got = loadPersisted('k', schema, fakeStorage({ k: legacy }));
    expect(got.theme).toBe('light');
    expect(got.scale).toBe(2);
  });

  it('does NOT migrate when stored version already equals current', () => {
    const store = fakeStorage();
    const schema = settingsSchema(2, () => {
      throw new Error('migrate should not run for current version');
    });
    savePersisted('k', schema, { theme: 'dark', scale: 1, enabled: true }, store);
    expect(() => loadPersisted('k', schema, store)).not.toThrow();
  });

  it('falls back to defaults + logs when migration throws', () => {
    const onCorrupt = vi.fn();
    const schema = settingsSchema(2, () => {
      throw new Error('boom');
    });
    const got = loadPersisted('k', schema, fakeStorage({ k: '{"x":1}' }), { onCorrupt });
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
    expect(onCorrupt).toHaveBeenCalledTimes(1);
    expect(onCorrupt.mock.calls[0][0]).toContain('migration from v0 failed');
  });
});

describe('fail-safe on schema bugs / storage errors', () => {
  it('returns defaults + logs when parse() itself throws', () => {
    const onCorrupt = vi.fn();
    const badSchema: PersistedSchema<Settings> = {
      version: 1,
      defaults: () => ({ theme: 'dark', scale: 1, enabled: true }),
      parse: () => {
        throw new Error('schema bug');
      },
    };
    const got = loadPersisted('k', badSchema, fakeStorage({ k: '{}' }), { onCorrupt });
    expect(got.theme).toBe('dark');
    expect(onCorrupt.mock.calls[0][0]).toContain('parse threw');
  });

  it('reports onError when the write throws (e.g. quota)', () => {
    const onError = vi.fn();
    const throwing: { getItem: (k: string) => string | null; setItem: (k: string, v: string) => void } = {
      getItem: () => null, // no prior value → backup step is a no-op
      setItem: () => {
        throw new Error('QuotaExceeded');
      },
    };
    savePersisted('k', settingsSchema(), { theme: 'dark', scale: 1, enabled: true }, throwing, { onError });
    expect(onError).toHaveBeenCalledTimes(1);
    expect(onError.mock.calls[0][0]).toContain('write failed');
  });
});

describe('last-known-good backup (1.19)', () => {
  it('first save writes no backup; a second save backs up the prior value', () => {
    const s = fakeStorage();
    savePersisted('k', settingsSchema(), { theme: 'dark', scale: 1, enabled: true }, s);
    expect(s._raw(`${BAK_PREFIX}k`)).toBeNull(); // nothing to back up on the first write
    savePersisted('k', settingsSchema(), { theme: 'light', scale: 2, enabled: false }, s);
    const bak = JSON.parse(s._raw(`${BAK_PREFIX}k`)!);
    expect(bak.data).toEqual({ theme: 'dark', scale: 1, enabled: true }); // the prior value
  });

  it('recovers from the backup when the primary is corrupted out-of-band', () => {
    const s = fakeStorage();
    savePersisted('k', settingsSchema(), { theme: 'dark', scale: 1, enabled: true }, s);
    savePersisted('k', settingsSchema(), { theme: 'light', scale: 2, enabled: false }, s);
    s.setItem('k', '{corrupt'); // primary wrecked; backup still holds the first value
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', settingsSchema(), s, { onCorrupt });
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
    expect(onCorrupt).toHaveBeenCalledTimes(1);
    expect(onCorrupt.mock.calls[0][0]).toContain('recovered from last-known-good backup');
  });

  it('falls back to defaults + logs when primary AND backup are both corrupt', () => {
    const s = fakeStorage({ k: '{bad', [`${BAK_PREFIX}k`]: 'also bad' });
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', settingsSchema(), s, { onCorrupt });
    expect(got).toEqual({ theme: 'dark', scale: 1, enabled: true });
    expect(onCorrupt).toHaveBeenCalledTimes(1);
    expect(onCorrupt.mock.calls[0][0]).toContain('no usable backup');
  });

  it('savePersistedBare maintains the backup too', () => {
    const s = fakeStorage();
    savePersistedBare('k', { theme: 'dark', scale: 1, enabled: true }, s);
    savePersistedBare('k', { theme: 'light', scale: 9, enabled: false }, s);
    expect(JSON.parse(s._raw(`${BAK_PREFIX}k`)!)).toEqual({ theme: 'dark', scale: 1, enabled: true });
  });
});

describe('guards', () => {
  it('isNumber rejects NaN / Infinity / strings', () => {
    expect(isNumber(3)).toBe(true);
    expect(isNumber(NaN)).toBe(false);
    expect(isNumber(Infinity)).toBe(false);
    expect(isNumber('3')).toBe(false);
  });

  it('isString / isBoolean basics', () => {
    expect(isString('x')).toBe(true);
    expect(isString(1)).toBe(false);
    expect(isBoolean(false)).toBe(true);
    expect(isBoolean(0)).toBe(false);
  });

  it('isOneOf accepts members, rejects non-members', () => {
    const g = isOneOf('a', 'b');
    expect(g('a')).toBe(true);
    expect(g('c')).toBe(false);
    expect(g(1)).toBe(false);
  });

  it('isArrayOf validates every element', () => {
    const g = isArrayOf(isString);
    expect(g(['a', 'b'])).toBe(true);
    expect(g(['a', 2])).toBe(false);
    expect(g('a')).toBe(false);
  });
});

describe('array-shaped store', () => {
  it('validates an object containing a typed array', () => {
    interface Clip {
      paths: string[];
      mode: 'copy' | 'cut';
    }
    const schema = objectSchema<Clip>({
      version: 1,
      defaults: () => ({ paths: [], mode: 'copy' }),
      fields: { paths: isArrayOf(isString), mode: isOneOf('copy', 'cut') },
    });
    // a poisoned array (mixed types) → default empty array; mode kept
    const raw = JSON.stringify({ paths: ['a', 5], mode: 'cut' });
    const got = loadPersisted('k', schema, fakeStorage({ k: raw }));
    expect(got.paths).toEqual([]);
    expect(got.mode).toBe('cut');
  });
});
