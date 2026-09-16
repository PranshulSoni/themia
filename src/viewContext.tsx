import { createContext, useContext } from 'solid-js';
import type { ViewLayoutStore } from './layout.ts';
import { activeLayoutStore } from './layout.ts';

export const LayoutContext = createContext<ViewLayoutStore | null>(null);

/**
 * Returns the layout store for the current ViewRoot. Falls back to the
 * active view's store when called outside any ViewRoot, useful for
 * shared chrome (modals, app shell) that always wants the active view.
 */
export function useLayout(): ViewLayoutStore {
  return useContext(LayoutContext) ?? activeLayoutStore();
}
