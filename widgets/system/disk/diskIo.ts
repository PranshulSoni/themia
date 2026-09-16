/**
 * Per-partition disk I/O selection (disk [S-09], [S-16]).
 *
 * The header read/write figures and the sparkline graph reflect the *selected*
 * partition, not system-wide disk activity. Sourcing them from the matched
 * disk's per-mount PDH rates (`disks[].read_per_sec` / `write_per_sec`) is what
 * stops multiple single-partition widgets from all mirroring the same number
 * (themia-releases#49).
 */

export interface DiskIoInfo {
  mount: string;
  used: number;
  total: number;
  read_per_sec?: number;
  write_per_sec?: number;
}

export interface DiskIoStats {
  disks?: DiskIoInfo[];
}

/** Resolve the monitored disk: exact mount match, else the first disk ([S-09]). */
export function activeDisk(stats: DiskIoStats | null, mount: string): DiskIoInfo | null {
  const disks = stats?.disks;
  if (!disks?.length) return null;
  return disks.find(d => d.mount === mount) ?? disks[0];
}

/**
 * Read/write bytes-per-second for the selected partition ([S-16]). Missing
 * per-mount rates report 0, never a silent fall back to the system-wide total,
 * which is exactly the value #49 was showing everywhere.
 */
export function selectedDiskIo(
  stats: DiskIoStats | null,
  mount: string,
): { read: number; write: number } {
  const d = activeDisk(stats, mount);
  return { read: d?.read_per_sec ?? 0, write: d?.write_per_sec ?? 0 };
}
