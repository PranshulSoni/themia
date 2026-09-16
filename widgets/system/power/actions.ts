import { Power, RotateCw, Moon, Snowflake, Lock, LogOut, MonitorOff } from 'lucide-solid';
import type { MetricDef } from '../../../src/widgets/metricSet.ts';
import { resolveMetrics } from '../../../src/widgets/metricSet.ts';
import { t } from '../../../src/i18n.ts';

/** Mirrors `widget_power::PowerAction`. Kept as a closed union rather than a
 *  bare string so a typo is a compile error here and a deserialization error at
 *  the IPC boundary, instead of a no-op at runtime. */
export const POWER_ACTIONS = [
  'shutdown',
  'restart',
  'sleep',
  'hibernate',
  'lock',
  'signOut',
  'screenOff',
] as const;

export type PowerActionName = (typeof POWER_ACTIONS)[number];

const ACTION_SET: ReadonlySet<string> = new Set(POWER_ACTIONS);

export function isPowerAction(value: unknown): value is PowerActionName {
  return typeof value === 'string' && ACTION_SET.has(value);
}

/** The actions that end the session: they close every app, so unsaved work can
 *  be lost. Sleep, hibernate, lock and screen-off all come back to the same
 *  desktop, so an accidental click costs a keypress and nothing else — they are
 *  deliberately NOT behind the confirm step ([S-05]). */
const ENDS_SESSION: ReadonlySet<PowerActionName> = new Set<PowerActionName>([
  'shutdown',
  'restart',
  'signOut',
]);

export function needsConfirm(action: PowerActionName, confirmEnabled: boolean): boolean {
  return confirmEnabled && ENDS_SESSION.has(action);
}

export const ACTION_DEFS: MetricDef[] = [
  { key: 'shutdown',  label: () => t('power.action.shutdown'),  icon: Power,      defaultOn: true  },
  { key: 'restart',   label: () => t('power.action.restart'),   icon: RotateCw,   defaultOn: true  },
  { key: 'sleep',     label: () => t('power.action.sleep'),     icon: Moon,       defaultOn: true  },
  { key: 'lock',      label: () => t('power.action.lock'),      icon: Lock,       defaultOn: true  },
  { key: 'hibernate', label: () => t('power.action.hibernate'), icon: Snowflake,  defaultOn: false },
  { key: 'signOut',   label: () => t('power.action.signOut'),   icon: LogOut,     defaultOn: false },
  { key: 'screenOff', label: () => t('power.action.screenOff'), icon: MonitorOff, defaultOn: false },
];

/** Persisted config is a trust boundary: `visibleActions` comes back from
 *  localStorage and may hold keys from an older build. `resolveMetrics` drops
 *  anything not in `ACTION_DEFS`, and the guard narrows the survivors to the
 *  union so no unchecked string can reach `invoke` ([S-02]). */
export function visibleActions(raw: unknown): PowerActionName[] {
  return resolveMetrics(raw, ACTION_DEFS).visible.filter(isPowerAction);
}
