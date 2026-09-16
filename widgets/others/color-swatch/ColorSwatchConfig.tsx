import { Show } from 'solid-js';
import { Paintbrush, Blend, BringToFront, SendToBack } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ColorPicker from '../../../src/ColorPicker.tsx';
import '../../../src/styles/config-base.css';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { t } from '../../../src/i18n.ts';

interface ColorSwatchConfigData {
  mode?: string;
  layer?: string;
  color?: string;
  alpha?: number;
  colorA?: string;
  alphaA?: number;
  colorB?: string;
  alphaB?: number;
  angle?: number;
}

interface ColorSwatchConfigProps {
  config: ColorSwatchConfigData;
  onChange: (c: ColorSwatchConfigData) => void;
}

export default function ColorSwatchConfig(props: ColorSwatchConfigProps) {
  function update<K extends keyof ColorSwatchConfigData>(key: K, value: ColorSwatchConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  const mode = () => props.config.mode || 'solid';

  return (
    <ConfigTabs tabs={[
      { key: 'color', label: t('colorSwatch.tabColor'), icon: Paintbrush },
    ]}>
      {(tab) => <>
        <Show when={tab === 'color'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('colorSwatch.modeLabel')}</span>
              <span class="design-desc">{t('colorSwatch.modeDesc')}</span>
            </div>
            <Dropdown
              value={mode()}
              options={[
                { value: 'solid',    label: t('colorSwatch.modeSolid'),    icon: () => <Paintbrush size={13} strokeWidth={1.5} /> },
                { value: 'gradient', label: t('colorSwatch.modeGradient'), icon: () => <Blend size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('mode', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('colorSwatch.layerLabel')}</span>
              <span class="design-desc">{t('colorSwatch.layerDesc')}</span>
            </div>
            <Dropdown
              value={props.config.layer || 'front'}
              options={[
                { value: 'front',  label: t('colorSwatch.layerFront'), icon: () => <BringToFront size={13} strokeWidth={1.5} /> },
                { value: 'behind', label: t('colorSwatch.layerBehind'),   icon: () => <SendToBack size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('layer', v)}
            />
          </div>

          <Show when={mode() === 'solid'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('colorSwatch.colorLabel')}</span>
              </div>
              <ColorPicker
                value={props.config.color || '#60cdff'}
                onChange={(hex) => update('color', hex)}
                alpha={props.config.alpha ?? 1}
                onAlphaChange={(a) => update('alpha', a)}
              />
            </div>
          </Show>

          <Show when={mode() === 'gradient'}>
            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('colorSwatch.startColorLabel')}</span>
              </div>
              <ColorPicker
                value={props.config.colorA || '#60cdff'}
                onChange={(hex) => update('colorA', hex)}
                alpha={props.config.alphaA ?? 1}
                onAlphaChange={(a) => update('alphaA', a)}
              />
            </div>

            <div class="design-row stacked">
              <div class="design-left">
                <span class="design-label">{t('colorSwatch.endColorLabel')}</span>
              </div>
              <ColorPicker
                value={props.config.colorB || '#34d399'}
                onChange={(hex) => update('colorB', hex)}
                alpha={props.config.alphaB ?? 1}
                onAlphaChange={(a) => update('alphaB', a)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('colorSwatch.angle.label')}</span>
                <span class="design-desc">{t('colorSwatch.angle.desc')}</span>
              </div>
              <NumberStepper
                value={props.config.angle ?? 135}
                min={0}
                max={360}
                step={15}
                unit="°"
                onChange={(v) => update('angle', v)}
              />
            </div>
          </Show>
        </Show>
      </>}
    </ConfigTabs>
  );
}
