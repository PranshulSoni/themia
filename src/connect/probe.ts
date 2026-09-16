// ---------------------------------------------------------------------------
// Preset health — [S-47]
//
// A preset carries a provider's published server settings, and providers change
// them. Rather than trust the catalog, selecting a preset contacts its endpoint
// from the user's own machine (`email_probe_endpoint`, anonymous — see [S-46])
// and reports back, so an out-of-date preset is badged at the moment it would
// otherwise waste someone's afternoon.
//
// Advisory, never blocking: a probe cannot see credentials, and a locked-down
// network can fail one while a real sign-in succeeds. The badge informs; the
// Connect button stays enabled and `add_*_account` remains the real test.
// ---------------------------------------------------------------------------

import { invoke, type ProbeOutcome, type ProbeResult } from '../ipc.ts';
import type { ConnectPreset } from './presets.ts';

export type PresetHealth =
  /** Nothing to check (self-hosted preset), or not checked yet. */
  | { status: 'unknown' }
  | { status: 'checking' }
  | { status: 'ok'; ms: number }
  /** The endpoint didn't answer as expected — the preset may be out of date. */
  | { status: 'stale'; outcome: Exclude<ProbeOutcome, 'reachable'>; detail: string };

/** Probes are cached for the window's lifetime: the Connect window is a
 *  short-lived wizard, so re-selecting a preset must not re-dial. No TTL — a
 *  fresh window is a fresh check, which keeps this free of wall-clock timing. */
const cache = new Map<string, PresetHealth>();

/** Test seam + window teardown. */
export function clearProbeCache(): void {
  cache.clear();
}

export function cachedHealth(preset: ConnectPreset): PresetHealth {
  return cache.get(preset.id) ?? { status: 'unknown' };
}

/** Map a raw probe result to the health the badge renders. Pure. */
export function toHealth(result: ProbeResult): PresetHealth {
  if (result.outcome === 'reachable') return { status: 'ok', ms: result.elapsed_ms };
  return { status: 'stale', outcome: result.outcome, detail: result.detail };
}

/**
 * Check a preset's endpoint, memoized by preset id. Self-hosted presets (probe
 * `none`) resolve to `unknown` without touching the network — their host is
 * whatever the user types, so there is nothing generic to verify.
 *
 * Never rejects: a failed probe is an answer to render, not an error to catch
 * at every call site.
 */
export async function probePreset(preset: ConnectPreset): Promise<PresetHealth> {
  if (preset.probe.kind === 'none') return { status: 'unknown' };

  const cached = cache.get(preset.id);
  if (cached && cached.status !== 'checking') return cached;

  cache.set(preset.id, { status: 'checking' });
  try {
    const result = await invoke('email_probe_endpoint', { target: preset.probe });
    const health = toHealth(result);
    cache.set(preset.id, health);
    return health;
  } catch (e) {
    // The command itself failed (blocked host, IPC error). That is still a
    // "couldn't reach it" for the user, and the detail explains why.
    const health: PresetHealth = { status: 'stale', outcome: 'unreachable', detail: String(e) };
    cache.set(preset.id, health);
    return health;
  }
}

/** Where the preset points, for the badge's tooltip. Pure. */
export function probeTargetLabel(preset: ConnectPreset): string {
  const p = preset.probe;
  if (p.kind === 'imap') return `${p.host}:${p.port}`;
  if (p.kind === 'dav') return p.url;
  return '';
}

/**
 * One line of user-facing copy for a health state. Pure, so the wording is
 * tested without a socket. Deliberately blames the settings, not the user:
 * at this point they have typed nothing that could be wrong.
 */
export function healthMessage(preset: ConnectPreset, health: PresetHealth): string {
  const where = probeTargetLabel(preset);
  switch (health.status) {
    case 'unknown':
      return '';
    case 'checking':
      return `Checking ${where}…`;
    case 'ok':
      return `${where} answered — these settings are current.`;
    case 'stale':
      switch (health.outcome) {
        case 'server_error':
          return `${where} is having trouble right now. You can still connect, or try again in a moment.`;
        case 'not_dav':
          return `${where} answered, but not as expected — ${preset.label}’s settings may have changed. Check the fields below before connecting.`;
        case 'unreachable':
        default:
          return `Couldn’t reach ${where}. ${preset.label}’s settings may be out of date, or your network is blocking the connection.`;
      }
  }
}
