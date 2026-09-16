import { describe, it, expect } from 'vitest';
import { cpuStatsArgs, ramStatsArgs } from './systemStatsArgs.ts';

// cpu [S-21] / ram [S-16]: widgets request the expensive backend work
// (process-table refresh, WMI temperature sampler) only when a visible
// metric actually consumes it.

describe('cpuStatsArgs (cpu S-21)', () => {
  it('defaults: no procs, no temp for the canonical metric set', () => {
    expect(cpuStatsArgs(['usage', 'frequency', 'graph'])).toEqual({
      includeProcs: false,
      includeTemp: false,
    });
  });

  it('topProcs visible -> includeProcs', () => {
    expect(cpuStatsArgs(['usage', 'topProcs']).includeProcs).toBe(true);
  });

  it('processes (count) visible -> includeProcs', () => {
    expect(cpuStatsArgs(['usage', 'processes']).includeProcs).toBe(true);
  });

  it('temperature visible -> includeTemp only', () => {
    expect(cpuStatsArgs(['usage', 'temperature'])).toEqual({
      includeProcs: false,
      includeTemp: true,
    });
  });

  // issue #40: the CPU fan RPM is read by the same WMI sensor sampler as
  // temperature, so the `fan` metric must also arm includeTemp.
  it('fan visible -> includeTemp (shared sensor sampler)', () => {
    expect(cpuStatsArgs(['usage', 'fan'])).toEqual({
      includeProcs: false,
      includeTemp: true,
    });
  });
});

describe('ramStatsArgs (ram S-16)', () => {
  it('defaults: no procs for the canonical metric set', () => {
    expect(ramStatsArgs(['usage', 'graph', 'bar'])).toEqual({
      includeProcs: false,
      includeTemp: false,
    });
  });

  it('topProcs visible -> includeProcs, never temp', () => {
    expect(ramStatsArgs(['usage', 'topProcs'])).toEqual({
      includeProcs: true,
      includeTemp: false,
    });
  });
});
