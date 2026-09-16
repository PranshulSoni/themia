# Weather Forecast: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header shows label ("Forecast" by default, or `label` override) and location name with a pin (lucide MapPin) icon when set.
- [S-02] Shows empty state "Select location to see your forecast" when lat/lon missing.
- [S-03] Shows "Loading..." on first fetch with no cached data.
- [S-04] Shows "Failed to load forecast." on fetch failure.
- [S-05] Day labels: first day is "Today", subsequent days use three-letter weekday names (Sun/Mon/Tue/...).
- [S-06] Each day shows WMO weather icon (lucide line-icon), description, low and high temperatures with unit suffix, and a high/low temp bar whose position is scaled against the min/max across all shown days.
- [S-07] Each day shows precipitation as a lucide Droplet icon + `{amount}` when precipitation > 0 mm, hidden otherwise.
- [S-08] Rows layout: one row per day with day label, icon, description, shared temp-range bar, lo, hi, precip.
- [S-31] Rows layout grows rows to fill the body when they fit, but never below a legible min-height; when the day count outgrows the body the list scrolls vertically (scrollbar hidden per the widget-body convention) instead of squeezing rows.
- [S-09] Columns layout: vertical card per day with day label, icon, description, hi/lo stack, temp bar, precip.
- [S-10] Grid layout: card per day with header (day + icon), description, temp bar, lo/hi, optional precip with "mm" suffix.
- [S-11] Today's card/row gets a `today` modifier class. Day cards/rows render flat (transparent, hover-only `--card-hover`), matching peer widgets; only the `today` card carries the subtle primary highlight.

## Configuration
- [S-12] `lat`/`lon`: coordinates required to fetch; cleared via "Change" button.
- [S-13] `locationName`: free-form display string from geocoding pick.
- [S-14] `unit`: 'celsius' or 'fahrenheit' (default celsius).
- [S-15] `days` (1-14, default 7): number of forecast days; clamped in fetch call.
- [S-16] `layout`: 'rows' | 'columns' | 'grid' (default 'rows').
- [S-17] `refreshMinutes`: 5 / 15 / 30 / 60 (default 60); minimum clamped to 5 minutes for polling.
- [S-18] `hideHeader`: toggles widget title bar.
- [S-19] Config Source tab offers debounced city search (400ms, min 2 chars); picks up to 6 results from open-meteo geocoding; Enter triggers immediate search. There is no search button: the input is the only trigger. Rendered by the shared `src/components/LocationSearch.tsx`, which Weather uses too ([S-13] there); the two widgets held a line-for-line copy each.
- [S-32] While a search is in flight the Source tab shows a "Searching…" hint. A search that returns nothing shows "No locations found." as an empty hint; a search that FAILS (network/HTTP) shows "Search failed." in the destructive colour, and logs. The two states are told apart: absent is not an error.
- [S-33] A geocoding result is rendered only if it carries a name and finite coordinates; malformed entries are dropped rather than shown.
- [S-34] The search feedback (Searching…/No-match/Failed hint and the results) renders as a floating overlay anchored to the input — a `.dropdown-panel` in the shared popup layer, `position: fixed`, `--z-dropdown` — not inline in the config's scroll flow, so it covers the sections below (Display/Refresh) instead of pushing them down. Dismissed by picking a result, clearing the query, Escape, or a click outside the input and panel. Same overlay path the app's dropdowns use ([S-27] in Weather).

## Context menu
- [S-20] "Refresh" action triggers immediate reload via `_refresh` bump.
- [S-21] Layout submenu toggles between Rows / Columns / Grid via `viewActions` helper.

## Data / backend
- [S-22] Fetches `https://api.open-meteo.com/v1/forecast?latitude=&longitude=&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum&temperature_unit={unit}&timezone=auto&forecast_days={days}`.
- [S-23] Config geocoding uses `https://geocoding-api.open-meteo.com/v1/search?name=&count=6&language=en&format=json`.
- [S-24] Polls every `refreshMinutes` minutes (min 5) via `createPolling`.
- [S-25] Demo mode returns `demoWeatherForecast()` fixture; bypasses lat/lon requirement.

## Caching
- [S-26] Cached forecast persists across app restarts (localStorage); cold launch shows last-known days instantly without "Loading..." flash.
- [S-27] Forecast widgets sharing lat/lon/unit/days share a single cache entry: only one network request fires per refresh.
- [S-28] On fetch failure when cached forecast is present, last-known forecast remains visible (error state suppressed).

## Configuration (continued)
- [S-29] Config Source tab hides the search input, error, and results when a city is selected; only the selected-city row with Change button is shown. Clicking Change clears lat/lon/locationName and restores the search view. "Selected" means both coordinates are finite numbers, so a location on the equator or the prime meridian (lat/lon 0) counts.
- [S-30] `hideLocation` (default false): when true, the pin + location name is hidden in the header while the label remains; toggled via "Location name" switch (Shown/Hidden) in the config Display tab. Independent of `hideHeader`.
