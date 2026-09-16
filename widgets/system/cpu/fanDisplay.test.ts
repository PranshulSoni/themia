import { describe, it, expect } from 'vitest';
import { fanCell } from './fanDisplay.ts';

// issue #40 / [S-23]: the `fan` metric must render *something* once stats load.
// Previously the fan chip was gated on `cpu_fan_rpm != null`, so on machines
// without LibreHardwareMonitor/OpenHardwareMonitor running (i.e. most machines,
// since there is no driverless fan source) the row vanished with no explanation and
// the metric looked broken. When enabled we now always emit a cell: the RPM when
// a sensor exposes one, otherwise an "N/A" placeholder.
describe('fanCell [S-23]', () => {
  it('issue_40_returns_null_when_metric_off', () => {
    expect(fanCell(false, true, 1200)).toBeNull();
    expect(fanCell(false, true, null)).toBeNull();
  });

  it('issue_40_returns_null_before_stats_load', () => {
    // Avoid an "N/A" flash on the first tick before any poll has landed.
    expect(fanCell(true, false, null)).toBeNull();
    expect(fanCell(true, false, 1200)).toBeNull();
  });

  it('issue_40_returns_value_when_sensor_present', () => {
    expect(fanCell(true, true, 1200)).toEqual({ kind: 'value', rpm: 1200 });
    expect(fanCell(true, true, 1)).toEqual({ kind: 'value', rpm: 1 });
  });

  it('issue_40_returns_na_when_no_sensor', () => {
    // The reported bug: enabled, stats loaded, but no fan source -> placeholder.
    expect(fanCell(true, true, null)).toEqual({ kind: 'na' });
    expect(fanCell(true, true, undefined)).toEqual({ kind: 'na' });
  });

  it('issue_40_treats_zero_or_bogus_rpm_as_na', () => {
    // Backend already filters <1 RPM, but guard defensively at the boundary.
    expect(fanCell(true, true, 0)).toEqual({ kind: 'na' });
    expect(fanCell(true, true, -5)).toEqual({ kind: 'na' });
    expect(fanCell(true, true, Number.NaN)).toEqual({ kind: 'na' });
  });
});
