// Multi-drive selection + layout for the Disk Monitor (#67).
//
// The widget moved from a single `mount?: string` to `mounts?: string[]`.
// `resolveMounts` is the load-time migration/validation boundary: it prefers an
// explicit `mounts[]` (even an empty one — the user cleared the selection) and
// only falls back to migrating a legacy `mount` string when `mounts` is absent.
// This is the "distinguish absent from empty/corrupt" rule from CLAUDE.md.

import type { DiskIoInfo, DiskIoStats } from './diskIo.ts';

export interface DiskMountConfig {
  mount?: string;
  mounts?: string[];
}

export type DiskLayout = 'single' | 'multi';

/**
 * Resolve the selected mounts from a (possibly legacy) config. [S-23]
 *  - `mounts` present (array) → use it, dropping any non-string entries.
 *  - else legacy non-empty `mount` string → `[mount]`.
 *  - else → `[]` (no selection; the widget shows the primary/active disk).
 */
export function resolveMounts(cfg: DiskMountConfig | null | undefined): string[] {
  const m = cfg?.mounts;
  if (Array.isArray(m)) return m.filter((x): x is string => typeof x === 'string');
  const legacy = cfg?.mount;
  if (typeof legacy === 'string' && legacy.length > 0) return [legacy];
  return [];
}

/**
 * Render layout from the number of selected drives. [S-24]
 * 0 or 1 → `single` (one drive stretched to fill, exactly as before #67);
 * 2+     → `multi` (each drive stacked, container overflow-scrolls).
 */
export function diskLayout(n: number): DiskLayout {
  return n > 1 ? 'multi' : 'single';
}

/** Resolve selected mounts to their backend disks, in selection order, dropping unmatched. */
export function selectedDisks(stats: DiskIoStats | null, mounts: string[]): DiskIoInfo[] {
  const disks = stats?.disks;
  if (!disks?.length) return [];
  const out: DiskIoInfo[] = [];
  for (const m of mounts) {
    const d = disks.find(x => x.mount === m);
    if (d) out.push(d);
  }
  return out;
}
