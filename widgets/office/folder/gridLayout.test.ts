import { describe, it, expect } from 'vitest';
import { gridTemplate } from './gridLayout.ts';

// Mirrors GRID_SIZE_PRESETS.small in FolderView.tsx.
const SMALL = { minColWidth: 80, rowHeight: 90 };

describe('folder grid template (#37)', () => {
  it('columns stretch to fill the row (minmax auto-fill)', () => {
    const t = gridTemplate({ ...SMALL, stretchHeight: false });
    expect(t.columns).toBe('repeat(auto-fill, minmax(80px, 1fr))');
    expect(t.columns).toContain('1fr');
    expect(t.autoRows).toBe('90px');
  });

  it('honours the effective column width / row height the caller passes', () => {
    // The icon-grid view feeds smaller values (no filename gutter); the template
    // must reflect them verbatim rather than owning any size math itself.
    const t = gridTemplate({ minColWidth: 52, rowHeight: 52, stretchHeight: false });
    expect(t.columns).toBe('repeat(auto-fill, minmax(52px, 1fr))');
    expect(t.autoRows).toBe('52px');
  });

  it('stretchHeight overrides the row height with 1fr', () => {
    expect(gridTemplate({ ...SMALL, stretchHeight: true }).autoRows).toBe('1fr');
    // Columns are unaffected by the height stretch.
    expect(gridTemplate({ ...SMALL, stretchHeight: true }).columns).toBe('repeat(auto-fill, minmax(80px, 1fr))');
  });
});
