import { ArrowDown, ArrowUp, TrendingUp, Layers, Database, Globe } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';

export const METRIC_DEFS: MetricDef[] = [
  { key: 'rx',           label: () => 'Download (RX)', icon: ArrowDown,   defaultOn: true  },
  { key: 'tx',           label: () => 'Upload (TX)',   icon: ArrowUp,     defaultOn: true  },
  { key: 'graph',        label: () => 'Graph',         icon: TrendingUp,  defaultOn: true  },
  { key: 'perInterface', label: () => 'Per-interface', icon: Layers,      defaultOn: false },
  { key: 'session',      label: () => 'Session totals', icon: Database,   defaultOn: false },
  { key: 'publicIp',     label: () => 'Public IP',     icon: Globe,       defaultOn: false },
];
