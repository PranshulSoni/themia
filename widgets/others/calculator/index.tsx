import { lazy } from 'solid-js';
import Calculator from './Calculator.tsx';
import { Calculator as CalculatorIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const CalculatorConfig = lazy(() => import('./CalculatorConfig.tsx'));

const calculator: WidgetDefinition = {
  component: Calculator,
  configComponent: CalculatorConfig,
  get label() { return t('widget.calculator.label'); },
  get description() { return t('widget.calculator.description'); },
  icon: () => <CalculatorIcon size={20} strokeWidth={1.5} />,
  category: 'Others',
  defaultHeaderMode: 'none',
  defaultSize: { width: 220, height: 330 },
  defaultConfig: {
    decimalPlaces: 4,
  },
};

export default calculator;
