// @vitest-environment jsdom
//
// The contenteditable projection [S-14]. Two properties matter more than any
// individual assertion:
//   1. render -> read round-trips the source exactly, and
//   2. a character offset survives setCaret -> getCaret.
// Together they are what lets every editing operation work on plain strings.

import { describe, it, expect, beforeEach } from 'vitest';
import { parseNote } from './markdown.ts';
import { renderLines, readText, getCaret, setCaret, lineIndexOf } from './editorDom.ts';

let root: HTMLElement;

beforeEach(() => {
  document.body.innerHTML = '';
  root = document.createElement('div');
  document.body.appendChild(root);
});

const project = (text: string) => { renderLines(root, parseNote(text)); };

const SOURCES = [
  '',
  'plain',
  'two\nlines',
  '\n',
  '\n\n\n',
  'a\n\nb',
  '# Heading\n\n- [ ] milk\n- [x] bread\n',
  '**bold** and `code` and ~~strike~~',
  '```js\nconst x = 1;\n```',
  '  - nested item',
  'unicode héllo 日本語 🎉',
  'trailing space \n  leading',
];

describe('[S-14] render -> read round-trip', () => {
  for (const src of SOURCES) {
    it(`round-trips ${JSON.stringify(src)}`, () => {
      project(src);
      expect(readText(root)).toBe(src);
    });
  }

  it('renders one .md-line per source line', () => {
    project('a\nb\nc');
    expect(root.querySelectorAll('.md-line')).toHaveLength(3);
  });

  it('gives an empty line a <br> so it keeps its height', () => {
    project('a\n\nb');
    const middle = root.children[1];
    expect(middle.innerHTML).toBe('<br>');
  });

  it('tags each line with its kind for styling', () => {
    project('# h\n- [x] done\n> quote');
    const kinds = Array.from(root.children).map(el => (el as HTMLElement).dataset.kind);
    expect(kinds).toEqual(['h1', 'task', 'quote']);
    expect((root.children[1] as HTMLElement).dataset.checked).toBe('1');
  });

  it('reuses the element of a line whose content did not change', () => {
    project('keep me\nchange me');
    const [first, second] = Array.from(root.children);
    project('keep me\nchanged!');
    expect(root.children[0]).toBe(first);
    expect(root.children[1]).not.toBe(second);
  });

  it('drops trailing lines when the note shrinks', () => {
    project('a\nb\nc');
    project('a');
    expect(root.children).toHaveLength(1);
    expect(readText(root)).toBe('a');
  });
});

describe('[S-14] caret offsets survive the projection', () => {
  for (const src of SOURCES) {
    it(`maps every offset of ${JSON.stringify(src)}`, () => {
      project(src);
      for (let i = 0; i <= src.length; i++) {
        setCaret(root, i);
        expect(getCaret(root), `offset ${i}`).toEqual({ start: i, end: i });
      }
    });
  }

  it('maps a non-collapsed selection', () => {
    project('# Title\nbody text');
    setCaret(root, 2, 10);
    expect(getCaret(root)).toEqual({ start: 2, end: 10 });
  });

  it('clamps an offset past the end to the end', () => {
    project('abc');
    setCaret(root, 99);
    expect(getCaret(root)).toEqual({ start: 3, end: 3 });
  });

  it('returns null when the selection is outside the editor', () => {
    project('abc');
    const outside = document.createElement('div');
    outside.textContent = 'elsewhere';
    document.body.appendChild(outside);
    const range = document.createRange();
    range.selectNodeContents(outside);
    const sel = document.getSelection();
    sel?.removeAllRanges();
    sel?.addRange(range);
    expect(getCaret(root)).toBe(null);
  });
});

describe('lineIndexOf', () => {
  it('finds the line a nested span belongs to', () => {
    project('- [ ] a\n- [ ] b\n- [ ] c');
    const boxes = root.querySelectorAll('.md-box');
    expect(lineIndexOf(root, boxes[2])).toBe(2);
  });

  it('is -1 for a node outside the editor', () => {
    project('a');
    expect(lineIndexOf(root, document.body)).toBe(-1);
  });
});

describe('readText tolerates DOM the browser built', () => {
  it('treats a bare text node at the root as its own line', () => {
    project('a\nb');
    root.insertBefore(document.createTextNode('typed'), root.firstChild);
    expect(readText(root)).toBe('typed\na\nb');
  });

  it('reads a <br> with a following sibling as a line break', () => {
    project('a');
    const line = root.children[0];
    line.appendChild(document.createElement('br'));
    line.appendChild(document.createTextNode('b'));
    expect(readText(root)).toBe('a\nb');
  });

  it('reads a nested block as a line break', () => {
    project('a');
    const nested = document.createElement('div');
    nested.textContent = 'b';
    root.children[0].appendChild(nested);
    expect(readText(root)).toBe('a\nb');
  });

  it('reads an emptied editor as an empty note', () => {
    project('a\nb');
    root.innerHTML = '';
    expect(readText(root)).toBe('');
  });
});

describe('[S-17] the checkbox icon is a decoration, invisible to text and caret', () => {
  // Stands in for the lucide <Square/> Notes.tsx clones in; the contract is the
  // `data-deco` marking, not which element it is.
  const stubIcon = () => {
    const el = document.createElement('span');
    el.setAttribute('data-deco', '');
    el.textContent = 'IGNORED';
    return el;
  };
  const decorate = (text: string) => renderLines(root, parseNote(text), { boxIcon: stubIcon });

  it('renders one icon per checkbox, inside the box span', () => {
    decorate('- [ ] milk\n- [x] bread\n- plain');
    expect(root.querySelectorAll('[data-deco]')).toHaveLength(2);
    expect(root.querySelector('.md-box [data-deco]')).to.not.equal(null);
  });

  it('is passed the line\'s checked state', () => {
    const seen: boolean[] = [];
    renderLines(root, parseNote('- [ ] a\n- [x] b'), {
      boxIcon: (checked) => { seen.push(checked); return stubIcon(); },
    });
    expect(seen).toEqual([false, true]);
  });

  it('contributes nothing to readText', () => {
    const src = '- [ ] milk\n- [x] bread';
    decorate(src);
    expect(readText(root)).toBe(src);
  });

  it('leaves every caret offset unchanged', () => {
    const src = '- [x] done\ntail';
    decorate(src);
    for (let i = 0; i <= src.length; i++) {
      setCaret(root, i);
      expect(getCaret(root), `offset ${i}`).toEqual({ start: i, end: i });
    }
  });

  it('never becomes the caret container', () => {
    decorate('- [ ] milk');
    for (let i = 0; i <= 10; i++) {
      setCaret(root, i);
      const anchor = document.getSelection()?.anchorNode as Node | null;
      const inDeco = !!anchor && !!(anchor.nodeType === 1
        ? (anchor as Element).closest('[data-deco]')
        : (anchor.parentElement?.closest('[data-deco]')));
      expect(inDeco, `offset ${i} landed inside the decoration`).toBe(false);
    }
  });

  it('renders the brackets as plain text when no icon factory is given', () => {
    renderLines(root, parseNote('- [ ] milk'));
    expect(root.querySelectorAll('[data-deco]')).toHaveLength(0);
    expect(root.querySelector('.md-box')?.textContent).toBe('[ ]');
  });
});
