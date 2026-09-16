import { Index, Show } from 'solid-js';
import { Database, ChevronUp, ChevronDown, Plus, X as XIcon } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import '../../../src/styles/config-base.css';
import {
  newTimerId, isoToLocalInput, localInputToIso, FORMAT_LABELS,
  type Timer, type DisplayFormat,
} from './timers.ts';
import './CountdownConfig.css';
import { t } from '../../../src/i18n.ts';

interface CountdownConfigData {
  timers: Timer[];
}

interface CountdownConfigProps {
  config: CountdownConfigData;
  onChange: (c: CountdownConfigData) => void;
}

const FORMATS: DisplayFormat[] = ['auto', 'days', 'dhm', 'dhms'];

export default function CountdownConfig(props: CountdownConfigProps) {
  const items = () => props.config.timers ?? [];

  function writeItems(next: Timer[]) {
    props.onChange({ ...props.config, timers: next });
  }

  function addTimer() {
    const target = new Date(Date.now() + 24 * 60 * 60 * 1000);
    target.setSeconds(0, 0);
    writeItems([
      ...items(),
      { id: newTimerId(), label: '', target: target.toISOString(), format: 'auto' },
    ]);
  }

  function updateTimer(id: string, patch: Partial<Timer>) {
    writeItems(items().map(t => t.id === id ? { ...t, ...patch } : t));
  }

  function removeItem(id: string) {
    writeItems(items().filter(t => t.id !== id));
  }

  function moveItem(id: string, delta: -1 | 1) {
    const list = items().slice();
    const idx = list.findIndex(t => t.id === id);
    if (idx < 0) return;
    const swapIdx = idx + delta;
    if (swapIdx < 0 || swapIdx >= list.length) return;
    [list[idx], list[swapIdx]] = [list[swapIdx], list[idx]];
    writeItems(list);
  }

  return (
    <ConfigTabs tabs={[{ key: 'timers', label: t('countdown.timersSection'), icon: Database }]}>
      {(tab) => <>
        <Show when={tab === 'timers'}>
          <Show when={items().length > 0}>
            <div class="field">
              <span class="section-label">{t('countdown.timersSection')}</span>
              <div class="items-list">
                <Index each={items()}>
                  {(it, i) => (
                    <div class="timer-row" data-timer-id={it().id}>
                      <div class="timer-reorder">
                        <button
                          class="reorder-btn"
                          title={t('countdown.moveUp')}
                          disabled={i === 0}
                          onClick={() => moveItem(it().id, -1)}
                        ><ChevronUp size={14} strokeWidth={1.5} /></button>
                        <button
                          class="reorder-btn"
                          title={t('countdown.moveDown')}
                          disabled={i === items().length - 1}
                          onClick={() => moveItem(it().id, 1)}
                        ><ChevronDown size={14} strokeWidth={1.5} /></button>
                      </div>
                      <div class="timer-fields">
                        <input
                          class="timer-label"
                          type="text"
                          placeholder={t('countdown.labelPlaceholder')}
                          value={it().label}
                          onInput={(e) => updateTimer(it().id, { label: e.currentTarget.value })}
                        />
                        <div class="timer-row-bottom">
                          <input
                            class="timer-datetime"
                            type="datetime-local"
                            value={isoToLocalInput(it().target)}
                            onChange={(e) => { const iso = localInputToIso(e.currentTarget.value); if (iso !== null) updateTimer(it().id, { target: iso }); }}
                          />
                          <Dropdown
                            class="timer-format"
                            value={it().format}
                            options={FORMATS.map(f => ({ value: f, label: FORMAT_LABELS[f] }))}
                            onChange={(v) => updateTimer(it().id, { format: v as DisplayFormat })}
                          />
                        </div>
                      </div>
                      <button
                        class="item-remove"
                        onClick={() => removeItem(it().id)}
                        title={t('common.remove')}
                      ><XIcon size={12} strokeWidth={2} /></button>
                    </div>
                  )}
                </Index>
              </div>
            </div>
          </Show>

          <button class="btn-add" onClick={addTimer}>
            <Plus size={14} strokeWidth={1.5} />
            Add timer
          </button>
        </Show>
      </>}
    </ConfigTabs>
  );
}
