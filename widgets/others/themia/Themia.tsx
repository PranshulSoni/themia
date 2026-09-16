import { createSignal, onMount, Show } from 'solid-js';
import { getVersion } from '@tauri-apps/api/app';
import { isDemo } from '../../../src/demo/invoke.ts';
import './Themia.css';

export default function Themia() {
  const [version, setVersion] = createSignal('');

  onMount(() => {
    // Rejects outside a Tauri window (the DEV browser harnesses), which used to
    // leave a bare "v" on screen. Swallow it and let the Show below hide the
    // row instead.
    getVersion().then(v => setVersion(v)).catch(() => setVersion(''));
  });

  return (
    <div class="themia-widget">
      <img class="themia-widget__logo" src="/logo.svg" alt="Themia" />
      <span class="themia-widget__name">Themia</span>
      <Show when={version() || isDemo()}>
        <span class="themia-widget__version">v{version() || DEMO_VERSION}</span>
      </Show>
    </div>
  );
}

/** Stand-in for previews and marketing shots, where the real version is either
 *  unavailable (no Tauri) or would date the image. */
const DEMO_VERSION = '1.0.0';
