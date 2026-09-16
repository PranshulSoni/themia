/**
 * Smoke specs for the Office widget suite: Calendar, Contacts, Email,
 * OneDrive, To Do. These widgets all read from Microsoft Graph through
 * wrapped `invoke()` calls in `src/demo/invoke.ts`; with no accounts
 * configured they short-circuit on `effectiveIds.length === 0` (or
 * equivalent) and render an "add an account" empty state.
 *
 * Widgets are seeded directly into the layout (`seedWidgets`) so we don't
 * pay the ~2–3 s per widget cost of `pickWidget` + `clickGridCell`. Deeper
 * data-pipeline behaviour (token refresh, sign-in flow, paginated fetch)
 * belongs in dedicated specs alongside a mock Graph HTTP fixture server.
 */
import { expect } from 'chai';
import { $, $$ } from '@wdio/globals';
import { resetAppState, stubThemiaInvoke, seedWidgets } from '../helpers/app.js';

interface Case {
  type: string;
  rootSelector: string;
}

const CASES: Case[] = [
  { type: 'calendar', rootSelector: '.calendar-widget' },
  { type: 'contacts', rootSelector: '.contacts-widget' },
  { type: 'email',    rootSelector: '.email-widget' },
  { type: 'onedrive', rootSelector: '.onedrive-widget' },
  { type: 'todo',     rootSelector: '.todo-widget' },
];

describe('widget smoke (Office suite, no accounts)', () => {
  before(async () => {
    await resetAppState();
    await stubThemiaInvoke({
      email_list_accounts:    [],
      email_get_messages:     [],
      email_get_folder_status: { total: 0, unread: 0 },
      calendar_get_events:    [],
      calendar_get_birthdays: [],
      contacts_list:          [],
      onedrive_list_recent:   [],
      todo_lists:             [],
      todo_tasks:             [],
      '*': null,
    });
    await seedWidgets(CASES.map((c, i) => ({
      type: c.type,
      col: 1 + (i * 3),
      row: 1,
      colSpan: 3,
      rowSpan: 2,
    })));
  });

  for (const c of CASES) {
    it(`${c.type} renders ${c.rootSelector} with no accounts configured`, async () => {
      await $(c.rootSelector).waitForExist({ timeout: 5_000 });
      expect(await $$(c.rootSelector).length).to.be.at.least(1);
    });
  }
});
