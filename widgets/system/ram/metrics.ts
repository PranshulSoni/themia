import { Activity, TrendingUp, MemoryStick, ArrowLeftRight, ListTree, BarChart3 } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'usage',     label: () => 'Usage',         icon: Activity,       defaultOn: true  },
  { key: 'graph',     label: () => 'Graph',         icon: TrendingUp,     defaultOn: true  },
  { key: 'bar',       label: () => 'Usage bar',     icon: BarChart3,      defaultOn: true  },
  { key: 'available', label: () => 'Available',     icon: MemoryStick,    defaultOn: false },
  { key: 'swap',      label: () => 'Swap / pagefile', icon: ArrowLeftRight, defaultOn: false },
  { key: 'topProcs',  label: () => 'Top processes', icon: ListTree,       defaultOn: false },
];
