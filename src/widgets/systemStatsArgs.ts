// Args for `get_system_stats` derived from a widget's visible metrics.
// The process-table refresh (rayon fan-out over every PID) and the WMI
// temperature sampler are the expensive parts of a backend tick; the backend
// only runs them when a caller asks (cpu [S-21], ram [S-16]).

export interface SystemStatsArgs {
  includeProcs: boolean;
  includeTemp: boolean;
}

/**
 * CPU widget: procs feed `topProcs` + `processes`; the WMI sensor sampler feeds
 * both `temperature` and `fan` (one worker thread reads temp + fan RPM
 * together), so either metric arms `includeTemp`.
 */
export function cpuStatsArgs(visible: string[]): SystemStatsArgs {
  return {
    includeProcs: visible.includes('topProcs') || visible.includes('processes'),
    includeTemp: visible.includes('temperature') || visible.includes('fan'),
  };
}

/** RAM widget: procs feed `topProcs`; RAM has no temperature metric. */
export function ramStatsArgs(visible: string[]): SystemStatsArgs {
  return {
    includeProcs: visible.includes('topProcs'),
    includeTemp: false,
  };
}
