/**
 * Video Stream widget (themia-releases #102) — video-stream/SPEC.md.
 *
 * The widget is one `<img>` pointed at an MJPEG-over-HTTP endpoint, so the
 * contract worth pinning end-to-end is what actually reaches the DOM:
 *
 *  1. [S-09] the trust boundary — only an absolute http(s) URL becomes an
 *     `<img src>`; a `javascript:`/`data:` URL renders the empty state and NO
 *     image element at all,
 *  2. [S-09]/[S-10] an accepted URL is handed over byte-identical (cameras route
 *     on `?action=stream`, and nothing appends a cache-busting parameter),
 *  3. [S-05] a dead endpoint lands in the error state with the picture hidden,
 *     rather than flashing the webview's broken-image glyph, and
 *  4. [S-07]/[S-08] pause and rotate are persisted config, driven from the
 *     context menu.
 *
 * No camera and no network: the "reachable" case points at 127.0.0.1:9
 * (discard), which refuses instantly, and every other case never opens a socket.
 *
 * NOTE: `type` is the REGISTRY KEY (camelCase `videoStream`), not the kebab
 * folder name — seeding an unknown type renders no widget.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, seedWidgets, switchToMain, ctxMenuItem, waitForPersist,
} from '../helpers/app.js';

const WIDGET_ID = 'video-stream-seed';

/** Fire a synthetic contextmenu at the centre of the seeded widget and wait for
 *  the menu. Mirrors 82-context-menu-refresh's fireWidgetCtx (webdriver
 *  right-click is intermittently swallowed when a detached window holds focus). */
async function fireWidgetCtx(): Promise<void> {
  await switchToMain();
  await browser.execute(() => {
    const w = document.querySelector('.grid > .widget') as HTMLElement | null;
    if (!w) throw new Error('.widget missing');
    const r = w.getBoundingClientRect();
    w.dispatchEvent(new MouseEvent('contextmenu', {
      bubbles: true, cancelable: true, view: window,
      button: 2, buttons: 2,
      clientX: r.left + r.width / 2, clientY: r.top + r.height / 2,
    }));
  });
  await $('.ctx-menu').waitForDisplayed({ timeout: 3_000 });
}

async function seedStream(config: Record<string, unknown>): Promise<void> {
  await resetAppState();
  await seedWidgets([{
    type: 'videoStream', id: WIDGET_ID, col: 2, row: 2, colSpan: 3, rowSpan: 2, config,
  }]);
  // Cold-boot on WebView2 can be slow; give the first paint room.
  await $('.grid > .widget').waitForExist({ timeout: 8_000 });
  await $('.vs-frame').waitForExist({ timeout: 8_000 });
}

function readConfig(): Promise<Record<string, unknown> | null> {
  return browser.execute((id: string) => {
    try {
      const raw = localStorage.getItem('widget-layout') ?? '{}';
      const w = (JSON.parse(raw).widgets ?? [])
        .find((x: { id?: string }) => x.id === id);
      return (w?.config ?? null) as Record<string, unknown> | null;
    } catch { return null; }
  }, WIDGET_ID);
}

describe('video stream widget [#102]', () => {
  it('[S-02] with no URL shows the empty state and mounts no <img>', async () => {
    await seedStream({});

    expect(await $$('.vs-img').length, 'no image before a URL is configured').to.equal(0);
    expect(await (await $('.vs-frame')).getAttribute('data-phase')).to.equal('idle');
    expect(await (await $('.vs-msg')).getText()).to.include('camera stream URL');
  });

  it('[S-09] a javascript: URL is rejected outright — no <img> is ever created', async () => {
    // The regression this guards: passing the raw config value through to
    // <img src> instead of gating it on resolveConfig's scheme check.
    await seedStream({ url: 'javascript:alert(1)' });

    expect(await $$('.vs-img').length, 'a rejected URL must not reach the DOM').to.equal(0);
    expect(await (await $('.vs-frame')).getAttribute('data-phase')).to.equal('idle');
  });

  it('[S-09] an accepted URL reaches <img src> byte-identical', async () => {
    const url = 'http://127.0.0.1:9/webcam/?action=stream';
    await seedStream({ url });

    const img = await $('.vs-img');
    await img.waitForExist({ timeout: 5_000 });
    // No cache-busting parameter, no normalisation: cameras route on the query.
    expect(await img.getAttribute('src')).to.equal(url);
  });

  it('[S-05] a dead endpoint lands in the error state with the picture hidden', async () => {
    await seedStream({ url: 'http://127.0.0.1:9/webcam/?action=stream' });

    const frame = await $('.vs-frame');
    await browser.waitUntil(
      async () => (await frame.getAttribute('data-phase')) === 'error',
      { timeout: 10_000, timeoutMsg: 'stream never reported a connection failure' },
    );
    expect(await (await $('.vs-msg')).getText()).to.include('unavailable');
    // Only a decoded frame is ever visible, so the broken-image glyph can't show.
    const opacity = await browser.execute(() => {
      const el = document.querySelector('.vs-img');
      return el ? getComputedStyle(el).opacity : null;
    });
    expect(opacity).to.equal('0');
  });

  it('[S-08] rotation reaches the frame as a data attribute', async () => {
    await seedStream({ url: 'http://127.0.0.1:9/s', rotation: 270 });

    expect(await (await $('.vs-frame')).getAttribute('data-rot')).to.equal('270');
  });

  it('[S-07] pausing from the context menu persists and tears the stream down', async () => {
    await seedStream({ url: 'http://127.0.0.1:9/webcam/?action=stream' });
    expect(await $$('.vs-img').length, 'stream is live before pausing').to.equal(1);

    await fireWidgetCtx();
    await (await ctxMenuItem('Pause stream')).click();
    await waitForPersist();

    expect((await readConfig())?.paused, 'paused must persist').to.equal(true);
    await browser.waitUntil(
      async () => (await $$('.vs-img').length) === 0,
      { timeout: 5_000, timeoutMsg: 'pausing must unmount the <img> (that is what closes the socket)' },
    );
    expect(await (await $('.vs-msg')).getText()).to.include('Paused');

    // …and the menu now offers the inverse action.
    await fireWidgetCtx();
    expect(await (await ctxMenuItem('Resume stream')).isExisting()).to.equal(true);
    await browser.keys(['Escape']);
  });

  it('[S-16] stretch maps to CSS object-fit: fill', async () => {
    await seedStream({ url: 'http://127.0.0.1:9/s', fit: 'stretch' });

    const fit = await browser.execute(() => {
      const el = document.querySelector('.vs-img');
      return el ? getComputedStyle(el).objectFit : null;
    });
    expect(fit).to.equal('fill');
  });

  it('[S-17] refreshSeconds = 0 streams into an <img>, > 0 paints a <canvas>', async () => {
    // Live: the picture IS the streaming element, so there is nothing to paint.
    await seedStream({ url: 'http://127.0.0.1:9/s' });
    expect(await $$('canvas.vs-img').length, 'live mode needs no canvas').to.equal(0);
    expect(await $$('img.vs-img').length).to.equal(1);

    // Interval: the canvas holds the last frame, and the <img> is demoted to a
    // hidden loader that only exists while a grab is open.
    await seedStream({ url: 'http://127.0.0.1:9/s', refreshSeconds: 10 });
    expect(await $$('canvas.vs-img').length, 'interval mode paints a canvas').to.equal(1);
    expect(await $$('img.vs-img').length, 'the streaming <img> is gone in interval mode').to.equal(0);
  });

  it('[S-08] the Rotate action cycles the persisted rotation', async () => {
    await seedStream({ url: 'http://127.0.0.1:9/s', rotation: 180 });

    await fireWidgetCtx();
    await (await ctxMenuItem('Rotate')).click();
    await waitForPersist();

    expect((await readConfig())?.rotation).to.equal(270);
    expect(await (await $('.vs-frame')).getAttribute('data-rot')).to.equal('270');
  });
});
