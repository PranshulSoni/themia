import { createSignal, createMemo, onCleanup, For, Show } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { Power as PowerIcon } from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { showToast } from '../../../src/toastStore.ts';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { ACTION_DEFS, needsConfirm, visibleActions, type PowerActionName } from './actions.ts';
import './Power.css';

/** How long an armed tile stays armed. Long enough to read the prompt and
 *  click, short enough that a tile armed and forgotten cannot be triggered by a
 *  stray click later. */
const ARM_TIMEOUT_MS = 5000;

export interface PowerProps {
  headerMode?: HeaderMode;
  label?: string;
  visibleActions?: unknown;
  showLabels?: boolean;
  confirm?: boolean;
}

export default function Power(props: PowerProps) {
  // The tile awaiting its second click, or null. One tile at a time: arming a
  // second one disarms the first.
  const [armed, setArmed] = createSignal<PowerActionName | null>(null);
  let armTimer: number | null = null;

  const actions = createMemo(() => visibleActions(props.visibleActions));
  const defByKey = new Map(ACTION_DEFS.map((d) => [d.key, d]));
  const confirmEnabled = () => props.confirm !== false;
  const showLabels = () => props.showLabels !== false;

  function disarm() {
    if (armTimer != null) {
      clearTimeout(armTimer);
      armTimer = null;
    }
    setArmed(null);
  }
  onCleanup(disarm);

  function arm(action: PowerActionName) {
    disarm();
    setArmed(action);
    armTimer = window.setTimeout(() => {
      armTimer = null;
      setArmed(null);
    }, ARM_TIMEOUT_MS);
  }

  function run(action: PowerActionName) {
    const name = defByKey.get(action)?.label() ?? action;
    invoke('power_run', { action }).catch((err) => {
      // The machine stayed up, so the user needs to know why. Sleep resolves on
      // RESUME, so a rejection here is always a real failure, never the normal
      // path (power SPEC [S-06]).
      logger.error('power', `${action} failed: ${String(err)}`);
      showToast(t('power.failed', { action: name, reason: String(err) }), 'error', 6000);
    });
  }

  function onTile(action: PowerActionName) {
    if (needsConfirm(action, confirmEnabled()) && armed() !== action) {
      arm(action);
      return;
    }
    disarm();
    run(action);
  }

  return (
    <div class="power-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={PowerIcon}
        title={props.label || t('widget.power.label')}
      />
      {/* Moving the pointer off the widget disarms: the confirm step exists to
          catch a mis-click, and a tile left armed under the cursor would defeat
          that on the user's next visit. */}
      <div class="widget-body power-body" onMouseLeave={disarm}>
        <Show
          when={actions().length > 0}
          fallback={<div class="power-empty">{t('power.empty')}</div>}
        >
          <div class="power-grid" classList={{ 'no-labels': !showLabels() }}>
            <For each={actions()}>
              {(action) => {
                const def = defByKey.get(action);
                const isArmed = () => armed() === action;
                return (
                  <button
                    type="button"
                    class="power-tile"
                    classList={{ armed: isArmed() }}
                    title={isArmed() ? t('power.confirmHint', { action: def?.label() ?? action }) : def?.label()}
                    onClick={() => onTile(action)}
                  >
                    <Show when={def}>
                      <Dynamic component={def!.icon} size={20} strokeWidth={1.5} />
                    </Show>
                    <Show when={showLabels()}>
                      <span class="power-tile-label">
                        {isArmed() ? t('power.confirmTile') : def?.label()}
                      </span>
                    </Show>
                  </button>
                );
              }}
            </For>
          </div>
        </Show>
      </div>
    </div>
  );
}
