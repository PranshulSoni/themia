// Issue #42: PR state-filter + sort-order applied to the raw `/pulls?state=all`
// payload before rendering. Pure, side-effect-free, no DOM, unit-tested in
// prFilterSort.test.ts. Closed sets are typed unions, never loose strings.

/** PR state filter. "closed" means anything not open (merged OR plain-closed). */
export const PR_STATE_FILTERS = ['open', 'closed', 'all'] as const;
export type PrStateFilter = (typeof PR_STATE_FILTERS)[number];

/** Sort order by PR `created_at`. */
export const PR_SORT_ORDERS = ['newest', 'oldest'] as const;
export type PrSortOrder = (typeof PR_SORT_ORDERS)[number];

export const DEFAULT_PR_STATE: PrStateFilter = 'open';
export const DEFAULT_PR_SORT: PrSortOrder = 'newest';

/** Minimal PR shape needed for filtering/sorting. */
export interface PrLike {
  number: number;
  state: string;
  merged_at: string | null;
  created_at: string;
}

/** Narrow an arbitrary string to a PrStateFilter, falling back to the default. */
export function asPrStateFilter(v: unknown): PrStateFilter {
  return PR_STATE_FILTERS.includes(v as PrStateFilter) ? (v as PrStateFilter) : DEFAULT_PR_STATE;
}

/** Narrow an arbitrary string to a PrSortOrder, falling back to the default. */
export function asPrSortOrder(v: unknown): PrSortOrder {
  return PR_SORT_ORDERS.includes(v as PrSortOrder) ? (v as PrSortOrder) : DEFAULT_PR_SORT;
}

function isOpen(pr: PrLike): boolean {
  return pr.state === 'open';
}

/**
 * Filter a mixed PR list by state and sort by creation time.
 * Returns a new array (input is never mutated).
 */
export function filterSortPRs<T extends PrLike>(
  prs: readonly T[],
  filter: PrStateFilter,
  order: PrSortOrder,
): T[] {
  const filtered = prs.filter((pr) => {
    if (filter === 'all') return true;
    if (filter === 'open') return isOpen(pr);
    return !isOpen(pr); // 'closed': merged or plain-closed
  });
  const sorted = [...filtered].sort((a, b) => {
    const ta = new Date(a.created_at).getTime();
    const tb = new Date(b.created_at).getTime();
    return order === 'newest' ? tb - ta : ta - tb;
  });
  return sorted;
}
