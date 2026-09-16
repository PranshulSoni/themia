// Notes editing operations [S-19][S-20][S-21][S-22]. These are the whole
// behaviour of the intercepted keys; the contenteditable layer only applies
// what they return, so pinning them here pins the editing model.

import { describe, it, expect } from 'vitest';
import {
  pressEnter, shiftIndent, toggleWrap, insertText, toggleTaskLine,
  lineBounds, lineIndexAt, lineStart,
  toggleBlock, cycleHeading, activeMarks,
} from './editing.ts';
import { History, isTypingRun } from './history.ts';

/** `text` with `|` marking the caret, for readable expectations. */
function mark(text: string, start: number, end: number = start): string {
  return start === end
    ? text.slice(0, start) + '|' + text.slice(start)
    : text.slice(0, start) + '[' + text.slice(start, end) + ']' + text.slice(end);
}
/** Inverse of `mark` for inputs. */
function at(marked: string): [string, number] {
  const i = marked.indexOf('|');
  return [marked.slice(0, i) + marked.slice(i + 1), i];
}

describe('offset helpers', () => {
  it('finds the bounds of the line an offset sits on', () => {
    expect(lineBounds('ab\ncd\nef', 4)).toEqual({ start: 3, end: 5 });
    expect(lineBounds('ab\ncd', 0)).toEqual({ start: 0, end: 2 });
    expect(lineBounds('ab\ncd', 5)).toEqual({ start: 3, end: 5 });
  });

  it('maps offsets to line indices and back', () => {
    expect(lineIndexAt('a\nb\nc', 4)).toBe(2);
    expect(lineStart('a\nb\nc', 2)).toBe(4);
    expect(lineStart('a\nb\nc', 99)).toBe(5);
  });
});

describe('[S-19] Enter continues the list', () => {
  it('carries a bullet to the next line', () => {
    const [text, caret] = at('- milk|');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('- milk\n- |');
  });

  it('increments an ordered marker', () => {
    const [text, caret] = at('1. first|');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('1. first\n2. |');
  });

  it('carries a task box across unchecked', () => {
    const [text, caret] = at('- [x] done|');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('- [x] done\n- [ ] |');
  });

  it('preserves the indent of a nested item', () => {
    const [text, caret] = at('  - nested|');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('  - nested\n  - |');
  });

  it('clears the marker instead of nesting when the item is empty', () => {
    const [text, caret] = at('- a\n- |');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('- a\n|');
  });

  it('clears an empty task item too', () => {
    const [text, caret] = at('- [ ] |');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('|');
  });

  it('splits mid-item, carrying the marker to the tail', () => {
    const [text, caret] = at('- mi|lk');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('- mi\n- |lk');
  });

  it('does not inherit a marker when the caret is inside it', () => {
    const [text, caret] = at('-| milk');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('-\n| milk');
  });

  it('replaces a selection', () => {
    const out = pressEnter('- one two', 6, 9);
    expect(mark(out.text, out.start)).toBe('- one \n- |');
  });

  it('is a plain newline on ordinary text', () => {
    const [text, caret] = at('hello|world');
    const out = pressEnter(text, caret, caret);
    expect(mark(out.text, out.start)).toBe('hello\n|world');
  });
});

describe('[S-20] Tab indents', () => {
  it('indents the caret line by two spaces', () => {
    const [text, caret] = at('- a|');
    const out = shiftIndent(text, caret, caret, 1);
    expect(mark(out.text, out.start)).toBe('  - a|');
  });

  it('indents every line the selection touches', () => {
    const out = shiftIndent('- a\n- b\n- c', 1, 6, 1);
    expect(out.text).toBe('  - a\n  - b\n- c');
  });

  it('outdents by up to two spaces', () => {
    expect(shiftIndent('    - a', 7, 7, -1).text).toBe('  - a');
    expect(shiftIndent(' - a', 4, 4, -1).text).toBe('- a');
  });

  it('outdenting an unindented line is a no-op', () => {
    const out = shiftIndent('- a', 3, 3, -1);
    expect(out.text).toBe('- a');
    expect(out.start).toBe(3);
  });

  it('outdents a tab as one unit', () => {
    expect(shiftIndent('\t- a', 4, 4, -1).text).toBe('- a');
  });
});

describe('[S-21] Ctrl+B / Ctrl+I wrapping', () => {
  it('wraps the selection', () => {
    const out = toggleWrap('make bold now', 5, 9, '**');
    expect(mark(out.text, out.start, out.end)).toBe('make **[bold]** now');
  });

  it('unwraps when the markers are inside the selection', () => {
    const out = toggleWrap('a **bold** b', 2, 10, '**');
    expect(mark(out.text, out.start, out.end)).toBe('a [bold] b');
  });

  it('unwraps when the markers sit just outside the selection', () => {
    const out = toggleWrap('a **bold** b', 4, 8, '**');
    expect(mark(out.text, out.start, out.end)).toBe('a [bold] b');
  });

  it('inserts an empty pair with the caret between when nothing is selected', () => {
    const out = toggleWrap('ab', 1, 1, '*');
    expect(mark(out.text, out.start)).toBe('a*|*b');
  });

  it('round-trips wrap then unwrap', () => {
    const first = toggleWrap('word', 0, 4, '*');
    const second = toggleWrap(first.text, first.start, first.end, '*');
    expect(second.text).toBe('word');
  });
});

describe('[S-17] checkbox toggling', () => {
  it('flips unchecked to checked and back', () => {
    expect(toggleTaskLine('- [ ] milk', 0)).toBe('- [x] milk');
    expect(toggleTaskLine('- [x] milk', 0)).toBe('- [ ] milk');
  });

  it('toggles the addressed line only', () => {
    expect(toggleTaskLine('- [ ] a\n- [ ] b', 1)).toBe('- [ ] a\n- [x] b');
  });

  it('preserves length, so the caret stays valid', () => {
    const src = '  - [ ] indented';
    expect(toggleTaskLine(src, 0)).toHaveLength(src.length);
  });

  it('returns null for a line that is not a task', () => {
    expect(toggleTaskLine('- plain bullet', 0)).toBe(null);
    expect(toggleTaskLine('text', 0)).toBe(null);
  });
});

describe('insertText', () => {
  it('replaces the selection and leaves the caret after the insert', () => {
    const out = insertText('hello world', 6, 11, 'there');
    expect(mark(out.text, out.start)).toBe('hello there|');
  });
});

describe('[S-22] undo history', () => {
  it('coalesces a run of single-character typing into one step', () => {
    const h = new History('');
    for (const t of ['a', 'ab', 'abc']) h.record(t, t.length, t.length);
    expect(h.undo()?.text).toBe('');
    expect(h.undo()).toBe(null);
  });

  it('breaks the run at a newline', () => {
    const h = new History('a');
    h.record('a\n', 2, 2);
    h.record('a\nb', 3, 3);
    expect(h.undo()?.text).toBe('a\n');
    expect(h.undo()?.text).toBe('a');
  });

  it('treats a multi-character edit (paste) as its own step', () => {
    const h = new History('a');
    h.record('a and more', 10, 10);
    h.record('a and more!', 11, 11);
    expect(h.undo()?.text).toBe('a and more');
    expect(h.undo()?.text).toBe('a');
  });

  it('restores the selection along with the text', () => {
    const h = new History('hello');
    h.record('hello world', 11, 11);
    h.record('goodbye', 7, 7);
    const back = h.undo();
    expect(back).toEqual({ text: 'hello world', start: 11, end: 11 });
  });

  it('redoes what it undid, and drops the redo stack on a new edit', () => {
    const h = new History('a');
    h.record('ab!', 3, 3);
    h.undo();
    expect(h.redo()?.text).toBe('ab!');
    h.undo();
    h.record('xyz', 3, 3);
    expect(h.redo()).toBe(null);
  });

  it('reset drops the stacks (external overwrite)', () => {
    const h = new History('a');
    h.record('ab!', 3, 3);
    h.reset('');
    expect(h.undo()).toBe(null);
    expect(h.state.text).toBe('');
  });

  it('a caret-only change does not create a step', () => {
    const h = new History('abc');
    h.record('abc', 0, 0);
    expect(h.undo()).toBe(null);
    expect(h.state.start).toBe(0);
  });

  it('isTypingRun recognises exactly one inserted non-newline character', () => {
    expect(isTypingRun('ab', 'abc')).toBe(true);
    expect(isTypingRun('ab', 'axb')).toBe(true);
    expect(isTypingRun('ab', 'ab\n')).toBe(false);
    expect(isTypingRun('ab', 'abcd')).toBe(false);
    expect(isTypingRun('ab', 'a')).toBe(false);
    expect(isTypingRun('ab', 'ba')).toBe(false);
  });
});

describe('[S-27] block commands', () => {
  it('applies a bullet to the caret line and strips it again', () => {
    const on = toggleBlock('milk', 4, 4, 'ul');
    expect(on.text).toBe('- milk');
    expect(toggleBlock(on.text, on.start, on.end, 'ul').text).toBe('milk');
  });

  it('numbers an ordered list from one across the selection', () => {
    expect(toggleBlock('a\nb\nc', 0, 5, 'ol').text).toBe('1. a\n2. b\n3. c');
  });

  it('replaces the existing marker instead of stacking markers', () => {
    expect(toggleBlock('- milk', 6, 6, 'task').text).toBe('- [ ] milk');
    expect(toggleBlock('- [ ] milk', 10, 10, 'quote').text).toBe('> milk');
    expect(toggleBlock('> milk', 6, 6, 'ul').text).toBe('- milk');
  });

  it('strips a heading when a block marker is applied', () => {
    expect(toggleBlock('## Title', 8, 8, 'ul').text).toBe('- Title');
  });

  it('keeps the indent of a nested line', () => {
    expect(toggleBlock('    milk', 8, 8, 'ul').text).toBe('    - milk');
  });

  it('only strips when EVERY touched line already has the marker', () => {
    // Mixed selection -> apply to all, rather than half-removing.
    expect(toggleBlock('- a\nb', 0, 5, 'ul').text).toBe('- a\n- b');
    expect(toggleBlock('- a\n- b', 0, 7, 'ul').text).toBe('a\nb');
  });

  it('keeps the selection over the same words', () => {
    const out = toggleBlock('milk', 0, 4, 'ul');
    expect(out.text.slice(out.start, out.end)).toBe('milk');
  });
});

describe('[S-27] heading cycle', () => {
  it('steps H1 -> H2 -> H3 -> body -> H1', () => {
    let text = 'Title';
    const seen: string[] = [];
    for (let i = 0; i < 4; i++) {
      const out = cycleHeading(text, 0, 0);
      text = out.text;
      seen.push(text);
    }
    expect(seen).toEqual(['# Title', '## Title', '### Title', 'Title']);
  });

  it('takes the level from the first line of a multi-line selection', () => {
    expect(cycleHeading('# a\nb', 0, 5).text).toBe('## a\n## b');
  });

  it('replaces a list marker rather than nesting inside it', () => {
    expect(cycleHeading('- milk', 6, 6).text).toBe('# milk');
  });
});

describe('[S-27] active marks', () => {
  it('reports the emphasis around a collapsed caret', () => {
    const text = 'a **bold** b';
    expect(activeMarks(text, 6, 6).bold).toBe(true);   // inside "bold"
    expect(activeMarks(text, 1, 1).bold).toBe(false);  // in the plain run
  });

  it('reads as bold immediately after the opening markers', () => {
    // Caret at `**|bold**` — the char before is a marker, so it looks ahead.
    expect(activeMarks('**bold**', 2, 2).bold).toBe(true);
  });

  it('reports emphasis over a ranged selection', () => {
    const marks = activeMarks('~~gone~~ and `x`', 2, 6);
    expect(marks.strike).toBe(true);
    expect(marks.code).toBe(false);
  });

  it('reports the block kind of the caret line', () => {
    expect(activeMarks('- milk', 3, 3).block).toBe('ul');
    expect(activeMarks('1. milk', 4, 4).block).toBe('ol');
    expect(activeMarks('- [ ] milk', 7, 7).block).toBe('task');
    expect(activeMarks('> quoted', 4, 4).block).toBe('quote');
    expect(activeMarks('plain', 3, 3).block).toBe(null);
  });

  it('reports the heading level of the caret line', () => {
    expect(activeMarks('# a', 3, 3).heading).toBe(1);
    expect(activeMarks('### a', 5, 5).heading).toBe(3);
    expect(activeMarks('a', 1, 1).heading).toBe(0);
  });

  it('does not call a checked task strikethrough', () => {
    // The strike comes from the checkbox, not from ~~ the user typed, so the
    // toolbar button must stay unlit.
    expect(activeMarks('- [x] done', 8, 8).strike).toBe(false);
    expect(activeMarks('~~done~~', 4, 4).strike).toBe(true);
  });

  it('uses the caret line, not the first line of the note', () => {
    expect(activeMarks('# head\n- item', 10, 10).block).toBe('ul');
    expect(activeMarks('# head\n- item', 10, 10).heading).toBe(0);
  });
});
