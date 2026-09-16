import type { Component } from 'solid-js';
import {
  Trash2, Monitor, Download, FileText, LayoutGrid,
  Image as ImageIcon, Settings, Home, Network,
} from 'lucide-solid';
import { SHELL_LABELS, isShellPath } from './shellPaths.ts';

export { isShellPath };

export interface ShortcutQuickSet {
  path: string;
  label: string;
  icon: Component<{ size?: number; strokeWidth?: number }>;
}

// Icon per quick-set path; labels are single-sourced in shellPaths.ts. Key
// order = render order in the config "Quick folders" grid.
const SHELL_QUICK_SET_ICONS: Record<string, Component<{ size?: number; strokeWidth?: number }>> = {
  'shell:RecycleBinFolder':    Trash2,
  'shell:MyComputerFolder':    Monitor,
  'shell:Downloads':           Download,
  'shell:Personal':            FileText,
  'shell:Desktop':             LayoutGrid,
  'shell:My Pictures':         ImageIcon,
  'shell:UsersFilesFolder':    Home,
  'shell:ControlPanelFolder':  Settings,
  'shell:NetworkPlacesFolder': Network,
};

export const SHELL_QUICK_SETS: ShortcutQuickSet[] = Object.entries(SHELL_QUICK_SET_ICONS)
  .map(([path, icon]) => ({ path, label: SHELL_LABELS[path] ?? path, icon }));

export function findQuickSet(path: string | undefined): ShortcutQuickSet | undefined {
  if (!path) return undefined;
  return SHELL_QUICK_SETS.find(s => s.path === path);
}
