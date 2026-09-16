// The source card owns its extra — notes [S-38], habit-tracker [S-19],
// to-do [S-61], calendar [S-49].
//
// The bug this pins: `.account-row` carried the border and fill, so an extra
// rendered after it (the note / list / calendar picker) landed OUTSIDE the card
// and read as a loose section floating under the source rather than as part of
// it. The chrome has to move up to `.account-entry` whenever an extra is
// actually present.
//
// A source scan, like the other CSS-rule guards: vitest has no browser, and the
// rule is a fact about the stylesheet.

import { describe, it, expect } from 'vitest';
import { readSource } from '../../tests/cssRules.ts';

const CSS = 'src/components/AccountManager.css';

/** The stylesheet with comments stripped, so prose can't satisfy a guard. */
function css(): string {
  return readSource(CSS).replace(/\/\*[\s\S]*?\*\//g, '');
}

/** The declaration block of the first rule whose selector matches. */
function block(selectorPart: string): string {
  const text = css();
  const at = text.indexOf(selectorPart);
  expect(at, `no rule for ${selectorPart}`).toBeGreaterThan(-1);
  const open = text.indexOf('{', at);
  const close = text.indexOf('}', open);
  expect(close).toBeGreaterThan(open);
  return text.slice(open + 1, close);
}

describe('an entry carrying an extra becomes the card', () => {
  it('the entry takes the border, fill and radius', () => {
    const b = block('.account-entry:has(> *:not(.account-row)) {');
    expect(b).toMatch(/border:\s*1px solid var\(--border\)/);
    expect(b).toMatch(/background:\s*var\(--overlay-subtle\)/);
    expect(b).toMatch(/border-radius:\s*8px/);
    // The row's corners are squared off below, so the entry must clip.
    expect(b).toMatch(/overflow:\s*hidden/);
  });

  it('the row inside gives up its own surface, so there is one border not two', () => {
    const b = block('.account-entry:has(> *:not(.account-row)) > .account-row');
    expect(b).toMatch(/background:\s*transparent/);
    expect(b).toMatch(/border-color:\s*transparent/);
    expect(b).toMatch(/border-radius:\s*0/);
  });

  it('the selected accent moves to the whole card', () => {
    const b = block('.account-entry:has(> .account-row.active)');
    expect(b).toMatch(/border-color:\s*var\(--primary-subtle-border\)/);
    expect(b).toMatch(/background:\s*var\(--primary-subtle-bg\)/);
  });

  it('the extra is inset by the shared rule, not by each widget', () => {
    // Owned here so every picker landing in this slot is inset identically —
    // and so a widget stylesheet cannot drift from the card's padding.
    expect(block('.account-entry > *:not(.account-row)')).toMatch(/padding:\s*0 12px 10px/);
  });

  it('gates on :has, not a class, so a closed <Show> keeps the plain row', () => {
    // To Do in account mode renders a comment node and no element, so the entry
    // must fall back to a bare wrapper with nothing to keep in sync.
    expect(css()).toContain(':has(> *:not(.account-row))');
    expect(css()).not.toMatch(/\.account-entry\.has-extra/);
  });
});

describe('the widget stylesheets no longer indent their own picker', () => {
  // Two paddings would compound into a double indent the moment the shared one
  // changed. Every picker in this slot is positioned by AccountManager.css.
  const PICKERS: [string, string][] = [
    ['widgets/others/notes/NotesConfig.css', '.notes-doc-list'],
    ['widgets/others/habit-tracker/HabitTrackerConfig.css', '.habit-source-lists'],
    ['widgets/office/todo/TodoConfig.css', '.todo-source-lists'],
    ['widgets/office/calendar/CalendarConfig.css', '.cal-cal-list'],
  ];

  for (const [file, cls] of PICKERS) {
    it(`${cls} declares no padding-left of its own`, () => {
      const text = readSource(file).replace(/\/\*[\s\S]*?\*\//g, '');
      // The picker's own base rule: `<cls> {` — not its descendants.
      const at = text.indexOf(`${cls} {`);
      expect(at, `no base rule for ${cls} in ${file}`).toBeGreaterThan(-1);
      const body = text.slice(text.indexOf('{', at) + 1, text.indexOf('}', at));
      expect(body).not.toMatch(/padding-left/);
    });
  }
});
