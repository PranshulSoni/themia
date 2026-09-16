// ---------------------------------------------------------------------------
// Notes editing operations (pure string transforms — no DOM).
//
// Every keystroke the Notes editor intercepts (Enter, Tab, Ctrl+B/I) is
// implemented here as `(text, selection) -> (text, selection)`. Keeping them
// out of the DOM layer is what makes them unit-testable, and it keeps the
// contenteditable layer to one job: apply text + place the caret.
//
// Offsets are plain character offsets into the note, with `\n` counting as one
// character (see editorDom.ts, which maps DOM positions to the same space).
// ---------------------------------------------------------------------------

import { parseNote, type Span } from './markdown.ts';

export interface Edit {
  text: string;
  /** Selection after the edit; collapsed when `end` is omitted. */
  start: number;
  end: number;
}

/** Marker patterns that a new line continues when Enter is pressed. */
const TASK_RE = /^(\s*)([-*+] )\[[ xX]\]( ?)/;
const UL_RE = /^(\s*)([-*+] )/;
const OL_RE = /^(\s*)(\d{1,9})([.)] )/;

/** Character bounds of the line containing `offset`. */
export function lineBounds(text: string, offset: number): { start: number; end: number } {
  const clamped = Math.max(0, Math.min(text.length, offset));
  const start = text.lastIndexOf('\n', clamped - 1) + 1;
  const nl = text.indexOf('\n', clamped);
  return { start, end: nl === -1 ? text.length : nl };
}

/** Index of the line containing `offset` (0-based). */
export function lineIndexAt(text: string, offset: number): number {
  const clamped = Math.max(0, Math.min(text.length, offset));
  let n = 0;
  for (let i = 0; i < clamped; i++) if (text[i] === '\n') n++;
  return n;
}

/** Character offset where line `index` starts. */
export function lineStart(text: string, index: number): number {
  let at = 0;
  for (let n = 0; n < index; n++) {
    const nl = text.indexOf('\n', at);
    if (nl === -1) return text.length;
    at = nl + 1;
  }
  return at;
}

function replaceRange(text: string, start: number, end: number, insert: string): string {
  return text.slice(0, start) + insert + text.slice(end);
}

/** The list marker a new line should inherit, or '' when the line isn't a list. */
function continuationPrefix(line: string): string {
  const task = TASK_RE.exec(line);
  if (task) return `${task[1]}${task[2]}[ ]${task[3] || ' '}`;
  const ol = OL_RE.exec(line);
  if (ol) return `${ol[1]}${String(Number(ol[2]) + 1)}${ol[3]}`;
  const ul = UL_RE.exec(line);
  if (ul) return `${ul[1]}${ul[2]}`;
  return '';
}

/** Length of the leading list marker on `line`, or 0. */
function markerLength(line: string): number {
  const task = TASK_RE.exec(line);
  if (task) return task[0].length;
  const ol = OL_RE.exec(line);
  if (ol) return ol[0].length;
  const ul = UL_RE.exec(line);
  if (ul) return ul[0].length;
  return 0;
}

/**
 * Enter. Continues the current list item (`- `, `1. `, `- [ ] `), or — when the
 * item is empty, i.e. the marker is all it holds — clears the marker instead,
 * which is how every markdown editor lets you leave a list.
 *
 * Handled unconditionally rather than falling through to the browser, because
 * contenteditable's native Enter splits our styled spans in ways that don't
 * round-trip through `readText`.
 */
export function pressEnter(text: string, start: number, end: number): Edit {
  const { start: ls, end: le } = lineBounds(text, start);
  const line = text.slice(ls, le);
  const mark = markerLength(line);

  // Empty list item, caret at its end: drop the marker and stay on this line.
  if (mark > 0 && start === end && start >= ls + mark && line.slice(mark).trim() === '') {
    const out = replaceRange(text, ls, le, '');
    return { text: out, start: ls, end: ls };
  }

  // Only inherit the marker when the caret sits in the item's content.
  const prefix = start >= ls + mark ? continuationPrefix(line) : '';
  const insert = `\n${prefix}`;
  const caret = start + insert.length;
  return { text: replaceRange(text, start, end, insert), start: caret, end: caret };
}

/** Tab / Shift+Tab: indent or outdent every line the selection touches. */
export function shiftIndent(text: string, start: number, end: number, dir: 1 | -1): Edit {
  const first = lineBounds(text, start).start;
  const last = lineBounds(text, end).end;
  const lines = text.slice(first, last).split('\n');

  let deltaFirst = 0;
  let deltaTotal = 0;
  const shifted = lines.map((line, i) => {
    if (dir === 1) {
      if (i === 0) deltaFirst = 2;
      deltaTotal += 2;
      return `  ${line}`;
    }
    const strip = /^( {1,2}|\t)/.exec(line);
    const n = strip ? strip[0].length : 0;
    if (i === 0) deltaFirst = -n;
    deltaTotal -= n;
    return line.slice(n);
  });

  const out = replaceRange(text, first, last, shifted.join('\n'));
  return {
    text: out,
    start: Math.max(first, start + deltaFirst),
    end: Math.max(first, end + deltaTotal),
  };
}

/**
 * Ctrl+B / Ctrl+I. Wraps the selection in `marker`, or unwraps when it is
 * already wrapped (checking just outside the selection too, so hitting Ctrl+B
 * again on the word you just bolded removes it rather than doubling it).
 * With no selection, inserts an empty pair and puts the caret between them.
 */
export function toggleWrap(text: string, start: number, end: number, marker: string): Edit {
  const n = marker.length;

  if (start === end) {
    const out = replaceRange(text, start, start, marker + marker);
    return { text: out, start: start + n, end: start + n };
  }

  const inner = text.slice(start, end);
  if (inner.length > 2 * n && inner.startsWith(marker) && inner.endsWith(marker)) {
    const out = replaceRange(text, start, end, inner.slice(n, -n));
    return { text: out, start, end: end - 2 * n };
  }
  if (text.slice(start - n, start) === marker && text.slice(end, end + n) === marker) {
    const out = replaceRange(text, start - n, end + n, inner);
    return { text: out, start: start - n, end: end - n };
  }

  const out = replaceRange(text, start, end, marker + inner + marker);
  return { text: out, start: start + n, end: end + n };
}

/** Insert `insert` over the selection (paste, plain typing fallbacks). */
export function insertText(text: string, start: number, end: number, insert: string): Edit {
  const caret = start + insert.length;
  return { text: replaceRange(text, start, end, insert), start: caret, end: caret };
}

/**
 * Flip `[ ]` <-> `[x]` on line `index`. Returns null when that line isn't a
 * task, so callers can leave the note untouched. Length-preserving, which is
 * why the caller can keep the caret offset across the edit.
 */
export function toggleTaskLine(text: string, index: number): string | null {
  const ls = lineStart(text, index);
  const { end } = lineBounds(text, ls);
  const line = text.slice(ls, end);
  const m = /^(\s*[-*+] )\[([ xX])\]/.exec(line);
  if (!m) return null;
  const at = ls + m[1].length + 1;
  return text.slice(0, at) + (m[2] === ' ' ? 'x' : ' ') + text.slice(at + 1);
}

// ---------------------------------------------------------------------------
// Block commands + mark detection (the formatting toolbars)
// ---------------------------------------------------------------------------

/** Block markers the toolbar can toggle. Headings cycle separately. */
export type BlockKind = 'ul' | 'ol' | 'task' | 'quote';

/** What is in force at the caret, for lighting up toolbar buttons. */
export interface Marks {
  bold: boolean;
  italic: boolean;
  strike: boolean;
  code: boolean;
  heading: 0 | 1 | 2 | 3;
  block: BlockKind | null;
}

/** Split a line into its indent and its content, peeling one block marker. */
function splitBlock(line: string): { indent: string; rest: string } {
  const m = /^(\s*)([\s\S]*)$/.exec(line);
  const indent = m ? m[1] : '';
  const rest = m ? m[2] : line;
  // Order matters: a task marker starts with a bullet marker.
  for (const re of [/^#{1,3} /, /^>\s?/, /^[-*+] \[[ xX]\] ?/, /^[-*+] /, /^\d{1,9}[.)] /]) {
    const hit = re.exec(rest);
    if (hit) return { indent, rest: rest.slice(hit[0].length) };
  }
  return { indent, rest };
}

function blockOf(line: string): BlockKind | null {
  const rest = line.replace(/^\s*/, '');
  if (/^>\s?/.test(rest)) return 'quote';
  if (/^[-*+] \[[ xX]\]/.test(rest)) return 'task';
  if (/^[-*+] /.test(rest)) return 'ul';
  if (/^\d{1,9}[.)] /.test(rest)) return 'ol';
  return null;
}

function headingOf(line: string): 0 | 1 | 2 | 3 {
  const hit = /^\s*(#{1,3}) /.exec(line);
  return hit ? (hit[1].length as 1 | 2 | 3) : 0;
}

/**
 * Rewrite every line the selection touches, keeping the selection over the same
 * content: the anchor shifts by the first line's length change, the focus by the
 * total. Same accounting as `shiftIndent`.
 */
function mapLines(text: string, start: number, end: number, fn: (line: string, i: number) => string): Edit {
  const first = lineBounds(text, start).start;
  const last = lineBounds(text, end).end;
  const lines = text.slice(first, last).split('\n');

  let deltaFirst = 0;
  let deltaTotal = 0;
  const rewritten = lines.map((line, i) => {
    const next = fn(line, i);
    const delta = next.length - line.length;
    if (i === 0) deltaFirst = delta;
    deltaTotal += delta;
    return next;
  });

  return {
    text: replaceRange(text, first, last, rewritten.join('\n')),
    start: Math.max(first, start + deltaFirst),
    end: Math.max(first, end + deltaTotal),
  };
}

/**
 * Apply `kind` to every line the selection touches, or strip it when every one
 * of those lines already has it. Any other block marker is replaced, so the
 * buttons behave as one mutually-exclusive group rather than stacking markers.
 */
export function toggleBlock(text: string, start: number, end: number, kind: BlockKind): Edit {
  const first = lineBounds(text, start).start;
  const last = lineBounds(text, end).end;
  const alreadySet = text.slice(first, last).split('\n').every(line => blockOf(line) === kind);

  let ordinal = 0;
  return mapLines(text, start, end, (line) => {
    const { indent, rest } = splitBlock(line);
    if (alreadySet) return indent + rest;
    ordinal++;
    switch (kind) {
      case 'ul': return `${indent}- ${rest}`;
      case 'ol': return `${indent}${ordinal}. ${rest}`;
      case 'task': return `${indent}- [ ] ${rest}`;
      case 'quote': return `${indent}> ${rest}`;
    }
  });
}

/**
 * Step the touched lines through H1 -> H2 -> H3 -> body -> H1. One button
 * covers all three levels, which is the only way three of them fit in a widget
 * this narrow. The level comes from the first touched line so a multi-line
 * selection moves as a block.
 */
export function cycleHeading(text: string, start: number, end: number): Edit {
  const { start: ls, end: le } = lineBounds(text, start);
  const next = ((headingOf(text.slice(ls, le)) + 1) % 4) as 0 | 1 | 2 | 3;
  return mapLines(text, start, end, (line) => {
    const { indent, rest } = splitBlock(line);
    return next === 0 ? indent + rest : `${indent}${'#'.repeat(next)} ${rest}`;
  });
}

/** The content span covering `offset` within a line, ignoring syntax markers. */
function contentSpanAt(spans: Span[], offset: number): Span | undefined {
  let at = 0;
  for (const span of spans) {
    const end = at + span.text.length;
    if (!span.marker && offset >= at && offset < end) return span;
    at = end;
  }
  return undefined;
}

/**
 * What formatting applies at the selection. For a collapsed caret this is the
 * character BEFORE it (what you just typed), falling back to the one after so
 * that sitting just inside `**|bold**` still reads as bold.
 */
export function activeMarks(text: string, start: number, end: number): Marks {
  const line = parseNote(text)[lineIndexAt(text, start)];
  const { start: ls } = lineBounds(text, start);
  const probe = start === end ? start - ls - 1 : start - ls;
  const span = line
    ? contentSpanAt(line.spans, probe) ?? contentSpanAt(line.spans, probe + 1)
    : undefined;

  const kind = line?.kind;
  return {
    bold: !!span?.bold,
    italic: !!span?.italic,
    // A checked task strikes its own content; that is the line's state, not a
    // mark the user applied, so it must not light the strikethrough button.
    strike: !!span?.strike && kind !== 'task',
    code: !!span?.code,
    heading: kind === 'h1' ? 1 : kind === 'h2' ? 2 : kind === 'h3' ? 3 : 0,
    block: kind === 'ul' || kind === 'ol' || kind === 'task' || kind === 'quote' ? kind : null,
  };
}
