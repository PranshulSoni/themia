// App-layout model + tree helpers for the Shortcut widget's `layout` item kind.
//
// A layout item carries an app-layout tree (`item.layout`). The tree is either
// a single leaf or a binary split. Walking the tree with the screen work-area
// as the root rectangle yields one launch entry per *assigned* leaf, which the
// `launch_app_layout` Tauri command snaps onto the display the click happened
// on. (Folded in from the former standalone App Layouts widget.)

// Assignment kinds:
//   'lnk':      a Start Menu app (.lnk) launched via ShellExecute
//   'url':      a web URL opened in the default browser
//   'path':     a custom file/path launched via ShellExecute (+ optional args)
//   'explorer': a folder opened in File Explorer at that path
export interface Assignment {
  kind: 'lnk' | 'url' | 'path' | 'explorer';
  target: string;
  label?: string;
  args?: string;
  matchHint?: string;
  reuseIfRunning?: boolean;
}

export interface LeafNode {
  kind: 'leaf';
  id: string;
  assignment?: Assignment;
}

export interface SplitNode {
  kind: 'split';
  id: string;
  /** 'vertical' = left/right children; 'horizontal' = top/bottom. */
  direction: 'vertical' | 'horizontal';
  /** 0..1, share allocated to child `a`. */
  ratio: number;
  a: LayoutNode;
  b: LayoutNode;
}

export type LayoutNode = LeafNode | SplitNode;

// Rust-side launch entry shape (mirror of LayoutEntry in src-tauri/src/lib.rs).
// The target monitor is derived by the backend from the window that triggered
// the launch (layouts always apply to the display the user clicked on), so
// no monitor field travels with each entry.
export interface LaunchEntry {
  kind: 'lnk' | 'url' | 'path' | 'explorer';
  target: string;
  label?: string;
  args?: string;
  xPct: number;
  yPct: number;
  wPct: number;
  hPct: number;
  reuseIfRunning: boolean;
  matchHint?: string;
}

export const MIN_RATIO = 0.05;
export const MAX_RATIO = 0.95;

/** Sentinel `path` value marking a layout item. */
export const LAYOUT_PATH = 'layout:';

export function uid(): string {
  const c: Crypto | null = typeof crypto !== 'undefined' ? crypto : null;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  return `n-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

export function newLeaf(): LeafNode {
  return { kind: 'leaf', id: uid() };
}

// ── Tree walk / launch ───────────────────────────────────────────────────────

export function walkLeaves(
  node: LayoutNode | undefined | null,
  x: number, y: number, w: number, h: number,
  out: (leaf: LeafNode, rect: { x: number; y: number; w: number; h: number }) => void,
): void {
  if (!node || typeof node !== 'object') return;
  if (node.kind === 'leaf') { out(node, { x, y, w, h }); return; }
  if (node.kind !== 'split') return;
  const r = Math.max(0, Math.min(1, node.ratio));
  if (node.direction === 'vertical') {
    walkLeaves(node.a, x, y, w * r, h, out);
    walkLeaves(node.b, x + w * r, y, w * (1 - r), h, out);
  } else {
    walkLeaves(node.a, x, y, w, h * r, out);
    walkLeaves(node.b, x, y + h * r, w, h * (1 - r), out);
  }
}

/** Walk a layout tree into one launch entry per assigned leaf. */
export function rootToEntries(root: LayoutNode | undefined | null): LaunchEntry[] {
  const entries: LaunchEntry[] = [];
  walkLeaves(root, 0, 0, 100, 100, (leaf, rect) => {
    const a = leaf.assignment;
    if (!a || !a.target) return;
    // 'explorer' (open a folder in File Explorer) flows through as its own kind:
    // the backend matches the Explorer window by class (CabinetWClass) and snaps
    // it onto the pane. Its reuse/matchHint/args fields are irrelevant there.
    const isExplorer = a.kind === 'explorer';
    entries.push({
      kind: a.kind,
      target: a.target,
      label: a.label,
      args: isExplorer ? undefined : a.args,
      xPct: rect.x,
      yPct: rect.y,
      wPct: rect.w,
      hPct: rect.h,
      reuseIfRunning: isExplorer ? false : (a.reuseIfRunning ?? false),
      matchHint: isExplorer ? undefined : a.matchHint,
    });
  });
  return entries;
}

/** Collect the assignments of every assigned leaf, in tree (left-to-right /
 *  top-to-bottom) order. Used to preview the apps a layout item launches. */
export function collectAssignments(node: LayoutNode | undefined | null): Assignment[] {
  const out: Assignment[] = [];
  walkLeaves(node ?? null, 0, 0, 100, 100, (leaf) => {
    if (leaf.assignment && leaf.assignment.target) out.push(leaf.assignment);
  });
  return out;
}

export function countAssignedLeaves(node: LayoutNode | undefined | null): number {
  if (!node || typeof node !== 'object') return 0;
  if (node.kind === 'leaf') return node.assignment ? 1 : 0;
  if (node.kind !== 'split') return 0;
  return countAssignedLeaves(node.a) + countAssignedLeaves(node.b);
}

// ── Tree editing ops ───────────────────────────────────────────────────────

export function updateNode(root: LayoutNode, id: string, fn: (n: LayoutNode) => LayoutNode): LayoutNode {
  if (root.id === id) return fn(root);
  if (root.kind === 'split') {
    return { ...root, a: updateNode(root.a, id, fn), b: updateNode(root.b, id, fn) };
  }
  return root;
}

export function splitLeaf(root: LayoutNode, leafId: string, direction: 'vertical' | 'horizontal'): LayoutNode {
  return updateNode(root, leafId, (n) => {
    if (n.kind !== 'leaf') return n;
    const split: SplitNode = { kind: 'split', id: uid(), direction, ratio: 0.5, a: n, b: newLeaf() };
    return split;
  });
}

export function removeLeafFromTree(root: LayoutNode, leafId: string): LayoutNode {
  function rec(node: LayoutNode): LayoutNode | null {
    if (node.kind === 'leaf') return node.id === leafId ? null : node;
    const a = rec(node.a);
    const b = rec(node.b);
    if (!a && !b) return null;
    if (!a) return b!;
    if (!b) return a!;
    return { ...node, a, b };
  }
  return rec(root) ?? newLeaf();
}

export function findLeaf(node: LayoutNode, id: string): LeafNode | null {
  if (node.kind === 'leaf') return node.id === id ? node : null;
  return findLeaf(node.a, id) ?? findLeaf(node.b, id);
}

export function isRootSplit(node: LayoutNode): boolean {
  return node.kind === 'split';
}

// ── Migration / validation ───────────────────────────────────────────────────
// Heals any tree coming from older config shapes (or partially-saved data)
// into a well-formed node: missing `id`, invalid node shapes, bad ratios.

export function validateNode(raw: unknown): LayoutNode {
  if (!raw || typeof raw !== 'object') return newLeaf();
  const node = raw as Record<string, unknown>;
  if (node.kind === 'leaf') {
    const a = node.assignment;
    const assignment: Assignment | undefined = a && typeof a === 'object' && typeof (a as Record<string, unknown>).target === 'string'
      ? (() => {
          const ar = a as Record<string, unknown>;
          const kind: Assignment['kind'] =
            ar.kind === 'url' || ar.kind === 'path' || ar.kind === 'lnk' || ar.kind === 'explorer'
              ? ar.kind
              : 'lnk';
          return {
            kind,
            target: ar.target as string,
            label: typeof ar.label === 'string' ? ar.label : undefined,
            args: typeof ar.args === 'string' ? ar.args : undefined,
            matchHint: typeof ar.matchHint === 'string' ? ar.matchHint : undefined,
            // Default true: "click layout → app appears in pane" should work for
            // already-running single-instance apps (Word, Code, Outlook…) without
            // requiring the user to toggle anything.
            reuseIfRunning: typeof ar.reuseIfRunning === 'boolean' ? ar.reuseIfRunning : true,
          };
        })()
      : undefined;
    return { kind: 'leaf', id: typeof node.id === 'string' && node.id ? node.id : uid(), assignment };
  }
  if (node.kind === 'split' && node.a && node.b) {
    return {
      kind: 'split',
      id: typeof node.id === 'string' && node.id ? node.id : uid(),
      direction: node.direction === 'horizontal' ? 'horizontal' : 'vertical',
      ratio: typeof node.ratio === 'number' ? Math.max(MIN_RATIO, Math.min(MAX_RATIO, node.ratio)) : 0.5,
      a: validateNode(node.a),
      b: validateNode(node.b),
    };
  }
  return newLeaf();
}
