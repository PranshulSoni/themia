import { lazy } from 'solid-js';
import Group from './Group.tsx';
import { Group as GroupIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GroupConfig = lazy(() => import('./GroupConfig.tsx'));

const group: WidgetDefinition = {
  component: Group,
  configComponent: GroupConfig,
  get label() { return t('widget.group.label'); },
  get description() { return t('widget.group.description'); },
  icon: () => <GroupIcon size={20} strokeWidth={1.5} />,
  category: 'Layout',
  // Big by default: an area is only useful once several widgets fit in it.
  defaultSize: { width: 620, height: 420 },
  defaultConfig: {
    // Areas start open — a group that hides its contents the moment it is
    // created would look like a widget that failed to render.
    collapsed: false,
    headerSide: 'top',
    borderMode: 'theme',
    title: '',
    icon: '',
    bgMode: 'default',
  },
  // The group paints its own title bar + area surface (group [S-04]/[S-06]),
  // so it needs no WidgetHeader — but it deliberately does NOT set `noChrome`:
  // that makes the tile's border transparent and leaves the group drawing its
  // own INSIDE the slot, one border-width in from where every other widget's
  // border sits. The tile's border IS the group's border ([S-15]).
  noHeader: true,
  // [S-32]/[S-13] The shared transparent-background switch. It replaces the
  // area's old "None" fill mode: clearing the wash alone left a frosted title
  // bar floating over a see-through region, and it is the ONE control that
  // takes the bar's fill with it. A group is also the widget most likely to
  // want it — an area that marks where things belong without painting a panel.
  canTransparent: true,
};

export default group;
