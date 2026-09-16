// @vitest-environment jsdom
//
// [S-53] What the config panel's task-list fetch reacts to.
//
// Regression: the panel refetched the lists on EVERY config write. The fetch
// effect read the account out of `props.config`, and the panel's `update()`
// writes a whole new config object for any change — so picking a list, flipping
// the view mode or nudging the title lines each re-ran it, and each re-run put
// the picker back to its "Loading lists…" line. The panel jumped on every
// click. These tests drive the real reactive path, so they fail against the old
// read-the-config-bag effect.
//
// Mounted with solid-js/web `render` so the full scheduler runs — a bare
// createRoot under vitest never advances it (see createCachedFetch.test.ts).

import { describe, it, expect, afterEach } from 'vitest';
import { createSignal } from 'solid-js';
import { render } from 'solid-js/web';
import { createTaskLists, type TaskListsStore, type TodoTaskList } from './taskLists.ts';

let dispose: (() => void) | null = null;
let host: HTMLDivElement | null = null;
afterEach(() => {
  dispose?.();
  dispose = null;
  host?.remove();
  host = null;
});

const LISTS: TodoTaskList[] = [
  { id: 'L1', display_name: 'Tasks', is_default: true },
  { id: 'L2', display_name: 'Work', is_default: false },
];

/** The panel's shape: one config OBJECT signal, replaced wholesale on any edit
 *  — which is exactly how `update()` writes. */
function mountWithConfig(initial: Record<string, unknown>) {
  const [config, setConfig] = createSignal<Record<string, unknown>>(initial);
  const calls: string[] = [];
  let store!: TaskListsStore;

  host = document.createElement('div');
  document.body.appendChild(host);
  dispose = render(() => {
    store = createTaskLists({
      accountId: () => ((config().accountIds as string[]) ?? [])[0] ?? '',
      fetch: async (accountId) => {
        calls.push(accountId);
        return LISTS;
      },
      mapError: (raw) => raw,
    });
    return null;
  }, host);

  /** Write an unrelated field the way the panel does: a NEW object every time. */
  const edit = (patch: Record<string, unknown>) => setConfig({ ...config(), ...patch });
  return { store: () => store, calls, edit, setConfig };
}

const settle = () => new Promise((r) => setTimeout(r, 0));

describe('createTaskLists — what it reacts to [S-53]', () => {
  it('S-53_ fetches once for the selected account', async () => {
    const { calls } = mountWithConfig({ accountIds: ['A1'] });
    await settle();
    expect(calls).toEqual(['A1']);
  });

  it('S-53_ does NOT refetch when an unrelated config field changes', async () => {
    const { calls, edit } = mountWithConfig({ accountIds: ['A1'] });
    await settle();
    expect(calls).toEqual(['A1']);

    // Every one of these is a whole new config object, same account.
    edit({ listId: 'L2', listName: 'Work' });
    edit({ viewMode: 'compact' });
    edit({ titleLines: 3 });
    edit({ showCompleted: true });
    await settle();

    expect(calls).toEqual(['A1']);
  });

  it('S-53_ refetches when the account actually changes', async () => {
    const { calls, edit } = mountWithConfig({ accountIds: ['A1'] });
    await settle();
    edit({ accountIds: ['A2'] });
    await settle();
    expect(calls).toEqual(['A1', 'A2']);
  });

  it('S-53_ clears the lists and does not fetch when no account is selected', async () => {
    const { store, calls, edit } = mountWithConfig({ accountIds: ['A1'] });
    await settle();
    expect(store().lists()).toHaveLength(2);

    edit({ accountIds: [] });
    await settle();
    expect(calls).toEqual(['A1']);
    expect(store().lists()).toEqual([]);
  });
});

describe('createTaskLists — the picker does not collapse [S-53]', () => {
  it('S-53_ is pending only until the first lists arrive', async () => {
    const { store } = mountWithConfig({ accountIds: ['A1'] });
    expect(store().pending()).toBe(true);
    await settle();
    expect(store().pending()).toBe(false);
    expect(store().lists()).toHaveLength(2);
  });

  it('S-53_ keeps the lists on screen across an account-change refetch', async () => {
    const { store, edit } = mountWithConfig({ accountIds: ['A1'] });
    await settle();

    edit({ accountIds: ['A2'] });
    // Mid-flight: still loading, but the picker has rows, so it must not be
    // swapped for the loading line.
    expect(store().pending()).toBe(false);
    expect(store().lists()).toHaveLength(2);

    await settle();
    expect(store().lists()).toHaveLength(2);
  });

  it('S-53_ reset() empties the lists and goes pending again', async () => {
    const { store } = mountWithConfig({ accountIds: ['A1'] });
    await settle();
    store().reset();
    expect(store().lists()).toEqual([]);
    expect(store().error()).toBeNull();
  });

  it('S-53_ a fetch in flight when reset() lands cannot repopulate the picker', async () => {
    const [config, setConfig] = createSignal<Record<string, unknown>>({ accountIds: ['A1'] });
    let release!: (v: TodoTaskList[]) => void;
    let store!: TaskListsStore;

    host = document.createElement('div');
    document.body.appendChild(host);
    dispose = render(() => {
      store = createTaskLists({
        accountId: () => ((config().accountIds as string[]) ?? [])[0] ?? '',
        fetch: () => new Promise<TodoTaskList[]>((res) => { release = res; }),
        mapError: (raw) => raw,
      });
      return null;
    }, host);

    store.reset();
    release(LISTS);
    await settle();
    expect(store.lists()).toEqual([]);
    void setConfig;
  });
});

describe('createTaskLists — errors [S-53]', () => {
  it('S-53_ maps the raw backend error and stops pending', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    let store!: TaskListsStore;
    dispose = render(() => {
      store = createTaskLists({
        accountId: () => 'A1',
        fetch: async () => { throw 'todo_not_authorized'; },
        mapError: (raw) => (raw === 'todo_not_authorized' ? 'Re-sign in' : raw),
      });
      return null;
    }, host);

    await settle();
    expect(store.error()).toBe('Re-sign in');
    expect(store.pending()).toBe(false);
  });
});
