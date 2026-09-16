/**
 * Inline emphasis for translation strings.
 *
 * Prose keys (the Help widget's guide, issue #91) need emphasis *inside* a
 * sentence, and the emphasised span moves as word order changes between
 * languages — so the marker has to live in the locale string, not in the JSX.
 *
 * The markup is deliberately NOT HTML: a locale string is never parsed as
 * markup and never reaches `innerHTML`. It is tokenised into a plain array that
 * the caller renders with real elements, so an unexpected `<` in a translation
 * is text and nothing else. Two markers only:
 *
 *   *strong*   → <strong>   (a term being defined)
 *   _emphasis_ → <em>       (a UI element the user must find on screen)
 *
 * Markers do not nest, and an unpaired marker stays literal — a lone `*` in a
 * translation renders as `*` rather than swallowing the rest of the sentence.
 */

export type RichToken =
  | { kind: 'text'; text: string }
  | { kind: 'strong'; text: string }
  | { kind: 'em'; text: string };

/** Paired markers. `[^*]+` / `[^_]+` keeps a match inside one span and makes an
 *  unpaired marker fail to match at all (so it falls through as text). */
const MARKED = /\*([^*\n]+)\*|_([^_\n]+)_/g;

/** Split a translation string into text / strong / em runs. Pure. */
export function parseRich(s: string): RichToken[] {
  const out: RichToken[] = [];
  let last = 0;
  MARKED.lastIndex = 0;
  for (let m = MARKED.exec(s); m !== null; m = MARKED.exec(s)) {
    if (m.index > last) out.push({ kind: 'text', text: s.slice(last, m.index) });
    out.push(m[1] !== undefined
      ? { kind: 'strong', text: m[1] }
      : { kind: 'em', text: m[2] });
    last = m.index + m[0].length;
  }
  if (last < s.length) out.push({ kind: 'text', text: s.slice(last) });
  return out;
}
