import { lazy } from 'solid-js';
import Calendar from './Calendar.tsx';
import { RefreshCw, CalendarDays, CalendarRange, Clock, ListChecks } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const CalendarConfig = lazy(() => import('./CalendarConfig.tsx'));

const calendar: WidgetDefinition = {
  component: Calendar,
  configComponent: CalendarConfig,
  get label() { return t('widget.calendar.label'); },
  get description() { return t('widget.calendar.description'); },
  icon: () => <CalendarDays size={20} strokeWidth={1.5} />,
  category: 'Office',
  providers: ['Google', 'Microsoft'],
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    accountIds: [],
    accountId: '',
    defaultView: 'month',
    showBirthdays: true,
    agendaCount: 20,
    weekStart: 'monday',
    calendars: {},
    calendarColors: {},
  },
  contextActions: (config, setConfig, viewActions) => [
    {
      icon: RefreshCw, label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
    { sep: true },
    ...viewActions(config, setConfig, 'defaultView', [
      ['month',  t('ctxMenu.month'),  CalendarDays],
      ['week',   t('ctxMenu.week'),   CalendarRange],
      ['day',    t('ctxMenu.day'),    Clock],
      ['agenda', t('ctxMenu.agenda'), ListChecks],
    ]),
  ],
};

export default calendar;
