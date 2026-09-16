// CPU temperature display decision, extracted so it is unit-testable ([S-20]).
//
// The `temperature` metric has no guaranteed data source: CPU temp is read over
// WMI from LibreHardwareMonitor/OpenHardwareMonitor, or from the ACPI thermal
// zone — both commonly absent on consumer AMD boards. When neither yields a
// value the backend returns `cpu_temp_c: null`. Rather than hide the enabled
// metric (issue #60: it read as broken, the same failure mode the fan metric
// hit in #40), we render an explicit "N/A" placeholder so the user can see the
// metric is on but no sensor is exposed. Mirrors `fanDisplay.ts`.

export type TempCell = { kind: 'value'; c: number } | { kind: 'na' };

/**
 * Decide what the temperature chip should show.
 * - `null`  → render nothing (metric off, or stats not loaded yet; avoids an
 *   "N/A" flash on the first tick before any poll lands).
 * - `{ kind: 'value' }` → a real, finite temperature reading (the backend
 *   already rejects out-of-range readings, so any finite value is trusted).
 * - `{ kind: 'na' }` → metric enabled but no sensor value available (null /
 *   non-finite).
 */
export function tempCell(
  shown: boolean,
  statsLoaded: boolean,
  c: number | null | undefined,
): TempCell | null {
  if (!shown || !statsLoaded) return null;
  if (c != null && Number.isFinite(c)) return { kind: 'value', c };
  return { kind: 'na' };
}
