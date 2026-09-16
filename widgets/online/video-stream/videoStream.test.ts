import { describe, it, expect } from 'vitest';
import {
  resolveConfig, isStreamUrl, retryDelayMs, streamTitle, nextRotation,
  objectFitFor, withCacheBuster,
  DEFAULT_FIT, DEFAULT_ROTATION, RETRY_BASE_MS, RETRY_MAX_MS,
  LIVE_REFRESH_SECONDS, MAX_REFRESH_SECONDS,
} from './videoStream.ts';

describe('isStreamUrl [S-09]', () => {
  it('accepts the http(s) endpoints real cameras expose', () => {
    expect(isStreamUrl('http://192.168.1.50:8080/?action=stream')).toBe(true);
    expect(isStreamUrl('https://cam.example.com/stream.mjpg')).toBe(true);
    expect(isStreamUrl('  http://octopi.local/webcam/?action=stream  ')).toBe(true);
  });

  it('rejects every non-http scheme and unparseable input', () => {
    // The value is handed to <img src>; this check is the only gate.
    expect(isStreamUrl('javascript:alert(1)')).toBe(false);
    expect(isStreamUrl('data:image/png;base64,AAAA')).toBe(false);
    expect(isStreamUrl('file:///C:/secret.jpg')).toBe(false);
    expect(isStreamUrl('rtsp://192.168.1.50/live')).toBe(false);
    expect(isStreamUrl('192.168.1.50:8080/stream')).toBe(false);
    expect(isStreamUrl('')).toBe(false);
  });
});

describe('resolveConfig [S-09]', () => {
  it('returns defaults for an absent/garbage bag rather than throwing', () => {
    for (const raw of [undefined, null, 42, 'nope', []]) {
      expect(resolveConfig(raw)).toEqual({
        url: '', label: '', fit: DEFAULT_FIT, rotation: DEFAULT_ROTATION, paused: false,
        refreshSeconds: LIVE_REFRESH_SECONDS,
      });
    }
  });

  it('collapses a rejected URL to empty (the widget then shows its empty state)', () => {
    expect(resolveConfig({ url: 'javascript:alert(1)' }).url).toBe('');
    expect(resolveConfig({ url: 'not a url' }).url).toBe('');
    expect(resolveConfig({ url: 123 }).url).toBe('');
  });

  it('trims an accepted URL but leaves the query string byte-identical', () => {
    // Cameras route on the query (`?action=stream`); normalising it breaks them.
    expect(resolveConfig({ url: ' http://cam/?action=stream ' }).url)
      .toBe('http://cam/?action=stream');
  });

  it('clamps fit and rotation to their closed sets', () => {
    expect(resolveConfig({ fit: 'cover' }).fit).toBe('cover');
    expect(resolveConfig({ fit: 'stretch' }).fit).toBe('stretch');
    expect(resolveConfig({ fit: 'squish' }).fit).toBe(DEFAULT_FIT);
    expect(resolveConfig({ rotation: 270 }).rotation).toBe(270);
    expect(resolveConfig({ rotation: 45 }).rotation).toBe(DEFAULT_ROTATION);
    expect(resolveConfig({ rotation: '90' }).rotation).toBe(DEFAULT_ROTATION);
  });

  it('[S-17] clamps refreshSeconds into range, defaulting to live', () => {
    expect(resolveConfig({ refreshSeconds: 5 }).refreshSeconds).toBe(5);
    expect(resolveConfig({}).refreshSeconds).toBe(LIVE_REFRESH_SECONDS);
    expect(resolveConfig({ refreshSeconds: -10 }).refreshSeconds).toBe(LIVE_REFRESH_SECONDS);
    expect(resolveConfig({ refreshSeconds: 99999 }).refreshSeconds).toBe(MAX_REFRESH_SECONDS);
    expect(resolveConfig({ refreshSeconds: 2.6 }).refreshSeconds).toBe(3);
    // A corrupt value must not become NaN and stall the poll timer forever.
    expect(resolveConfig({ refreshSeconds: Number.NaN }).refreshSeconds).toBe(LIVE_REFRESH_SECONDS);
    expect(resolveConfig({ refreshSeconds: '10' }).refreshSeconds).toBe(LIVE_REFRESH_SECONDS);
  });

  it('treats paused as strictly boolean-true, never truthy', () => {
    expect(resolveConfig({ paused: true }).paused).toBe(true);
    expect(resolveConfig({ paused: 'yes' }).paused).toBe(false);
    expect(resolveConfig({}).paused).toBe(false);
  });
});

describe('retryDelayMs [S-06]', () => {
  it('doubles per attempt from the base delay', () => {
    expect(retryDelayMs(1)).toBe(RETRY_BASE_MS);
    expect(retryDelayMs(2)).toBe(RETRY_BASE_MS * 2);
    expect(retryDelayMs(3)).toBe(RETRY_BASE_MS * 4);
  });

  it('caps at RETRY_MAX_MS so an offline camera never backs off forever', () => {
    expect(retryDelayMs(50)).toBe(RETRY_MAX_MS);
    expect(retryDelayMs(5000)).toBe(RETRY_MAX_MS);
  });

  it('falls back to the base delay for a nonsense attempt count', () => {
    expect(retryDelayMs(0)).toBe(RETRY_BASE_MS);
    expect(retryDelayMs(-3)).toBe(RETRY_BASE_MS);
    expect(retryDelayMs(Number.NaN)).toBe(RETRY_BASE_MS);
  });
});

describe('streamTitle [S-01]', () => {
  it('prefers the explicit label', () => {
    expect(streamTitle('http://cam:8080/s', ' Printer ', 'Video Stream')).toBe('Printer');
  });

  it('falls back to host:port, which is how a LAN camera is recognised', () => {
    expect(streamTitle('http://192.168.1.50:8080/?action=stream', '', 'Video Stream'))
      .toBe('192.168.1.50:8080');
  });

  it('falls back to the widget name when there is no usable URL', () => {
    expect(streamTitle('', '', 'Video Stream')).toBe('Video Stream');
    expect(streamTitle('nonsense', '   ', 'Video Stream')).toBe('Video Stream');
  });
});

describe('objectFitFor [S-16]', () => {
  it('maps each fit mode to its CSS keyword', () => {
    expect(objectFitFor('contain')).toBe('contain');
    expect(objectFitFor('cover')).toBe('cover');
    // 'stretch' is the user-facing name; CSS calls the same thing 'fill'.
    expect(objectFitFor('stretch')).toBe('fill');
  });
});

describe('withCacheBuster [S-17]', () => {
  it('appends with ? when the URL has no query', () => {
    expect(withCacheBuster('http://cam/snapshot.jpg', 7)).toBe('http://cam/snapshot.jpg?_t=7');
  });

  it('appends with & and keeps the camera routing parameters intact', () => {
    expect(withCacheBuster('http://cam/?action=snapshot', 3))
      .toBe('http://cam/?action=snapshot&_t=3');
  });

  it('leaves a non-http URL untouched — appending would corrupt a data: frame', () => {
    const data = 'data:image/svg+xml;utf8,%3Csvg%2F%3E';
    expect(withCacheBuster(data, 1)).toBe(data);
    expect(withCacheBuster('', 1)).toBe('');
  });

  it('produces a different URL per nonce, which is the whole point', () => {
    const a = withCacheBuster('http://cam/s', 1);
    const b = withCacheBuster('http://cam/s', 2);
    expect(a).not.toBe(b);
  });
});

describe('nextRotation [S-08]', () => {
  it('cycles 0 -> 90 -> 180 -> 270 -> 0', () => {
    expect(nextRotation(0)).toBe(90);
    expect(nextRotation(90)).toBe(180);
    expect(nextRotation(180)).toBe(270);
    expect(nextRotation(270)).toBe(0);
  });
});
