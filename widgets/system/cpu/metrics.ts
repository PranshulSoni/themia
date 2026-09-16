import { Activity, Gauge, Grid3x3, Thermometer, Fan, ListTree, Layers, Clock, TrendingUp } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'usage',       label: () => 'Usage',          icon: Activity,   defaultOn: true  },
  { key: 'frequency',   label: () => 'Frequency',      icon: Gauge,      defaultOn: true  },
  { key: 'graph',       label: () => 'Graph',          icon: TrendingUp, defaultOn: true  },
  { key: 'cores',       label: () => 'Per-core',       icon: Grid3x3,    defaultOn: false },
  { key: 'temperature', label: () => 'Temperature',    icon: Thermometer, defaultOn: false },
  { key: 'fan',         label: () => 'Fan speed',      icon: Fan,        defaultOn: false },
  { key: 'topProcs',    label: () => 'Top processes',  icon: ListTree,   defaultOn: false },
  { key: 'processes',   label: () => 'Process count',  icon: Layers,     defaultOn: false },
  { key: 'uptime',      label: () => 'Uptime',         icon: Clock,      defaultOn: false },
];
