// ---------------------------------------------------------------------------
// Notes markdown tokenizer (pure — no DOM, no Solid).
//
// The Notes editor is a LIVE editor: the user always sees and edits the raw
// markdown source, styled in place. Syntax markers (`#`, `**`, `- [ ]`) stay
// visible but dimmed; only their effect is rendered.
//
// That imposes one non-negotiable invariant, which the renderer relies on to
// map DOM positions back to source offsets:
//
//   THE SPANS OF A LINE, CONCATENATED, ARE EXACTLY THE SOURCE LINE.
//
// No span may add, drop, reorder or rewrite a character. When a delimiter has
// no partner (`**oops`), it is emitted as plain text rather than swallowed.
// `markdown.test.ts` asserts the invariant over a corpus including adversarial
// input; keep it green rather than "fixing" the renderer downstream.
// ---------------------------------------------------------------------------

/** Block kind of a single source line. */
export type LineKind =
  | 'p'      // plain paragraph text (also: blank lines)
  | 'h1' | 'h2' | 'h3'
  | 'ul'     // `- item`
  | 'ol'     // `1. item`
  | 'task'   // `- [ ] item` / `- [x] item`
  | 'quote'  // `> quoted`
  | 'hr'     // `---`
  | 'fence'  // the ``` line itself
  | 'code';  // a line inside a fenced block

/**
 * One styled run of a source line. `text` is verbatim source; the flags are
 * presentation only.
 */
export interface Span {
  text: string;
  /** Markdown syntax rather than content — rendered dimmed. */
  marker?: boolean;
  bold?: boolean;
  italic?: boolean;
  strike?: boolean;
  code?: boolean;
  /** The `[ ]` / `[x]` run of a task line: the click target that toggles it. */
  box?: boolean;
}

export interface MdLine {
  kind: LineKind;
  /** Leading indent depth (2 spaces or 1 tab per level), capped at 6. */
  indent: number;
  /** Task lines only. */
  checked?: boolean;
  spans: Span[];
}

interface Emphasis {
  bold?: boolean;
  italic?: boolean;
  strike?: boolean;
}

// ---------------------------------------------------------------------------
// Line patterns
// ---------------------------------------------------------------------------

const FENCE_RE = /^\s*(?:```|~~~)/;
// A heading needs a space after the hashes, so `#tag` stays plain text.
const HEADING_RE = /^(\s*)(#{1,3})(\s+)([\s\S]*)$/;
const HR_RE = /^\s*(?:-{3,}|\*{3,}|_{3,})\s*$/;
const QUOTE_RE = /^(\s*)(>\s?)([\s\S]*)$/;
const TASK_RE = /^(\s*)([-*+] )(\[[ xX]\])( ?)([\s\S]*)$/;
const UL_RE = /^(\s*)([-*+] )([\s\S]*)$/;
const OL_RE = /^(\s*)(\d{1,9}[.)] )([\s\S]*)$/;

/** Indent level of a leading-whitespace run: 2 spaces or 1 tab per level. */
function indentOf(ws: string): number {
  let cols = 0;
  for (const ch of ws) cols += ch === '\t' ? 2 : 1;
  return Math.min(6, Math.floor(cols / 2));
}

// ---------------------------------------------------------------------------
// Inline emphasis
// ---------------------------------------------------------------------------

/** Push `text` as a span, merging into the previous span when styles match. */
function push(out: Span[], text: string, span: Omit<Span, 'text'>): void {
  if (!text) return;
  const prev = out[out.length - 1];
  if (prev
    && !!prev.marker === !!span.marker
    && !!prev.bold === !!span.bold
    && !!prev.italic === !!span.italic
    && !!prev.strike === !!span.strike
    && !!prev.code === !!span.code
    && !!prev.box === !!span.box) {
    prev.text += text;
    return;
  }
  out.push({ text, ...span });
}

const WORD_RE = /[\p{L}\p{N}]/u;

/**
 * `_` only opens/closes at a word boundary, so `snake_case_name` is not
 * italic. `*` has no such rule (matching what every editor does in practice).
 */
function underscoreOk(src: string, open: number, close: number): boolean {
  const before = open > 0 ? src[open - 1] : '';
  const after = close + 1 < src.length ? src[close + 1] : '';
  return !WORD_RE.test(before) && !WORD_RE.test(after);
}

/**
 * Tokenize inline emphasis inside `src`, appending to `out`.
 *
 * A delimiter is honoured only when its closer exists on the same line with at
 * least one character between them; otherwise it degrades to literal text, so
 * the concatenation invariant holds for any input.
 */
function inline(src: string, out: Span[], em: Emphasis): void {
  let plain = '';
  const flush = () => { push(out, plain, em); plain = ''; };

  let i = 0;
  while (i < src.length) {
    const ch = src[i];
    const two = src.slice(i, i + 2);

    // `code` — highest precedence, never nests.
    if (ch === '`') {
      const close = src.indexOf('`', i + 1);
      if (close > i + 1) {
        flush();
        push(out, '`', { ...em, marker: true });
        push(out, src.slice(i + 1, close), { ...em, code: true });
        push(out, '`', { ...em, marker: true });
        i = close + 1;
        continue;
      }
    }

    if ((two === '**' || two === '__') && !em.bold) {
      const close = src.indexOf(two, i + 2);
      if (close > i + 2 && (two === '**' || underscoreOk(src, i, close + 1))) {
        flush();
        push(out, two, { ...em, marker: true });
        inline(src.slice(i + 2, close), out, { ...em, bold: true });
        push(out, two, { ...em, marker: true });
        i = close + 2;
        continue;
      }
    }

    if (two === '~~' && !em.strike) {
      const close = src.indexOf('~~', i + 2);
      if (close > i + 2) {
        flush();
        push(out, '~~', { ...em, marker: true });
        inline(src.slice(i + 2, close), out, { ...em, strike: true });
        push(out, '~~', { ...em, marker: true });
        i = close + 2;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && !em.italic) {
      const close = src.indexOf(ch, i + 1);
      // A `**` run is handled above; a lone `*` must not eat into one.
      if (close > i + 1 && src[i + 1] !== ch && src[close - 1] !== ch
        && (ch === '*' || underscoreOk(src, i, close))) {
        flush();
        push(out, ch, { ...em, marker: true });
        inline(src.slice(i + 1, close), out, { ...em, italic: true });
        push(out, ch, { ...em, marker: true });
        i = close + 1;
        continue;
      }
    }

    plain += ch;
    i++;
  }
  flush();
}

// ---------------------------------------------------------------------------
// Block parsing
// ---------------------------------------------------------------------------

function parseLine(line: string, inFence: boolean): MdLine {
  // Checked before the in-fence branch, otherwise the closing fence would be
  // swallowed as code and the block would run to the end of the note.
  if (FENCE_RE.test(line)) {
    return { kind: 'fence', indent: 0, spans: line ? [{ text: line, marker: true }] : [] };
  }
  if (inFence) {
    return { kind: 'code', indent: 0, spans: line ? [{ text: line, code: true }] : [] };
  }
  if (HR_RE.test(line) && line.trim().length >= 3) {
    return { kind: 'hr', indent: 0, spans: [{ text: line, marker: true }] };
  }

  const heading = HEADING_RE.exec(line);
  if (heading) {
    const [, ws, hashes, gap, rest] = heading;
    const spans: Span[] = [];
    push(spans, ws + hashes + gap, { marker: true });
    inline(rest, spans, {});
    const kind = (['h1', 'h2', 'h3'] as const)[hashes.length - 1];
    return { kind, indent: 0, spans };
  }

  const quote = QUOTE_RE.exec(line);
  if (quote) {
    const [, ws, mark, rest] = quote;
    const spans: Span[] = [];
    push(spans, ws + mark, { marker: true });
    inline(rest, spans, {});
    return { kind: 'quote', indent: indentOf(ws), spans };
  }

  const task = TASK_RE.exec(line);
  if (task) {
    const [, ws, bullet, box, gap, rest] = task;
    const checked = box[1] !== ' ';
    const spans: Span[] = [];
    push(spans, ws + bullet, { marker: true });
    push(spans, box, { marker: true, box: true });
    push(spans, gap, { marker: true });
    // Completed items read as done: struck through and dimmed by CSS.
    inline(rest, spans, checked ? { strike: true } : {});
    return { kind: 'task', indent: indentOf(ws), checked, spans };
  }

  const ul = UL_RE.exec(line);
  if (ul) {
    const [, ws, bullet, rest] = ul;
    const spans: Span[] = [];
    push(spans, ws + bullet, { marker: true });
    inline(rest, spans, {});
    return { kind: 'ul', indent: indentOf(ws), spans };
  }

  const ol = OL_RE.exec(line);
  if (ol) {
    const [, ws, num, rest] = ol;
    const spans: Span[] = [];
    push(spans, ws + num, { marker: true });
    inline(rest, spans, {});
    return { kind: 'ol', indent: indentOf(ws), spans };
  }

  const spans: Span[] = [];
  inline(line, spans, {});
  return { kind: 'p', indent: 0, spans };
}

/**
 * Tokenize a note. Always returns one entry per source line (a trailing
 * newline yields a final empty line), so line index == source line index.
 */
export function parseNote(text: string): MdLine[] {
  const out: MdLine[] = [];
  let inFence = false;
  for (const line of text.split('\n')) {
    const parsed = parseLine(line, inFence);
    if (parsed.kind === 'fence') inFence = !inFence;
    out.push(parsed);
  }
  return out;
}

/** CSS class list for a span's emphasis flags (used by the renderer). */
export function spanClass(s: Span): string {
  let cls = '';
  if (s.marker) cls += ' md-mark';
  if (s.bold) cls += ' md-b';
  if (s.italic) cls += ' md-i';
  if (s.strike) cls += ' md-s';
  if (s.code) cls += ' md-c';
  if (s.box) cls += ' md-box';
  return cls.trim();
}
