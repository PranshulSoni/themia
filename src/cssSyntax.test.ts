// Every stylesheet must actually parse.
//
// `npm run check` is typecheck + lint + unit, and NONE of them parse CSS: tsc
// ignores it, eslint doesn't lint it, and vitest only ever reads stylesheets as
// text. So a CSS syntax error sails through the whole gate and only surfaces in
// `vite build`, which isn't part of check.
//
// The bug that motivated this: a comment in App.css read
//
//     /* the design-row vocabulary (.design-row/-left/-label/
//        -desc/-val*/-range-wrap) is owned by ... */
//
// The `*/` inside `-val*/-range-wrap` CLOSED the comment early, so the rest of
// the prose became CSS and its `)` was a stray token. Every unit test stayed
// green; the production build died with "Unexpected token CloseParenthesis".
//
// This runs the same parser the build uses (lightningcss) over every stylesheet,
// so that class of error fails in `npm run check` instead of at release time.

import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { transform } from 'lightningcss';

function walk(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (['node_modules', 'dist', 'target', '.git'].includes(e)) continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (p.endsWith('.css')) out.push(p);
  }
  return out;
}

const ROOT = process.cwd();
const rel = (p: string) => relative(ROOT, p).split(sep).join('/');
const cssFiles = [...walk(join(ROOT, 'src')), ...walk(join(ROOT, 'widgets'))].map(rel);

describe('every stylesheet parses', () => {
  it('the scan finds the stylesheets (guards the scanner)', () => {
    expect(cssFiles.length).toBeGreaterThanOrEqual(30);
  });

  it.each(cssFiles)('%s parses and minifies', (file) => {
    const code = readFileSync(join(ROOT, file), 'utf8');
    // minify: true is what the build does, and is what surfaces the error --
    // the non-minifying path is more forgiving.
    expect(() => transform({
      filename: file,
      code: Buffer.from(code),
      minify: true,
    })).not.toThrow();
  });
});
