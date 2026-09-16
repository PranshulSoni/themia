// Issue #42: GitHub widget can't filter/sort pull requests.
// Pure-logic regression test for the PR state-filter + sort-order applied to
// the raw `/pulls?state=all` payload before rendering. Default behaviour must be
// OPEN-only, newest-first (by created_at). State filter and sort order are typed
// unions (no loose strings).

import { describe, it, expect } from 'vitest';
import {
  filterSortPRs,
  PR_STATE_FILTERS,
  PR_SORT_ORDERS,
  type PrLike,
  type PrStateFilter,
  type PrSortOrder,
} from './prFilterSort.ts';

// Mixed list: open + closed (merged counts as closed) with out-of-order dates.
const PRS: PrLike[] = [
  { number: 1, state: 'open', merged_at: null, created_at: '2026-01-01T00:00:00Z' },
  { number: 2, state: 'closed', merged_at: '2026-03-01T00:00:00Z', created_at: '2026-02-01T00:00:00Z' }, // merged
  { number: 3, state: 'open', merged_at: null, created_at: '2026-06-01T00:00:00Z' },
  { number: 4, state: 'closed', merged_at: null, created_at: '2026-04-01T00:00:00Z' }, // closed unmerged
  { number: 5, state: 'open', merged_at: null, created_at: '2026-03-15T00:00:00Z' },
];

describe('issue_42 filterSortPRs', () => {
  it('issue_42_defaults_open_only_newest_first', () => {
    // Defaults: state=open, sort=newest. Closed/merged dropped; open sorted desc by created_at.
    const out = filterSortPRs(PRS, 'open', 'newest');
    expect(out.map((p) => p.number)).toEqual([3, 5, 1]);
  });

  it('issue_42_state_closed_keeps_merged_and_closed', () => {
    // "closed" filter keeps both merged + plain-closed PRs (anything not open).
    const out = filterSortPRs(PRS, 'closed', 'newest');
    expect(out.map((p) => p.number)).toEqual([4, 2]);
  });

  it('issue_42_state_all_keeps_everything', () => {
    const out = filterSortPRs(PRS, 'all', 'newest');
    expect(out.map((p) => p.number)).toEqual([3, 4, 5, 2, 1]);
  });

  it('issue_42_sort_oldest_first', () => {
    const out = filterSortPRs(PRS, 'all', 'oldest');
    expect(out.map((p) => p.number)).toEqual([1, 2, 5, 4, 3]);
  });

  it('issue_42_does_not_mutate_input', () => {
    const snapshot = PRS.map((p) => p.number);
    filterSortPRs(PRS, 'all', 'newest');
    expect(PRS.map((p) => p.number)).toEqual(snapshot);
  });

  it('issue_42_typed_union_constants', () => {
    // Closed sets are enums/unions, not loose strings.
    expect(PR_STATE_FILTERS).toEqual(['open', 'closed', 'all']);
    expect(PR_SORT_ORDERS).toEqual(['newest', 'oldest']);
    const f: PrStateFilter = 'open';
    const s: PrSortOrder = 'newest';
    expect(PR_STATE_FILTERS.includes(f)).toBe(true);
    expect(PR_SORT_ORDERS.includes(s)).toBe(true);
  });
});
