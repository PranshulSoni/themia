// [S-06] The Volume widget renders ONE horizontal row at every size. Its body
// element carries BOTH the global `widget-body` class (for the card fill in
// App.css) and its own `volume-body` class. App.css sets
// `.widget-body { flex-direction: column }` and, in the real bundle, loads
// AFTER Volume.css (App.tsx imports WidgetGrid -> ... -> Volume.css on line 3,
// then its own App.css on line 43). So a bare `.volume-body { flex-direction:
// row }` (specificity 0,1,0) TIES the global rule and loses on source order,
// stacking the bar vertically (themia-releases: "volume widget is vertical").
//
// The fix scopes the layout rule under `.volume-widget` (0,2,0) so it wins the
// cascade regardless of load order. jsdom can't cascade imported stylesheets
// (cf. configWindowBackground.test.ts), so we resolve the cascade for the
// relevant property directly from the two source files.

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

interface Decl { selector: string; value: string; order: number }

// Extract every `flex-direction` declaration from a stylesheet, one entry per
// comma-separated selector, tagged with a global source order (later = higher).
function flexDirDecls(css: string, orderBase: number): Decl[] {
  const flat = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const out: Decl[] = [];
  let i = orderBase;
  for (const m of flat.matchAll(/([^{}]+)\{([^}]*)\}/g)) {
    const dir = m[2].match(/(?:^|;)\s*flex-direction\s*:\s*([^;]+)/);
    if (!dir) continue;
    for (const sel of m[1].split(',')) out.push({ selector: sel.trim().replace(/\s+/g, ' '), value: dir[1].trim(), order: i++ });
  }
  return out;
}

// Minimal class-only selector matcher + specificity, enough for the closed set
// of selectors that touch flex-direction here (`.widget-body`, `.volume-body`,
// `.volume-widget .volume-body`). `element` is the target's own classes;
// `ancestors` are outer->inner class sets of its DOM ancestors.
function classTokens(compound: string): string[] {
  return [...compound.matchAll(/\.([\w-]+)/g)].map(x => x[1]);
}
function matches(selector: string, element: Set<string>, ancestors: Set<string>[]): boolean {
  const compounds = selector.split(/\s+/);
  const target = classTokens(compounds[compounds.length - 1]);
  if (!target.length || !target.every(c => element.has(c))) return false;
  let ai = 0;
  for (let k = 0; k < compounds.length - 1; k++) {
    const need = classTokens(compounds[k]);
    let ok = false;
    while (ai < ancestors.length) { const anc = ancestors[ai++]; if (need.every(c => anc.has(c))) { ok = true; break; } }
    if (!ok) return false;
  }
  return true;
}
function specificity(selector: string): number {
  // Class-only selectors: specificity is the class count (b-column).
  return (selector.match(/\.[\w-]+/g) ?? []).length;
}

// Resolve the winning flex-direction for `element` given competing declarations
// (highest specificity wins; ties broken by later source order).
function resolveFlexDirection(decls: Decl[], element: Set<string>, ancestors: Set<string>[]): string | null {
  let best: { spec: number; order: number; value: string } | null = null;
  for (const d of decls) {
    if (!matches(d.selector, element, ancestors)) continue;
    const spec = specificity(d.selector);
    if (!best || spec > best.spec || (spec === best.spec && d.order > best.order)) best = { spec, order: d.order, value: d.value };
  }
  return best?.value ?? null;
}

// DOM shape the widget renders: `.volume-widget > .widget-body.volume-body`.
const VOLUME_BODY = new Set(['widget-body', 'volume-body']);
const VOLUME_ANCESTORS = [new Set(['volume-widget'])];

describe('[S-06] volume widget is one horizontal row (cascade-order safe)', () => {
  it('[S-06] resolver models the bug: a bare .volume-body loses to a later .widget-body', () => {
    // The pre-fix shape: both selectors tie at 0,1,0; App.css loads later.
    const buggy: Decl[] = [
      { selector: '.volume-body', value: 'row', order: 0 },   // Volume.css (earlier)
      { selector: '.widget-body', value: 'column', order: 1 }, // App.css (later) wins the tie
    ];
    expect(resolveFlexDirection(buggy, VOLUME_BODY, VOLUME_ANCESTORS)).toBe('column');

    // The fix shape: scoping under .volume-widget (0,2,0) beats the tie.
    const fixed: Decl[] = [
      { selector: '.volume-widget .volume-body', value: 'row', order: 0 },
      { selector: '.widget-body', value: 'column', order: 1 },
    ];
    expect(resolveFlexDirection(fixed, VOLUME_BODY, VOLUME_ANCESTORS)).toBe('row');
  });

  it('[S-06] real App.css + Volume.css cascade to flex-direction: row for the volume body', () => {
    const appCss = readFileSync(resolve(process.cwd(), 'src/App.css'), 'utf8');
    const volCss = readFileSync(resolve(process.cwd(), 'widgets/system/volume/Volume.css'), 'utf8');

    // Real bundle order: Volume.css is injected BEFORE App.css.
    const decls = [...flexDirDecls(volCss, 0), ...flexDirDecls(appCss, 1000)];

    // Premise guard: App.css really does force column on the shared class, so
    // this test stays meaningful (it's not passing only because that rule left).
    expect(decls.some(d => d.selector === '.widget-body' && d.value === 'column')).toBe(true);

    expect(resolveFlexDirection(decls, VOLUME_BODY, VOLUME_ANCESTORS)).toBe('row');
  });
});
