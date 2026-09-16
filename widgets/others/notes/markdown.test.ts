// Notes markdown tokenizer. Pins the block/inline grammar [S-15][S-16][S-17]
// and, above all, the invariant the whole editor rests on: spans concatenate
// back to the exact source line [S-18]. If that breaks, caret offsets and the
// persisted note drift apart silently.

import { describe, it, expect } from 'vitest';
import { parseNote, type MdLine, type Span } from './markdown.ts';

const joined = (line: MdLine) => line.spans.map(s => s.text).join('');
const kinds = (src: string) => parseNote(src).map(l => l.kind);
/** Spans that carry actual content (markers excluded). */
const content = (line: MdLine) => line.spans.filter(s => !s.marker);
const find = (line: MdLine, flag: keyof Span) => line.spans.filter(s => s[flag]);

describe('[S-18] spans reconstruct the source exactly', () => {
  const corpus = [
    '',
    'plain text',
    '# Heading',
    '#not a heading',
    '   ## indented heading',
    '### h3 with **bold** and `code`',
    '- bullet\n- another',
    '1. first\n2) second',
    '  - nested\n    - deeper',
    '- [ ] todo\n- [x] done',
    '> quoted **text**',
    '---',
    '***',
    '```js\nconst x = 1;\n```',
    '~~~\nfenced\n~~~',
    '**bold** *italic* ~~strike~~ `code`',
    '***bold italic***',
    '**unclosed bold',
    '*unclosed italic',
    '`unclosed code',
    '~~unclosed strike',
    'snake_case_name stays plain',
    '_real italic_ here',
    'a * b * c',
    '****',
    '``',
    '**a**b**c**',
    'trailing newline\n',
    '\n\n\n',
    'unicode: héllo 日本語 🎉 **bold**',
    '\t- tab indented',
    '- [X] capital done',
  ];

  for (const src of corpus) {
    it(`round-trips ${JSON.stringify(src)}`, () => {
      const rebuilt = parseNote(src).map(joined).join('\n');
      expect(rebuilt).toBe(src);
    });
  }

  it('always yields one line per source line', () => {
    expect(parseNote('a\nb\nc')).toHaveLength(3);
    expect(parseNote('trailing\n')).toHaveLength(2);
    expect(parseNote('')).toHaveLength(1);
  });
});

describe('[S-15] block syntax', () => {
  it('classifies headings by hash count', () => {
    expect(kinds('# a\n## b\n### c')).toEqual(['h1', 'h2', 'h3']);
  });

  it('requires a space after the hashes', () => {
    expect(kinds('#tag')).toEqual(['p']);
    expect(kinds('#### four hashes')).toEqual(['p']);
  });

  it('classifies lists, quotes and rules', () => {
    expect(kinds('- a\n* b\n+ c')).toEqual(['ul', 'ul', 'ul']);
    expect(kinds('1. a\n2) b')).toEqual(['ol', 'ol']);
    expect(kinds('> quoted')).toEqual(['quote']);
    expect(kinds('---\n***\n___')).toEqual(['hr', 'hr', 'hr']);
  });

  it('a bullet needs a space, so `---` is a rule not a list', () => {
    expect(kinds('---')).toEqual(['hr']);
    expect(kinds('-nospace')).toEqual(['p']);
  });

  it('marks every line between fences as code', () => {
    expect(kinds('```js\ncode()\nmore\n```\nafter'))
      .toEqual(['fence', 'code', 'code', 'fence', 'p']);
  });

  it('does not apply inline syntax inside a fence', () => {
    const [, inside] = parseNote('```\n**not bold**\n```');
    expect(inside.spans).toEqual([{ text: '**not bold**', code: true }]);
  });

  it('records indent depth in two-space steps, capped at 6', () => {
    const depths = parseNote('- a\n  - b\n    - c\n' + ' '.repeat(40) + '- deep')
      .map(l => l.indent);
    expect(depths).toEqual([0, 1, 2, 6]);
  });
});

describe('[S-16] inline syntax', () => {
  it('flags bold, italic, strike and code runs', () => {
    const [line] = parseNote('**b** *i* ~~s~~ `c`');
    expect(find(line, 'bold').map(s => s.text)).toEqual(['b']);
    expect(find(line, 'italic').map(s => s.text)).toEqual(['i']);
    expect(find(line, 'strike').map(s => s.text)).toEqual(['s']);
    expect(find(line, 'code').map(s => s.text)).toEqual(['c']);
  });

  it('nests italic inside bold', () => {
    const [line] = parseNote('**bold *and* more**');
    const both = line.spans.filter(s => s.bold && s.italic);
    expect(both.map(s => s.text)).toEqual(['and']);
  });

  it('leaves underscores inside words alone', () => {
    const [line] = parseNote('snake_case_name');
    expect(find(line, 'italic')).toHaveLength(0);
    expect(line.spans).toEqual([{ text: 'snake_case_name' }]);
  });

  it('honours underscores at word boundaries', () => {
    const [line] = parseNote('_italic_ text');
    expect(find(line, 'italic').map(s => s.text)).toEqual(['italic']);
  });

  it('does not let a lone `*` eat into a `**` run', () => {
    const [line] = parseNote('**bold**');
    expect(find(line, 'italic')).toHaveLength(0);
    expect(find(line, 'bold').map(s => s.text)).toEqual(['bold']);
  });

  it('never nests inside a code span', () => {
    const [line] = parseNote('`**literal**`');
    expect(find(line, 'bold')).toHaveLength(0);
    expect(find(line, 'code').map(s => s.text)).toEqual(['**literal**']);
  });

  it('applies inline syntax to heading and quote content only', () => {
    const [heading] = parseNote('## a **b**');
    expect(heading.spans[0]).toEqual({ text: '## ', marker: true });
    expect(find(heading, 'bold').map(s => s.text)).toEqual(['b']);
  });
});

describe('[S-18] unmatched delimiters degrade to text', () => {
  it('leaves a lone opener plain', () => {
    for (const src of ['**oops', '*oops', '~~oops', '`oops']) {
      const [line] = parseNote(src);
      expect(line.spans, src).toEqual([{ text: src }]);
    }
  });

  it('needs at least one character between delimiters', () => {
    const [line] = parseNote('a **** b');
    expect(line.spans).toEqual([{ text: 'a **** b' }]);
  });

  it('a bare run of asterisks is a rule, not emphasis', () => {
    expect(parseNote('****')[0].kind).toBe('hr');
  });
});

describe('[S-17] task lists', () => {
  it('recognises unchecked and checked boxes', () => {
    const [todo, done] = parseNote('- [ ] milk\n- [x] bread');
    expect(todo.kind).toBe('task');
    expect(todo.checked).toBe(false);
    expect(done.checked).toBe(true);
  });

  it('accepts a capital X', () => {
    expect(parseNote('- [X] done')[0].checked).toBe(true);
  });

  it('exposes the box as its own clickable span', () => {
    const [line] = parseNote('- [ ] milk');
    expect(find(line, 'box').map(s => s.text)).toEqual(['[ ]']);
  });

  it('strikes through the content of a checked item', () => {
    const [line] = parseNote('- [x] bread');
    expect(content(line).every(s => s.strike)).toBe(true);
    expect(content(line).map(s => s.text).join('')).toBe('bread');
  });

  it('leaves an unchecked item unstruck', () => {
    const [line] = parseNote('- [ ] milk');
    expect(find(line, 'strike')).toHaveLength(0);
  });

  it('is a plain bullet without the box', () => {
    expect(parseNote('- [] milk')[0].kind).toBe('ul');
  });
});
