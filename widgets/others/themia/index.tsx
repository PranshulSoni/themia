import Themia from './Themia.tsx';
import { Gem } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

const themia: WidgetDefinition = {
  component: Themia,
  // No user-configurable options: ConfigWindow renders the shared placeholder.
  get label() { return t('widget.themia.label'); },
  get description() { return t('widget.themia.description'); },
  icon: () => <Gem size={20} strokeWidth={1.5} />,
  category: 'Themia',
  noHeader: true,
  defaultSize: { width: 110, height: 110 },
  defaultConfig: {},
};

export default themia;
