import { Activity, TrendingUp, Wifi, WifiOff, Sigma, BarChart3 } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'status',     label: () => 'Online status', icon: Wifi,        defaultOn: true  },
  { key: 'latency',    label: () => 'Latency',       icon: Activity,    defaultOn: true  },
  { key: 'address',    label: () => 'Address',       icon: Wifi,        defaultOn: true  },
  { key: 'graph',      label: () => 'Graph',         icon: TrendingUp,  defaultOn: true  },
  { key: 'jitter',     label: () => 'Jitter',        icon: Sigma,       defaultOn: false },
  { key: 'loss',       label: () => 'Packet loss',   icon: WifiOff,     defaultOn: false },
  { key: 'minAvgMax',  label: () => 'Min / Avg / Max', icon: BarChart3, defaultOn: false },
];
