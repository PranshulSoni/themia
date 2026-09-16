import { createMemo, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { Show, Switch, Match } from 'solid-js';
import {
  Sun, CloudSun, Cloudy, CloudFog, CloudDrizzle, CloudRain,
  CloudSnow, Snowflake, CloudLightning, Cloud,
  Thermometer, Droplets, Wind, MapPin,
} from 'lucide-solid';
import { isDemo } from '../../../src/demo/invoke.ts';
import { demoWeatherCurrent } from '../../../src/demo/fixtures.ts';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './WeatherCurrent.css';

type IconC = Component<{ size?: number; strokeWidth?: number }>;

export interface WeatherCurrentProps {
  lat?: number | null;
  lon?: number | null;
  locationName?: string;
  unit?: string;
  refreshMinutes?: number;
  label?: string;
  headerMode?: HeaderMode;
  hideLocation?: boolean;
  _refresh?: number;
}

interface WeatherData {
  temp: number;
  feels: number;
  humidity: number;
  wind: number;
  code: number;
}

const WMO: Record<number, [IconC, string]> = {
  0:  [Sun,            'Clear sky'],
  1:  [Sun,            'Mainly clear'],
  2:  [CloudSun,       'Partly cloudy'],
  3:  [Cloudy,         'Overcast'],
  45: [CloudFog,       'Fog'],
  48: [CloudFog,       'Icy fog'],
  51: [CloudDrizzle,   'Light drizzle'],
  53: [CloudDrizzle,   'Drizzle'],
  55: [CloudDrizzle,   'Heavy drizzle'],
  61: [CloudRain,      'Light rain'],
  63: [CloudRain,      'Rain'],
  65: [CloudRain,      'Heavy rain'],
  71: [CloudSnow,      'Light snow'],
  73: [CloudSnow,      'Snow'],
  75: [Snowflake,      'Heavy snow'],
  77: [CloudSnow,      'Snow grains'],
  80: [CloudRain,      'Light showers'],
  81: [CloudRain,      'Showers'],
  82: [CloudRain,      'Heavy showers'],
  85: [CloudSnow,      'Snow showers'],
  86: [Snowflake,      'Heavy snow showers'],
  95: [CloudLightning, 'Thunderstorm'],
  96: [CloudLightning, 'Thunderstorm + hail'],
  99: [CloudLightning, 'Severe thunderstorm'],
};

function wmo(code: number | null | undefined): [IconC, string] {
  if (code == null) return [Cloud, ''];
  return WMO[code] ?? [Cloud, `Code ${code}`];
}

export default function WeatherCurrent(props: WeatherCurrentProps) {
  function tl(): string { return props.unit === 'fahrenheit' ? '°F' : '°C'; }

  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 15) * 60_000);

  const cache = createCachedFetch<WeatherData>({
    namespace: 'weather-current',
    key: () => {
      if (isDemo()) return 'demo';
      if (!props.lat || !props.lon) return null;
      return `${props.lat},${props.lon},${props.unit ?? 'celsius'}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) return demoWeatherCurrent();
      const url = `https://api.open-meteo.com/v1/forecast`
        + `?latitude=${props.lat}&longitude=${props.lon}`
        + `&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code`
        + `&wind_speed_unit=kmh`
        + `&temperature_unit=${props.unit ?? 'celsius'}`;
      const res = await fetch(url, { signal });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = await res.json();
      const c = json.current;
      return {
        temp:     Math.round(c.temperature_2m),
        feels:    Math.round(c.apparent_temperature),
        humidity: c.relative_humidity_2m,
        wind:     Math.round(c.wind_speed_10m),
        code:     c.weather_code,
      };
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'weather');

  // Widget-level "Refresh" context action bumps the `_refresh` config token;
  // force a re-fetch when it changes. The mount run is skipped (createCachedFetch's
  // own effect covers first load), so a `_refresh` token persisted in config from a
  // previous session can't force a redundant network fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  return (
    <div class="weather-current-widget">

      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={props.label || t('weather.defaultLabel')}
        secondary={
          <Show when={props.locationName && !props.hideLocation}>
            <span class="info-loc"><MapPin size={10} strokeWidth={1.5} /> {props.locationName}</span>
          </Show>
        }
      />

      <div class="weather-current-body widget-body">
      <Switch fallback={<div class="empty"><span>{t('widgetUi.loading')}</span></div>}>
        <Match when={(!props.lat || !props.lon) && !isDemo()}>
          <div class="empty">
            <span class="empty-icon"><CloudSun size={32} strokeWidth={1.5} /></span>
            <span innerHTML={t('weather.selectLocation')} />
          </div>
        </Match>

        <Match when={cache.loading() && !cache.data()}>
          <div class="empty"><span>{t('widgetUi.loading')}</span></div>
        </Match>

        <Match when={cache.error() && !cache.data()}>
          <div class="empty err"><span>{t('weather.failed')}</span></div>
        </Match>

        <Match when={cache.data()}>
          {(() => {
            const d = cache.data()!;
            const [icon, desc] = wmo(d.code);
            return (
              <>
                <div class="main">
                  <span class="w-icon"><Dynamic component={icon} size={40} strokeWidth={1.5} /></span>
                  <div class="temp-block">
                    <div class="temp">{d.temp}<span class="unit">{tl()}</span></div>
                    <div class="desc">{desc}</div>
                  </div>
                </div>

                <div class="details">
                  <div class="detail-item">
                    <span class="d-icon"><Thermometer size={14} strokeWidth={1.5} /></span>
                    <span class="d-label">{t('weather.feelsLike')}</span>
                    <span class="d-val">{d.feels}{tl()}</span>
                  </div>
                  <div class="detail-item">
                    <span class="d-icon"><Droplets size={14} strokeWidth={1.5} /></span>
                    <span class="d-label">{t('weather.humidity')}</span>
                    <span class="d-val">{d.humidity}%</span>
                  </div>
                  <div class="detail-item">
                    <span class="d-icon"><Wind size={14} strokeWidth={1.5} /></span>
                    <span class="d-label">{t('weather.wind')}</span>
                    <span class="d-val">{d.wind} km/h</span>
                  </div>
                </div>
              </>
            );
          })()}
        </Match>
      </Switch>
      </div>

    </div>
  );
}
