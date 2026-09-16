import { describe, it, expect } from 'vitest';
import { QUOTES, quoteForDay, localDayNumber } from './quotes.ts';

describe('quote: quoteForDay', () => {
  it('is deterministic for the same local day', () => {
    const d1 = new Date(2026, 5, 5, 9);
    const d2 = new Date(2026, 5, 5, 22);
    expect(quoteForDay(d1)).toEqual(quoteForDay(d2));
  });

  it('returns an in-bounds entry with text and author', () => {
    const q = quoteForDay(new Date(2026, 0, 1));
    expect(QUOTES).toContainEqual(q);
    expect(q.text.length).toBeGreaterThan(0);
    expect(q.author.length).toBeGreaterThan(0);
  });

  it('advances by one entry per day', () => {
    const a = localDayNumber(new Date(2026, 5, 5));
    const b = localDayNumber(new Date(2026, 5, 6));
    expect(b - a).toBe(1);
  });
});
