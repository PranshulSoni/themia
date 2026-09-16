// ---------------------------------------------------------------------------
// Notes contenteditable <-> source-text bridge.
//
// The note's source string is the single source of truth; the DOM is a
// projection of `parseNote(text)`. These four functions are the only place
// that projection is created or read back:
//
//   renderLines  text -> DOM   (one `.md-line` per source line)
//   readText     DOM  -> text
//   getCaret     DOM selection -> character offsets
//   setCaret     character offsets -> DOM selection
//
// `readText` and the offset functions share one text-length walker
// (`nodeText`), which is what keeps them in agreement: a caret offset is always
// an index into the string `readText` would return for the same DOM. Change one
// and you must change the other.
//
// Offsets count `\n` as one character, matching editing.ts.
// ---------------------------------------------------------------------------

import { spanClass, type MdLine } from './markdown.ts';

const TEXT_NODE = 3;

/** Blocks the browser may inject on its own (paste, Shift+Enter fallbacks). */
const BLOCK_TAGS = new Set(['DIV', 'P', 'LI', 'UL', 'OL', 'PRE', 'BLOCKQUOTE', 'H1', 'H2', 'H3']);

// ---------------------------------------------------------------------------
// DOM -> text
// ---------------------------------------------------------------------------

/**
 * A rendered decoration (the task checkbox icon): present in the DOM, absent
 * from the text. Marked rather than sniffed by tag, so the walkers below can
 * never disagree with the renderer about what counts as content.
 */
function isDecoration(node: Node): boolean {
  return node.nodeType !== TEXT_NODE
    && (node as Element).getAttribute?.('data-deco') !== null
    && (node as Element).getAttribute?.('data-deco') !== undefined;
}

/**
 * Text a node contributes, in the same units the caret offsets use.
 *
 * A trailing `<br>` is the filler that gives an empty line its height, so it
 * contributes nothing; a `<br>` with a following sibling is a real break.
 */
function nodeText(node: Node): string {
  if (node.nodeType === TEXT_NODE) return node.nodeValue ?? '';
  if (isDecoration(node)) return '';
  if (!(node instanceof HTMLElement)) return '';
  if (node.tagName === 'BR') return node.nextSibling ? '\n' : '';
  let out = '';
  for (const child of Array.from(node.childNodes)) {
    if (child instanceof HTMLElement && BLOCK_TAGS.has(child.tagName) && out && !out.endsWith('\n')) {
      out += '\n';
    }
    out += nodeText(child);
  }
  return out;
}

/** Read the note back out of the editor. Root children are lines. */
export function readText(root: HTMLElement): string {
  return Array.from(root.childNodes).map(nodeText).join('\n');
}

// ---------------------------------------------------------------------------
// text -> DOM
// ---------------------------------------------------------------------------

/** Cheap identity of a rendered line, so unchanged lines keep their DOM. */
function signature(line: MdLine): string {
  let sig = `${line.kind}|${line.indent}|${line.checked ? 1 : 0}`;
  for (const s of line.spans) sig += `${spanClass(s)}${s.text}`;
  return sig;
}

export interface RenderOptions {
  /**
   * Node to overlay on a task line's `[ ]` / `[x]` run — the lucide checkbox.
   * Supplied by the widget (Notes.tsx) rather than built here, so this module
   * stays free of Solid/lucide and testable under the plain unit runner.
   * Omitted: the brackets render as plain text.
   */
  boxIcon?: (checked: boolean) => Node | null;
}

function buildLine(doc: Document, line: MdLine, sig: string, opts?: RenderOptions): HTMLElement {
  const el = doc.createElement('div');
  el.className = 'md-line';
  el.dataset.kind = line.kind;
  el.dataset.sig = sig;
  if (line.indent) el.dataset.indent = String(line.indent);
  if (line.checked) el.dataset.checked = '1';

  if (line.spans.length === 0) {
    // An empty <div> collapses to zero height and the caret can't enter it.
    el.appendChild(doc.createElement('br'));
    return el;
  }
  for (const span of line.spans) {
    const cls = spanClass(span);
    if (!cls) {
      el.appendChild(doc.createTextNode(span.text));
      continue;
    }
    const el2 = doc.createElement('span');
    el2.className = cls;
    el2.textContent = span.text;
    // The checkbox keeps its source text (invariant [S-18]); the icon is laid
    // over it and contributes nothing to the text or to caret offsets. It goes
    // AFTER the text so offset lookups reach the text node first.
    if (span.box && opts?.boxIcon) {
      const icon = opts.boxIcon(!!line.checked);
      if (icon) el2.appendChild(icon);
    }
    el.appendChild(el2);
  }
  return el;
}

/**
 * Project `lines` into `root`, reusing line elements whose content is
 * unchanged. Reuse is not just an optimisation: replacing a line destroys the
 * caret inside it, so leaving untouched lines alone keeps editing steady when
 * the caret is restored.
 */
export function renderLines(root: HTMLElement, lines: MdLine[], opts?: RenderOptions): void {
  const doc = root.ownerDocument;
  for (let i = 0; i < lines.length; i++) {
    const sig = signature(lines[i]);
    const existing = root.childNodes[i];
    if (existing instanceof HTMLElement && existing.dataset.sig === sig) continue;
    const el = buildLine(doc, lines[i], sig, opts);
    if (existing) root.replaceChild(el, existing);
    else root.appendChild(el);
  }
  while (root.childNodes.length > lines.length) {
    root.removeChild(root.lastChild as Node);
  }
}

// ---------------------------------------------------------------------------
// Selection <-> offsets
// ---------------------------------------------------------------------------

/** Characters preceding (`node`, `nodeOffset`) inside `container`. */
function offsetWithin(container: Node, node: Node, nodeOffset: number): number {
  if (container === node) {
    if (node.nodeType === TEXT_NODE) return Math.min(nodeOffset, (node.nodeValue ?? '').length);
    // On an element, `nodeOffset` counts child nodes, not characters.
    const kids = Array.from(node.childNodes);
    let total = 0;
    for (let i = 0; i < Math.min(nodeOffset, kids.length); i++) total += nodeText(kids[i]).length;
    return total;
  }
  let total = 0;
  for (const child of Array.from(container.childNodes)) {
    if (child === node || child.contains(node)) return total + offsetWithin(child, node, nodeOffset);
    total += nodeText(child).length;
  }
  return total;
}

function offsetOf(root: HTMLElement, node: Node, nodeOffset: number): number {
  const kids = Array.from(root.childNodes);
  if (node === root) {
    let total = 0;
    for (let i = 0; i < Math.min(nodeOffset, kids.length); i++) {
      total += (i > 0 ? 1 : 0) + nodeText(kids[i]).length;
    }
    return total;
  }
  let total = 0;
  for (let i = 0; i < kids.length; i++) {
    if (i > 0) total += 1;
    if (kids[i] === node || kids[i].contains(node)) return total + offsetWithin(kids[i], node, nodeOffset);
    total += nodeText(kids[i]).length;
  }
  return total;
}

/** Current selection as character offsets, or null when it isn't in `root`. */
export function getCaret(root: HTMLElement): { start: number; end: number } | null {
  const sel = root.ownerDocument.getSelection();
  if (!sel || sel.rangeCount === 0) return null;
  const range = sel.getRangeAt(0);
  if (!root.contains(range.startContainer) || !root.contains(range.endContainer)) return null;
  const start = offsetOf(root, range.startContainer, range.startOffset);
  return { start, end: range.collapsed ? start : offsetOf(root, range.endContainer, range.endOffset) };
}

/** Deepest (node, offset) pair for `offset` characters into `container`. */
function locateWithin(container: Node, offset: number): { node: Node; offset: number } {
  if (container.nodeType === TEXT_NODE) {
    return { node: container, offset: Math.min(offset, (container.nodeValue ?? '').length) };
  }
  const kids = Array.from(container.childNodes);
  let remaining = offset;
  for (let i = 0; i < kids.length; i++) {
    if (isDecoration(kids[i])) continue;
    const len = nodeText(kids[i]).length;
    if (remaining <= len) {
      // A childless, non-text node (a filler <br>) can't hold a caret; anchor
      // to its position in the parent instead.
      if (kids[i].nodeType === TEXT_NODE || kids[i].childNodes.length > 0) {
        return locateWithin(kids[i], remaining);
      }
      return { node: container, offset: i };
    }
    remaining -= len;
  }
  return { node: container, offset: kids.length };
}

function locate(root: HTMLElement, offset: number): { node: Node; offset: number } {
  const kids = Array.from(root.childNodes);
  if (kids.length === 0) return { node: root, offset: 0 };
  let remaining = Math.max(0, offset);
  for (let i = 0; i < kids.length; i++) {
    const len = nodeText(kids[i]).length;
    if (remaining <= len) return locateWithin(kids[i], remaining);
    remaining -= len + 1; // the line plus the `\n` that follows it
  }
  const last = kids[kids.length - 1];
  return locateWithin(last, nodeText(last).length);
}

/** Place the selection at character offsets `start`..`end`. */
export function setCaret(root: HTMLElement, start: number, end: number = start): void {
  const sel = root.ownerDocument.getSelection();
  if (!sel) return;
  const a = locate(root, start);
  const b = end === start ? a : locate(root, end);
  const range = root.ownerDocument.createRange();
  try {
    range.setStart(a.node, a.offset);
    range.setEnd(b.node, b.offset);
  } catch {
    // Offsets can go stale if the DOM changed under us between compute and
    // apply; collapsing at the start is better than throwing out of an event.
    range.selectNodeContents(root);
    range.collapse(true);
  }
  sel.removeAllRanges();
  sel.addRange(range);
}

/** Index of the `.md-line` containing `node`, or -1. */
export function lineIndexOf(root: HTMLElement, node: Node): number {
  let el: Node | null = node;
  while (el && el.parentNode !== root) el = el.parentNode;
  if (!el) return -1;
  return Array.prototype.indexOf.call(root.childNodes, el);
}
