import { describe, it, expect } from 'vitest';
import { computePickerGroups, CATEGORY_ORDER, KNOWN_CATEGORIES, type PickerEntry } from './pickerGroups.ts';
import type { WidgetDefinition } from '../widgets/registry.tsx';

// Minimal fake entry: computePickerGroups only reads `category` / `providers`.
const entry = (type: string, category: string, providers?: string[]): PickerEntry =>
  [type, { category, providers } as WidgetDefinition];

const headings = (groups: { heading: string | null }[]) => groups.map(g => g.heading);

describe('computePickerGroups: category mode', () => {
  it('stack + tabs land in their own Layout bucket, ordered before Others', () => {
    const groups = computePickerGroups(
      [
        entry('clock', 'Others'),
        entry('stack', 'Layout'),
        entry('tabs', 'Layout'),
      ],
      'category',
    );
    const layout = groups.find(g => g.heading === 'Layout');
    expect(layout).toBeDefined();
    expect(layout!.entries.map(([t]) => t).sort()).toEqual(['stack', 'tabs']);
    // Layout is a first-class category, ordered ahead of the misc Others bucket.
    const hs = headings(groups);
    expect(hs).toContain('Layout');
    expect(hs.indexOf('Layout')).toBeLessThan(hs.indexOf('Others'));
  });

  it('Layout is a known, ordered category (not an alphabetical straggler)', () => {
    expect(CATEGORY_ORDER).toContain('Layout');
    expect(KNOWN_CATEGORIES.has('Layout')).toBe(true);
  });

  it('known categories keep their fixed order; unknown ones follow alphabetically', () => {
    const groups = computePickerGroups(
      [
        entry('z', 'Zeta'),      // unknown → sorts after known
        entry('a', 'System'),
        entry('b', 'Office'),
        entry('c', 'Layout'),
      ],
      'category',
    );
    expect(headings(groups)).toEqual(['Office', 'System', 'Layout', 'Zeta']);
  });
});

describe('computePickerGroups: none / provider modes unchanged', () => {
  it('none mode returns a single unlabelled group', () => {
    const entries = [entry('a', 'Office'), entry('b', 'Layout')];
    expect(computePickerGroups(entries, 'none')).toEqual([{ heading: null, entries }]);
    expect(computePickerGroups([], 'none')).toEqual([]);
  });

  it('provider mode buckets by provider with General last', () => {
    const groups = computePickerGroups(
      [
        entry('gh', 'Online', ['GitHub']),
        entry('clock', 'Layout'),          // no providers → General
        entry('mail', 'Office', ['Microsoft']),
      ],
      'provider',
    );
    expect(headings(groups)).toEqual(['GitHub', 'Microsoft', 'General']);
  });
});
