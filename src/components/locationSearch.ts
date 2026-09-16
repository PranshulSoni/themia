// The pure half of LocationSearch: URL building, response parsing, label
// formatting. Kept out of the .tsx so it is unit-testable — vitest runs without
// the Solid JSX transform, so a module carrying JSX can only ever be scanned as
// text (cf. src/connect/visibility.test.ts).
//
// Contract: weather-current SPEC [S-13][S-14][S-17], weather-forecast SPEC
// [S-19][S-23].

/** Minimum query length before a search fires. */
export const GEOCODE_MIN_CHARS = 2;
/** Idle time after the last keystroke before the query goes out. */
export const GEOCODE_DEBOUNCE_MS = 400;
/** How many matches to ask for, and the hard cap on what we render. */
export const GEOCODE_RESULT_COUNT = 6;

/** One geocoding match, after validation. `admin1` is the region/state. */
export interface GeoResult {
  id: number;
  name: string;
  admin1?: string;
  country?: string;
  latitude: number;
  longitude: number;
}

export function geocodeUrl(query: string): string {
  return 'https://geocoding-api.open-meteo.com/v1/search'
    + `?name=${encodeURIComponent(query)}`
    + `&count=${GEOCODE_RESULT_COUNT}&language=en&format=json`;
}

function optionalString(v: unknown): string | undefined {
  return typeof v === 'string' && v.length > 0 ? v : undefined;
}

/** Validate the geocoding response at the trust boundary.
 *
 *  A missing `results` key is how open-meteo reports "no match", so it reads as
 *  an empty list — absent, not corrupt. An entry that survives is renderable:
 *  the previous code took `json.results` on trust, so an entry without a name
 *  or with a string latitude would have rendered `undefined` in the list and
 *  saved a coordinate the widget could never fetch with. */
export function parseGeoResults(json: unknown): GeoResult[] {
  if (!json || typeof json !== 'object') return [];
  const raw = (json as { results?: unknown }).results;
  if (!Array.isArray(raw)) return [];

  const out: GeoResult[] = [];
  for (const entry of raw) {
    if (!entry || typeof entry !== 'object') continue;
    const e = entry as Record<string, unknown>;
    const { name, latitude, longitude } = e;
    if (typeof name !== 'string' || name.length === 0) continue;
    if (typeof latitude !== 'number' || !Number.isFinite(latitude)) continue;
    if (typeof longitude !== 'number' || !Number.isFinite(longitude)) continue;

    const result: GeoResult = {
      id: typeof e.id === 'number' ? e.id : out.length,
      name,
      latitude,
      longitude,
    };
    const admin1 = optionalString(e.admin1);
    const country = optionalString(e.country);
    if (admin1) result.admin1 = admin1;
    if (country) result.country = country;
    out.push(result);
    if (out.length === GEOCODE_RESULT_COUNT) break;
  }
  return out;
}

/** Whether a config carries usable coordinates.
 *
 *  Both weather configs tested this as `lat() && lon()`, which reads 0 as "no
 *  location": a widget saved at the equator or on the prime meridian (London
 *  sits at lon -0.1276, but Greenwich itself is 0) reopened its config on the
 *  search view with its saved city gone. */
export function hasLocation(lat: number | null | undefined, lon: number | null | undefined): boolean {
  return typeof lat === 'number' && Number.isFinite(lat)
    && typeof lon === 'number' && Number.isFinite(lon);
}

/** The stored display name for a picked location: "city, region, country". */
export function formatLocationName(r: GeoResult): string {
  return [r.name, r.admin1, r.country].filter(Boolean).join(', ');
}

/** The region line under a result's name in the picker. */
export function formatResultSub(r: GeoResult): string {
  return [r.admin1, r.country].filter(Boolean).join(', ');
}

/** Fallback label for a saved location with no name. */
export function formatCoords(lat: number, lon: number): string {
  return `${lat.toFixed(3)}, ${lon.toFixed(3)}`;
}
