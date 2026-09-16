import { Globe, AlignLeft, Calendar } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';
import { t } from '../../../src/i18n.ts';

// Per-item fields the user can show/hide via the "Fields" multi-select on the
// Display tab (mirrors the email widget's field picker). The item title is the
// primary line and is always shown; source / preview / date are optional.
export const RSS_FIELD_DEFS: MetricDef[] = [
  { key: 'source',  label: () => t('rss.field.source'),  icon: Globe,     defaultOn: true },
  { key: 'preview', label: () => t('rss.field.preview'), icon: AlignLeft, defaultOn: true },
  { key: 'date',    label: () => t('rss.field.date'),    icon: Calendar,  defaultOn: true },
];
