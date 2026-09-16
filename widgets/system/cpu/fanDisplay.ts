// Fan-metric display decision, extracted so it is unit-testable ([S-23]).
//
// The `fan` metric has no driverless data source: CPU fan RPM is only available
// via LibreHardwareMonitor/OpenHardwareMonitor over WMI. When that isn't running
// the backend returns `cpu_fan_rpm: null`. Rather than hide the enabled metric
// (issue #40: it read as broken), we render an explicit "N/A" placeholder so the
// user can see the metric is on but no sensor is exposed.

export type FanCell = { kind: 'value'; rpm: number } | { kind: 'na' };

/**
 * Decide what the fan chip should show.
 * - `null`  → render nothing (metric off, or stats not loaded yet; avoids an
 *   "N/A" flash on the first tick before any poll lands).
 * - `{ kind: 'value' }` → a real, plausible RPM reading.
 * - `{ kind: 'na' }` → metric enabled but no sensor value available.
 */
export function fanCell(
  shown: boolean,
  statsLoaded: boolean,
  rpm: number | null | undefined,
): FanCell | null {
  if (!shown || !statsLoaded) return null;
  if (rpm != null && Number.isFinite(rpm) && rpm > 0) return { kind: 'value', rpm };
  return { kind: 'na' };
}
