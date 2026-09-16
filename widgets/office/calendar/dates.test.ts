import { describe, it, expect } from 'vitest';
import { gdt, weekStartOffset, startOfWeek, weekdayOrder, type WeekStart } from './dates.ts';

// [S-41] Regression for issue #48: Google Calendar returns timed events in the
// event's local UTC offset. For US-Eastern (and the Americas generally) that
// offset is negative, e.g. `2026-06-25T10:00:00-04:00`. The old parser only
// recognised `Z` / `+` as "already zoned", so it appended a stray `Z` to
// negative-offset strings, producing `Invalid Date`, so timed events vanished
// from Month view and showed "Invalid Date" in Agenda. All-day events (emitted
// as `...T00:00:00Z`) were unaffected, matching the reporter's symptoms.
describe('gdt datetime parsing [S-41]', () => {
  it('S-41_ parses a negative RFC3339 offset without corrupting it', () => {
    const d = gdt('2026-06-25T10:00:00-04:00');
    expect(Number.isNaN(d.getTime())).toBe(false);
    expect(d.toISOString()).toBe('2026-06-25T14:00:00.000Z');
  });

  it('S-41_ parses a half-hour negative offset', () => {
    // e.g. America/St_Johns (-02:30 in DST): offset with non-zero minutes.
    expect(gdt('2026-06-25T10:00:00-02:30').toISOString()).toBe('2026-06-25T12:30:00.000Z');
  });

  it('S-41_ parses a positive offset', () => {
    expect(gdt('2026-06-25T10:00:00+02:00').toISOString()).toBe('2026-06-25T08:00:00.000Z');
  });

  it('S-41_ treats a naive (zoneless) datetime as UTC', () => {
    expect(gdt('2026-06-25T10:00:00').toISOString()).toBe('2026-06-25T10:00:00.000Z');
  });

  it('S-41_ keeps an explicit Z', () => {
    expect(gdt('2026-06-25T10:00:00Z').toISOString()).toBe('2026-06-25T10:00:00.000Z');
  });

  it('S-41_ empty string maps to the epoch', () => {
    expect(gdt('').getTime()).toBe(0);
  });
});

// [S-46] Start day of the week is configurable (Monday default, or Sunday).
// The month grid start, week-view start and weekday header row all derive from
// these pure helpers so both modes align correctly (issue #57a).
describe('week-start helpers [S-46]', () => {
  it('S-46_ weekStartOffset (monday): days back from a getDay() to the Monday', () => {
    // getDay(): 0=Sun .. 6=Sat. Under a Monday start, Monday is offset 0 and
    // Sunday is the farthest back (6).
    expect(weekStartOffset(1, 'monday')).toBe(0); // Mon
    expect(weekStartOffset(2, 'monday')).toBe(1); // Tue
    expect(weekStartOffset(3, 'monday')).toBe(2); // Wed
    expect(weekStartOffset(6, 'monday')).toBe(5); // Sat
    expect(weekStartOffset(0, 'monday')).toBe(6); // Sun
  });

  it('S-46_ weekStartOffset (sunday): days back from a getDay() to the Sunday', () => {
    expect(weekStartOffset(0, 'sunday')).toBe(0); // Sun
    expect(weekStartOffset(1, 'sunday')).toBe(1); // Mon
    expect(weekStartOffset(6, 'sunday')).toBe(6); // Sat
  });

  it('S-46_ startOfWeek (monday) returns the Monday on/before the date', () => {
    const d = new Date(2026, 6, 16, 9, 30); // arbitrary time-of-day preserved
    const s = startOfWeek(d, 'monday');
    expect(s.getDay()).toBe(1); // Monday
    const diff = (d.getTime() - s.getTime()) / 86_400_000;
    expect(diff).toBeGreaterThanOrEqual(0);
    expect(diff).toBeLessThan(7);
  });

  it('S-46_ startOfWeek (sunday) returns the Sunday on/before the date', () => {
    const d = new Date(2026, 6, 16, 9, 30);
    const s = startOfWeek(d, 'sunday');
    expect(s.getDay()).toBe(0); // Sunday
    const diff = (d.getTime() - s.getTime()) / 86_400_000;
    expect(diff).toBeGreaterThanOrEqual(0);
    expect(diff).toBeLessThan(7);
  });

  it('S-46_ startOfWeek does not mutate its input', () => {
    const d = new Date(2026, 6, 16, 9, 30);
    const before = d.getTime();
    startOfWeek(d, 'monday');
    expect(d.getTime()).toBe(before);
  });

  it('S-46_ weekdayOrder lists the 7 getDay() columns left→right', () => {
    // Monday start: Mo Tu We Th Fr Sa Su
    expect(weekdayOrder('monday')).toEqual([1, 2, 3, 4, 5, 6, 0]);
    // Sunday start: Su Mo Tu We Th Fr Sa
    expect(weekdayOrder('sunday')).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });

  it('S-46_ WeekStart is a closed set the type accepts', () => {
    const a: WeekStart = 'monday';
    const b: WeekStart = 'sunday';
    expect([a, b]).toEqual(['monday', 'sunday']);
  });
});
