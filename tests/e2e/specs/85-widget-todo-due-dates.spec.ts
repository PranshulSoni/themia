/**
 * To Do widget — due-date rendering.
 *
 * Covers widgets/office/todo/SPEC.md [S-48] (day resolution), [S-09] (label)
 * and [S-10] (overdue highlight), end to end: a stubbed `todo_get_tasks`
 * returns the exact payload shapes the two backends produce, and the assertions
 * read the labels the widget actually paints.
 *
 * Regression for nwalser/themia-releases#101: tasks created in the Microsoft
 * To Do mobile app arrived a day early — one due today rendered "overdue", one
 * due next Monday rendered as Sunday. That encoding writes the *creator's*
 * local midnight already converted to UTC, in a zoneless string; the old parser
 * read it as local time and kept the converted instant's date.
 *
 * The payload is built inside the app window, so every due date is expressed
 * relative to the machine's own zone and the spec reproduces the bug in any
 * zone that isn't UTC itself (where the two encodings coincide).
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, seedWidgets, stubThemiaInvoke } from '../helpers/app.js';

interface DueRow { title: string; due: string; overdue: boolean }

/**
 * Build the stub payload in the app window. `dayOffset` is relative to the
 * app's today; `encoding` picks how that day is put on the wire:
 *
 *  - `graphMidnight` — Graph `dueDateTime.dateTime`, midnight, zoneless.
 *  - `graphMobile`   — Graph, the To Do mobile app's encoding: the creator's
 *                      local midnight converted to UTC, still zoneless (#101).
 *  - `googleUtc`     — Google Tasks `due`: UTC midnight with an explicit `Z`.
 */
type Encoding = 'graphMidnight' | 'graphMobile' | 'googleUtc';
interface TaskDef { title: string; dayOffset: number; encoding: Encoding }

async function seedTasks(defs: TaskDef[]): Promise<void> {
  await stubThemiaInvoke({
    todo_get_tasks: await browser.execute((ds: TaskDef[]) => {
      const pad = (n: number) => String(n).padStart(2, '0');
      return ds.map((d, i) => {
        const now = new Date();
        const day = new Date(now.getFullYear(), now.getMonth(), now.getDate() + d.dayOffset);
        const ymd = `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
        let due: string;
        if (d.encoding === 'graphMidnight') due = `${ymd}T00:00:00.0000000`;
        else if (d.encoding === 'googleUtc') due = `${ymd}T00:00:00.000Z`;
        // Local midnight → UTC, minus the zone designator, with Graph's
        // 7-digit fraction: exactly what the mobile app's tasks look like.
        else due = day.toISOString().replace(/\.\d+Z$/, '.0000000');
        return {
          id: `task-${i}`, title: d.title, status: 'notStarted',
          importance: 'normal', due_date: due, completed_at: null, list_id: 'L1',
        };
      });
    }, defs),
  });
  await seedWidgets([{
    type: 'todo', colSpan: 4, rowSpan: 5,
    config: {
      accountIds: ['acc-1'], listId: 'L1', listName: 'Tasks',
      viewMode: 'list', showCompleted: false, refreshInterval: 600,
    },
  }]);
  await $('.todo-widget .todo-item').waitForExist({ timeout: 10_000 });
}

async function dueRows(): Promise<DueRow[]> {
  return browser.execute(() =>
    Array.from(document.querySelectorAll('.todo-widget .todo-item')).map(el => ({
      title: (el.querySelector('.todo-title')?.textContent ?? '').trim(),
      due: (el.querySelector('.todo-due')?.textContent ?? '').trim(),
      overdue: !!el.querySelector('.todo-due.overdue'),
    })),
  );
}

/** Short weekday name the app would print for `dayOffset` days from today. */
async function weekdayIn(dayOffset: number): Promise<string> {
  return browser.execute((off: number) => {
    const now = new Date();
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() + off);
    return d.toLocaleDateString('en', { weekday: 'short' });
  }, dayOffset);
}

describe('to do — due dates [S-48]', () => {
  beforeEach(async () => {
    await resetAppState();
  });

  it('S-48_ renders the intended day for every backend encoding', async () => {
    await seedTasks([
      { title: 'Graph midnight', dayOffset: 1, encoding: 'graphMidnight' },
      { title: 'Graph mobile',   dayOffset: 1, encoding: 'graphMobile' },
      { title: 'Google UTC',     dayOffset: 1, encoding: 'googleUtc' },
    ]);
    const rows = await dueRows();
    expect(rows.map(r => r.title)).to.deep.equal(['Graph midnight', 'Graph mobile', 'Google UTC']);
    // All three describe tomorrow, so all three must say so.
    expect(rows.map(r => r.due)).to.deep.equal(['tomorrow', 'tomorrow', 'tomorrow']);
    expect(rows.some(r => r.overdue)).to.equal(false);
  });

  // The reporter's first symptom.
  it('S-48_ a mobile-created task due today is not overdue (#101)', async () => {
    await seedTasks([{ title: 'Due today', dayOffset: 0, encoding: 'graphMobile' }]);
    const [row] = await dueRows();
    expect(row.due).to.equal('today');
    expect(row.overdue).to.equal(false);
  });

  // The reporter's second symptom: due next Monday, shown as Sunday.
  it('S-48_ a mobile-created task keeps its weekday (#101)', async () => {
    await seedTasks([{ title: 'Due in three days', dayOffset: 3, encoding: 'graphMobile' }]);
    const [row] = await dueRows();
    expect(row.due).to.equal(await weekdayIn(3));
  });

  it('S-10_ a genuinely past task is still overdue', async () => {
    await seedTasks([{ title: 'Late', dayOffset: -1, encoding: 'graphMobile' }]);
    const [row] = await dueRows();
    expect(row.due).to.equal('overdue');
    expect(row.overdue).to.equal(true);
  });
});
