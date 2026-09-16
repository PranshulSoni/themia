// Test-only helpers for pinning CSS contracts from source.
//
// jsdom can't cascade the app's imported stylesheets, so the settings design
// system is characterised by scanning the CSS/TSX SOURCE directly (the pattern
// established by src/configWindowBackground.test.ts, which in turn follows
// ipc.test.ts pinning lib.rs command registration). This module exists so that
// scanner lives in ONE place rather than being re-inlined per test file.
//
// Lives under tests/ (not src/) so it never reaches the app bundle.

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

export interface CssRule {
  /** Whitespace-collapsed selector list, e.g. `.a, .b:hover`. */
  selector: string;
  /** Raw declaration block between the braces. */
  body: string;
}

/** Read a repo-relative file. Resolves from the vitest root (cwd), not
 *  import.meta.url: under the jsdom environment import.meta.url is an http://
 *  URL, which readFileSync rejects. */
export function readSource(relPath: string): string {
  return readFileSync(resolve(process.cwd(), relPath), 'utf8');
}

/** Flat rule scanner. Strips comments, then splits into {selector, body} pairs.
 *  The app's CSS is flat (no nesting), but @media blocks would otherwise be
 *  mis-parsed, so their wrapper is unwrapped first and inner rules are kept. */
export function parseRules(css: string): CssRule[] {
  const noComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
  // Unwrap at-rule blocks (@media/@supports) so the rules inside are scanned
  // flat. Non-greedy body match is safe because the app nests at most one level.
  const unwrapped = noComments.replace(/@[a-z-]+[^{]*\{([\s\S]*?\n\})\s*\}/gi, '$1');
  return [...unwrapped.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .map(m => ({ selector: m[1].trim().replace(/\s+/g, ' '), body: m[2] }));
}

/** Every rule whose selector targets `cls` in a disabled state — either the
 *  `:disabled` pseudo-class or an `[disabled]`/`.disabled` attribute/class
 *  hook. `:not(:disabled)` is explicitly NOT a disabled-state rule: it styles
 *  the ENABLED case, so counting it would let a class pass with no disabled
 *  treatment at all. */
export function disabledRulesFor(rules: CssRule[], cls: string): CssRule[] {
  const escaped = cls.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const target = new RegExp(`\\.${escaped}(?![\\w-])`);
  return rules.filter(r => {
    if (!target.test(r.selector)) return false;
    const withoutNot = r.selector.replace(/:not\([^)]*\)/g, '');
    return /:disabled|\[disabled\]|\.disabled(?![\w-])/.test(withoutNot);
  });
}

/** The class a rule PRIMARILY styles: in the last compound of its first
 *  selector, the FIRST class. `.cfg .col-chip.active` → `col-chip`, not
 *  `active` — `.active` is a modifier, not the thing being styled, and
 *  attributing it to the modifier makes every `.foo.active` rule in the repo
 *  look like a collision. */
export function primaryClass(selector: string): string | null {
  const first = selector.split(',')[0].trim();
  const compounds = first.split(/\s+|\s*>\s*|\s*\+\s*|\s*~\s*/).filter(Boolean);
  for (let i = compounds.length - 1; i >= 0; i--) {
    const m = compounds[i].match(/\.([\w-]+)/);
    if (m) return m[1];
  }
  return null;
}

export interface JsxButton {
  /** Static class list from `class="…"`, split on whitespace. Buttons whose
   *  class is a dynamic expression (`class={…}`) report an empty list. */
  classes: string[];
  /** True when the tag carries a `disabled` attribute of any form. */
  hasDisabled: boolean;
  file: string;
  /** 1-indexed line of the opening `<button`. */
  line: number;
}

/** Find `<button …>` opening tags in a TSX source and report their static
 *  classes + whether they can render disabled.
 *
 *  Attribute values may contain `>` (e.g. `disabled={a > b}`), so the tag body
 *  is consumed brace-aware rather than by a naive `[^>]*`. */
export function findButtons(tsx: string, file: string): JsxButton[] {
  const out: JsxButton[] = [];
  for (let i = 0; i < tsx.length; i++) {
    if (!tsx.startsWith('<button', i)) continue;
    const after = tsx[i + 7];
    if (after && /[\w-]/.test(after)) continue; // e.g. <buttonish>
    // Consume to the tag-closing '>', tracking brace/quote depth so a '>'
    // inside {…} or "…" doesn't end the tag early.
    let depth = 0, quote = '', end = -1;
    for (let j = i + 7; j < tsx.length; j++) {
      const c = tsx[j];
      if (quote) { if (c === quote && tsx[j - 1] !== '\\') quote = ''; continue; }
      if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
      if (c === '{') depth++;
      else if (c === '}') depth--;
      else if (c === '>' && depth === 0) { end = j; break; }
    }
    if (end === -1) continue;
    const tag = tsx.slice(i, end);
    const classMatch = tag.match(/\sclass="([^"]*)"/);
    out.push({
      classes: classMatch ? classMatch[1].trim().split(/\s+/).filter(Boolean) : [],
      hasDisabled: /\sdisabled(=|[\s/>])/.test(tag + ' '),
      file,
      line: tsx.slice(0, i).split('\n').length,
    });
  }
  return out;
}
