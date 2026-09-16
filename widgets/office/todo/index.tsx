import { lazy } from 'solid-js';
import Todo from './Todo.tsx';
import { RefreshCw, List, AlignJustify, ListChecks, CheckSquare } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { TITLE_LINES_DEFAULT } from './titleLines.ts';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const TodoConfig = lazy(() => import('./TodoConfig.tsx'));

const todo: WidgetDefinition = {
  component: Todo,
  configComponent: TodoConfig,
  get label() { return t('widget.todo.label'); },
  get description() { return t('widget.todo.description'); },
  icon: () => <CheckSquare size={20} strokeWidth={1.5} />,
  category: 'Office',
  providers: ['Google', 'Microsoft'],
  defaultSize: { width: 220, height: 440 },
  popOut: true,
  defaultConfig: {
    storageMode: 'account',
    // [S-57] Which device list the widget shows in local mode. The tasks live in
    // the device-wide pool (`localListStore`), not in here — a list is a shared
    // thing you point at, not something a widget owns a copy of.
    localListId: '',
    // [S-54] Account-mode row order (task ids). Empty until the user drags a row
    // or adds a task, so an untouched widget keeps the backend's own sorting.
    taskOrder: [],
    accountIds: [],
    accountId: '',
    listId: '',
    listName: '',
    viewMode: 'list',
    titleLines: TITLE_LINES_DEFAULT,
    showCompleted: false,
    refreshInterval: 60,
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
    ]),
    { sep: true },
    {
      icon: ListChecks,
      label: config.showCompleted ? t('todo.hideCompleted') : t('todo.showCompletedCtx'),
      action: () => setConfig({ showCompleted: !config.showCompleted }),
    },
  ],
};

export default todo;
