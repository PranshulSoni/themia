import { lazy } from 'solid-js';
import ColorSwatch from './ColorSwatch.tsx';
import { Palette } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ColorSwatchConfig = lazy(() => import('./ColorSwatchConfig.tsx'));

const colorSwatch: WidgetDefinition = {
  component: ColorSwatch,
  configComponent: ColorSwatchConfig,
  get label() { return t('widget.colorSwatch.label'); },
  get description() { return t('widget.colorSwatch.description'); },
  icon: () => <Palette size={20} strokeWidth={1.5} />,
  category: 'Others',
  noHeader: true,
  defaultSize: { width: 110, height: 110 },
  defaultConfig: {
    mode: 'solid',
    color: '#60cdff',
    alpha: 1,
    colorA: '#60cdff',
    alphaA: 1,
    colorB: '#34d399',
    alphaB: 1,
    angle: 135,
    layer: 'front',
  },
};

export default colorSwatch;
