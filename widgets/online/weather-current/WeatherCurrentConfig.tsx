import { Show } from 'solid-js';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import LocationSearch from '../../../src/components/LocationSearch.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface WeatherCurrentConfigData {
  locationName?: string;
  lat?: number | null;
  lon?: number | null;
  unit?: string;
  refreshMinutes?: number;
  hideLocation?: boolean;
}

interface WeatherCurrentConfigProps {
  config: WeatherCurrentConfigData;
  onChange: (c: WeatherCurrentConfigData) => void;
}

// A function, not a const: the labels must re-read t() after a
// language change (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'source', label: t('configTabs.source'), icon: Database },
    { key: 'display', label: t('configTabs.display'), icon: Monitor },
    { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
  ];
}

export default function WeatherCurrentConfig(props: WeatherCurrentConfigProps) {
  // [S-13] Stateless: derive every control's value straight from props.config,
  // so an external change shows immediately and is never reverted by a stale
  // local copy.
  const locationName = (): string => props.config.locationName ?? '';
  const lat = (): number | null => props.config.lat ?? null;
  const lon = (): number | null => props.config.lon ?? null;
  const unit = (): string => props.config.unit ?? 'celsius';
  const refreshMinutes = (): number => props.config.refreshMinutes ?? 15;
  const hideLocation = (): boolean => !!props.config.hideLocation;

  // Write-through: patch props.config directly, so the panel keeps no copy.
  function update(patch: Partial<WeatherCurrentConfigData>) {
    props.onChange({ ...props.config, ...patch });
  }

  return (
    <div class="weather-current-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <LocationSearch
              lat={lat()}
              lon={lon()}
              locationName={locationName()}
              onPick={(loc) => update({ lat: loc.lat, lon: loc.lon, locationName: loc.name })}
              onClear={() => update({ lat: null, lon: null, locationName: '' })}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('weather.unitLabel')}</span>
                <span class="design-desc">{t('weather.unitDesc')}</span>
              </div>
              <Dropdown
                value={unit()}
                options={[
                  { value: 'celsius',    label: '°C' },
                  { value: 'fahrenheit', label: '°F' },
                ]}
                onChange={(v) => update({ unit: v })}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('weather.locationNameLabel')}</span>
                <span class="design-desc">{t('weather.locationNameDesc')}</span>
              </div>
              <ToggleSwitch
                checked={!hideLocation()}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update({ hideLocation: !v })}
              />
            </div>

          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('widgetUi.refreshLabel')}</span>
                <span class="design-desc">{t('widgetUi.refreshDescUpdates')}</span>
              </div>
              <NumberStepper
                value={refreshMinutes()}
                min={5}
                max={120}
                step={5}
                unit="m"
                onChange={(v) => update({ refreshMinutes: v })}
              />
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
