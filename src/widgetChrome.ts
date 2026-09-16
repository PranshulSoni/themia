/**
 * Widget chrome context.
 *
 * Chrome affordances belong to the GRID (it owns the widget's identity and its
 * OS-window plumbing), but the best place to draw them is inside the widget's
 * own header — which the widget renders. Rather than thread a `_onPopOut` prop
 * through all ~50 widget components just so each can forward it to the shared
 * `WidgetHeader`, the grid publishes the affordance here and `WidgetHeader`
 * picks it up. No widget knows this exists.
 *
 * Absent context (pop-out window, config previews, stack/tab children, dev
 * harnesses) simply means no chrome, which is the right answer in all of them.
 */

import { createContext, useContext } from 'solid-js';

export interface WidgetChrome {
  /** Pop this widget out into its own OS window (registry `popOut` only). */
  popOut?: () => void;
}

export const WidgetChromeContext = createContext<WidgetChrome | null>(null);

export function useWidgetChrome(): WidgetChrome | null {
  return useContext(WidgetChromeContext);
}
