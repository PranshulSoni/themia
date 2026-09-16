import { Pencil } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ColorPicker from '../../../src/ColorPicker.tsx';
import '../../../src/styles/config-base.css';
import { SIZES } from './Scratchpad.tsx';
import { t } from '../../../src/i18n.ts';

interface ScratchpadConfigData {
  strokeColor?: string;
  penSize?: number;
  [k: string]: unknown;
}

interface ScratchpadConfigProps {
  config: ScratchpadConfigData;
  onChange: (c: ScratchpadConfigData) => void;
}

export default function ScratchpadConfig(props: ScratchpadConfigProps) {
  function update<K extends keyof ScratchpadConfigData>(key: K, value: ScratchpadConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  const color = () => props.config.strokeColor || '#60cdff';
  const size = () => props.config.penSize ?? SIZES[1];

  return (
    <ConfigTabs tabs={[{ key: 'pen', label: t('scratchpad.penTab'), icon: Pencil }]}>
      {() => (
        <>
          <div class="design-row stacked">
            <div class="design-left">
              <span class="design-label">{t('scratchpad.penColorLabel')}</span>
              <span class="design-desc">{t('scratchpad.penColorDesc')}</span>
            </div>
            <ColorPicker
              value={color()}
              onChange={(hex) => update('strokeColor', hex)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('scratchpad.penSizeLabel')}</span>
              <span class="design-desc">{t('scratchpad.penSizeDesc')}</span>
            </div>
            <Dropdown
              value={String(size())}
              options={SIZES.map((s) => ({ value: String(s), label: `${s}px` }))}
              onChange={(v) => update('penSize', Number(v))}
            />
          </div>
        </>
      )}
    </ConfigTabs>
  );
}
