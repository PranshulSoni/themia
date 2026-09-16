import { describe, it, expect } from 'vitest';
import { readSource, parseRules } from '../../../tests/cssRules.ts';

// [S-01] The empty-note placeholder is a pseudo-element, and an absolutely
// positioned pseudo-element anchors to its originating element's PADDING box —
// inside the border, but OUTSIDE the padding. Pinned at `top: 0; left: 0` it
// therefore printed the hint hard against the editor's corner while the caret
// sat at the editor's `14px 16px` padding: the two were visibly out of line.
//
// The fix inherits the editor's padding instead of repeating it, so the hint and
// the first line of real text cannot drift apart when the padding is retuned.
// Measured in a browser at the time of the fix: placeholder text origin and
// first-line origin both (16, 14).
describe('notes [S-01] the placeholder sits where the caret does', () => {
  const rules = parseRules(readSource('widgets/others/notes/Notes.css'));

  const editor = rules.find(r => r.selector.trim() === '.notes-widget__editor');
  const placeholder = rules.find(r => r.selector.includes('.is-empty::before'));

  it('has both rules to compare', () => {
    expect(editor).toBeDefined();
    expect(placeholder).toBeDefined();
  });

  it('the editor still pads its content, which is what the caret follows', () => {
    expect(editor!.body).toMatch(/padding:\s*14px\s+16px/);
  });

  it('the placeholder inherits that padding rather than hardcoding an offset', () => {
    // `padding: inherit` is the whole point: a repeated literal would be free to
    // drift from the editor's, which is how the misalignment could return.
    expect(placeholder!.body).toMatch(/padding:\s*inherit/);
    expect(placeholder!.body).not.toMatch(/padding:\s*\d/);
  });

  it('the placeholder is not pinned to the padding-box corner', () => {
    // The old rule: `top: 0; left: 0` with no padding, i.e. origin (0, 0)
    // against the editor's text origin of (16, 14).
    const pinnedToCorner = /(^|[;{\s])top:\s*0/.test(placeholder!.body)
      && /(^|[;{\s])left:\s*0/.test(placeholder!.body)
      && !/padding:\s*inherit/.test(placeholder!.body);
    expect(pinnedToCorner).toBe(false);
  });
});
