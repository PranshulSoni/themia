import { isShellPath, shellLabel } from './shellPaths.ts';
import { LAYOUT_PATH, type LayoutNode } from './layout.ts';

export type ShortcutKind = 'url' | 'file' | 'shell' | 'layout';

export interface ShortcutItem {
  id: string;
  path: string;
  label?: string;
  /** Optional icon source path (e.g. the `IconFile` of a dropped `.url`
   *  shortcut) used to render a real app/game icon for custom-protocol URLs
   *  that have no favicon. Resolved via `get_file_icon`. */
  iconPath?: string;
  /** App-layout tree, present only for `layout`-kind items. */
  layout?: LayoutNode;
}

export function itemKind(path: string | undefined | null): ShortcutKind {
  const p = path ?? '';
  if (p.startsWith('http://') || p.startsWith('https://')) return 'url';
  if (p === LAYOUT_PATH || p.startsWith('layout:')) return 'layout';
  if (isShellPath(p)) return 'shell';
  // Custom URI protocols (steam://, com.epicgames.launcher://, file://, …) open
  // via ShellExecute like a URL, so they are never a local file. Match a scheme
  // followed by '://'; Windows paths (C:\, \\server) never contain '://'.
  if (/^[a-z][a-z0-9+.-]*:\/\//i.test(p)) return 'url';
  return 'file';
}

export function newId(): string {
  const c: Crypto | null = typeof crypto !== 'undefined' ? crypto : null;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  return `s-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

export function displayName(item: ShortcutItem): string {
  if (item.label) return item.label;
  const p = item.path ?? '';
  if (!p) return '';
  const k = itemKind(p);
  if (k === 'layout') return 'App layout';
  if (k === 'url') {
    try { return new URL(p).hostname.replace(/^www\./, ''); } catch { return p; }
  }
  if (k === 'shell') {
    return shellLabel(p) ?? p.slice('shell:'.length);
  }
  const base = p.replace(/\\/g, '/').split('/').filter(Boolean).pop() ?? p;
  const dot = base.lastIndexOf('.');
  return dot > 0 ? base.slice(0, dot) : base;
}

export function normalizeUrl(raw: string): string {
  const t = raw.trim();
  if (!t) return '';
  return /^https?:\/\//i.test(t) ? t : 'https://' + t;
}

/** Best-effort parse of a Windows `.url` InternetShortcut file body. */
export function parseUrlFileBody(body: string): string | null {
  const m = body.match(/^\s*URL\s*=\s*(.+?)\s*$/mi);
  return m ? m[1].trim() : null;
}

/** Best-effort parse of a `.url` file's `IconFile=` line (the icon source a
 *  shortcut points at, e.g. a Steam game's `.ico`). Returns null if absent. */
export function parseUrlFileIcon(body: string): string | null {
  const m = body.match(/^\s*IconFile\s*=\s*(.+?)\s*$/mi);
  const v = m ? m[1].trim() : '';
  return v ? v : null;
}

/** Filename stem (basename without extension) of a path, used to label a
 *  dropped `.url` shortcut with its file name (e.g. "Unrailed 2.url" → the
 *  shortcut's display name), since a custom-protocol URL has no good auto-name. */
export function fileStem(path: string): string {
  const base = (path ?? '').replace(/\\/g, '/').split('/').filter(Boolean).pop() ?? '';
  const dot = base.lastIndexOf('.');
  return dot > 0 ? base.slice(0, dot) : base;
}
