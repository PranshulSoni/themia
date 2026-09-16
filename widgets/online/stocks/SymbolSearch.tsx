import { createSignal, createEffect, For, Show, onCleanup } from 'solid-js';
import { Portal } from 'solid-js/web';
import { Search, TrendingUp, Bitcoin, Banknote, Boxes, ChartCandlestick } from 'lucide-solid';
import { Dynamic } from 'solid-js/web';
import { assetClass, SEARCHABLE_QUOTE_TYPES, type AssetClass } from './instrument.ts';
import { invoke } from '../../../src/demo/invoke.ts';
import { popupMount } from '../../../src/popupLayer.ts';
import { globalDesign } from '../../../src/design.ts';
import { t } from '../../../src/i18n.ts';
import './SymbolSearch.css';

export interface SearchHit {
  symbol: string;
  name: string;
  /** Which backend serves it. Not the asset class — see `asset`. */
  type: 'stock' | 'crypto';
  /** [S-38] What it is, for the row icon and the chosen-symbol label. */
  asset: AssetClass;
  exchange?: string;
}

const ASSET_ICONS: Record<AssetClass, typeof TrendingUp> = {
  equity: TrendingUp,
  crypto: Bitcoin,
  currency: Banknote,
  commodity: Boxes,
  index: ChartCandlestick,
};

interface SymbolSearchProps {
  onPick: (hit: SearchHit) => void;
}

async function httpJson<T>(url: string): Promise<T> {
  const text = await invoke('http_get', { url });
  return JSON.parse(text) as T;
}

interface YahooQuote {
  symbol: string;
  shortname?: string;
  longname?: string;
  exchDisp?: string;
  quoteType?: string;
}

async function searchYahoo(q: string): Promise<SearchHit[]> {
  try {
    const json = await httpJson<{ quotes?: YahooQuote[] }>(`https://query2.finance.yahoo.com/v1/finance/search?q=${encodeURIComponent(q)}&quotesCount=8&newsCount=0`);
    const quotes = json.quotes ?? [];
    return quotes
      // [S-40] Everything here is served by the same v8 chart endpoint, so the
      // set is the classes that endpoint quotes — including currency pairs
      // (`EURUSD=X`) and futures contracts (`GC=F`, the way Yahoo lists
      // commodities).
      .filter((it) => SEARCHABLE_QUOTE_TYPES.includes(it.quoteType ?? ''))
      .slice(0, 8)
      .map((it) => ({
        symbol: it.symbol,
        name: it.shortname || it.longname || it.symbol,
        type: 'stock' as const,
        asset: assetClass(it.symbol, 'stock', it.quoteType),
        exchange: it.exchDisp,
      }));
  } catch {
    return [];
  }
}

interface CoinGeckoCoin {
  id: string;
  name: string;
  symbol?: string;
}

async function searchCoinGecko(q: string): Promise<SearchHit[]> {
  try {
    const json = await httpJson<{ coins?: CoinGeckoCoin[] }>(`https://api.coingecko.com/api/v3/search?query=${encodeURIComponent(q)}`);
    const coins = json.coins ?? [];
    return coins.slice(0, 8).map((c) => ({
      symbol: c.id,
      name: `${c.name} (${(c.symbol || '').toUpperCase()})`,
      type: 'crypto' as const,
      asset: 'crypto' as const,
    }));
  } catch {
    return [];
  }
}

export default function SymbolSearch(props: SymbolSearchProps) {
  const [query, setQuery] = createSignal('');
  const [hits, setHits] = createSignal<SearchHit[]>([]);
  const [loading, setLoading] = createSignal(false);
  const [open, setOpen] = createSignal(false);
  const [panelStyle, setPanelStyle] = createSignal<Record<string, string>>({});
  const [activeIndex, setActiveIndex] = createSignal(-1);
  let inputRef: HTMLInputElement | undefined;
  let panelRef: HTMLDivElement | undefined;
  let seq = 0;
  let debounceTimer: number | null = null;

  function runSearch(q: string) {
    seq++;
    const my = seq;
    setLoading(true);
    Promise.all([searchYahoo(q), searchCoinGecko(q)]).then(([yahoo, gecko]) => {
      if (my !== seq) return;
      // interleave: stock first, crypto second, but cap each
      const merged: SearchHit[] = [];
      const max = Math.max(yahoo.length, gecko.length);
      for (let i = 0; i < max; i++) {
        if (yahoo[i]) merged.push(yahoo[i]);
        if (gecko[i]) merged.push(gecko[i]);
      }
      setHits(merged.slice(0, 12));
      setActiveIndex(-1);
      setLoading(false);
    }).catch(() => {
      if (my !== seq) return;
      setHits([]);
      setLoading(false);
    });
  }

  function onInput(e: InputEvent) {
    const v = (e.currentTarget as HTMLInputElement).value;
    setQuery(v);
    setActiveIndex(-1);
    if (debounceTimer != null) clearTimeout(debounceTimer);
    const trimmed = v.trim();
    if (!trimmed) { setHits([]); setLoading(false); seq++; return; }
    setOpen(true);
    debounceTimer = window.setTimeout(() => runSearch(trimmed), 250);
  }

  function positionPanel() {
    if (!inputRef) return;
    const s = globalDesign.uiScale || 1;
    const rect = inputRef.getBoundingClientRect();
    const top = rect.bottom / s;
    const width = rect.width / s;
    setPanelStyle({
      left: `${rect.left / s}px`,
      top: `${top + 4}px`,
      width: `${width}px`,
    });
  }

  function pick(hit: SearchHit) {
    props.onPick(hit);
    setQuery('');
    setHits([]);
    setActiveIndex(-1);
    setOpen(false);
    seq++;
    inputRef?.focus();
  }

  // Keyboard path so the results are pickable without a mouse: Arrow keys move
  // the highlight (wrapping), Enter commits the highlighted hit (or the first
  // when none is highlighted yet), Escape dismisses the panel.
  function scrollActiveIntoView() {
    const idx = activeIndex();
    if (idx < 0 || !panelRef) return;
    const items = panelRef.querySelectorAll<HTMLElement>('.symbol-search__item');
    items[idx]?.scrollIntoView({ block: 'nearest' });
  }

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      if (open()) { e.preventDefault(); setOpen(false); }
      return;
    }
    const list = hits();
    if (!open() || !list.length) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIndex((i) => (i + 1) % list.length);
      scrollActiveIntoView();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIndex((i) => (i <= 0 ? list.length - 1 : i - 1));
      scrollActiveIntoView();
    } else if (e.key === 'Enter') {
      const idx = activeIndex();
      const hit = idx >= 0 ? list[idx] : list[0];
      if (hit) { e.preventDefault(); pick(hit); }
    }
  }

  function onClickOutside(e: MouseEvent) {
    if (inputRef && inputRef.contains(e.target as Node)) return;
    if (panelRef && panelRef.contains(e.target as Node)) return;
    setOpen(false);
  }

  createEffect(() => {
    if (open()) {
      positionPanel();
      document.addEventListener('mousedown', onClickOutside);
    } else {
      document.removeEventListener('mousedown', onClickOutside);
    }
  });

  onCleanup(() => {
    if (debounceTimer != null) clearTimeout(debounceTimer);
    document.removeEventListener('mousedown', onClickOutside);
  });

  return (
    <div class="symbol-search">
      <div class="symbol-search__field">
        <Search size={13} strokeWidth={1.5} class="symbol-search__icon" />
        <input
          ref={inputRef}
          class="symbol-search__input"
          type="text"
          value={query()}
          placeholder={t('stocks.searchPlaceholder')}
          autocomplete="off"
          spellcheck={false}
          onInput={onInput}
          onKeyDown={onKeyDown}
          onFocus={() => { if (hits().length || loading()) setOpen(true); }}
        />
      </div>
      <Show when={open() && query().trim().length > 0}>
        <Portal mount={popupMount()}>
          <div class="symbol-search__panel" style={panelStyle()} ref={panelRef}>
            <Show when={loading() && !hits().length}>
              <div class="symbol-search__empty">{t('stocks.searching')}</div>
            </Show>
            <Show when={!loading() && !hits().length}>
              <div class="symbol-search__empty">{t('stocks.noResults')}</div>
            </Show>
            <For each={hits()}>
              {(hit, i) => (
                <div
                  class="symbol-search__item"
                  classList={{ 'symbol-search__item--active': i() === activeIndex() }}
                  onClick={() => pick(hit)}
                  onMouseEnter={() => setActiveIndex(i())}
                >
                  <span class="symbol-search__type">
                    <Dynamic component={ASSET_ICONS[hit.asset]} size={12} strokeWidth={1.5} />
                  </span>
                  <div class="symbol-search__text">
                    <span class="symbol-search__sym">{hit.symbol.toUpperCase()}</span>
                    <span class="symbol-search__name">{hit.name}</span>
                  </div>
                  <Show when={hit.exchange}>
                    <span class="symbol-search__exch">{hit.exchange}</span>
                  </Show>
                </div>
              )}
            </For>
          </div>
        </Portal>
      </Show>
    </div>
  );
}
