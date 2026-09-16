import { createSignal } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { Volume2, Volume1, VolumeX } from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { createPolling } from '../../../src/createPolling';
import { logger } from '../../../src/debugLog.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Volume.css';

export interface VolumeProps {
  headerMode?: HeaderMode;
  label?: string;
}

export default function Volume(props: VolumeProps) {
  const [level, setLevel] = createSignal(0.5);
  const [muted, setMuted] = createSignal(false);
  // While the user drags, ignore polled values so the thumb doesn't fight them.
  let dragging = false;

  async function refresh() {
    if (dragging) return;
    try {
      const s = await invoke('volume_get');
      if (s) {
        setLevel(s.level);
        setMuted(s.muted);
      }
    } catch {
      // ignore: no output device, or running in browser preview
    }
  }
  createPolling(refresh, () => 1500, 'volume');

  function onInput(e: InputEvent & { currentTarget: HTMLInputElement }) {
    dragging = true;
    const v = Number(e.currentTarget.value) / 100;
    setLevel(v);
    if (muted() && v > 0) {
      setMuted(false);
      invoke('volume_set_mute', { muted: false }).catch((err) => logger.error('volume', String(err)));
    }
    invoke('volume_set', { level: v }).catch((err) => logger.error('volume', String(err)));
  }

  function onChange() {
    dragging = false;
  }

  function toggleMute() {
    const next = !muted();
    setMuted(next);
    invoke('volume_set_mute', { muted: next }).catch((err) => logger.error('volume', String(err)));
  }

  const pct = () => Math.round(level() * 100);
  const icon = () => (muted() || level() === 0 ? VolumeX : level() < 0.5 ? Volume1 : Volume2);

  return (
    <div class="volume-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Volume2}
        title={props.label || 'Volume'}
      />
      <div class="widget-body volume-body" classList={{ muted: muted() }}>
        <button class="volume-mute" classList={{ muted: muted() }} onClick={toggleMute} title={muted() ? 'Unmute' : 'Mute'}>
          <Dynamic component={icon()} size={18} strokeWidth={1.5} />
        </button>
        <input
          class="volume-slider"
          type="range"
          min="0"
          max="100"
          value={muted() ? 0 : pct()}
          onInput={onInput}
          onChange={onChange}
          style={`--vol-pct:${muted() ? 0 : pct()}%`}
        />
        <span class="volume-pct">{muted() ? 0 : pct()}<span class="volume-unit">%</span></span>
      </div>
    </div>
  );
}
