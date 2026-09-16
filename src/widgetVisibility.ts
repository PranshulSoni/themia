// Widgets that are currently painting nothing at all (Now Playing with nothing
// playing, music [S-20]).
//
// A widget REPORTS itself hidden here; two readers act on it:
//   - `WidgetGrid` drops the tile's chrome, so an empty bordered frame doesn't
//     outline the gap where the widget was.
//   - `App.tsx`'s interactive-rect push drops the tile from the window
//     hit-mask, so clicks land on the Windows desktop rather than on an
//     invisible box that eats them.
//
// A signal over a plain array, not a Set or a store: widget counts are tiny, and
// the rect push needs ONE coarse reactive source to depend on. A widget always
// clears its flag on unmount, so an id can't leak and hide a tile forever.
import { createSignal } from 'solid-js';

const [ids, setIds] = createSignal<readonly string[]>([]);

/** Reactive: ids of every widget currently reporting itself hidden. */
export const hiddenWidgetIds = ids;

/** Reactive: whether `id` is currently hidden. */
export function isWidgetHidden(id: string): boolean {
  return ids().includes(id);
}

/** Report (or clear) a widget's hidden state. A no-op when the flag is already
 *  in the requested state, so a widget may call it on every snapshot without
 *  churning the rect push. */
export function setWidgetHidden(id: string, hidden: boolean): void {
  const cur = ids();
  if (cur.includes(id) === hidden) return;
  setIds(hidden ? [...cur, id] : cur.filter(x => x !== id));
}
