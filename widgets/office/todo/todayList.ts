// The virtual "Today" list ([S-52]). Kept out of Todo.tsx so the membership
// rule can be unit-tested without mounting the widget, and i18n-free so it is
// node-importable.
//
// Microsoft To Do's own "My Day" is not reachable: Graph exposes no My Day
// property on `todoTask` (v1.0 or beta), and `todoTaskList.wellknownListName`
// only has `none | defaultList | flaggedEmails`. `/beta/me/planner/myDayTasks`
// returns Planner tasks, not To Do ones, and is work-or-school only. So the
// half of My Day that *is* visible over the API — "due today", per Microsoft's
// own definition of the view — is assembled here across every list instead.
// Issue nwalser/themia-releases#80.

import { resolveDue } from './dueDate.ts';

/** Config `listId` standing for the cross-list Today view. Deliberately not a
 *  possible Graph/Google list id (both are opaque base64-ish strings). */
export const TODAY_LIST_ID = '__today__';

export function isTodayList(listId: string | null | undefined): boolean {
  return listId === TODAY_LIST_ID;
}

/** The shape Today needs off a task; the widget's fuller `TodoTask` satisfies it. */
export interface DueBearing {
  due_date: string | null;
}

/**
 * [S-52] Today = every task due today or already overdue, oldest first.
 *
 * A task with no due date is not in Today: it isn't scheduled for any day, and
 * including it would make Today a copy of "all tasks". Overdue tasks *are*
 * included — a task that slipped is exactly what daily planning is for, and it
 * mirrors how the widget already labels them ([S-10]).
 *
 * Day resolution goes through `resolveDue` ([S-48]) rather than comparing
 * strings, so the two datetime encodings both backends ship land on the right
 * calendar day for the viewer's zone.
 */
export function todayTasks<T extends DueBearing>(tasks: T[], now: Date = new Date()): T[] {
  return tasks
    .map((task) => ({ task, due: resolveDue(task.due_date, now) }))
    .filter((row) => row.due !== null && row.due.days <= 0)
    // Sort is stable, so tasks sharing a day keep the order the backend gave.
    .sort((a, b) => (a.due?.days ?? 0) - (b.due?.days ?? 0))
    .map((row) => row.task);
}

/**
 * [S-52] The day a task created from the Today view is due: today, local.
 *
 * Returned as a bare `YYYY-MM-DD` because that is the one encoding both
 * backends read back as the day meant (`dueDay` reads a midnight wall clock
 * verbatim, [S-48]); the Rust side wraps it in each provider's field shape.
 * Computed here, not in Rust, because "today" is the *user's* day and this side
 * already owns day semantics.
 */
export function todayDueDate(now: Date = new Date()): string {
  const mm = String(now.getMonth() + 1).padStart(2, '0');
  const dd = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${mm}-${dd}`;
}
