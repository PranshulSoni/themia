// Pure filename-filter logic for the folder widget. [S-21]
//
// Kept side-effect-free and reactivity-free so the comma-separated keyword
// matching can be unit-tested without a DOM. FolderView.tsx routes both the
// configured `filter` and the runtime header-search string through these
// helpers when reducing the entry list.

/**
 * Parse a raw filter string into a list of lowercase keyword tokens.
 *
 * Tokens are split on commas; empty/whitespace-only tokens (including the one
 * produced by a trailing comma) are dropped, and each surviving token is
 * trimmed and lowercased. Returns an empty array when no real keyword is
 * present; callers treat that as "no filter". [S-21]
 */
export function parseFilterKeywords(raw: string | null | undefined): string[] {
  if (!raw) return [];
  return raw
    .split(',')
    .map((t) => t.trim().toLowerCase())
    .filter((t) => t.length > 0);
}

/**
 * True when `name` matches the parsed `keywords` (OR semantics): the filename
 * contains AT LEAST ONE keyword, case-insensitively. An empty keyword list
 * matches everything (no active filter). [S-21]
 */
export function matchesFilter(name: string, keywords: string[]): boolean {
  if (keywords.length === 0) return true;
  const lower = name.toLowerCase();
  return keywords.some((k) => lower.includes(k));
}
