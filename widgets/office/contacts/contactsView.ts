// Pure view-derivation for the Contacts widget: search filtering + the
// "Contacts shown" cap. Extracted so it can be unit-tested without the .tsx
// component (the vitest suite has no JSX transform).

/** Minimal shape the view logic reads; the real `Contact` is a superset. */
export interface ContactLike {
  display_name: string;
  email?: string | null;
  company?: string | null;
  job_title?: string | null;
}

/** Fallback when `count` is unset — mirrors the config panel's `?? 20`. */
export const CONTACTS_DEFAULT_COUNT = 20;

/** Case-insensitive substring match over name / email / company / job title. */
export function filterContacts<T extends ContactLike>(contacts: T[], query: string): T[] {
  const q = query.trim().toLowerCase();
  if (!q) return contacts;
  return contacts.filter((c: T) =>
    (c.display_name || '').toLowerCase().includes(q) ||
    (c.email        || '').toLowerCase().includes(q) ||
    (c.company      || '').toLowerCase().includes(q) ||
    (c.job_title    || '').toLowerCase().includes(q)
  );
}

/** Cap the list to the "Contacts shown" config value (min 5 in the UI, no max). */
export function limitContacts<T>(contacts: T[], count: number | undefined): T[] {
  const n = Number.isFinite(count)
    ? Math.max(0, Math.floor(count as number))
    : CONTACTS_DEFAULT_COUNT;
  return contacts.length > n ? contacts.slice(0, n) : contacts;
}

/**
 * The list the widget renders: filter by the live search query first (so the
 * search spans the whole address book), then apply the `count` cap. [S-17]
 */
export function visibleContacts<T extends ContactLike>(
  contacts: T[],
  query: string,
  count: number | undefined,
): T[] {
  return limitContacts(filterContacts(contacts, query), count);
}
