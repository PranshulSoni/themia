import { describe, it, expect, vi } from 'vitest';
import { resolveHeaderMode, isHeaderMode, type HeaderMode } from './headerMode.ts';

// WidgetHeader.SPEC [S-16] resolution order:
//   noHeader → 'none'
//   valid config.headerMode → that
//   legacy config.hideHeader === true → 'none'
//   registry defaultHeaderMode
//   else 'normal'

describe('isHeaderMode', () => {
  it('accepts only the closed set [S-01]', () => {
    for (const m of ['normal', 'compact', 'none'] as HeaderMode[]) {
      expect(isHeaderMode(m)).toBe(true);
    }
    for (const v of ['', 'hidden', 'Normal', 0, null, undefined, {}]) {
      expect(isHeaderMode(v)).toBe(false);
    }
  });
});

describe('resolveHeaderMode [S-16]', () => {
  it('noHeader widget always resolves to none, even with a config override', () => {
    expect(resolveHeaderMode({ headerMode: 'normal' }, { noHeader: true })).toBe('none');
    expect(resolveHeaderMode(undefined, { noHeader: true })).toBe('none');
  });

  it('explicit valid headerMode wins', () => {
    expect(resolveHeaderMode({ headerMode: 'compact' }, { defaultHeaderMode: 'normal' })).toBe('compact');
    expect(resolveHeaderMode({ headerMode: 'none' }, {})).toBe('none');
  });

  it('legacy hideHeader:true maps to none when no headerMode is set [S-15]', () => {
    expect(resolveHeaderMode({ hideHeader: true }, {})).toBe('none');
    expect(resolveHeaderMode({ hideHeader: true }, { defaultHeaderMode: 'compact' })).toBe('none');
  });

  it('headerMode takes precedence over a stale legacy hideHeader', () => {
    expect(resolveHeaderMode({ headerMode: 'compact', hideHeader: true }, {})).toBe('compact');
  });

  it('falls back to the registry default, then normal', () => {
    expect(resolveHeaderMode({}, { defaultHeaderMode: 'compact' })).toBe('compact');
    expect(resolveHeaderMode({}, {})).toBe('normal');
    expect(resolveHeaderMode(undefined, {})).toBe('normal');
    expect(resolveHeaderMode({ hideHeader: false }, { defaultHeaderMode: 'compact' })).toBe('compact');
  });

  it('out-of-set persisted headerMode is ignored with a loud warn, falls through', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    expect(resolveHeaderMode({ headerMode: 'bogus' }, { defaultHeaderMode: 'compact' })).toBe('compact');
    expect(warn).toHaveBeenCalledOnce();
    warn.mockRestore();
  });
});
