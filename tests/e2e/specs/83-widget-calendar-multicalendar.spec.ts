/**
 * Multi-calendar selection for the Calendar widget.
 *
 * SPEC tags mirrored in test titles:
 *  - [S-49] With no `calendars` config an account shows its DEFAULT calendar
 *    (events fetched with `calendarId` undefined). With an explicit selection
 *    it shows exactly those calendars.
 *  - [S-50] One `calendar_get_events` request per selected calendar; the stub
 *    branches on `calendarId` so each calendar returns distinct events.
 *  - [S-51] More than one displayed calendar → each event chip gets a colored
 *    left border, and the two calendars' borders differ.
 *
 * The widget is seeded directly (`seedWidgets`) with config; the invoke stub
 * returns per-`calendarId` events. Agenda view gives a deterministic list.
 */
import { expect } from 'chai';
import { $, $$, browser } from '@wdio/globals';
import { resetAppState, seedWidgets } from '../helpers/app.js';

// Install an invoke stub that returns different events per calendarId, so a
// fetched calendar is identifiable by its event subjects.
async function stubCalendarInvoke(): Promise<void> {
  await browser.execute(() => {
    const hook = (window as any).__themiaInvoke;
    if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed');
    const iso = (dayOffset: number, hour: number) => {
      const d = new Date();
      d.setHours(0, 0, 0, 0);
      d.setDate(d.getDate() + dayOffset);
      d.setHours(hour, 0, 0, 0);
      return d.toISOString().replace(/\.\d{3}Z$/, 'Z');
    };
    const ev = (id: string, subject: string) => ({
      id, subject,
      start_dt: iso(1, 10), end_dt: iso(1, 11),
      is_all_day: false, location: null, organizer: null, web_link: '',
    });
    const byCalendar: Record<string, any[]> = {
      __default__: [ev('ev-primary', 'Primary Event')],
      'cal-work': [ev('ev-work', 'Work Event')],
      'cal-personal': [ev('ev-personal', 'Personal Event')],
    };
    hook.setInvoke(async (cmd: string, args: any) => {
      switch (cmd) {
        case 'email_list_accounts':
          return [{ id: 'acc', email: 'a@example.com', display_name: 'Acc', provider: 'google' }];
        case 'calendar_get_events':
          return byCalendar[args?.calendarId ?? '__default__'] ?? [];
        case 'calendar_get_birthdays':
          return [];
        case 'calendar_list_calendars':
          return [
            { id: 'cal-primary', name: 'Primary', is_default: true, color: null },
            { id: 'cal-work', name: 'Work', is_default: false, color: null },
            { id: 'cal-personal', name: 'Personal', is_default: false, color: null },
          ];
        default:
          return null;
      }
    });
  });
}

const agendaSubjects = async (): Promise<string[]> => {
  const out: string[] = [];
  for await (const el of $$('.calendar-widget .ag-title')) out.push((await el.getText()).trim());
  return out;
};

describe('calendar multi-calendar selection', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubCalendarInvoke();
  });

  it('[S-49] with no selection shows the account default calendar only', async () => {
    await seedWidgets([{
      type: 'calendar', col: 1, row: 1, colSpan: 4, rowSpan: 3,
      config: { accountIds: ['acc'], defaultView: 'agenda' },
    }]);
    await $('.calendar-widget .ag-ev').waitForExist({ timeout: 5_000 });
    const subjects = await agendaSubjects();
    expect(subjects).to.include('Primary Event');
    expect(subjects).to.not.include('Work Event');
    expect(subjects).to.not.include('Personal Event');
  });

  it('[S-49][S-50] an explicit selection shows exactly the selected calendars', async () => {
    await seedWidgets([{
      type: 'calendar', col: 1, row: 1, colSpan: 4, rowSpan: 3,
      config: {
        accountIds: ['acc'],
        calendars: { acc: ['cal-work', 'cal-personal'] },
        defaultView: 'agenda',
      },
    }]);
    await $('.calendar-widget .ag-ev').waitForExist({ timeout: 5_000 });
    const subjects = await agendaSubjects();
    expect(subjects).to.include('Work Event');
    expect(subjects).to.include('Personal Event');
    // The default (primary) calendar is NOT fetched when a selection exists.
    expect(subjects).to.not.include('Primary Event');
  });

  it('[S-51] more than one displayed calendar gives each a distinct color', async () => {
    await seedWidgets([{
      type: 'calendar', col: 1, row: 1, colSpan: 4, rowSpan: 3,
      config: {
        accountIds: ['acc'],
        calendars: { acc: ['cal-work', 'cal-personal'] },
        defaultView: 'agenda',
      },
    }]);
    await $('.calendar-widget .ag-ev').waitForExist({ timeout: 5_000 });
    const borders = await browser.execute(() =>
      [...document.querySelectorAll('.calendar-widget .ag-ev')].map(
        el => getComputedStyle(el as Element).borderLeftColor,
      ),
    );
    expect(borders.length).to.be.at.least(2);
    // Two calendars → two different palette colors on the chips.
    expect(new Set(borders).size).to.be.at.least(2);
  });
});
