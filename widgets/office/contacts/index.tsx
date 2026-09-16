import { lazy } from 'solid-js';
import Contacts from './Contacts.tsx';
import { Users, List, AlignJustify, Table, LayoutGrid, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ContactsConfig = lazy(() => import('./ContactsConfig.tsx'));

const contacts: WidgetDefinition = {
  component: Contacts,
  configComponent: ContactsConfig,
  get label() { return t('widget.contacts.label'); },
  get description() { return t('widget.contacts.description'); },
  icon: () => <Users size={20} strokeWidth={1.5} />,
  category: 'Office',
  providers: ['Google', 'Microsoft'],
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    accountIds: [],
    accountId: '',
    count: 20,
    defaultSearch: '',
    viewMode: 'list',
    rowColumns: ['name', 'email', 'phone', 'company', 'title'],
  },
  contextActions: (config, setConfig, viewActions) => [
    {
      icon: RefreshCw, label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
    { sep: true },
    ...viewActions(config, setConfig, 'viewMode', [
      ['list',    t('ctxMenu.list'),    List],
      ['compact', t('ctxMenu.compact'), AlignJustify],
      ['details', t('ctxMenu.details'), Table],
      ['grid',    t('ctxMenu.grid'),    LayoutGrid],
    ]),
  ],
};

export default contacts;
