import { Show } from 'solid-js';
import { Clock, Clock12, Monitor, LayoutGrid, Watch, AlignVerticalJustifyCenter, AlignLeft, AlignCenter, AlignRight, EyeOff, MapPin } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import { cityFromTimezone } from './Clock.tsx';
import '../../../src/styles/config-base.css';
import './ClockConfig.css';
import { t } from '../../../src/i18n.ts';

interface ClockConfigData {
  timezone?: string;
  customLabel?: string;
  style?: string;
  align?: string;
  format?: string;
  showSeconds?: boolean;
  showDate?: boolean;
  offsetMode?: string;
}

interface ClockConfigProps {
  config: ClockConfigData;
  onChange: (c: ClockConfigData) => void;
  globals?: Record<string, unknown>;
}

const LOCAL_TZ = (() => {
  try { return Intl.DateTimeFormat().resolvedOptions().timeZone; } catch { return ''; }
})();

// Build the timezone option list once. `Intl.supportedValuesOf` is available in
// modern Chromium (Tauri's WebView2). Fallback to a tiny static list so the
// widget remains functional in unlikely envs without it. The first option,
// empty value, pins the clock to the current (local) timezone.
// Label swaps IANA underscores for spaces so the searchable Dropdown (which
// matches on label text) finds multi-word cities: "New York" must hit
// "America/New York". The value stays the raw IANA id the clock needs.
// IANA ids are not translated — they are the same identifiers worldwide.
const TZ_ZONES: { value: string; label: string }[] = (() => {
  const anyIntl = Intl as unknown as { supportedValuesOf?: (k: string) => string[] };
  let zones: string[];
  if (typeof anyIntl.supportedValuesOf === 'function') {
    try {
      zones = anyIntl.supportedValuesOf('timeZone');
    } catch {
      zones = ['UTC'];
    }
  } else {
    zones = ['UTC'];
  }
  return zones.map(z => ({ value: z, label: z.replace(/_/g, ' ') }));
})();

// Only the leading "current location" entry is translated, so the (large)
// zone list above is still built once while its label still follows the
// language signal. The empty value pins the clock to the local timezone.
const tzOptions = () => [
  {
    value: '',
    label: LOCAL_TZ
      ? t('clock.tzCurrent', { city: cityFromTimezone(LOCAL_TZ) })
      : t('clock.tzCurrentLocation'),
  },
  ...TZ_ZONES,
];

// A function, not a module-level const: `t()` reads the reactive language
// signal, so evaluating it once at import time would freeze these labels in
// whatever language was active at startup and never update on a switch.
const offsetOptions = () => [
  { value: 'none',  label: t('clock.offsetNone'), icon: () => <EyeOff size={13} strokeWidth={1.5} /> },
  { value: 'utc',   label: t('clock.offsetUtc'), icon: () => <Clock size={13} strokeWidth={1.5} /> },
  { value: 'local', label: t('clock.offsetLocal'), icon: () => <MapPin size={13} strokeWidth={1.5} /> },
];

export default function ClockConfig(props: ClockConfigProps) {
  function update<K extends keyof ClockConfigData>(key: K, value: ClockConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  const tz = () => props.config.timezone ?? '';
  const labelPlaceholder = () => cityFromTimezone(tz() || LOCAL_TZ);

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.timezoneLabel')}</span>
              <span class="design-desc">{t('clock.timezoneDesc')}</span>
            </div>
            <Dropdown
              class="clock-tz-dropdown"
              value={tz()}
              options={tzOptions()}
              onChange={(v) => update('timezone', v)}
              searchable
              searchPlaceholder={t('clock.timezoneSearch')}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.labelLabel')}</span>
              <span class="design-desc">{t('clock.labelDesc')}</span>
            </div>
            <input
              class="text-input clock-label-input"
              type="text"
              placeholder={labelPlaceholder()}
              value={props.config.customLabel ?? ''}
              onInput={(e) => update('customLabel', e.currentTarget.value)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.styleLabel')}</span>
              <span class="design-desc">{t('clock.styleDesc')}</span>
            </div>
            <Dropdown
              value={props.config.style ?? 'digital'}
              options={[
                { value: 'digital', label: t('clock.styleDigital'), icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
                { value: 'analog', label: t('clock.styleAnalog'), icon: () => <Watch size={13} strokeWidth={1.5} /> },
                { value: 'stacked', label: t('clock.styleStacked'), icon: () => <AlignVerticalJustifyCenter size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('style', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.alignLabel')}</span>
              <span class="design-desc">{t('clock.alignDesc')}</span>
            </div>
            <Dropdown
              value={props.config.align ?? 'center'}
              options={[
                { value: 'left', label: t('clock.alignLeft'), icon: () => <AlignLeft size={13} strokeWidth={1.5} /> },
                { value: 'center', label: t('clock.alignCenter'), icon: () => <AlignCenter size={13} strokeWidth={1.5} /> },
                { value: 'right', label: t('clock.alignRight'), icon: () => <AlignRight size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('align', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.formatLabel')}</span>
              <span class="design-desc">{t('clock.formatDesc')}</span>
            </div>
            <Dropdown
              value={props.config.format ?? '24h'}
              options={[
                { value: '24h', label: '24h', icon: () => <Clock size={13} strokeWidth={1.5} /> },
                { value: '12h', label: '12h', icon: () => <Clock12 size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('format', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.secondsLabel')}</span>
              <span class="design-desc">{t('clock.secondsDesc')}</span>
            </div>
            <ToggleSwitch
              checked={!!props.config.showSeconds}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showSeconds', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.dateLabel')}</span>
              <span class="design-desc">{t('clock.dateDesc')}</span>
            </div>
            <ToggleSwitch
              checked={!!props.config.showDate}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showDate', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clock.offsetLabel')}</span>
              <span class="design-desc">{t('clock.offsetDesc')}</span>
            </div>
            <Dropdown
              value={props.config.offsetMode ?? 'none'}
              options={offsetOptions()}
              onChange={(v) => update('offsetMode', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
