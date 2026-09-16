// [S-20] issue #60: the `temperature` metric must render *something* once stats
// load. Previously the chip was gated on `cpu_temp_c != null`, so on machines
// with no LibreHardwareMonitor/OpenHardwareMonitor and no ACPI thermal zone
// (common on consumer AMD boards) the row vanished with no explanation and the
// enabled metric looked broken — the same failure mode as the fan metric (#40).
// When enabled we now always emit a cell: the °C reading when a sensor exposes
// one, otherwise an "N/A" placeholder.
import { describe, it, expect } from 'vitest';
import { tempCell } from './tempDisplay.ts';

describe('tempCell [S-20]', () => {
  it('s60_returns_null_when_metric_off', () => {
    expect(tempCell(false, true, 55)).toBeNull();
    expect(tempCell(false, true, null)).toBeNull();
  });

  it('s60_returns_null_before_stats_load', () => {
    // Avoid an "N/A" flash on the first tick before any poll has landed.
    expect(tempCell(true, false, null)).toBeNull();
    expect(tempCell(true, false, 55)).toBeNull();
  });

  it('s60_returns_value_when_sensor_present', () => {
    expect(tempCell(true, true, 55)).toEqual({ kind: 'value', c: 55 });
    expect(tempCell(true, true, 0)).toEqual({ kind: 'value', c: 0 });
  });

  it('s60_temp_placeholder_when_no_sensor', () => {
    // The reported bug: enabled, stats loaded, but no temp source -> placeholder,
    // not hidden entirely.
    expect(tempCell(true, true, null)).toEqual({ kind: 'na' });
    expect(tempCell(true, true, undefined)).toEqual({ kind: 'na' });
  });

  it('s60_treats_non_finite_reading_as_na', () => {
    expect(tempCell(true, true, Number.NaN)).toEqual({ kind: 'na' });
    expect(tempCell(true, true, Infinity)).toEqual({ kind: 'na' });
  });
});
