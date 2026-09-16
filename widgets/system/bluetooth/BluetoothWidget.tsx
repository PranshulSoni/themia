import { createSignal, createEffect, createMemo, For } from 'solid-js';
import { Show } from 'solid-js/web';
import { invoke } from '../../../src/demo/invoke.ts';
import { Headphones, Mouse, Keyboard, Smartphone, Gamepad2, Bluetooth } from 'lucide-solid';
import { logger } from '../../../src/debugLog.ts';
import { createPolling } from '../../../src/createPolling.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './BluetoothWidget.css';
import { refreshInterval } from '../refreshInterval.ts';
import { t } from '../../../src/i18n.ts';

interface BtDevice {
  name: string;
  id: string;
  is_connected: boolean;
  device_type: string;
  battery_pct?: number | null;
  rssi?: number | null;
}

interface BluetoothWidgetProps {
  interval?: number;
  label?: string;
  headerMode?: HeaderMode;
  _refresh?: number;
  visibleMetrics?: unknown;
}

function deviceIcon(type: string) {
  const s = 16, sw = 1.5;
  switch (type) {
    case 'audio': return <Headphones size={s} strokeWidth={sw} />;
    case 'mouse': return <Mouse size={s} strokeWidth={sw} />;
    case 'keyboard': return <Keyboard size={s} strokeWidth={sw} />;
    case 'phone': return <Smartphone size={s} strokeWidth={sw} />;
    case 'gamepad': return <Gamepad2 size={s} strokeWidth={sw} />;
    default: return <Bluetooth size={s} strokeWidth={sw} />;
  }
}

export default function BluetoothWidget(props: BluetoothWidgetProps) {
  const [devices, setDevices] = createSignal<BtDevice[]>([]);
  const [loading, setLoading] = createSignal(true);

  const interval = () => refreshInterval(props.interval, 'bluetooth');

  const visible = createMemo<string[]>(() => {
    const raw = props.visibleMetrics;
    if (raw === undefined || raw === null) return canonicalVisible(METRIC_DEFS);
    return resolveMetrics(raw, METRIC_DEFS).visible;
  });
  const shows = (k: string) => visible().includes(k);

  async function refresh() {
    try {
      const devs = await invoke('get_bluetooth_devices');
      setDevices(devs);
    } catch (e) { logger.error('bluetooth', `get_bluetooth_devices failed: ${e}`); }
    setLoading(false);
  }

  // Background refresh: hibernation pause + wakeup stagger (createPolling, 2.2).
  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'bluetooth');

  // Instant mount fetch + manual `_refresh` trigger (createPolling staggers its
  // first tick, so this keeps first paint + the refresh button snappy).
  createEffect(() => {
    void props._refresh;
    refresh();
  });

  return (
    <div class="bt-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Bluetooth}
        title={props.label || 'Bluetooth'}
      />
      <div class="widget-body">
      <Show when={!loading() || devices().length > 0} fallback={
        <div class="bt-widget__msg">{t('widgetUi.loading')}</div>
      }>
        <Show when={devices().length > 0} fallback={
          <div class="bt-widget__msg">{t('net.noPairedDevices')}</div>
        }>
          <div class="bt-widget__list">
            <For each={devices()}>
              {(dev) => (
                <div class="bt-widget__item">
                  <Show when={shows('type')}>
                    <div class="bt-widget__item-icon">{deviceIcon(dev.device_type)}</div>
                  </Show>
                  <div class="bt-widget__item-info">
                    <Show when={shows('deviceName')}>
                      <span class="bt-widget__item-name">{dev.name || 'Unknown device'}</span>
                    </Show>
                    <Show when={(shows('battery') && dev.battery_pct != null) || (shows('rssi') && dev.rssi != null)}>
                      <div class="bt-widget__item-meta">
                        <Show when={shows('battery') && dev.battery_pct != null}>
                          <span class="bt-widget__item-meta-item">{dev.battery_pct}%</span>
                        </Show>
                        <Show when={shows('rssi') && dev.rssi != null}>
                          <span class="bt-widget__item-meta-item">{dev.rssi} dBm</span>
                        </Show>
                      </div>
                    </Show>
                  </div>
                  <Show when={shows('connection')}>
                    <div class="bt-widget__item-status" classList={{ connected: dev.is_connected }} />
                  </Show>
                </div>
              )}
            </For>
          </div>
        </Show>
      </Show>
      </div>
    </div>
  );
}
