// [S-47] Preset health: the pure mapping from a probe result to what the badge
// says, plus the memoization contract. No network — the live endpoint check is
// the opt-in presets.network.test.ts suite.
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { toHealth, healthMessage, probeTargetLabel, clearProbeCache, cachedHealth, probePreset } from './probe.ts';
import { findPreset } from './presets.ts';
import type { ProbeResult } from '../ipc.ts';

const gmail = findPreset('imap-gmail')!;
const nextcloud = findPreset('caldav-nextcloud')!;
const icloudCal = findPreset('caldav-icloud')!;

const result = (r: Partial<ProbeResult>): ProbeResult =>
  ({ outcome: 'reachable', detail: '', elapsed_ms: 12, ...r });

describe('toHealth', () => {
  it('reachable becomes ok, carrying the round-trip time', () => {
    expect(toHealth(result({ outcome: 'reachable', elapsed_ms: 40 }))).toEqual({ status: 'ok', ms: 40 });
  });

  it('every non-reachable outcome becomes stale, keeping the evidence', () => {
    for (const outcome of ['unreachable', 'not_dav', 'server_error'] as const) {
      expect(toHealth(result({ outcome, detail: 'HTTP 404' }))).toEqual({
        status: 'stale', outcome, detail: 'HTTP 404',
      });
    }
  });
});

describe('probeTargetLabel', () => {
  it('shows host:port for IMAP and the URL for DAV', () => {
    expect(probeTargetLabel(gmail)).toBe('imap.gmail.com:993');
    expect(probeTargetLabel(icloudCal)).toBe('https://caldav.icloud.com');
  });
  it('is empty for a self-hosted preset', () => {
    expect(probeTargetLabel(nextcloud)).toBe('');
  });
});

describe('healthMessage', () => {
  it('says nothing when there is nothing to report', () => {
    expect(healthMessage(gmail, { status: 'unknown' })).toBe('');
  });

  it('names the endpoint being checked', () => {
    expect(healthMessage(gmail, { status: 'checking' })).toContain('imap.gmail.com:993');
  });

  it('confirms current settings on success', () => {
    expect(healthMessage(gmail, { status: 'ok', ms: 30 })).toContain('current');
  });

  it('blames the settings, not the user, when unreachable', () => {
    const msg = healthMessage(gmail, { status: 'stale', outcome: 'unreachable', detail: 'dns' });
    expect(msg).toContain('out of date');
    expect(msg).toContain('Gmail');
  });

  it('distinguishes a transient server problem from wrong settings', () => {
    const busy = healthMessage(icloudCal, { status: 'stale', outcome: 'server_error', detail: 'HTTP 503' });
    expect(busy).toContain('trouble');
    expect(busy).not.toContain('out of date');
  });

  it('flags a moved endpoint as changed settings', () => {
    const moved = healthMessage(icloudCal, { status: 'stale', outcome: 'not_dav', detail: 'HTTP 404' });
    expect(moved).toContain('may have changed');
  });
});

describe('probePreset', () => {
  beforeEach(() => { clearProbeCache(); vi.resetModules(); });

  it('never dials for a self-hosted preset — its host is whatever the user types', async () => {
    await expect(probePreset(nextcloud)).resolves.toEqual({ status: 'unknown' });
    expect(cachedHealth(nextcloud)).toEqual({ status: 'unknown' });
  });

  it('starts out unknown for an unprobed preset', () => {
    expect(cachedHealth(gmail)).toEqual({ status: 'unknown' });
  });
});
