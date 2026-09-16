import { open } from '@tauri-apps/plugin-dialog';
import { invoke } from '../../../src/ipc.ts';
import { Show } from 'solid-js';
import { LayoutGrid, Play, Shuffle, LayoutDashboard, Maximize2, Minimize2, Database, Monitor, RefreshCw, Folder, FileImage, Globe } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import '../../../src/styles/config-base.css';
import './GalleryConfig.css';
import { t } from '../../../src/i18n.ts';

// Closed set, never a loose string (CLAUDE.md trust-boundary rule).
type GallerySource = 'folder' | 'file' | 'bing';

interface GalleryConfigData {
  source?: GallerySource;
  path?: string;
  filePath?: string;
  recursive?: boolean;
  showCaption?: boolean;
  mode?: string;
  fit?: string;
  maxImages?: number;
  slideshowInterval?: number;
  refreshInterval?: number;
  refreshNonce?: number;
}

interface GalleryConfigProps {
  config: GalleryConfigData;
  onChange: (c: GalleryConfigData) => void;
}

const IMAGE_EXTS = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'svg', 'avif'];

export default function GalleryConfig(props: GalleryConfigProps) {
  const config = () => props.config;
  const source = (): GallerySource => config().source ?? 'folder';

  function update<K extends keyof GalleryConfigData>(key: K, value: GalleryConfigData[K]) {
    props.onChange({ ...config(), [key]: value });
  }

  async function pickFolder() {
    const selected = await open({ directory: true, multiple: false });
    if (selected) update('path', selected as string);
    await invoke('push_windows_to_desktop');
  }

  async function pickFile() {
    const selected = await open({
      multiple: false,
      filters: [{ name: 'Images', extensions: IMAGE_EXTS }],
    });
    if (selected) update('filePath', selected as string);
    await invoke('push_windows_to_desktop');
  }

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('gallery.sourceLabel')}</span>
              <span class="design-desc">{t('gallery.sourceDesc')}</span>
            </div>
            <Dropdown
              value={source()}
              options={[
                { value: 'folder', label: t('gallery.sourceFolder'), icon: () => <Folder size={13} strokeWidth={1.5} /> },
                { value: 'file',   label: t('gallery.sourceFile'), icon: () => <FileImage size={13} strokeWidth={1.5} /> },
                { value: 'bing',   label: t('gallery.sourceBing'), icon: () => <Globe size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('source', v as GallerySource)}
            />
          </div>

          <Show when={source() === 'folder'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.folderLabel')}</span>
                <span class="design-desc">{t('gallery.folderDesc')}</span>
              </div>
              <div class="gal-folder-pick">
                <span class="gal-folder-path">{config().path || t('widgetUi.noneSelected')}</span>
                <button onClick={pickFolder}>{t('widgetUi.browse')}</button>
              </div>
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.recursiveLabel')}</span>
                <span class="design-desc">{t('gallery.recursiveDesc')}</span>
              </div>
              <ToggleSwitch
                checked={config().recursive ?? false}
                onLabel={t('toggle.yes')}
                offLabel={t('toggle.no')}
                onChange={(v) => update('recursive', v)}
              />
            </div>
          </Show>

          <Show when={source() === 'file'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.imageLabel')}</span>
                <span class="design-desc">{t('gallery.imageDesc')}</span>
              </div>
              <div class="gal-folder-pick">
                <span class="gal-folder-path">{config().filePath || t('widgetUi.noneSelected')}</span>
                <button onClick={pickFile}>{t('widgetUi.browse')}</button>
              </div>
            </div>
          </Show>

          <Show when={source() === 'bing'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.captionLabel')}</span>
                <span class="design-desc">{t('gallery.captionDesc')}</span>
              </div>
              <ToggleSwitch
                checked={config().showCaption ?? true}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update('showCaption', v)}
              />
            </div>
          </Show>
        </Show>

        <Show when={tab === 'display'}>
          {/* Layout: only the multi-image folder source has arrangement modes. */}
          <Show when={source() === 'folder'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.layoutLabel')}</span>
                <span class="design-desc">{t('gallery.layoutDesc')}</span>
              </div>
              <Dropdown
                value={config().mode ?? 'grid'}
                options={[
                  { value: 'grid',      label: t('gallery.modeGrid'), icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
                  { value: 'slideshow', label: t('gallery.modeSlideshow'), icon: () => <Play size={13} strokeWidth={1.5} /> },
                  { value: 'scatter',   label: t('gallery.modeScatter'), icon: () => <Shuffle size={13} strokeWidth={1.5} /> },
                  { value: 'mosaic',    label: t('gallery.modeMosaic'), icon: () => <LayoutDashboard size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update('mode', v)}
              />
            </div>
          </Show>

          {/* Image fit applies to every source: folder grid/slideshow/scatter, Single image,
              and Photo of the Day. Only folder+mosaic hides it (mosaic tiles are always cover).
              Sits under Layout so folder users see arrangement first, then fit. */}
          <Show when={source() !== 'folder' || config().mode !== 'mosaic'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.fitLabel')}</span>
                <span class="design-desc">{t('gallery.fitDesc')}</span>
              </div>
              <Dropdown
                value={config().fit ?? 'cover'}
                options={[
                  { value: 'cover',   label: t('gallery.fitCover'), icon: () => <Maximize2 size={13} strokeWidth={1.5} /> },
                  { value: 'contain', label: t('gallery.fitContain'), icon: () => <Minimize2 size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update('fit', v)}
              />
            </div>
          </Show>

          {/* Count / interval only apply when the source is a multi-image folder. */}
          <Show when={source() === 'folder'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('gallery.maxImagesLabel')}</span>
                <span class="design-desc">{t('gallery.maxImagesDesc')}</span>
              </div>
              <NumberStepper
                value={config().maxImages ?? 12}
                min={1}
                max={20}
                step={1}
                onChange={(v) => update('maxImages', v)}
              />
            </div>

            <Show when={config().mode === 'slideshow'}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('gallery.slideshowInterval.label')}</span>
                  <span class="design-desc">{t('gallery.slideshowInterval.desc')}</span>
                </div>
                {/* This row used to hand-build design-range-wrap + design-val-wrap
                    + a raw number input, while the SAME file drove maxImages and
                    refreshInterval with NumberStepper. Two "interval in seconds"
                    fields, two different controls, one panel.
                    max 3600 is new: the raw input was unbounded, but an hour
                    between slides is already past any real use and the shared
                    control needs a ceiling. */}
                <NumberStepper
                  value={config().slideshowInterval ?? 10}
                  min={1}
                  max={3600}
                  step={1}
                  unit="s"
                  onChange={(v) => update('slideshowInterval', v)}
                />
              </div>
            </Show>
          </Show>
        </Show>

        <Show when={tab === 'refresh'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.refreshLabel')}</span>
              <span class="design-desc">{t('gallery.refreshDesc')}</span>
            </div>
            <NumberStepper
              value={config().refreshInterval ?? 60}
              min={5}
              max={240}
              step={5}
              unit="m"
              onChange={(v) => update('refreshInterval', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
