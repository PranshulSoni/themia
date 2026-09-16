import { HardDrive, Database, TrendingUp, Gauge, Layers } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'space',    label: () => 'Space usage',       icon: HardDrive,  defaultOn: true  },
  { key: 'ioBytes',  label: () => 'Read/Write speed',  icon: Database,   defaultOn: true  },
  { key: 'graph',    label: () => 'Graph',             icon: TrendingUp, defaultOn: true  },
  { key: 'iops',     label: () => 'IOPS',              icon: Gauge,      defaultOn: false },
  { key: 'perDisk',  label: () => 'Per-disk I/O',      icon: Layers,     defaultOn: false },
];
