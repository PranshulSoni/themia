import { Type, Smartphone, Wifi, Battery, Signal } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'deviceName', label: () => 'Device name',  icon: Type,       defaultOn: true  },
  { key: 'type',       label: () => 'Device icon',  icon: Smartphone, defaultOn: true  },
  { key: 'connection', label: () => 'Connection',   icon: Wifi,       defaultOn: true  },
  { key: 'battery',    label: () => 'Battery',      icon: Battery,    defaultOn: true  },
  { key: 'rssi',       label: () => 'Signal (RSSI)', icon: Signal,    defaultOn: false },
];
