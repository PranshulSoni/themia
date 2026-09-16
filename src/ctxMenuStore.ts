import { createStore } from 'solid-js/store';
import type { Component, JSX } from 'solid-js';
import { globalDesign } from './design.ts';

export type CtxMenuIconProps = { size?: number; strokeWidth?: number };

export interface CtxMenuItem {
  icon?: Component<CtxMenuIconProps> | JSX.Element;
  label?: string;
  action?: () => void;
  sep?: boolean;
  header?: boolean;
  active?: boolean;
  danger?: boolean;
  disabled?: boolean;
  shortcut?: string;
  keepOpen?: boolean;
  children?: CtxMenuItem[];
}

interface CtxMenuState {
  menu: { x: number; y: number; items: CtxMenuItem[] } | null;
}

export const [ctxMenu, setCtxMenu] = createStore<CtxMenuState>({ menu: null });

export function showCtxMenu(e: MouseEvent, items: CtxMenuItem[], menuWidth = 200): void {
  e.preventDefault();
  e.stopPropagation();
  const s = globalDesign.uiScale || 1;
  const estimatedHeight = items.reduce((h, i) => h + (i.sep ? 5 : i.header ? 22 : 26), 8);
  const x = Math.min(e.clientX / s, window.innerWidth / s  - menuWidth - 8);
  const y = Math.min(e.clientY / s, window.innerHeight / s - estimatedHeight - 8);
  setCtxMenu({ menu: { x, y, items } });
  // NOTE: no `push_windows_to_desktop` here. The menu renders in-window via the
  // popup-layer portal and its full-window backdrop expands the SetWindowRgn
  // hit-mask, so a widget-window z-order shove is unnecessary. The call was a
  // vestige of the pre-hit-mask era; worse, it issued a SetWindowPos against
  // the desktop band on every menu open, which, when a desktop context menu
  // was still modal, helped saturate the message pump and freeze the region.
}

export function closeCtxMenu(): void {
  setCtxMenu({ menu: null });
}

export function updateCtxMenuItems(items: CtxMenuItem[]): void {
  if (ctxMenu.menu) setCtxMenu('menu', 'items', items);
}
