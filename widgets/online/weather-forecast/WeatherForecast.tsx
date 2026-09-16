import { createMemo, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { Show, For, Switch, Match } from 'solid-js';
import {
  Sun, CloudSun, Cloudy, CloudFog, CloudDrizzle, CloudRain,
  CloudSnow, Snowflake, CloudLightning, Cloud, Droplet, MapPin,
} from 'lucide-solid';
import { isDemo } from '../../../src/demo/invoke.ts';
import { demoWeatherForecast } from '../../../src/demo/fixtures.ts';
import { t, lang } from '../../../src/i18n.ts';
import { dayLabel, isToday } from './dayLabel.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './WeatherForecast.css';

type IconC = Component<{ size?: number; strokeWidth?: number }>;

export interface WeatherForecastProps {
  lat?: number | null;
  lon?: number | null;
  locationName?: string;
  unit?: string;
  days?: number;
  layout?: string;
  refreshMinutes?: number;
  label?: string;
  headerMode?: HeaderMode;
  hideLocation?: boolean;
  _refresh?: number;
}

interface ForecastDay {
  date: string;
  code: number;
  hi: number;
  lo: number;
  precip: number;
}

const WMO: Record<number, [IconC, string]> = {
  0:  [Sun,            'forecast.wmo.clear'],
  1:  [Sun,            'forecast.wmo.mainlyClear'],
  2:  [CloudSun,       'forecast.wmo.partlyCloudy'],
  3:  [Cloudy,         'forecast.wmo.overcast'],
  45: [CloudFog,       'forecast.wmo.fog'],
  48: [CloudFog,       'forecast.wmo.icyFog'],
  51: [CloudDrizzle,   'forecast.wmo.drizzle'],
  53: [CloudDrizzle,   'forecast.wmo.drizzle'],
  55: [CloudDrizzle,   'forecast.wmo.drizzle'],
  61: [CloudRain,      'forecast.wmo.lightRain'],
  63: [CloudRain,      'forecast.wmo.rain'],
  65: [CloudRain,      'forecast.wmo.heavyRain'],
  71: [CloudSnow,      'forecast.wmo.lightSnow'],
  73: [CloudSnow,      'forecast.wmo.snow'],
  75: [Snowflake,      'forecast.wmo.heavySnow'],
  77: [CloudSnow,      'forecast.wmo.snowGrains'],
  80: [CloudRain,      'forecast.wmo.showers'],
  81: [CloudRain,      'forecast.wmo.showers'],
  82: [CloudRain,      'forecast.wmo.heavyShowers'],
  85: [CloudSnow,      'forecast.wmo.snowShowers'],
  86: [Snowflake,      'forecast.wmo.snowShowers'],
  95: [CloudLightning, 'forecast.wmo.storm'],
  96: [CloudLightning, 'forecast.wmo.storm'],
  99: [CloudLightning, 'forecast.wmo.storm'],
};

function wmo(code: number): [IconC, string] { return WMO[code] ?? [Cloud, '']; }

export default function WeatherForecast(props: WeatherForecastProps) {
  function tl(): string { return props.unit === 'fahrenheit' ? '°F' : '°C'; }

  const refreshMs = createMemo(() => Math.max(5, props.refreshMinutes ?? 60) * 60_000);
  const days = createMemo(() => Math.min(14, Math.max(1, props.days ?? 7)));

  const cache = createCachedFetch<ForecastDay[]>({
    namespace: 'weather-forecast',
    key: () => {
      if (isDemo()) return 'demo';
      if (!props.lat || !props.lon) return null;
      return `${props.lat},${props.lon},${props.unit ?? 'celsius'},${days()}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) {
        const d = demoWeatherForecast().daily;
        return d.time.map((date: string, i: number) => ({
          date,
          code: d.weather_code[i],
          hi: d.temperature_2m_max[i],
          lo: d.temperature_2m_min[i],
          precip: d.precipitation_sum[i] ?? 0,
        }));
      }
      const url = `https://api.open-meteo.com/v1/forecast`
        + `?latitude=${props.lat}&longitude=${props.lon}`
        + `&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum`
        + `&temperature_unit=${props.unit ?? 'celsius'}`
        + `&timezone=auto`
        + `&forecast_days=${days()}`;
      const res = await fetch(url, { signal });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = await res.json();
      const d = json.daily;
      return d.time.map((date: string, i: number) => ({
        date,
        code: d.weather_code[i],
        hi: Math.round(d.temperature_2m_max[i]),
        lo: Math.round(d.temperature_2m_min[i]),
        precip: d.precipitation_sum[i] ?? 0,
      }));
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'forecast');

  // [S-20] Context-menu "Refresh" bumps the `_refresh` config token; force a
  // re-fetch when it changes. The mount run is skipped (createCachedFetch's own
  // effect covers first load), so a `_refresh` token persisted in config from a
  // previous session can't force a redundant network fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  const forecast = createMemo<ForecastDay[]>(() => cache.data() ?? []);

  const tempRange = createMemo(() => {
    const f = forecast();
    if (!f.length) return { min: 0, span: 1 };
    const allLo = f.map(d => d.lo);
    const allHi = f.map(d => d.hi);
    const min = Math.min(...allLo);
    const max = Math.max(...allHi);
    return { min, span: max - min || 1 };
  });

  function pct(v: number): string { return ((v - tempRange().min) / tempRange().span * 100).toFixed(1) + '%'; }
  function barLeft(lo: number): string { return pct(lo); }
  function barWidth(lo: number, hi: number): string { return ((hi - lo) / tempRange().span * 100).toFixed(1) + '%'; }

  return (
    <div class="weather-forecast-widget">

      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={props.label || t('forecast.title')}
        secondary={
          <Show when={props.locationName && !props.hideLocation}>
            <span class="info-loc"><MapPin size={10} strokeWidth={1.5} /> {props.locationName}</span>
          </Show>
        }
      />

      <div class="weather-forecast-body widget-body">
      <Switch>
        <Match when={(!props.lat || !props.lon) && !isDemo()}>
          <div class="empty" innerHTML={t('forecast.selectLocation')} />
        </Match>

        <Match when={cache.loading() && !forecast().length}>
          <div class="empty">{t('widgetUi.loading')}</div>
        </Match>

        <Match when={cache.error() && !forecast().length}>
          <div class="empty err">{t('forecast.failed')}</div>
        </Match>

        {/* ROWS layout */}
        <Match when={(props.layout === 'rows' || !props.layout) && forecast().length > 0}>
          <div class="rows-list">
            <For each={forecast()}>
              {(day) => {
                const [icon, desc] = wmo(day.code);
                return (
                  <div class="row-item" classList={{ today: isToday(day.date) }}>
                    <span class="r-day">{dayLabel(day.date, new Date(), lang(), t('common.today'))}</span>
                    <span class="r-icon"><Dynamic component={icon} size={15} strokeWidth={1.5} /></span>
                    <span class="r-desc">{desc ? t(desc) : ''}</span>
                    <div class="r-bar-track">
                      <div class="r-bar-fill" style={{ left: barLeft(day.lo), width: barWidth(day.lo, day.hi) }} />
                    </div>
                    <span class="r-lo">{day.lo}{tl()}</span>
                    <span class="r-hi">{day.hi}{tl()}</span>
                    <Show when={day.precip > 0} fallback={<span class="r-precip" />}>
                      <span class="r-precip"><Droplet size={9} strokeWidth={1.5} />{day.precip.toFixed(1)}</span>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>
        </Match>

        {/* COLUMNS layout */}
        <Match when={props.layout === 'columns' && forecast().length > 0}>
          <div class="cols-list">
            <For each={forecast()}>
              {(day) => {
                const [icon, desc] = wmo(day.code);
                return (
                  <div class="col-card" classList={{ today: isToday(day.date) }}>
                    <span class="c-day">{dayLabel(day.date, new Date(), lang(), t('common.today'))}</span>
                    <span class="c-icon"><Dynamic component={icon} size={22} strokeWidth={1.5} /></span>
                    <span class="c-desc">{desc ? t(desc) : ''}</span>
                    <div class="c-temps">
                      <span class="c-hi">{day.hi}{tl()}</span>
                      <span class="c-lo">{day.lo}{tl()}</span>
                    </div>
                    <div class="c-bar-track">
                      <div class="c-bar-fill" style={{ left: barLeft(day.lo), width: barWidth(day.lo, day.hi) }} />
                    </div>
                    <Show when={day.precip > 0} fallback={<span class="c-precip">&nbsp;</span>}>
                      <span class="c-precip"><Droplet size={9} strokeWidth={1.5} /> {day.precip.toFixed(1)}</span>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>
        </Match>

        {/* GRID layout */}
        <Match when={props.layout === 'grid' && forecast().length > 0}>
          <div class="grid-list">
            <For each={forecast()}>
              {(day) => {
                const [icon, desc] = wmo(day.code);
                return (
                  <div class="grid-card" classList={{ today: isToday(day.date) }}>
                    <div class="g-header">
                      <span class="g-day">{dayLabel(day.date, new Date(), lang(), t('common.today'))}</span>
                      <span class="g-icon"><Dynamic component={icon} size={18} strokeWidth={1.5} /></span>
                    </div>
                    <span class="g-desc">{desc ? t(desc) : ''}</span>
                    <div class="g-bar-track">
                      <div class="g-bar-fill" style={{ left: barLeft(day.lo), width: barWidth(day.lo, day.hi) }} />
                    </div>
                    <div class="g-temps">
                      <span class="g-lo">{day.lo}{tl()}</span>
                      <span class="g-hi">{day.hi}{tl()}</span>
                    </div>
                    <Show when={day.precip > 0}>
                      <span class="g-precip"><Droplet size={10} strokeWidth={1.5} /> {day.precip.toFixed(1)} mm</span>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>
        </Match>
      </Switch>
      </div>

    </div>
  );
}
