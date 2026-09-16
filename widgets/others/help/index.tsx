import Help from './Help.tsx';
import { LifeBuoy } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

const help: WidgetDefinition = {
  component: Help,
  // No user-configurable options: ConfigWindow renders the shared placeholder.
  get label() { return t('widget.help.label'); },
  get description() { return t('widget.help.description'); },
  icon: () => <LifeBuoy size={20} strokeWidth={1.5} />,
  category: 'Themia',
  defaultSize: { width: 440, height: 440 },
  defaultConfig: {},
};

export default help;
