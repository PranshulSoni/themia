import { describe, it, expect } from 'vitest';
import { scanHardcodedText, scanTranslationKeys } from '../tests/i18nScan.ts';
import { en } from './locales/en.ts';

function leafKeys(obj: unknown, prefix = '', out: string[] = []): string[] {
  if (obj && typeof obj === 'object') {
    for (const k of Object.keys(obj as Record<string, unknown>)) {
      leafKeys((obj as Record<string, unknown>)[k], prefix ? `${prefix}.${k}` : k, out);
    }
  } else {
    out.push(prefix);
  }
  return out;
}

// issue #91 ("Enhanced multilingual support"): a Simplified Chinese user found
// whole surfaces still in English. zh already had 100% key parity with en, so
// the parity test could not see it — the strings had never reached the locale
// files at all. This guards the class of bug rather than the instances: a new
// English literal in a user-facing slot fails here instead of shipping.
describe('issue_91 no hardcoded user-facing text', () => {
  it('every config label, description, hint and title comes from t()', () => {
    const offenders = scanHardcodedText(['src', 'widgets']).map(
      (h) => `${h.file.split('\\').join('/')}:${h.line} [${h.slot}] ${h.text}`,
    );
    expect(offenders).toEqual([]);
  });
});

// issue #91: three rows called t() with a key nobody had ever defined
// (`gallery.slideshowInterval.label`, `sunMoon.place.*`, `common.general`), so
// they rendered the raw dotted path to the user in EVERY language.
describe('issue_91 no t() call references an undefined key', () => {
  it('every key used in source exists in en.ts', () => {
    const known = new Set(leafKeys(en));
    const unknown = [...new Set(
      scanTranslationKeys(['src', 'widgets'])
        .filter((r) => !known.has(r.key))
        .map((r) => r.key),
    )];
    expect(unknown).toEqual([]);
  });
});

describe('i18nScan itself', () => {
  it('reports a literal in a scanned slot', () => {
    // Sanity check that the scanner can still fail — a regex typo that matched
    // nothing would make the guard above pass forever.
    const hits = scanHardcodedText(['tests/fixtures/i18nScan']);
    expect(hits.map((h) => h.text)).toContain('Refresh interval');
  });

  it('does not report a translated slot or a brand name', () => {
    const hits = scanHardcodedText(['tests/fixtures/i18nScan']);
    expect(hits.map((h) => h.text)).not.toContain('GitHub');
    expect(hits.some((h) => h.text.includes('translated'))).toBe(false);
  });

  it('reports catalog data properties, not just styled spans', () => {
    // The Add-Widget picker's `description` and the Connections `blurb` never
    // reach a scanned class — they are data the renderer reads.
    const texts = scanHardcodedText(['tests/fixtures/i18nScan']).map((h) => h.text);
    expect(texts).toContain('Shows something useful on your desktop');
    expect(texts).toContain('One line about this connection');
  });

  it('skips strings inside an import.meta.env.DEV block', () => {
    // Vite strips these from a release build, so they are not translation bugs.
    const texts = scanHardcodedText(['tests/fixtures/i18nScan']).map((h) => h.text);
    expect(texts).not.toContain('My Office Layout');
    expect(texts).not.toContain('Dev harness fixture nobody ships');
  });
});
