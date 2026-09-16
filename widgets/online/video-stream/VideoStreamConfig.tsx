import { Show, onCleanup } from 'solid-js';
import { Video, Monitor, RefreshCw } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown, { type DropdownOption } from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { t } from '../../../src/i18n.ts';
import {
  isStreamUrl, DEFAULT_FIT, DEFAULT_ROTATION,
  LIVE_REFRESH_SECONDS, MAX_REFRESH_SECONDS,
  type StreamFit, type StreamRotation,
} from './videoStream.ts';
import '../../../src/styles/config-base.css';

interface VideoStreamConfigData {
  url?: string;
  fit?: StreamFit;
  rotation?: StreamRotation;
  refreshSeconds?: number;
}

interface VideoStreamConfigProps {
  config: VideoStreamConfigData;
  onChange: (c: VideoStreamConfigData) => void;
}

// Functions, not consts: the labels must re-read t() after a language change
// (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'source', label: t('configTabs.source'), icon: Video },
    { key: 'display', label: t('configTabs.display'), icon: Monitor },
    { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
  ];
}

function fitOptions(): DropdownOption[] {
  return [
    { value: 'contain', label: t('videoStream.fitContain') },
    { value: 'cover', label: t('videoStream.fitCover') },
    { value: 'stretch', label: t('videoStream.fitStretch') },
  ];
}

const ROTATION_OPTIONS: DropdownOption[] = [
  { value: '0', label: '0°' },
  { value: '90', label: '90°' },
  { value: '180', label: '180°' },
  { value: '270', label: '270°' },
];

export default function VideoStreamConfig(props: VideoStreamConfigProps) {
  function update<K extends keyof VideoStreamConfigData>(key: K, value: VideoStreamConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  // The input is controlled straight off props.config.url ([S-13]: config panels
  // keep no local mirror). Only the WRITE is debounced, because props.config.url
  // is the display widget's connection key -- an un-debounced write would open
  // and abort one camera connection per keystroke.
  let urlTimer: ReturnType<typeof setTimeout> | null = null;
  onCleanup(() => { if (urlTimer) clearTimeout(urlTimer); });

  function onUrlInput(v: string) {
    if (urlTimer) clearTimeout(urlTimer);
    urlTimer = setTimeout(() => update('url', v), 400);
  }

  const url = () => props.config.url ?? '';
  const urlRejected = () => url().trim() !== '' && !isStreamUrl(url());

  return (
    <div class="videostream-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('videoStream.urlLabel')}</span>
                <span class="design-desc">{t('videoStream.urlDesc')}</span>
              </div>
              <input
                class="text-input"
                type="url"
                inputmode="url"
                placeholder="http://192.168.1.50:8080/?action=stream"
                value={url()}
                onInput={(e) => onUrlInput(e.currentTarget.value)}
              />
            </div>

            <Show when={urlRejected()}>
              <div class="add-error">{t('videoStream.urlInvalid')}</div>
            </Show>
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('videoStream.fitLabel')}</span>
                <span class="design-desc">{t('videoStream.fitDesc')}</span>
              </div>
              <Dropdown
                value={props.config.fit ?? DEFAULT_FIT}
                options={fitOptions()}
                onChange={(v) => update('fit', v as StreamFit)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('videoStream.rotationLabel')}</span>
                <span class="design-desc">{t('videoStream.rotationDesc')}</span>
              </div>
              <Dropdown
                value={String(props.config.rotation ?? DEFAULT_ROTATION)}
                options={ROTATION_OPTIONS}
                onChange={(v) => update('rotation', Number(v) as StreamRotation)}
              />
            </div>
          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('videoStream.refreshLabel')}</span>
                <span class="design-desc">{t('videoStream.refreshDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshSeconds ?? LIVE_REFRESH_SECONDS}
                min={LIVE_REFRESH_SECONDS}
                max={MAX_REFRESH_SECONDS}
                step={1}
                unit="s"
                onChange={(v) => update('refreshSeconds', v)}
              />
            </div>

            <div class="field-hint">
              {(props.config.refreshSeconds ?? LIVE_REFRESH_SECONDS) > 0
                ? t('videoStream.refreshHintInterval')
                : t('videoStream.refreshHintLive')}
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
