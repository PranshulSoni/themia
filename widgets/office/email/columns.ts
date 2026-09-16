import { User, Type, Calendar, AlignLeft, AtSign } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';
import { t } from '../../../src/i18n.ts';

export const COLUMN_DEFS: MetricDef[] = [
  { key: 'from',    label: () => t('email.col.from'),    icon: User,      defaultOn: true  },
  { key: 'subject', label: () => t('email.col.subject'), icon: Type,      defaultOn: true  },
  { key: 'date',    label: () => t('email.col.date'),    icon: Calendar,  defaultOn: true  },
  { key: 'preview', label: () => t('email.col.preview'), icon: AlignLeft, defaultOn: false },
  { key: 'account', label: () => t('email.col.account'), icon: AtSign,    defaultOn: false },
];

// Fields the user can show/hide in list & compact view. From and time are always
// shown (primary row); subject/preview/account are optional. Preview only renders
// in list view; account tag is additionally gated on a multi-account feed.
export const LIST_FIELD_DEFS: MetricDef[] = [
  { key: 'subject', label: () => t('email.col.subject'), icon: Type,      defaultOn: true },
  { key: 'preview', label: () => t('email.col.preview'), icon: AlignLeft, defaultOn: true },
  { key: 'account', label: () => t('email.col.account'), icon: AtSign,    defaultOn: true },
];
