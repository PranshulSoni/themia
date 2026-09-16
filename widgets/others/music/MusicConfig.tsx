import { Show } from 'solid-js';
import { Monitor, RefreshCw, Eye, EyeOff, CirclePause } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { t } from '../../../src/i18n.ts';
import { resolveHideWhenIdle, type HideWhenIdle } from './hideWhenIdle.ts';

export interface MusicConfigData {
  showAlbumArt?: boolean;
  showSpectrum?: boolean;
  hideWhenIdle?: HideWhenIdle;
  pollInterval?: number;
}

export interface MusicConfigProps {
  config: MusicConfigData;
  onChange: (config: MusicConfigData) => void;
  globals?: Record<string, unknown>;
}

export default function MusicConfig(props: MusicConfigProps): JSX.Element {
  const config = (): MusicConfigData => props.config ?? {};
  function update<K extends keyof MusicConfigData>(k: K, v: MusicConfigData[K]) { props.onChange({ ...config(), [k]: v }); }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('music.albumArtLabel')}</span>
              <span class="design-desc">{t('music.albumArtDesc')}</span>
            </div>
            <ToggleSwitch
              checked={!!(config().showAlbumArt ?? true)}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showAlbumArt', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('music.spectrumLabel')}</span>
              <span class="design-desc">{t('music.spectrumDesc')}</span>
            </div>
            <ToggleSwitch
              checked={!!config().showSpectrum}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showSpectrum', v)}
            />
          </div>

          {/* [S-20] Three-way, not a toggle: "nothing playing" has two honest
              readings. `noSession` keeps a paused track on screen so it can be
              resumed from the widget; `notPlaying` also clears the card that
              Spotify & co. leave sitting there for hours after you stop. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('music.hideWhenIdle.label')}</span>
              <span class="design-desc">{t('music.hideWhenIdle.desc')}</span>
            </div>
            <Dropdown
              value={resolveHideWhenIdle(config().hideWhenIdle)}
              options={[
                { value: 'never',      label: t('music.hideWhenIdle.never'),      icon: () => <Eye size={13} strokeWidth={1.5} /> },
                { value: 'noSession',  label: t('music.hideWhenIdle.noSession'),  icon: () => <EyeOff size={13} strokeWidth={1.5} /> },
                { value: 'notPlaying', label: t('music.hideWhenIdle.notPlaying'), icon: () => <CirclePause size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('hideWhenIdle', resolveHideWhenIdle(v))}
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
              value={config().pollInterval ?? 1}
              min={1}
              max={30}
              step={1}
              unit="s"
              onChange={(v) => update('pollInterval', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
