// Source scanner for untranslated UI text (issue #91).
//
// The complaint was not that a translation was wrong, but that whole surfaces
// never reached the locale files at all — they were English literals in JSX, so
// no locale could override them. A key-parity check cannot see that class of
// bug: the key simply does not exist. This scans the source instead.
//
// Deliberately narrow: it only inspects slots that are ALWAYS user-visible
// chrome (the config-panel label/description spans, field hints, inline error
// text, and the placeholder/title/aria-label/onLabel/offLabel attributes). Body
// copy inside arbitrary elements is not scanned, so this is a floor on the
// problem, not a ceiling.

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

export interface Hit {
  file: string;
  line: number;
  text: string;
  slot: string;
}

/** Class names whose text content is user-facing copy. */
const TEXT_SLOTS = [
  'design-label',
  'design-desc',
  'field-hint',
  'add-error',
  'section-label',
];

/** Attributes carrying user-facing copy. */
const ATTR_SLOTS = ['placeholder', 'title', 'aria-label', 'onLabel', 'offLabel', 'searchPlaceholder'];

/** Object properties carrying user-facing copy.
 *
 *  `description` is the Add-Widget picker's subtitle (rendered by
 *  AddWidgetPage), `blurb` and `hint` belong to the Connections catalog, and
 *  `label` covers both context-menu items and dropdown options. All four were
 *  invisible to the class-based scan above because they never reach a styled
 *  span — they are data the renderer reads. */
const PROP_SLOTS = ['description', 'blurb', 'hint', 'label'];

/** Looks like English prose a user would read, rather than a code token.
 *
 *  Requires a leading capital and at least one lowercase letter, which keeps
 *  identifiers ("PR", "URL"), CSS values and enum-ish tokens out. Pure brand
 *  and protocol names are listed below — translating "GitHub" is not a bug. */
const BRANDS = new Set([
  'GitHub', 'Google', 'Microsoft', 'Outlook', 'Gmail', 'OneDrive', 'Claude',
  'Claude Code', 'Themia', 'Windows', 'Spotify', 'YouTube',
  'Bing', 'Reddit', 'Google DNS',
  'IMAP', 'CalDAV', 'CardDAV', 'RSS', 'Atom', 'RSS / Atom', 'Nextcloud',
  'Exchange Online',
]);

/** Byte ranges guarded by `import.meta.env.DEV`.
 *
 *  `src/main.tsx` hosts the browser harnesses used to render a widget without
 *  Tauri. Their fixture strings ("My Office Layout", "Shopping list") are not
 *  translation bugs — Vite strips the whole block from a release build. Skipping
 *  the ranges rather than the file keeps main.tsx's real, shipped strings in
 *  scope. */
function devRanges(text: string): [number, number][] {
  const out: [number, number][] = [];
  for (const m of text.matchAll(/import\.meta\.env\.DEV/g)) {
    const open = text.indexOf('{', m.index);
    if (open < 0) continue;
    let depth = 0;
    for (let i = open; i < text.length; i++) {
      if (text[i] === '{') depth++;
      else if (text[i] === '}' && --depth === 0) { out.push([m.index, i]); break; }
    }
  }
  return out;
}

const inRanges = (ranges: [number, number][], i: number) =>
  ranges.some(([a, b]) => i >= a && i <= b);

function isProse(s: string): boolean {
  const t = s.trim();
  if (t.length < 2) return false;
  if (BRANDS.has(t)) return false;
  if (!/^[A-Z]/.test(t)) return false;
  if (!/[a-z]/.test(t)) return false;
  // JSX expression or entity rather than a literal.
  if (/[{}<>]/.test(t)) return false;
  return true;
}

function walk(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (e === 'node_modules' || e === 'target' || e === 'dist' || e === '.git') continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (p.endsWith('.tsx') && !p.includes('.test.')) out.push(p);
  }
  return out;
}

const textRe = new RegExp(
  `class="[^"]*\\b(${TEXT_SLOTS.join('|')})\\b[^"]*"\\s*>([^<>{}]+)<`,
  'g',
);
const attrRe = new RegExp(`\\b(${ATTR_SLOTS.join('|')})="([^"]+)"`, 'g');
const propRe = new RegExp(`\\b(${PROP_SLOTS.join('|')}):\\s*'([^']+)'`, 'g');

/** Every `t('some.key')` referenced from source under `roots`.
 *
 *  A key the locale files never define is worse than an untranslated string:
 *  `t()` returns the key itself, so the user reads "sunMoon.place.label" in
 *  every language, English included. */
export function scanTranslationKeys(roots: string[]): { key: string; file: string }[] {
  const out: { key: string; file: string }[] = [];
  for (const root of roots) {
    for (const file of walkAll(root)) {
      const text = readFileSync(file, 'utf8');
      for (const m of text.matchAll(/\bt\(\s*'([a-zA-Z0-9_.]+)'/g)) {
        out.push({ key: m[1], file });
      }
    }
  }
  return out;
}

/** Like `walk`, but includes plain `.ts` too — `t()` is called from both. */
function walkAll(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (e === 'node_modules' || e === 'target' || e === 'dist' || e === '.git') continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walkAll(p, out);
    else if ((p.endsWith('.ts') || p.endsWith('.tsx')) && !p.includes('.test.')) out.push(p);
  }
  return out;
}

/** Scan the given roots for user-facing English literals. */
export function scanHardcodedText(roots: string[]): Hit[] {
  const hits: Hit[] = [];
  for (const root of roots) {
    for (const file of walk(root)) {
      // Dev-only browser harnesses are stripped from release builds and never
      // shown to a user, so their fixture strings are not translation bugs.
      if (/[\\/](demo|dev)[\\/]/.test(file)) continue;
      const text = readFileSync(file, 'utf8');
      const lineOf = (idx: number) => text.slice(0, idx).split('\n').length;
      const dev = devRanges(text);
      for (const re of [textRe, attrRe, propRe]) {
        for (const m of text.matchAll(re)) {
          if (!isProse(m[2]) || inRanges(dev, m.index)) continue;
          hits.push({ file, line: lineOf(m.index), text: m[2].trim(), slot: m[1] });
        }
      }
    }
  }
  return hits;
}
