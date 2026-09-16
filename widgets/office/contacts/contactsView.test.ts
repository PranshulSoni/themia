import { describe, it, expect } from 'vitest';
import {
  filterContacts,
  limitContacts,
  visibleContacts,
  CONTACTS_DEFAULT_COUNT,
  type ContactLike,
} from './contactsView.ts';

// Regression for issue #21: the "Contacts shown" (5–100) config control was a
// no-op. The fetch pulled 500 per account and the rendered list was never
// sliced, so changing `count` did nothing. [S-17]
function mk(n: number): ContactLike[] {
  return Array.from({ length: n }, (_, i) => ({
    display_name: `Person ${i}`,
    email: `p${i}@example.com`,
  }));
}

describe('contacts view: count cap', () => {
  it('issue_21 [S-17] slices the list to `count`', () => {
    expect(limitContacts(mk(500), 25)).toHaveLength(25);
    expect(visibleContacts(mk(500), '', 5)).toHaveLength(5);
  });

  it('issue_21 [S-17] returns everything when fewer than `count`', () => {
    expect(limitContacts(mk(3), 25)).toHaveLength(3);
  });

  it('issue_21 [S-17] preserves order when slicing', () => {
    const out = limitContacts(mk(500), 2);
    expect(out.map(c => c.display_name)).toEqual(['Person 0', 'Person 1']);
  });

  it('issue_21 [S-17] falls back to the default cap when count is unset', () => {
    expect(limitContacts(mk(500), undefined)).toHaveLength(CONTACTS_DEFAULT_COUNT);
    expect(limitContacts(mk(500), NaN)).toHaveLength(CONTACTS_DEFAULT_COUNT);
  });
});

describe('contacts view: search filter then cap', () => {
  const list: ContactLike[] = [
    { display_name: 'Ada Lovelace', email: 'ada@calc.io', company: 'Analytical', job_title: 'Engineer' },
    { display_name: 'Alan Turing',  email: 'alan@bletchley.uk', company: 'GC&CS', job_title: 'Cryptanalyst' },
    { display_name: 'Grace Hopper', email: 'grace@navy.mil', company: 'US Navy', job_title: 'Admiral' },
  ];

  it('issue_21 [S-17] filters across name/email/company/title, then caps', () => {
    // Search spans the whole set; the cap applies to the RESULT, not a
    // pre-sliced window — otherwise search would only see the first N.
    expect(filterContacts(list, 'navy').map(c => c.display_name)).toEqual(['Grace Hopper']);
    expect(visibleContacts(list, 'a', 1)).toHaveLength(1);
  });

  it('issue_21 [S-17] empty query returns the full (capped) list', () => {
    expect(filterContacts(list, '')).toHaveLength(3);
    expect(visibleContacts(list, '   ', 100)).toHaveLength(3);
  });
});
