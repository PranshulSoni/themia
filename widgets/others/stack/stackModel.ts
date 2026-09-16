// Pure stack-widget model helper, kept lucide/tauri-free so it is
// node-importable for unit tests (same pattern as src/widgets/headerMode.ts).
// See widgets/others/stack/SPEC.md. The stack is accordion-only; the tabbed
// container is a separate widget (widgets/others/tabs).

/**
 * The one open child ([S-03]). A stack shows TITLE ROWS and opens AT MOST ONE
 * child, so the state is a single id, not a set: opening another closes the
 * previous one, and the open child always gets all the space the rows leave.
 *
 * The persisted `expandedId` is read straight through: a stale id (child popped
 * out or removed) resolves to "nothing open" and is never rewritten, so it
 * re-applies if that child comes back. Anything that is not a current child's
 * id — absent, null, junk — is likewise "nothing open", which is also the
 * default a fresh stack starts in.
 */
export function resolveExpandedId(
  childIds: readonly string[],
  raw: unknown,
): string | null {
  return typeof raw === 'string' && childIds.includes(raw) ? raw : null;
}
