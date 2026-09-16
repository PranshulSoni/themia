import { describe, it, expect } from 'vitest';
import { parseRich } from './i18nRich.ts';
import { en } from './locales/en.ts';
import { zh } from './locales/zh.ts';

describe('parseRich', () => {
  it('returns a single text run when there is no markup', () => {
    expect(parseRich('plain sentence')).toEqual([
      { kind: 'text', text: 'plain sentence' },
    ]);
  });

  it('splits *strong* and _em_ runs out of the surrounding text', () => {
    expect(parseRich('*Views*: switch with _View settings_ later')).toEqual([
      { kind: 'strong', text: 'Views' },
      { kind: 'text', text: ': switch with ' },
      { kind: 'em', text: 'View settings' },
      { kind: 'text', text: ' later' },
    ]);
  });

  it('keeps an unpaired marker literal instead of swallowing the rest', () => {
    expect(parseRich('2 * 3 is six')).toEqual([{ kind: 'text', text: '2 * 3 is six' }]);
    expect(parseRich('a_b')).toEqual([{ kind: 'text', text: 'a_b' }]);
  });

  it('marks CJK spans with no surrounding whitespace', () => {
    expect(parseRich('*视图*：可切换的桌面')).toEqual([
      { kind: 'strong', text: '视图' },
      { kind: 'text', text: '：可切换的桌面' },
    ]);
  });

  it('is not an HTML parser: angle brackets stay text', () => {
    expect(parseRich('<b>hi</b>')).toEqual([{ kind: 'text', text: '<b>hi</b>' }]);
  });

  it('is stateless across calls (shared regex lastIndex is reset)', () => {
    const s = '*a* and *b*';
    expect(parseRich(s)).toEqual(parseRich(s));
  });
});

// issue #91: the Help guide was hardcoded English. Its prose now lives in the
// locale files, so the markers themselves have to survive translation — an
// unbalanced marker renders a stray `*` to the user rather than emphasis.
describe('issue_91 help guide prose', () => {
  const helpStrings = (dict: Record<string, unknown>): [string, string][] => {
    const out: [string, string][] = [];
    const walk = (o: unknown, path: string) => {
      if (typeof o === 'string') out.push([path, o]);
      else if (o && typeof o === 'object') {
        for (const [k, v] of Object.entries(o as Record<string, unknown>)) {
          walk(v, path ? `${path}.${k}` : k);
        }
      }
    };
    walk(dict.help, 'help');
    return out;
  };

  for (const [name, dict] of Object.entries({ en, zh })) {
    it(`${name}: every marker in the help guide is balanced`, () => {
      const unbalanced = helpStrings(dict as Record<string, unknown>)
        .filter(([, s]) => (s.match(/\*/g)?.length ?? 0) % 2 !== 0
                        || (s.match(/_/g)?.length ?? 0) % 2 !== 0)
        .map(([k]) => k);
      expect(unbalanced).toEqual([]);
    });
  }

  it('zh translates the guide rather than falling back to English prose', () => {
    // A fallback would leave the English sentence verbatim in zh.
    const enMap = new Map(helpStrings(en as unknown as Record<string, unknown>));
    const identical = helpStrings(zh as unknown as Record<string, unknown>)
      .filter(([k, s]) => enMap.get(k) === s)
      // Tab labels are single words that legitimately differ; the prose is what matters.
      .filter(([k]) => !k.startsWith('help.tabs.'))
      .map(([k]) => k);
    expect(identical).toEqual([]);
  });
});
