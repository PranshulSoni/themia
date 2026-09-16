// [S-22] When two config-section lists mean the same thing.
//
// Split out of ConfigTabs.tsx so it can be unit-tested: vitest here has no JSX
// transform, so nothing importable from a `.tsx` can be reached from a test
// (see pingConfig.test.ts).
//
// The section list arrives through a props getter, and a panel whose sections
// depend on its config (To Do drops "Refresh" in local mode, [S-47]) rebuilds
// that array on every config write. Compared by reference, each write looked
// like a brand-new section list and re-created the whole panel body; compared
// by content, only a real change does.

export interface ConfigSection {
  key: string;
  label: string;
}

/**
 * True when two section lists are the same sections in the same order.
 *
 * Compares `key` and `label` only. `key` identifies the section; `label` is
 * user-visible and changes with the language, which must re-render. Icons are
 * module-level component references — stable by construction, and comparing
 * them would make every list unequal if a panel ever built them inline.
 */
export function sameSections(a: readonly ConfigSection[], b: readonly ConfigSection[]): boolean {
  if (a === b) return true;
  if (a.length !== b.length) return false;
  return a.every((section, i) => section.key === b[i].key && section.label === b[i].label);
}
