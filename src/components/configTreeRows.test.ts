// @vitest-environment jsdom
//
// Runs under jsdom because configTreeRows imports `isContainerType` from
// layout.ts (the single source of truth for the container type set), which
// pulls in sync.ts (window + BroadcastChannel).
import { describe, it, expect } from 'vitest';
import { buildWidgetRows } from './configTreeRows.ts';
import type { Widget } from '../layout.ts';

// Config-tree nesting for container widgets (stack SPEC [S-14] / tabs [S-12]).

const w = (id: string, type: string, extra: Partial<Widget> = {}): Widget =>
  ({ id, type, x: 0, y: 0, w: 0.2, h: 0.2, ...extra });

const shape = (rows: ReturnType<typeof buildWidgetRows>) =>
  rows.map(r => `${r.widget.id}@${r.depth}`);

describe('buildWidgetRows: container children nest under their container', () => {
  it('S-14_a container is followed by its own children, at depth 2', () => {
    const rows = buildWidgetRows([
      w('clock-1', 'clock'),
      w('stack-1', 'stack'),
      w('notes-1', 'notes', { parentId: 'stack-1' }),
      w('todo-1', 'todo', { parentId: 'stack-1' }),
    ]);
    expect(shape(rows)).toEqual(['clock-1@1', 'stack-1@1', 'notes-1@2', 'todo-1@2']);
    expect(rows[2].parentId).toBe('stack-1');
    expect(rows[0].parentId).toBeUndefined();
  });

  it('S-14_children keep widgets-array order, matching the order the tile renders', () => {
    const rows = buildWidgetRows([
      w('stack-1', 'stack'),
      w('b', 'notes', { parentId: 'stack-1' }),
      w('a', 'clock', { parentId: 'stack-1' }),
    ]);
    expect(shape(rows)).toEqual(['stack-1@1', 'b@2', 'a@2']);
  });

  it('S-14_a child is never ALSO listed at top level (no duplicate rows)', () => {
    const rows = buildWidgetRows([
      w('stack-1', 'stack'),
      w('notes-1', 'notes', { parentId: 'stack-1' }),
    ]);
    expect(rows.filter(r => r.widget.id === 'notes-1')).toHaveLength(1);
  });

  it('S-14_nests under a tabs container too (both container types)', () => {
    const rows = buildWidgetRows([
      w('tabs-1', 'tabs'),
      w('notes-1', 'notes', { parentId: 'tabs-1' }),
    ]);
    expect(shape(rows)).toEqual(['tabs-1@1', 'notes-1@2']);
  });

  it('S-14_two containers each keep their own children', () => {
    const rows = buildWidgetRows([
      w('stack-1', 'stack'),
      w('tabs-1', 'tabs'),
      w('a', 'clock', { parentId: 'stack-1' }),
      w('b', 'notes', { parentId: 'tabs-1' }),
    ]);
    expect(shape(rows)).toEqual(['stack-1@1', 'a@2', 'tabs-1@1', 'b@2']);
  });

  it('S-14_an unhonourable parentId still yields a visible top-level row (never unreachable)', () => {
    // parse() strips these ([S-20]); the tree is the only route to a widget's
    // config, so a bad reference must not hide it.
    const rows = buildWidgetRows([
      w('clock-1', 'clock'),
      w('orphan', 'notes', { parentId: 'gone' }),
      w('mis-parented', 'todo', { parentId: 'clock-1' }), // parent isn't a container
    ]);
    expect(shape(rows)).toEqual(['clock-1@1', 'orphan@1', 'mis-parented@1']);
  });

  it('S-14_an empty container is just its own row', () => {
    expect(shape(buildWidgetRows([w('stack-1', 'stack')]))).toEqual(['stack-1@1']);
  });

  it('S-14_no widgets yields no rows', () => {
    expect(buildWidgetRows([])).toEqual([]);
  });
});
