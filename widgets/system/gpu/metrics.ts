import { Activity, TrendingUp, MemoryStick, Share2, Thermometer, Zap, Fan, Gauge, Film } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'utilization', label: () => 'Utilization', icon: Activity,    defaultOn: true  },
  { key: 'graph',       label: () => 'Graph',       icon: TrendingUp,  defaultOn: true  },
  { key: 'vram',        label: () => 'Dedicated VRAM', icon: MemoryStick, defaultOn: true },
  { key: 'shared',      label: () => 'Shared memory', icon: Share2,    defaultOn: true  },
  { key: 'temperature', label: () => 'Temperature', icon: Thermometer, defaultOn: false },
  { key: 'power',       label: () => 'Power draw',  icon: Zap,         defaultOn: false },
  { key: 'fan',         label: () => 'Fan speed',   icon: Fan,         defaultOn: false },
  { key: 'clocks',      label: () => 'Clocks',      icon: Gauge,       defaultOn: false },
  { key: 'encoder',     label: () => 'Encode/Decode', icon: Film,      defaultOn: false },
];
