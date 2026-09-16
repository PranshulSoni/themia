// Instrument classification and quote precision for the Stocks widget
// (SPEC.md [S-38], [S-39]).
//
// Yahoo's symbol space is one namespace covering equities, ETFs, indices,
// currency pairs and futures. These helpers say which of those a symbol is, and
// how many decimals its quote needs — an FX pair moves in the fourth decimal,
// so the share-price rule of "two places above a dollar" throws away the whole
// signal (EUR/USD 1.1567 printed as "1.16").

export type AssetClass = 'equity' | 'crypto' | 'currency' | 'commodity' | 'index';

/**
 * [S-40] Yahoo quote types the symbol search keeps: exactly the classes the
 * one chart endpoint ([S-22]) can quote. `OPTION` is left out — it needs a
 * strike and an expiry this widget has nowhere to show.
 */
export const SEARCHABLE_QUOTE_TYPES: readonly string[] = [
  'EQUITY', 'ETF', 'MUTUALFUND', 'INDEX', 'CURRENCY', 'FUTURE',
];

/** Yahoo `quoteType` values, mapped to the class each one denotes. */
const QUOTE_TYPES: Record<string, AssetClass> = {
  CURRENCY: 'currency',
  FUTURE: 'commodity',
  INDEX: 'index',
  CRYPTOCURRENCY: 'crypto',
  EQUITY: 'equity',
  ETF: 'equity',
  MUTUALFUND: 'equity',
};

/**
 * [S-38] Which kind of instrument a symbol names.
 *
 * Live search results carry Yahoo's own `quoteType` and it is authoritative
 * when present; a persisted config entry carries only the symbol, so the
 * fallback reads Yahoo's symbol grammar — `^` prefix for an index, `=X` for a
 * currency pair, `=F` for a futures contract. One function, two strengths of
 * evidence, so a saved widget and a search row classify the same way.
 *
 * `type` is the fetch-path selector ('crypto' means CoinGecko), which outranks
 * both: a coin id like "bitcoin" has no Yahoo grammar to read.
 */
export function assetClass(
  symbol: string,
  type?: 'stock' | 'crypto',
  quoteType?: string,
): AssetClass {
  if (type === 'crypto') return 'crypto';
  const byQuote = QUOTE_TYPES[(quoteType ?? '').toUpperCase()];
  if (byQuote) return byQuote;
  const s = symbol.trim().toUpperCase();
  if (s.startsWith('^')) return 'index';
  if (s.endsWith('=X')) return 'currency';
  if (s.endsWith('=F')) return 'commodity';
  return 'equity';
}

/** Decimals implied by size alone — the rule that applied before `priceHint`. */
function magnitudeDecimals(price: number): number {
  return Math.abs(price) >= 1 ? 2 : 4;
}

/**
 * [S-39] How many decimals to print a quote with.
 *
 * `meta.priceHint` is the venue's own tick precision (2 for a share, 4 for an
 * FX pair) and is the only field that distinguishes EUR/USD at 1.1567 from a
 * stock at $1.16. It is a remote number, so it is validated, and it may only
 * ever *raise* precision: a sub-dollar instrument quoted with `priceHint: 2`
 * keeps the four places the magnitude rule already gave it.
 */
export function priceDecimals(price: number, priceHint?: unknown): number {
  const base = magnitudeDecimals(price);
  if (typeof priceHint !== 'number' || !Number.isInteger(priceHint)) return base;
  if (priceHint < 0 || priceHint > 8) return base;
  return Math.max(base, priceHint);
}

/** [S-08] Quote as displayed: grouped thousands, `priceDecimals` places. */
export function formatPrice(price: number, priceHint?: unknown): string {
  const dp = priceDecimals(price, priceHint);
  return price.toLocaleString('en-US', {
    minimumFractionDigits: dp,
    maximumFractionDigits: dp,
  });
}
