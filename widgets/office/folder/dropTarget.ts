// Pure drop-target path logic for the folder widget. [S-54]
//
// Kept side-effect-free and reactivity-free so the same-folder no-op guard can
// be unit-tested without a DOM. The live drop handler in FolderView.tsx routes
// every native file drop through `pastableSources` before invoking `fs_paste`.

/**
 * Normalize a Windows path for equality comparison: forward slashes → back,
 * trailing separators stripped, lowercased (Windows paths are case-insensitive
 * and separator-agnostic). A configured root like `D:/Docs/` and the derived
 * `D:\Docs` must compare equal; otherwise a same-folder move is mistaken for a
 * real one and forwarded to the shell.
 */
export function normWinPath(p: string): string {
  return p.replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
}

/** Parent directory of a file/folder path, in normalized form. */
export function parentDir(p: string): string {
  return normWinPath(p).replace(/\\[^\\]+$/, '');
}

/** True when two paths refer to the same filesystem location. */
export function samePath(a: string, b: string): boolean {
  return normWinPath(a) === normWinPath(b);
}

/**
 * Filter `srcPaths` down to the ones that should actually be pasted into
 * `destDir`. Drops two no-op cases that, forwarded to the shell move engine,
 * can destroy the dragged items:
 *   1. a source already living in `destDir` (same-folder drop), and
 *   2. a folder dropped onto itself.
 */
export function pastableSources(srcPaths: string[], destDir: string): string[] {
  return srcPaths.filter(
    (src) => !samePath(parentDir(src), destDir) && !samePath(src, destDir),
  );
}
