// Pure helpers for the Video Stream widget (themia-releases #102).
//
// The stream URL is USER-ENTERED and lands straight in an `<img src>`, so it is
// a trust boundary (CLAUDE.md): `resolveConfig` only lets an absolute http(s)
// URL through and collapses everything else — `javascript:`, `data:`, `file:`,
// a bare host — to '', which the widget renders as its "not configured" state.
// Nothing else in the widget re-checks the scheme, so this is the single gate.

export type StreamFit = 'contain' | 'cover' | 'stretch';
export type StreamRotation = 0 | 90 | 180 | 270;

export interface VideoStreamSettings {
  /** Absolute http(s) stream URL, or '' when unset/rejected. */
  url: string;
  /** Header title override ('' → the stream host). */
  label: string;
  /** How the picture fills the widget. */
  fit: StreamFit;
  /** Clockwise rotation for sideways/upside-down cameras. */
  rotation: StreamRotation;
  /** Stream stopped by the user (persisted, so a parked camera stays parked). */
  paused: boolean;
  /** 0 = keep the stream open at the camera's own framerate. Any other value =
   *  grab ONE frame that often and hang up in between ([S-17]). */
  refreshSeconds: number;
}

export const DEFAULT_FIT: StreamFit = 'contain';
export const DEFAULT_ROTATION: StreamRotation = 0;

/** `refreshSeconds` sentinel: hold the connection open, full framerate. */
export const LIVE_REFRESH_SECONDS = 0;
export const MAX_REFRESH_SECONDS = 300;

/** CSS `object-fit` for a fit mode. 'stretch' distorts to fill both axes, which
 *  is `fill` in CSS — the config name says what it does to the picture. */
export function objectFitFor(fit: StreamFit): 'contain' | 'cover' | 'fill' {
  return fit === 'stretch' ? 'fill' : fit;
}

/** Reconnect backoff bounds. The timer is ARMED BY the `<img>` error event and
 *  disarmed on success/pause/unmount — it is a retry-after-failure delay, never
 *  used to sequence state (CLAUDE.md: no wall-clock timing in correctness paths). */
export const RETRY_BASE_MS = 2_000;
export const RETRY_MAX_MS = 30_000;

const FITS = new Set<string>(['contain', 'cover', 'stretch']);
const ROTATIONS = new Set<number>([0, 90, 180, 270]);

/** True iff `raw` is an absolute http(s) URL the widget is allowed to load. */
export function isStreamUrl(raw: string): boolean {
  let parsed: URL;
  try {
    parsed = new URL(raw.trim());
  } catch {
    return false;
  }
  return parsed.protocol === 'http:' || parsed.protocol === 'https:';
}

/** Narrow an unknown persisted/props bag into a valid `VideoStreamSettings`.
 *  Absent and corrupt both fall back to the default; neither throws. */
export function resolveConfig(raw: unknown): VideoStreamSettings {
  const o = (raw && typeof raw === 'object') ? raw as Record<string, unknown> : {};
  const str = (v: unknown): string => (typeof v === 'string' ? v : '');
  const url = str(o.url).trim();
  const fit = str(o.fit);
  const rotation = typeof o.rotation === 'number' ? o.rotation : DEFAULT_ROTATION;
  const refreshRaw = o.refreshSeconds;
  const refresh = (typeof refreshRaw === 'number' && Number.isFinite(refreshRaw))
    ? Math.round(refreshRaw)
    : LIVE_REFRESH_SECONDS;
  return {
    url: isStreamUrl(url) ? url : '',
    label: str(o.label),
    fit: FITS.has(fit) ? fit as StreamFit : DEFAULT_FIT,
    rotation: ROTATIONS.has(rotation) ? rotation as StreamRotation : DEFAULT_ROTATION,
    paused: o.paused === true,
    refreshSeconds: Math.min(MAX_REFRESH_SECONDS, Math.max(LIVE_REFRESH_SECONDS, refresh)),
  };
}

/**
 * Append a cache-busting parameter, for interval mode ONLY ([S-17]).
 *
 * Between grabs there is no open connection, so the next grab re-requests the
 * same URL — and the webview's memory cache would happily serve the previous
 * response forever, freezing the picture. A changing parameter is the only
 * thing that reliably forces a new request from an <img>.
 *
 * Live mode never calls this: [S-09] hands the camera its URL byte-identical.
 * A non-http URL (the demo data: frame) is returned untouched — appending to it
 * would corrupt the payload.
 */
export function withCacheBuster(url: string, nonce: number): string {
  if (!isStreamUrl(url)) return url;
  const sep = url.includes('?') ? '&' : '?';
  return `${url}${sep}_t=${nonce}`;
}

/** Backoff for reconnect attempt `n` (1-based): 2s, 4s, 8s … capped at 30s. */
export function retryDelayMs(attempt: number): number {
  if (!Number.isFinite(attempt) || attempt < 1) return RETRY_BASE_MS;
  const steps = Math.min(Math.floor(attempt) - 1, 10);   // 2**10 already exceeds the cap
  return Math.min(RETRY_MAX_MS, RETRY_BASE_MS * 2 ** steps);
}

/** Header title: the explicit label, else the stream's host:port, else `fallback`. */
export function streamTitle(url: string, label: string, fallback: string): string {
  const trimmed = label.trim();
  if (trimmed) return trimmed;
  try {
    return new URL(url).host || fallback;
  } catch {
    return fallback;
  }
}

/** The next rotation in the 0 → 90 → 180 → 270 → 0 cycle. */
export function nextRotation(current: StreamRotation): StreamRotation {
  return ((current + 90) % 360) as StreamRotation;
}
