// The refresh-interval default for the system widgets, in ONE place.
//
// It used to live in three per widget — the registry `defaultConfig`, the
// widget's `props.interval ?? N` fallback, and the config panel's
// `props.config.interval ?? N` — with nothing keeping them in step. They
// drifted: battery/bluetooth showed "2 s" in the panel while polling every 10 s,
// and disk polled 2 s against its own SPEC [S-13] default of 5 s, but only for
// configs persisted before `interval` existed (a fresh disk widget stored 5 and
// behaved correctly, which is why it survived review).
//
// Both consumers and the registry now read from here, so a panel cannot
// disagree with the widget it configures. `refreshInterval.test.ts` scans the
// group for a bare `interval ?? <literal>` and fails if a fourth copy appears.

/** System widgets that poll on a user-configurable interval. Closed set, not a
 *  loose string (CLAUDE.md: closed sets are unions, not strings). */
export type SystemWidgetId =
  | 'battery' | 'bluetooth' | 'cpu' | 'disk' | 'gpu' | 'mixer' | 'net' | 'ping' | 'ram';

/** Seconds between polls when the stored config carries no interval. Each
 *  number is fixed by that widget's SPEC; change the SPEC and this together. */
export const REFRESH_DEFAULTS: Record<SystemWidgetId, number> = {
  battery: 10,   // battery/SPEC.md   [S-10]
  bluetooth: 10, // bluetooth/SPEC.md [S-08]
  cpu: 2,        // cpu/SPEC.md       [S-11]
  disk: 5,       // disk/SPEC.md      [S-13]
  gpu: 2,        // gpu/SPEC.md       [S-14]
  mixer: 2,      // mixer/SPEC.md     [S-07]
  net: 2,        // net/SPEC.md       [S-09]
  ping: 30,      // ping/SPEC.md      [S-16]
  ram: 2,        // ram/SPEC.md       [S-09]
};

/**
 * Resolve the effective poll interval (seconds) from a persisted config value.
 *
 * Absent and corrupt both yield the widget's default, so a junk value from the
 * JSON trust boundary can never reach `max(1, interval)` as NaN and spin the
 * poller. A stored number passes through UNCHANGED and is deliberately not
 * clamped to the config stepper's 1..60 range: an imported or hand-edited 300s
 * interval stays 300s rather than being silently sped up 5x.
 */
export function refreshInterval(raw: unknown, widget: SystemWidgetId): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return REFRESH_DEFAULTS[widget];
  return raw;
}
