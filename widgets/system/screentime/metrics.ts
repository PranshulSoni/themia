import { Sigma, Repeat, Percent } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';
import { t } from '../../../src/i18n.ts';

// [S-57] only the two EXTRA figures are metrics. What the body is made of is
// controlled by the settings that shape it: the drawing by `displayStyle`
// (`none` = no chart) and the app list by `maxApps` (0 = no apps) — [S-53].
export const METRIC_DEFS: MetricDef[] = [
  { key: 'average',  label: () => t('screentime.metric.average'),  icon: Sigma,  defaultOn: true },
  { key: 'switches', label: () => t('screentime.metric.switches'), icon: Repeat, defaultOn: false },
  // [S-58] each app's share of the period's total.
  { key: 'percent',  label: () => t('screentime.metric.percent'),  icon: Percent, defaultOn: false },
];
