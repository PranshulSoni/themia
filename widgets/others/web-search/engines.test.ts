import { describe, it, expect } from 'vitest';
import { ENGINES, ENGINE_KEYS, resolveEngine } from './engines.ts';

describe('web-search: engines', () => {
  it('falls back to google for unknown/missing engines', () => {
    expect(resolveEngine(undefined)).toBe('google');
    expect(resolveEngine('')).toBe('google');
    expect(resolveEngine('not-an-engine')).toBe('google');
  });

  it('keeps a known engine', () => {
    expect(resolveEngine('bing')).toBe('bing');
  });

  it('URL-encodes the query for every engine', () => {
    for (const k of ENGINE_KEYS) {
      const url = ENGINES[k].url('hello world & co');
      expect(url).toContain('hello%20world');
      expect(url).not.toContain('hello world');
      expect(url.startsWith('https://')).toBe(true);
    }
  });
});
