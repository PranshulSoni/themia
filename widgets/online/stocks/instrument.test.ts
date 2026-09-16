// Instrument classification and quote precision (SPEC.md [S-08], [S-38], [S-39]).
//
// Regression cover for issue #116: forex, commodities and indices all ride the
// same Yahoo chart endpoint as equities, so the only things that made them
// unusable were classification (commodities were unreachable from search) and
// precision (an FX pair rounded to two decimals shows no movement at all).
import { describe, it, expect } from 'vitest';
import { assetClass, formatPrice, priceDecimals, SEARCHABLE_QUOTE_TYPES } from './instrument.ts';

describe('SEARCHABLE_QUOTE_TYPES', () => {
  it('[S-40] #116: commodities and currency pairs survive the search filter', () => {
    // The real shape of a Yahoo `v1/finance/search` response for "gold" and
    // "EURUSD" — before this, only the equity and the ETF got through, so no
    // commodity was reachable from the picker at all.
    const hits = [
      { symbol: 'GOLD', quoteType: 'EQUITY' },
      { symbol: 'GC=F', quoteType: 'FUTURE' },
      { symbol: 'GLD', quoteType: 'ETF' },
      { symbol: 'EURUSD=X', quoteType: 'CURRENCY' },
      { symbol: '^GSPC', quoteType: 'INDEX' },
      { symbol: 'VFIAX', quoteType: 'MUTUALFUND' },
      { symbol: 'AAPL260116C00300000', quoteType: 'OPTION' },
      { symbol: 'BTC-USD', quoteType: 'CRYPTOCURRENCY' },
    ];
    const kept = hits.filter((h) => SEARCHABLE_QUOTE_TYPES.includes(h.quoteType)).map((h) => h.symbol);
    expect(kept).toEqual(['GOLD', 'GC=F', 'GLD', 'EURUSD=X', '^GSPC', 'VFIAX']);
  });

  it('[S-40] excludes options, which have no strike/expiry slot in the widget', () => {
    expect(SEARCHABLE_QUOTE_TYPES).not.toContain('OPTION');
    // Crypto comes from CoinGecko, not from the Yahoo half of the merge.
    expect(SEARCHABLE_QUOTE_TYPES).not.toContain('CRYPTOCURRENCY');
  });
});

describe('assetClass', () => {
  it('[S-38] reads Yahoo quoteType when a live search result carries one', () => {
    expect(assetClass('EURUSD=X', 'stock', 'CURRENCY')).toBe('currency');
    expect(assetClass('GC=F', 'stock', 'FUTURE')).toBe('commodity');
    expect(assetClass('^GSPC', 'stock', 'INDEX')).toBe('index');
    expect(assetClass('AAPL', 'stock', 'EQUITY')).toBe('equity');
    expect(assetClass('GLD', 'stock', 'ETF')).toBe('equity');
    // Yahoo reports some exchange-listed index products under a plain symbol;
    // the quote type is what makes them classifiable at all.
    expect(assetClass('18QQ.Z', 'stock', 'INDEX')).toBe('index');
  });

  it('[S-38] falls back to Yahoo symbol grammar for persisted entries', () => {
    // A config saved before this existed carries only {symbol, type}.
    expect(assetClass('EURUSD=X', 'stock')).toBe('currency');
    expect(assetClass('JPY=X', 'stock')).toBe('currency');
    expect(assetClass('GC=F', 'stock')).toBe('commodity');
    expect(assetClass('CL=F', 'stock')).toBe('commodity');
    expect(assetClass('^GSPC', 'stock')).toBe('index');
    expect(assetClass('^DJI', 'stock')).toBe('index');
    expect(assetClass('AAPL', 'stock')).toBe('equity');
    expect(assetClass('NESN.SW', 'stock')).toBe('equity');
  });

  it('[S-38] is case- and whitespace-insensitive', () => {
    expect(assetClass('  eurusd=x  ', 'stock')).toBe('currency');
    expect(assetClass('gc=f', 'stock', 'future')).toBe('commodity');
  });

  it('[S-38] the CoinGecko fetch path outranks any symbol grammar', () => {
    // Coin ids are free-form and must never be read as Yahoo symbols.
    expect(assetClass('bitcoin', 'crypto')).toBe('crypto');
    expect(assetClass('bitcoin', 'crypto', 'EQUITY')).toBe('crypto');
  });

  it('[S-38] treats an unknown quote type as no evidence, not as equity', () => {
    expect(assetClass('EURUSD=X', 'stock', 'OPTION')).toBe('currency');
    expect(assetClass('AAPL', 'stock', '')).toBe('equity');
  });
});

describe('priceDecimals', () => {
  it('[S-39] uses the venue tick precision Yahoo reports', () => {
    expect(priceDecimals(1.1567, 4)).toBe(4);   // EUR/USD
    expect(priceDecimals(158.945, 4)).toBe(4);  // USD/JPY
    expect(priceDecimals(305.26, 2)).toBe(2);   // AAPL
    expect(priceDecimals(4429.2, 2)).toBe(2);   // gold future
  });

  it('[S-39] never lets a hint reduce precision below the magnitude rule', () => {
    // A sub-dollar instrument is quoted priceHint: 2; it keeps four places.
    expect(priceDecimals(0.0234, 2)).toBe(4);
    expect(priceDecimals(0.0234, 0)).toBe(4);
  });

  it('[S-39] ignores a missing or unusable hint', () => {
    expect(priceDecimals(1.1567)).toBe(2);
    expect(priceDecimals(1.1567, undefined)).toBe(2);
    expect(priceDecimals(1.1567, null)).toBe(2);
    expect(priceDecimals(1.1567, '4')).toBe(2);
    expect(priceDecimals(1.1567, 2.5)).toBe(2);
    expect(priceDecimals(1.1567, NaN)).toBe(2);
    expect(priceDecimals(1.1567, -1)).toBe(2);
    expect(priceDecimals(1.1567, 99)).toBe(2);
  });
});

describe('formatPrice', () => {
  it('[S-39] #116: an FX pair keeps its pips instead of rounding to cents', () => {
    expect(formatPrice(1.1567, 4)).toBe('1.1567');
    expect(formatPrice(158.945, 4)).toBe('158.9450');
    // Without the hint this is the old, lossy output — the bug being fixed.
    expect(formatPrice(1.1567)).toBe('1.16');
  });

  it('[S-08] equities and commodities are unchanged: 2dp, grouped thousands', () => {
    expect(formatPrice(305.26, 2)).toBe('305.26');
    expect(formatPrice(4429.2, 2)).toBe('4,429.20');
    expect(formatPrice(7798.99, 2)).toBe('7,798.99');
    expect(formatPrice(999.5, 2)).toBe('999.50');
  });

  it('[S-08] sub-dollar quotes keep four places, hint or not', () => {
    expect(formatPrice(0.4231)).toBe('0.4231');
    expect(formatPrice(0.4231, 2)).toBe('0.4231');
  });
});
