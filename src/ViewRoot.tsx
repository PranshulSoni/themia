import { type JSX } from 'solid-js';
import { LayoutContext } from './viewContext.tsx';
import { getViewLayoutStore } from './layout.ts';

/**
 * Mounts a view's widget tree wrapped in a per-view layout context.
 * Exists for every view in viewStore.list; only the active one is
 * visible (via the `active` prop). Hidden views stay mounted so switching
 * is instant: no widget remount, no createEffect re-run, no refetch.
 */
export default function ViewRoot(props: {
  viewId: string;
  active: boolean;
  children: JSX.Element;
}): JSX.Element {
  const layoutStore = getViewLayoutStore(props.viewId);

  return (
    <LayoutContext.Provider value={layoutStore}>
      <div
        class="view-root"
        classList={{ active: props.active, inactive: !props.active }}
        data-view-id={props.viewId}
      >
        {props.children}
      </div>
    </LayoutContext.Provider>
  );
}
