import { describe, it, expect } from 'vitest';
import { previewChildren, previewChildId } from './containerPreview.ts';

// Preview composition for the container widgets in the Add-Widget picker
// (stack SPEC [S-12], tabs SPEC [S-10]). A blank preview pane is the failure
// this exists to prevent, so the empty/garbage cases must degrade to [] (the
// container's own empty state) rather than to half-built children.

const cfg = (type: string) => ({ marker: type });

describe('previewChildren ([S-12]/[S-10])', () => {
  it('S-12_maps a type list to children with deterministic preview ids', () => {
    const kids = previewChildren(['clock', 'notes'], cfg);
    expect(kids.map(k => k.id)).toEqual([previewChildId(0), previewChildId(1)]);
    expect(kids.map(k => k.type)).toEqual(['clock', 'notes']);
    // Ids are what a fixture's collapsedIds / activeChildId refer to.
    expect(previewChildId(1)).toBe('preview-1');
  });

  it('S-12_gives each child the config the caller resolves for its type', () => {
    const kids = previewChildren(['clock'], cfg);
    expect(kids[0].config).toEqual({ marker: 'clock' });
  });

  it('S-12_children carry a dormant rect (never laid out on a grid)', () => {
    const [k] = previewChildren(['clock'], cfg);
    expect([k.x, k.y, k.w, k.h]).toEqual([0, 0, 0, 0]);
  });

  it('S-12_absent or non-array spec yields no children (falls back to the empty state)', () => {
    for (const junk of [undefined, null, 'clock', 42, { 0: 'clock' }]) {
      expect(previewChildren(junk, cfg)).toEqual([]);
    }
  });

  it('S-12_skips non-string and empty members, and ids stay contiguous', () => {
    const kids = previewChildren(['clock', 42, '', null, 'notes'], cfg);
    expect(kids.map(k => k.id)).toEqual(['preview-0', 'preview-1']);
    expect(kids.map(k => k.type)).toEqual(['clock', 'notes']);
  });

  it('S-12_a missing config resolver value degrades to an empty config, not undefined', () => {
    const kids = previewChildren(['clock'], () => undefined);
    expect(kids[0].config).toEqual({});
  });
});
