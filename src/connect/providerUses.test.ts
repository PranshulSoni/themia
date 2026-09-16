// [S-49] Every `uses:` entry in the connect catalog must resolve to a real
// widget label.
//
// The tags render through `t('widget.<key>.label')`, and a missing i18n key
// resolves to the raw key — so a renamed or removed widget would quietly put
// "githubReviewRequested" on a button instead of "GitHub Review Requests".
//
// Source-scanned rather than imported: providers.tsx carries Solid JSX in its
// `hint` fields, which vitest (no solid transform) cannot compile — the same
// reason visibility.ts exists as a separate module.
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { en } from '../locales/en.ts';

const source = readFileSync(join(process.cwd(), 'src/connect/providers.tsx'), 'utf8');

/** `key: 'imap',` … `uses: ['email'],` pairs, in catalog order. */
function catalogUses(): { provider: string; uses: string[] }[] {
  const out: { provider: string; uses: string[] }[] = [];
  const re = /key: '([a-z]+)',[\s\S]*?uses: \[([^\]]*)\]/g;
  for (const m of source.matchAll(re)) {
    const uses = m[2].split(',').map(s => s.trim().replace(/^'|'$/g, '')).filter(Boolean);
    out.push({ provider: m[1], uses });
  }
  return out;
}

const widgetLabels = en.widget as Record<string, { label: string } | undefined>;

describe('S-49 provider "uses" tags', () => {
  const entries = catalogUses();

  it('finds a uses list for every provider in the catalog', () => {
    // Guards the scan itself: if the catalog's shape changes so the regex stops
    // matching, this fails instead of silently vacuously passing below.
    const declaredProviders = [...source.matchAll(/^\s{4}key: '([a-z]+)',$/gm)].map(m => m[1]);
    expect(declaredProviders.length).toBeGreaterThanOrEqual(8);
    expect(entries.map(e => e.provider)).toEqual(declaredProviders);
  });

  it('every provider names at least one widget it feeds', () => {
    for (const { provider, uses } of entries) {
      expect(uses.length, `${provider} lists no widgets`).toBeGreaterThan(0);
    }
  });

  it('every named widget has a label in the message catalog', () => {
    for (const { provider, uses } of entries) {
      for (const key of uses) {
        expect(widgetLabels[key]?.label, `${provider} → widget.${key}.label is missing`).toBeTruthy();
      }
    }
  });

  it('a provider never lists the same widget twice', () => {
    for (const { provider, uses } of entries) {
      expect(new Set(uses).size, `${provider} has duplicate entries`).toBe(uses.length);
    }
  });

  it('the mail/calendar/contacts protocols map to their one widget', () => {
    const by = Object.fromEntries(entries.map(e => [e.provider, e.uses]));
    expect(by.imap).toEqual(['email']);
    expect(by.caldav).toEqual(['calendar']);
    expect(by.carddav).toEqual(['contacts']);
    // Microsoft is the broadest connection; if it ever stops covering these,
    // the picker would undersell it.
    expect(by.microsoft).toEqual(expect.arrayContaining(['email', 'calendar', 'contacts']));
  });
});
