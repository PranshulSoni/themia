import { lazy } from 'solid-js';
import Email from './Email.tsx';
import {
  Mail, RefreshCw, List, AlignJustify, Table,
  Inbox, Send, FileEdit, Trash2, ShieldAlert,
  ArrowDownWideNarrow, ArrowUpNarrowWide,
} from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const EmailConfig = lazy(() => import('./EmailConfig.tsx'));

const email: WidgetDefinition = {
  component: Email,
  configComponent: EmailConfig,
  get label() { return t('widget.email.label'); },
  get description() { return t('widget.email.description'); },
  icon: () => <Mail size={20} strokeWidth={1.5} />,
  category: 'Office',
  providers: ['Google', 'Microsoft'],
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    folder: 'inbox',
    messageCount: 10,
    accountIds: [],
    accountId: '',
    viewMode: 'list',
    sortOrder: 'desc',
    previewLines: 2,
    rowColumns: ['from', 'subject', 'date'],
    listFields: ['subject', 'preview', 'account'],
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
      ['row',     t('ctxMenu.row'),     Table],
    ]),
    { sep: true },
    { header: true, label: t('ctxMenu.folder') },
    ...([
      ['inbox',        t('ctxMenu.inbox'),   Inbox],
      ['sentitems',    t('ctxMenu.sent'),    Send],
      ['drafts',       t('ctxMenu.drafts'),  FileEdit],
      ['deleteditems', t('ctxMenu.deleted'), Trash2],
      ['junkemail',    t('ctxMenu.junk'),    ShieldAlert],
    ] as const).map(([val, label, icon]) => ({
      icon, label,
      active: (config.folder ?? 'inbox') === val,
      action: () => setConfig({ folder: val }),
    })),
    { sep: true },
    { header: true, label: t('ctxMenu.sort') },
    {
      icon: ArrowDownWideNarrow, label: t('ctxMenu.newestFirst'),
      active: (config.sortOrder ?? 'desc') === 'desc',
      action: () => setConfig({ sortOrder: 'desc' }),
    },
    {
      icon: ArrowUpNarrowWide, label: t('ctxMenu.oldestFirst'),
      active: (config.sortOrder ?? 'desc') === 'asc',
      action: () => setConfig({ sortOrder: 'asc' }),
    },
  ],
};

export default email;
