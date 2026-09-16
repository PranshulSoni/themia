import { describe, it, expect } from 'vitest';
import { validate } from './importValidate.ts';

function baseV3(): Record<string, unknown> {
  return {
    version: 3,
    appVersion: '0.0.0',
    exportedAt: '2026-01-01T00:00:00.000Z',
    screens: [{ id: 'default', name: 'Home' }],
    monitorScreenMap: {},
    defaultScreenForNewDisplays: null,
    globalDesign: {},
    layouts: {},
    editMode: false,
    autoCheckUpdates: true,
  };
}

describe('validate: accepts current shape', () => {
  it('passes a valid v3 export through unchanged (version stays 3)', () => {
    const out = validate(baseV3());
    expect(out.version).toBe(3);
    expect(out.screens).toEqual([{ id: 'default', name: 'Home' }]);
  });
});

describe('validate: migrations', () => {
  it('v1 → v3: bumps version and preserves globalDesign', () => {
    const out = validate({ ...baseV3(), version: 1, globalDesign: { theme: 'dark' } });
    expect(out.version).toBe(3);
    expect(out.globalDesign.theme).toBe('dark');
  });

  it('v2 → v3: folds first screenDesign up into globalDesign and drops screenDesigns', () => {
    const out = validate({
      ...baseV3(),
      version: 2,
      globalDesign: {},
      screenDesigns: { s1: { primaryColor: '#abc', cornerRadius: 8 } },
    });
    expect(out.version).toBe(3);
    expect(out.globalDesign.primaryColor).toBe('#abc');
    expect(out.globalDesign.cornerRadius).toBe(8);
    // `screenDesigns` is dropped on v2→v3 and is not part of ThemiaExport, so
    // read it back through a permissive view to assert it's gone.
    expect((out as unknown as Record<string, unknown>).screenDesigns).toBeUndefined();
  });

  it('v2 → v3: globalDesign wins over the folded screen value', () => {
    const out = validate({
      ...baseV3(),
      version: 2,
      globalDesign: { primaryColor: '#fff' },
      screenDesigns: { s1: { primaryColor: '#abc' } },
    });
    expect(out.globalDesign.primaryColor).toBe('#fff');
  });

  it('header surface: seeds header* from widget* when an old export omits them', () => {
    const out = validate({
      ...baseV3(),
      globalDesign: { widgetBgColor: '#112233', widgetBgOpacity: 0.4 },
    });
    expect(out.globalDesign.headerBgColor).toBe('#112233');
    expect(out.globalDesign.headerBgOpacity).toBe(0.4);
  });

  it('header surface: keeps explicit header* values over the widget* fallback', () => {
    const out = validate({
      ...baseV3(),
      globalDesign: { widgetBgColor: '#112233', widgetBgOpacity: 0.4, headerBgColor: '#aabbcc', headerBgOpacity: 0.9 },
    });
    expect(out.globalDesign.headerBgColor).toBe('#aabbcc');
    expect(out.globalDesign.headerBgOpacity).toBe(0.9);
  });
});

describe('validate: rejects unsafe input (boundary guard)', () => {
  it.each([
    ['null', null],
    ['a number', 5],
    ['a string', 'nope'],
  ])('throws on %s', (_label, input) => {
    expect(() => validate(input)).toThrow(/not a JSON object/);
  });

  it('throws on an unsupported version', () => {
    expect(() => validate({ ...baseV3(), version: 4 })).toThrow(/unsupported version/);
  });

  it('throws when screens is not an array', () => {
    expect(() => validate({ ...baseV3(), screens: 'x' })).toThrow(/missing screens/);
  });

  it('throws when globalDesign is missing', () => {
    expect(() => validate({ ...baseV3(), globalDesign: 'x' })).toThrow(/missing design/);
  });

  it('throws when layouts is missing', () => {
    expect(() => validate({ ...baseV3(), layouts: 'x' })).toThrow(/missing layouts/);
  });
});
