import { createSignal, createEffect, onCleanup, Show, For } from 'solid-js';
import { Portal } from 'solid-js/web';
import { t } from '../i18n.ts';
import { logger } from '../debugLog.ts';
import { globalDesign } from '../design.ts';
import { popupMount } from '../popupLayer.ts';
import {
  geocodeUrl, parseGeoResults, formatLocationName, formatResultSub, formatCoords,
  hasLocation, GEOCODE_MIN_CHARS, GEOCODE_DEBOUNCE_MS, type GeoResult,
} from './locationSearch.ts';
import '../styles/config-base.css';
import './Dropdown.css';
import './LocationSearch.css';

// The city picker on the config Source tab of every location-backed widget:
// search (debounced) -> pick -> the selected city with a Change button.
//
// Weather and Weather Forecast each carried a line-for-line copy of this, down
// to stylesheets that differed only in the class prefix. This is that
// behaviour, once. Parameterised by nothing: the widgets differ only in which
// config fields they write the pick into, which is what onPick/onClear are for.
//
// Contract: weather-current SPEC [S-13][S-14][S-17][S-23],
// weather-forecast SPEC [S-19][S-23][S-29].

export interface PickedLocation {
  lat: number;
  lon: number;
  /** Display name, "city, region, country". */
  name: string;
}

interface LocationSearchProps {
  lat: number | null | undefined;
  lon: number | null | undefined;
  locationName: string;
  onPick: (loc: PickedLocation) => void;
  onClear: () => void;
}

// A closed set, not a message string: the copy is looked up at render so it
// follows a language change (Settings.SPEC.md [S-08]/[S-09]), and "no match"
// stays distinguishable from "the request failed" -- they read differently.
type SearchState =
  | { status: 'idle' }
  | { status: 'searching' }
  | { status: 'results'; results: GeoResult[] }
  | { status: 'notFound' }
  | { status: 'failed' };

export default function LocationSearch(props: LocationSearchProps) {
  const [query, setQuery] = createSignal('');
  const [state, setState] = createSignal<SearchState>({ status: 'idle' });
  // The results/hints float in a Portal below the input rather than in the
  // config's scroll flow, so they overlay the sections beneath instead of
  // shoving them down (autocomplete behaviour). `open` gates that panel;
  // `panelStyle` anchors it to the input, viewport-fixed like Dropdown.
  const [open, setOpen] = createSignal(false);
  const [panelStyle, setPanelStyle] = createSignal<Record<string, string>>({});
  let inputRef: HTMLInputElement | undefined;
  let panelRef: HTMLDivElement | undefined;

  const results = () => {
    const s = state();
    return s.status === 'results' ? s.results : [];
  };

  // The panel has something to say for every state except idle.
  const showable = () => state().status !== 'idle';

  const selectedLabel = () => {
    if (props.locationName) return props.locationName;
    const lat = props.lat, lon = props.lon;
    return typeof lat === 'number' && typeof lon === 'number' ? formatCoords(lat, lon) : '';
  };

  // Bumped by every new search and every reset, so a slow response that lands
  // after its query stopped being the current one is dropped instead of
  // overwriting a newer result (Enter can outrun the debounce).
  let seq = 0;
  let debounceTimer: ReturnType<typeof setTimeout> | undefined;

  createEffect(() => {
    const q = query().trim();
    clearTimeout(debounceTimer);
    if (q.length < GEOCODE_MIN_CHARS) { seq++; setState({ status: 'idle' }); return; }
    debounceTimer = setTimeout(() => void search(), GEOCODE_DEBOUNCE_MS);
  });

  // Open the overlay whenever there's a state to show, and keep it anchored to
  // the input; close when there's nothing to say. Reruns on every state change
  // (i.e. as the user types), so the panel repositions if the layout shifted.
  createEffect(() => {
    if (showable()) { reposition(); setOpen(true); }
    else setOpen(false);
  });

  // Dismiss/reposition listeners live only while the panel is open.
  createEffect(() => {
    if (!open()) return;
    document.addEventListener('mousedown', handleClickOutside);
    window.addEventListener('resize', reposition);
    onCleanup(() => {
      document.removeEventListener('mousedown', handleClickOutside);
      window.removeEventListener('resize', reposition);
    });
  });

  onCleanup(() => { seq++; clearTimeout(debounceTimer); });

  // Anchor the fixed panel under the input, full input width. Positions in
  // pre-scale coordinates (÷ uiScale) because the popup layer is itself scaled,
  // matching Dropdown; flips above the input when there's no room below.
  function reposition() {
    if (!inputRef) return;
    const s = globalDesign.uiScale || 1;
    const rect = inputRef.getBoundingClientRect();
    const top = rect.bottom / s;
    const inputTop = rect.top / s;
    const left = rect.left / s;
    const width = rect.width / s;
    const viewH = window.innerHeight / s;
    const estH = 280; // enough to decide flip; the panel caps its own height
    const flipUp = top + estH + 8 > viewH && inputTop - estH > 8;
    const style: Record<string, string> = { left: `${left}px`, width: `${width}px` };
    if (flipUp) { style.bottom = `${viewH - inputTop + 4}px`; style.top = 'auto'; }
    else { style.top = `${top + 4}px`; style.bottom = 'auto'; }
    setPanelStyle(style);
  }

  function handleClickOutside(e: MouseEvent) {
    const target = e.target as Node;
    if (inputRef && inputRef.contains(target)) return;
    if (panelRef && panelRef.contains(target)) return;
    setOpen(false);
  }

  async function search() {
    const q = query().trim();
    if (q.length < GEOCODE_MIN_CHARS) return;
    const mine = ++seq;
    setState({ status: 'searching' });
    try {
      const res = await fetch(geocodeUrl(q));
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const found = parseGeoResults(await res.json());
      if (mine !== seq) return; // superseded
      setState(found.length ? { status: 'results', results: found } : { status: 'notFound' });
    } catch (e) {
      if (mine !== seq) return;
      logger.warn('location-search', `Geocoding "${q}" failed: ${e}`);
      setState({ status: 'failed' });
    }
  }

  function pick(r: GeoResult) {
    seq++;
    clearTimeout(debounceTimer);
    setQuery('');
    setState({ status: 'idle' });
    setOpen(false);
    props.onPick({ lat: r.latitude, lon: r.longitude, name: formatLocationName(r) });
  }

  return (
    <div class="location-search">
      <Show
        when={hasLocation(props.lat, props.lon)}
        fallback={
          <>
            {/* No search button: the query fires on its own after the debounce
                (and immediately on Enter), so a button would be a second way to
                do the one thing the input already does -- which is how it grew
                a bespoke style outside the documented button set. Same shape as
                stocks' SymbolSearch. */}
            <input
              ref={inputRef}
              class="text-input"
              type="text"
              placeholder={t('locationSearch.placeholder')}
              value={query()}
              autocomplete="off"
              spellcheck={false}
              onInput={(e) => setQuery(e.currentTarget.value)}
              onFocus={() => { if (showable()) { reposition(); setOpen(true); } }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') { clearTimeout(debounceTimer); void search(); return; }
                // Dismiss the overlay only; stopPropagation so Escape doesn't
                // also close the config window behind it (cf. Dropdown).
                if (e.key === 'Escape' && open()) { e.preventDefault(); e.stopPropagation(); setOpen(false); }
              }}
            />

            {/* The hints and results float over the sections below rather than
                pushing them down: a fixed panel anchored to the input, in the
                shared popup layer (`.dropdown-panel` chrome). [S-25]/[S-26] */}
            <Show when={open()}>
              <Portal mount={popupMount()}>
                <div class="dropdown-panel locsearch-panel" style={panelStyle()} ref={panelRef}>
                  <Show when={state().status === 'searching'}>
                    <span class="locsearch-hint">{t('locationSearch.searching')}</span>
                  </Show>
                  {/* "No match" is an empty state, not a failure; only a request
                      that could not be answered at all gets the destructive
                      treatment. */}
                  <Show when={state().status === 'notFound'}>
                    <span class="locsearch-hint">{t('locationSearch.noResults')}</span>
                  </Show>
                  <Show when={state().status === 'failed'}>
                    <span class="locsearch-hint locsearch-hint-error">{t('locationSearch.failed')}</span>
                  </Show>

                  <Show when={results().length}>
                    <div class="locsearch-results">
                      <For each={results()}>
                        {(r) => (
                          <button class="locsearch-result" onClick={() => pick(r)}>
                            <span class="locsearch-result-name">{r.name}</span>
                            <Show when={formatResultSub(r)}>
                              <span class="locsearch-result-sub">{formatResultSub(r)}</span>
                            </Show>
                          </button>
                        )}
                      </For>
                    </div>
                  </Show>
                </div>
              </Portal>
            </Show>
          </>
        }
      >
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">
              <span class="locsearch-name">{selectedLabel()}</span>
            </span>
          </div>
          <button class="btn-secondary" onClick={() => props.onClear()}>{t('common.change')}</button>
        </div>
      </Show>
    </div>
  );
}
