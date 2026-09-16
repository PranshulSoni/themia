import { createSignal, onMount, onCleanup, Show } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { Globe, RefreshCw, Crosshair, X } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import { listen } from '@tauri-apps/api/event';
import { invoke } from '../../../src/ipc.ts';
import { MIN_REFRESH_MINUTES, MAX_REFRESH_MINUTES, DEFAULT_REFRESH_MINUTES } from './webElement.ts';

interface PickedEvent {
  widgetId: string;
  url: string;
  selector: string;
  pickedLabel: string;
}
import '../../../src/styles/config-base.css';
import './WebElementConfig.css';

interface WebElementConfigData {
  url?: string;
  selector?: string;
  label?: string;
  refreshMinutes?: number;
  /** Cosmetic: a friendly description of the picked element (config UI only). */
  pickedLabel?: string;
}

interface WebElementConfigProps {
  config: WebElementConfigData;
  onChange: (c: WebElementConfigData) => void;
  /** Widget instance id: routes the picker result back to THIS config. */
  _widgetId?: string;
}

// A function, not a const: the labels must re-read t() after a
// language change (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'source', label: t('configTabs.source'), icon: Globe },
    { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
  ];
}

function errMsg(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

export default function WebElementConfig(props: WebElementConfigProps) {
  const [pickError, setPickError] = createSignal<string | null>(null);

  function update<K extends keyof WebElementConfigData>(key: K, value: WebElementConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  // The URL input is controlled directly by props.config.url ([S-13]: config
  // panels keep no local mirror of props.config). The config WRITE is debounced
  // because props.config.url is the display widget's fetch cache key — an
  // un-debounced write fires one serialized off-screen WebView2 page render per
  // keystroke. Solid only re-sets the input's DOM value when props.config.url
  // actually changes, so the typed text stays put during the debounce window,
  // and the picker writing a resolved url back updates it normally.
  let urlTimer: ReturnType<typeof setTimeout> | null = null;
  onCleanup(() => { if (urlTimer) clearTimeout(urlTimer); });

  function onUrlInput(v: string) {
    if (urlTimer) clearTimeout(urlTimer);
    urlTimer = setTimeout(() => update('url', v), 400);
  }

  const url = () => props.config.url ?? '';
  const selector = () => props.config.selector ?? '';
  const pickedLabel = () => props.config.pickedLabel ?? '';

  // The picker runs in its OWN live webview window; on pick the Rust host emits
  // `web-element-picked` to this config window, keyed by widget id so only THIS
  // config instance applies it.
  onMount(() => {
    const un = listen<PickedEvent>('web-element-picked', (e) => {
      const m = e.payload;
      if (m.widgetId !== (props._widgetId ?? '')) return;
      props.onChange({
        ...props.config,
        url: m.url,
        selector: m.selector,
        pickedLabel: m.pickedLabel,
      });
    });
    onCleanup(() => { un.then((fn) => fn()).catch(() => {}); });
  });

  async function openPicker() {
    const u = url().trim();
    if (!u) return;
    setPickError(null);
    try {
      await invoke('open_web_element_picker_window', {
        parentLabel: getCurrentWindow().label,
        targetUrl: u,
        widgetId: props._widgetId ?? '',
      });
    } catch (e) {
      setPickError(errMsg(e));
    }
  }

  return (
    <div class="webelement-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('webElement.urlLabel')}</span>
                <span class="design-desc">{t('webElement.urlDesc')}</span>
              </div>
              <input
                class="text-input"
                type="url"
                inputmode="url"
                placeholder="https://example.com"
                value={url()}
                onInput={(e) => onUrlInput(e.currentTarget.value)}
              />
            </div>

            <button class="btn-add webelement-pick-btn" disabled={!url().trim()} onClick={openPicker}>
              <Crosshair size={15} strokeWidth={1.5} />
              {selector() ? t('webElement.repick') : t('webElement.pick')}
            </button>

            <Show when={selector()}>
              <div class="design-row webelement-selected">
                <div class="design-left">
                  <span class="design-label">{t('webElement.selected')}</span>
                  <span class="design-desc" title={selector()}>{pickedLabel() || selector()}</span>
                </div>
                <button
                  class="btn-reset"
                  title={t('webElement.clear')}
                  onClick={() => props.onChange({ ...props.config, selector: '', pickedLabel: '' })}
                >
                  <X size={14} strokeWidth={1.5} />
                </button>
              </div>
            </Show>

            <Show when={pickError()}>
              <div class="add-error">{pickError()}</div>
            </Show>
          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('webElement.refreshLabel')}</span>
                <span class="design-desc">{t('webElement.refreshDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshMinutes ?? DEFAULT_REFRESH_MINUTES}
                min={MIN_REFRESH_MINUTES}
                max={MAX_REFRESH_MINUTES}
                step={5}
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
