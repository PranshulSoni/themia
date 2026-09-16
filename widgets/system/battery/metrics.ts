import { Battery, BatteryCharging, TrendingUp, HeartPulse, RotateCw, Zap, Thermometer, Clock } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'percent',     label: () => 'Percent',     icon: Battery,         defaultOn: true  },
  { key: 'status',      label: () => 'Status',      icon: BatteryCharging, defaultOn: true  },
  { key: 'eta',         label: () => 'Time remaining', icon: Clock,        defaultOn: true  },
  { key: 'graph',       label: () => 'Graph',       icon: TrendingUp,      defaultOn: true  },
  { key: 'health',      label: () => 'Health',      icon: HeartPulse,      defaultOn: false },
  { key: 'cycles',      label: () => 'Cycle count', icon: RotateCw,        defaultOn: false },
  { key: 'voltage',     label: () => 'Voltage',     icon: Zap,             defaultOn: false },
  { key: 'temperature', label: () => 'Temperature', icon: Thermometer,     defaultOn: false },
];
