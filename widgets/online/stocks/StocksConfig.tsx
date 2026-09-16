import { Show, createMemo } from 'solid-js';
import { X, Monitor, Tag, RefreshCw } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import SymbolSearch, { type SearchHit } from './SymbolSearch.tsx';
import { assetClass, type AssetClass } from './instrument.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';
import './StocksConfig.css';

interface SymbolEntry {
  symbol: string;
  type: 'stock' | 'crypto';
  name?: string;
}

interface StocksConfigData {
  symbols?: SymbolEntry[];
  sparkRange?: string;
  showSparkline?: boolean;
  areaFill?: boolean;
  showMinMax?: boolean;
  customColors?: boolean;
  upColor?: string;
  downColor?: string;
  dimWhenClosed?: boolean;
  showExtendedHours?: boolean;
  refreshMinutes?: number;
}

interface StocksConfigProps {
  config: StocksConfigData;
  onChange: (c: StocksConfigData) => void;
}

/**
 * [S-38] Per class: what to call it, and a hand-typeable example so Yahoo's
 * symbol grammar is discoverable without leaving the panel. Keyed by
 * `AssetClass` so adding a class fails to compile until both are filled in, and
 * each label is a literal `t()` call so the missing-key scanner can see it.
 */
const ASSET_META: Record<AssetClass, { label: () => string; example: string }> = {
  equity:    { label: () => t('stocks.asset.equity'),    example: 'AAPL' },
  crypto:    { label: () => t('stocks.asset.crypto'),    example: 'bitcoin' },
  currency:  { label: () => t('stocks.asset.currency'),  example: 'EURUSD=X' },
  commodity: { label: () => t('stocks.asset.commodity'), example: 'GC=F' },
  index:     { label: () => t('stocks.asset.index'),     example: '^GSPC' },
};

const RANGE_OPTS = [
  { label: '1D', value: '1d' },
  { label: '5D', value: '5d' },
  { label: '1M', value: '1mo' },
  { label: '3M', value: '3mo' },
  { label: '6M', value: '6mo' },
  { label: '1Y', value: '1y' },
  { label: '2Y', value: '2y' },
  { label: '5Y', value: '5y' },
  { label: '10Y', value: '10y' },
];

export default function StocksConfig(props: StocksConfigProps) {
  // At most one symbol per widget.
  const symbol = (): SymbolEntry | undefined => props.config.symbols?.[0];

  const tabs = createMemo(() => [
    { key: 'display', label: t('stocks.tab.display'), icon: Monitor },
    { key: 'symbols', label: t('stocks.tab.symbols'), icon: Tag },
    { key: 'refresh', label: t('stocks.tab.refresh'), icon: RefreshCw },
  ]);

  function update<K extends keyof StocksConfigData>(key: K, value: StocksConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  function setSymbol(next: SymbolEntry | undefined) {
    update('symbols', next ? [next] : []);
  }

  // Picking a symbol replaces any existing one: one symbol per widget.
  function addFromSearch(hit: SearchHit) {
    const entry: SymbolEntry = { symbol: hit.symbol, type: hit.type };
    if (hit.name) entry.name = hit.name;
    setSymbol(entry);
  }

  return (
    <div class="stocks-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('stocks.timeframe')}</span>
                <span class="design-desc">{t('stocks.timeframeDesc')}</span>
              </div>
              <Dropdown
                value={props.config.sparkRange ?? '1d'}
                options={RANGE_OPTS}
                onChange={(v) => update('sparkRange', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('stocks.sparkline')}</span>
                <span class="design-desc">{t('stocks.sparklineDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.showSparkline ?? true}
                onLabel={t('stocks.sparkShown')}
                offLabel={t('stocks.sparkHidden')}
                onChange={(v) => update('showSparkline', v)}
              />
            </div>

            <Show when={props.config.showSparkline ?? true}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('stocks.areaFill')}</span>
                  <span class="design-desc">{t('stocks.areaFillDesc')}</span>
                </div>
                <ToggleSwitch
                  checked={!!props.config.areaFill}
                  onChange={(v) => update('areaFill', v)}
                />
              </div>

              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('stocks.showMinMax')}</span>
                  <span class="design-desc">{t('stocks.showMinMaxDesc')}</span>
                </div>
                <ToggleSwitch
                  checked={!!props.config.showMinMax}
                  onChange={(v) => update('showMinMax', v)}
                />
              </div>

              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('stocks.customColors')}</span>
                  <span class="design-desc">{t('stocks.customColorsDesc')}</span>
                </div>
                <ToggleSwitch
                  checked={!!props.config.customColors}
                  onChange={(v) => update('customColors', v)}
                />
              </div>

              <Show when={props.config.customColors}>
                <div class="design-row">
                  <div class="design-left">
                    <span class="design-label">{t('stocks.upColor')}</span>
                  </div>
                  <input type="color"
                    class="stocks-config__color"
                    value={props.config.upColor ?? '#22c55e'}
                    onInput={(e) => update('upColor', e.currentTarget.value)}
                  />
                </div>
                <div class="design-row">
                  <div class="design-left">
                    <span class="design-label">{t('stocks.downColor')}</span>
                  </div>
                  <input type="color"
                    class="stocks-config__color"
                    value={props.config.downColor ?? '#ef4444'}
                    onInput={(e) => update('downColor', e.currentTarget.value)}
                  />
                </div>
              </Show>
            </Show>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('stocks.dimWhenClosed')}</span>
                <span class="design-desc">{t('stocks.dimWhenClosedDesc')}</span>
              </div>
              <ToggleSwitch
                checked={props.config.dimWhenClosed ?? true}
                onChange={(v) => update('dimWhenClosed', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('stocks.extendedHours')}</span>
                <span class="design-desc">{t('stocks.extendedHoursDesc')}</span>
              </div>
              <ToggleSwitch
                checked={!!props.config.showExtendedHours}
                onChange={(v) => update('showExtendedHours', v)}
              />
            </div>
          </Show>

          <Show when={tab === 'symbols'}>
            <SymbolSearch onPick={addFromSearch} />

            <Show when={symbol()}>
              {(s) => (
                <div class="stocks-config__list">
                  <div class="stocks-config__row">
                    <div class="stocks-config__fields">
                      <input
                        class="stocks-config__edit-input stocks-config__edit-name"
                        type="text"
                        value={s().name ?? ''}
                        placeholder={t('stocks.displayNamePlaceholder')}
                        onInput={(e) => setSymbol({ ...s(), name: e.currentTarget.value || undefined })}
                      />
                      <input
                        class="stocks-config__edit-input stocks-config__edit-sym"
                        type="text"
                        value={s().symbol}
                        placeholder={ASSET_META[assetClass(s().symbol, s().type)].example}
                        onInput={(e) => setSymbol({ ...s(), symbol: e.currentTarget.value })}
                      />
                    </div>
                    <span class="stocks-config__type">{ASSET_META[assetClass(s().symbol, s().type)].label()}</span>
                    <button class="stocks-config__remove" onClick={() => setSymbol(undefined)} title={t('common.remove')}>
                      <X size={12} strokeWidth={1.5} />
                    </button>
                  </div>
                </div>
              )}
            </Show>
          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('stocks.refreshInterval')}</span>
                <span class="design-desc">{t('stocks.refreshIntervalDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshMinutes ?? 5}
                min={1}
                max={60}
                step={1}
                unit="m"
                onChange={(v) => update('refreshMinutes', v)}
              />
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
