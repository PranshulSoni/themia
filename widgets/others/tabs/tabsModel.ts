// Pure tabs-widget model helper, lucide/tauri-free so it is node-importable
// for unit tests (same pattern as widgets/others/stack/stackModel.ts).
// See widgets/others/tabs/SPEC.md.

/**
 * Active-tab resolution ([S-02]): a persisted `activeChildId` that is a current
 * child wins; otherwise the first child (a tab bar always has an active tab);
 * an empty container → null. A stale id is never rewritten, so re-adding the
 * child re-activates it.
 */
export function resolveActiveTabId(
  childIds: readonly string[],
  raw: unknown,
): string | null {
  if (typeof raw === 'string' && childIds.includes(raw)) return raw;
  return childIds.length > 0 ? childIds[0] : null;
}
