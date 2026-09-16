# Weather: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header shows label ("Weather" by default, or `label` override) and location name with a pin (lucide MapPin) icon when set.
- [S-02] Shows empty state "Select location to see your weather" when lat/lon missing.
- [S-03] Shows "Loading..." on first fetch with no cached data.
- [S-04] Shows "Failed to load weather." on fetch failure.
- [S-05] Main block shows WMO weather icon (lucide line-icon, e.g. Sun / CloudSun / CloudRain) and rounded current temperature with unit suffix.
- [S-06] Main block shows weather description derived from WMO code (e.g., "Clear sky", "Partly cloudy"); unknown codes fall back to "Code <n>".
- [S-07] Details row shows Feels-like temperature, humidity percentage, and wind speed in km/h, each with a lucide icon (Thermometer / Droplets / Wind). Details render flat (no boxed background), matching peer widgets.

## Configuration
- [S-08] `lat`/`lon`: coordinates required to fetch; cleared together via "Change" button in config.
- [S-09] `locationName`: free-form display string stored from geocoding pick.
- [S-10] `unit`: 'celsius' or 'fahrenheit' (default celsius); controls both API query param and displayed unit symbol.
- [S-11] `refreshMinutes`: 5 / 15 / 30 / 60 (default 15).
- [S-12] `headerMode` (`'normal' | 'compact' | 'none'`, default `'compact'`): selects the shared header presentation; see `src/components/WidgetHeader.SPEC.md`. Legacy `hideHeader: true` still resolves to `'none'`.
- [S-13] Config Source tab offers city search with 400ms debounce (min 2 chars); picks up to 6 geocoding results; Enter triggers immediate search. There is no search button: the input is the only trigger. Rendered by the shared `src/components/LocationSearch.tsx`, which Weather Forecast uses too ([S-19] there); the two widgets held a line-for-line copy each.
- [S-14] Picking a result sets lat/lon and populates locationName as "name, admin1, country".
- [S-25] While a search is in flight the Source tab shows a "Searching…" hint. A search that returns nothing shows "No locations found." as an empty hint; a search that FAILS (network/HTTP) shows "Search failed." in the destructive colour, and logs. The two states are told apart: absent is not an error.
- [S-26] A geocoding result is rendered only if it carries a name and finite coordinates; malformed entries are dropped rather than shown.
- [S-27] The search feedback (Searching…/No-match/Failed hint and the results) renders as a floating overlay anchored to the input — a `.dropdown-panel` in the shared popup layer, `position: fixed`, `--z-dropdown` — not inline in the config's scroll flow, so it covers the sections below (Display/Refresh) instead of pushing them down. Dismissed by picking a result, clearing the query, Escape, or a click outside the input and panel. Same overlay path the app's dropdowns use.

## Context menu
- [S-15] "Refresh" action triggers immediate reload via `_refresh` bump.

## Data / backend
- [S-16] Fetches `https://api.open-meteo.com/v1/forecast?latitude=&longitude=&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code&wind_speed_unit=kmh&temperature_unit={unit}`.
- [S-17] Config geocoding uses `https://geocoding-api.open-meteo.com/v1/search?name=&count=6&language=en&format=json`.
- [S-18] Polls every `refreshMinutes` minutes via `createPolling`.
- [S-19] Demo mode returns `demoWeatherCurrent()` fixture; bypasses lat/lon requirement.

## Caching
- [S-20] Cached weather data persists across app restarts (localStorage); on cold launch, last-known temperature renders instantly without "Loading..." flash.
- [S-21] Multiple Weather widgets configured with the same lat/lon/unit share a single cache entry: only one network request fires per refresh.
- [S-22] On fetch failure when cached data is present, last-known data remains visible (error state suppressed).

## Configuration (continued)
- [S-23] Config Source tab hides the search input, error, and results when a city is selected; only the selected-city row with Change button is shown. Clicking Change clears lat/lon/locationName and restores the search view. "Selected" means both coordinates are finite numbers, so a location on the equator or the prime meridian (lat/lon 0) counts.
- [S-24] `hideLocation` (default false): when true, the pin + location name is hidden in the header while the label remains; toggled via "Location name" switch (Shown/Hidden) in the config Display tab. Independent of `hideHeader`.
