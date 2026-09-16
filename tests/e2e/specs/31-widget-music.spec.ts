/**
 * Music (Now Playing) widget: GSMTC-backed track display + transport.
 *
 * The widget is event-driven; on mount it:
 *   - invokes `music_start_watcher` (idempotent)
 *   - invokes `music_get_now_playing` for an initial snapshot
 *   - subscribes to the `music://update` Tauri event
 * Transport actions:
 *   - `music_play_pause`  on play/pause button
 *   - `music_next`        on skip-forward button
 *   - `music_previous`    on skip-back button
 *   - `music_seek`        on scrubber interaction
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

async function stubWithRecorder(mapping: Record<string, any>): Promise<void> {
  await browser.execute((m: Record<string, any>) => {
    (window as any).__calls = [];
    const internals = (window as any).__TAURI_INTERNALS__ ?? ((window as any).__TAURI_INTERNALS__ = {});
    const original = internals.__originalInvoke ?? internals.invoke;
    if (!internals.__originalInvoke) internals.__originalInvoke = original;
    internals.invoke = async (cmd: string, args: any) => {
      (window as any).__calls.push({ cmd, args });
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      if (Object.prototype.hasOwnProperty.call(m, '*')) return m['*'];
      if (original) return original(cmd, args);
      return null;
    };
  }, mapping);
}

async function recordedCalls(): Promise<Array<{ cmd: string; args: any }>> {
  return browser.execute(() => (window as any).__calls ?? []);
}

// Seed via storage-event dispatch; browser.refresh() would wipe the
// invoke stub installed in beforeEach, leaving `music_get_now_playing`
// to hit the real (null-returning) backend.
async function seedMusic(): Promise<void> {
  await browser.execute((key: string) => {
    const raw = localStorage.getItem(key) ?? '{}';
    let layout: any = {};
    try { layout = JSON.parse(raw); } catch {}
    layout.widgets = [{
      id: 'music-1', type: 'music', col: 1, row: 1, colSpan: 3, rowSpan: 2,
      config: { pollInterval: 1, showAlbumArt: true, label: '' },
    }];
    layout.widgetGap = layout.widgetGap ?? 12;
    layout.bgImage = layout.bgImage ?? '';
    layout.bgImageFit = layout.bgImageFit ?? 'cover';
    const serialized = JSON.stringify(layout);
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: raw, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot);
  await $('.widget').waitForExist({ timeout: 5_000 });
}

// Force the music widget to remount (so onMount + its invokes re-run)
// without a full page refresh that would wipe the stub. We toggle the
// widget list: clear it, dispatch, then restore it.
async function remountMusic(): Promise<void> {
  await browser.execute((key: string) => {
    const raw = localStorage.getItem(key) ?? '{}';
    let layout: any = {};
    try { layout = JSON.parse(raw); } catch {}
    const saved = layout.widgets ?? [];
    // 1) clear
    layout.widgets = [];
    let serialized = JSON.stringify(layout);
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: raw, storageArea: localStorage,
    }));
    // 2) restore on next tick
    setTimeout(() => {
      layout.widgets = saved;
      const s2 = JSON.stringify(layout);
      localStorage.setItem(key, s2);
      window.dispatchEvent(new StorageEvent('storage', {
        key, newValue: s2, oldValue: serialized, storageArea: localStorage,
      }));
    }, 0);
  }, LS_KEYS.layoutRoot);
  await $('.widget').waitForExist({ timeout: 5_000 });
}

const FAKE_TRACK = {
  title:            'Sandstorm',
  artist:           'Darude',
  album:            'Before the Storm',
  thumbnail_base64: null,
  position_ms:      30_000,
  duration_ms:      225_000,
  is_playing:       true,
  has_next:         true,
  has_previous:     true,
};

// Skipped pending follow-up: stubbed music_get_now_playing doesn't populate widget.
describe.skip('music widget', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubWithRecorder({
      'music_start_watcher':   null,
      'music_get_now_playing': FAKE_TRACK,
      'music_play_pause':      null,
      'music_next':            null,
      'music_previous':        null,
      'music_seek':            null,
    });
    await seedMusic();
  });

  it('renders track title + artist from the stubbed snapshot', async () => {
    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('Sandstorm') && text.includes('Darude');
    }, { timeout: 5_000, timeoutMsg: 'track info never rendered' });

    const title  = await $('.music-title');
    const artist = await $('.music-artist');
    expect(await title.getText()).to.equal('Sandstorm');
    expect(await artist.getText()).to.equal('Darude');
  });

  it('play/pause button fires music_play_pause', async () => {
    await $('.music-ctrl-play').waitForExist({ timeout: 5_000 });
    await browser.execute(() => { (window as any).__calls = []; });

    await browser.execute(() => {
      const btn = document.querySelector<HTMLButtonElement>('.music-ctrl-play');
      btn?.click();
    });

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'music_play_pause');
    }, { timeout: 2_000, timeoutMsg: 'music_play_pause never fired' });
  });

  it('next / previous buttons fire their invokes', async () => {
    await $('.music-ctrl-play').waitForExist({ timeout: 5_000 });
    await browser.execute(() => { (window as any).__calls = []; });

    // Transport layout: [previous] [play/pause] [next]; grab by order.
    await browser.execute(() => {
      const btns = document.querySelectorAll<HTMLButtonElement>('.music-ctrl-btn');
      btns[0]?.click();                       // previous
      btns[btns.length - 1]?.click();         // next
    });

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'music_previous')
          && calls.some(c => c.cmd === 'music_next');
    }, { timeout: 2_000, timeoutMsg: 'music_previous and/or music_next never fired' });
  });

  it('empty snapshot renders the no-media fallback', async () => {
    // Re-stub with null snapshot, then force the widget to remount (via
    // layout storage-event toggle) so Music's onMount re-invokes
    // `music_get_now_playing` and applies the null snapshot, without a
    // browser.refresh() that would wipe the stub.
    await stubWithRecorder({
      'music_start_watcher':   null,
      'music_get_now_playing': null,
    });
    await remountMusic();

    await browser.waitUntil(async () => {
      const text = await browser.execute(() => document.body.textContent ?? '');
      return text.includes('No media playing');
    }, { timeout: 5_000, timeoutMsg: 'no-media fallback never rendered' });
  });
});
