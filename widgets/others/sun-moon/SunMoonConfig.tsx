import { Show } from 'solid-js';
import { MapPin, Monitor } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import LocationSearch from '../../../src/components/LocationSearch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import { isSunPathMode, type SunPathMode } from './sunPath.ts';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface SunMoonConfigData {
  lat?: number | null;
  lon?: number | null;
  place?: string;
  sunPathMode?: string;
  showEvents?: boolean;
  showSunPath?: boolean;
  [k: string]: unknown;
}

interface SunMoonConfigProps {
  config: SunMoonConfigData;
  onChange: (c: SunMoonConfigData) => void;
}

// A function, not a const: the labels must re-read t() after a language change
// (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'location', label: t('sunMoon.locationTab'), icon: MapPin },
    { key: 'display', label: t('configTabs.display'), icon: Monitor },
  ];
}

export default function SunMoonConfig(props: SunMoonConfigProps) {
  // [S-13] Stateless: every control reads straight from props.config, so a
  // change made elsewhere (the widget's own context menu) shows immediately
  // instead of being reverted by a stale local copy.
  const mode = (): SunPathMode => {
    if (isSunPathMode(props.config.sunPathMode)) return props.config.sunPathMode;
    if (props.config.showSunPath === false) return 'hidden';
    return 'numbers';
  };
  const showEvents = (): boolean => props.config.showEvents !== false;

  function update(patch: Partial<SunMoonConfigData>) {
    props.onChange({ ...props.config, ...patch });
  }

  return (
    <ConfigTabs tabs={tabs()}>
      {(tab) => <>
        <Show when={tab === 'location'}>
          {/* The shared city picker, not a pair of coordinate steppers: nobody
              knows their own latitude, and this is the one copy of the
              geocoding search (see src/components/locationSearch.test.ts). */}
          <LocationSearch
            lat={props.config.lat ?? null}
            lon={props.config.lon ?? null}
            locationName={props.config.place ?? ''}
            onPick={(loc) => update({ lat: loc.lat, lon: loc.lon, place: loc.name })}
            onClear={() => update({ lat: null, lon: null, place: '' })}
          />
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('sunMoon.sunPath.label')}</span>
              <span class="design-desc">{t('sunMoon.sunPath.desc')}</span>
            </div>
            <Dropdown
              value={mode()}
              options={[
                { value: 'numbers', label: t('sunMoon.pathMode.numbers') },
                { value: 'plain', label: t('sunMoon.pathMode.plain') },
                { value: 'hidden', label: t('sunMoon.pathMode.hidden') },
              ]}
              onChange={(v) => update({ sunPathMode: v })}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('sunMoon.events.label')}</span>
              <span class="design-desc">{t('sunMoon.events.desc')}</span>
            </div>
            <ToggleSwitch
              checked={showEvents()}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update({ showEvents: v })}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
