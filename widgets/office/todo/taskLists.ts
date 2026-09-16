// The config panel's task-list source ([S-53]). Lifted out of TodoConfig.tsx so
// what it reacts to is a contract that can be tested — vitest has no JSX
// transform, so a config panel itself can never be mounted in a unit test
// (see pingConfig.test.ts).
//
// The panel is stateless by rule ([S-13]): every control derives from
// `props.config`, and `update()` writes a WHOLE NEW config object. So an effect
// that reads the config bag to find the account re-runs on every unrelated
// write — picking a list, changing the view mode, nudging the title lines — and
// each re-run refetched the lists and flipped the picker back to its loading
// line, so the panel jumped on every click. The fix is the `accountId` memo
// below: it still recomputes on each config write, but only *notifies* when the
// selected account actually changes.

import { createSignal, createMemo, createEffect, type Accessor } from 'solid-js';

export interface TodoTaskList {
  id: string;
  display_name: string;
  is_default: boolean;
}

export interface TaskListsStore {
  lists: Accessor<TodoTaskList[]>;
  error: Accessor<string | null>;
  /** [S-53] True only while there is nothing to draw yet. The loading line may
   *  replace the picker then, and only then: a refresh that already has lists
   *  keeps them on screen instead of collapsing the panel. */
  pending: Accessor<boolean>;
  /** Drop the cached lists — for a switch that invalidates them outright (a
   *  different account, or going local), where showing the old account's lists
   *  until the fetch lands would be worse than the gap. */
  reset: () => void;
}

export interface TaskListsInput {
  /** The selected account. Read through a memo, so the config bag it comes out
   *  of can churn without re-triggering the fetch. Empty means "no account". */
  accountId: Accessor<string>;
  /** Bumped when accounts are added/removed elsewhere (the accounts modal);
   *  a change refetches even if the selected account id is unchanged. */
  revision?: Accessor<unknown>;
  fetch: (accountId: string) => Promise<TodoTaskList[]>;
  /** Raw backend error → the string to show. */
  mapError: (raw: string) => string;
  /** Runs after a successful fetch, for the caller's own follow-up (selecting a
   *  default list). Outside the tracking scope, so it may read and write config. */
  onLoaded?: (lists: TodoTaskList[]) => void;
}

export function createTaskLists(input: TaskListsInput): TaskListsStore {
  const [lists, setLists] = createSignal<TodoTaskList[]>([]);
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);

  // The whole point: `===` on the id string, so a new config object carrying the
  // same account is not a change.
  const accountId = createMemo(() => input.accountId());

  // Request-id guard: a slower in-flight fetch for a previously-selected account
  // must not land on top of the current one. Mirrors `loadSeq` in Todo.tsx (#31).
  let seq = 0;

  async function run(id: string): Promise<void> {
    const my = ++seq;
    setLoading(true);
    setError(null);
    try {
      const result = await input.fetch(id);
      if (my !== seq) return;
      setLists(result);
      input.onLoaded?.(result);
    } catch (e) {
      if (my !== seq) return;
      setError(input.mapError(String(e)));
    } finally {
      if (my === seq) setLoading(false);
    }
  }

  // Sync effect body: everything it tracks is read here, before any await, so
  // the dependency set is exactly `revision` + `accountId` and nothing can be
  // added by accident further down.
  createEffect(() => {
    input.revision?.();
    const id = accountId();
    if (!id) {
      setLists([]);
      setError(null);
      return;
    }
    void run(id);
  });

  return {
    lists,
    error,
    pending: () => loading() && lists().length === 0,
    reset: () => {
      // Invalidate any in-flight fetch too, or its result would repopulate the
      // list we just cleared.
      seq++;
      setLists([]);
      setLoading(false);
      setError(null);
    },
  };
}
