// @vitest-environment jsdom
/**
 * Frontend half of analytics: it runs TelemetryDeck's official SDK, but may
 * only send what the backend hands it. See docs/ANALYTICS.md ([S-04], [S-15]).
 *
 * jsdom rather than the default node env: the module reads the webview's own
 * environment (screen, locale, colour scheme) for [S-18].
 *
 * The transport is deliberately injected so these tests never touch the network
 * or the real SDK — what matters here is the consent/confirmation protocol, not
 * TelemetryDeck's HTTP client.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { AnalyticsEvent, AnalyticsPlan, AnalyticsSignal } from './ipc.ts';

// Stand in for the real SDK so the adapter can be pinned without the network.
// Records how it was constructed and what it was asked to send.
const { sdkCalls } = vi.hoisted(() => ({
  sdkCalls: [] as { opts: Record<string, unknown>; signals: unknown[][] }[],
}));
let sdkOk = true;

vi.mock('@telemetrydeck/sdk', () => ({
  default: class {
    private rec: { opts: Record<string, unknown>; signals: unknown[][] };
    constructor(opts: Record<string, unknown>) {
      this.rec = { opts, signals: [] };
      sdkCalls.push(this.rec);
    }
    async signal(type: string, payload?: unknown) {
      this.rec.signals.push([type, payload]);
      return { ok: sdkOk } as Response;
    }
  },
}));

// Static import is fine: vitest hoists `vi.mock` above it, so the module under
// test receives the stub above rather than the real SDK.
import { runPlan, sdkSignal, windowScreen, type SignalFn } from './analytics.ts';

/** The default parameters the backend builds; the frontend only forwards them. */
const PAYLOAD = {
  'TelemetryDeck.AppInfo.version': '0.0.0-test',
  'TelemetryDeck.Device.platform': 'Windows',
  'TelemetryDeck.Device.systemVersion': '11.0.26200',
};

function signalFor(type: string, marker: AnalyticsEvent | null = null): AnalyticsSignal {
  return { type, payload: PAYLOAD, marker };
}

function plan(signals: AnalyticsSignal[]): AnalyticsPlan {
  return {
    appId: 'A-TEST',
    clientUser: 'ba7816bf',
    target: 'https://example.invalid/v2/',
    testMode: true,
    signals,
  };
}

const HEARTBEAT = signalFor('app_heartbeat', 'app_heartbeat');
const INSTALL = signalFor(
  'TelemetryDeck.Acquisition.newInstallDetected',
  'TelemetryDeck.Acquisition.newInstallDetected',
);

describe('analytics SDK adapter', () => {
  beforeEach(() => { sdkCalls.length = 0; });

  // [S-16] the SDK's own body is only {clientUser, sessionID, appID, type,
  // telemetryClientVersion} — it knows nothing about the app it is embedded in.
  // TelemetryDeck's default parameters must be forwarded as the signal payload
  // or every dashboard breakdown (version, platform, OS version) reads "unset".
  it('S-16 configures the SDK from the plan and forwards the default parameters', async () => {
    const send = sdkSignal(plan([HEARTBEAT]));

    expect(await send(HEARTBEAT)).to.equal(true);

    expect(sdkCalls).to.have.lengthOf(1);
    expect(sdkCalls[0].opts).to.deep.equal({
      appID: 'A-TEST',
      clientUser: 'ba7816bf',
      target: 'https://example.invalid/v2/',
      testMode: true,
    });
    expect(sdkCalls[0].signals).to.deep.equal([['app_heartbeat', PAYLOAD]]);
  });

  // [S-15] a non-2xx from TelemetryDeck must surface as "not delivered" so the
  // backend never persists a marker for it.
  it('S-15 reports a non-ok response as not delivered', async () => {
    sdkOk = false;
    try {
      expect(await sdkSignal(plan([HEARTBEAT]))(HEARTBEAT)).to.equal(false);
    } finally {
      sdkOk = true;
    }
  });
});

describe('analytics runPlan', () => {
  // [S-04] an empty plan is the opted-out / nothing-due / already-claimed case.
  // The SDK must not even be constructed: no client, no request, no marker.
  it('S-04 sends nothing and builds no client for an empty plan', async () => {
    const makeSignal = vi.fn();
    const markSent = vi.fn();

    const sent = await runPlan(async () => plan([]), makeSignal, markSent);

    expect(sent).to.deep.equal([]);
    expect(makeSignal).not.toHaveBeenCalled();
    expect(markSent).not.toHaveBeenCalled();
  });

  // [S-15] every signal the backend handed over is sent, then confirmed back so
  // the backend can persist its delivery marker.
  it('S-15 sends each planned signal and confirms it', async () => {
    const signal = vi.fn<SignalFn>(async () => true);
    const markSent = vi.fn<(event: AnalyticsEvent) => Promise<void>>(async () => {});

    const sent = await runPlan(async () => plan([INSTALL, HEARTBEAT]), () => signal, markSent);

    expect(sent).to.deep.equal(['TelemetryDeck.Acquisition.newInstallDetected', 'app_heartbeat']);
    expect(signal.mock.calls.map(c => c[0].type)).to.deep.equal([
      'TelemetryDeck.Acquisition.newInstallDetected',
      'app_heartbeat',
    ]);
    expect(markSent.mock.calls.map(c => c[0])).to.deep.equal([
      'TelemetryDeck.Acquisition.newInstallDetected',
      'app_heartbeat',
    ]);
  });

  // [S-15] a signal with no marker (a session start, an error, a navigation) is
  // sent but never confirmed: there is nothing to persist, and re-sending it on
  // a later launch would be wrong rather than helpful.
  it('S-15 sends an unmarked signal without confirming it', async () => {
    const markSent = vi.fn<(event: AnalyticsEvent) => Promise<void>>(async () => {});

    const sent = await runPlan(
      async () => plan([signalFor('TelemetryDeck.Error.occurred')]),
      () => async () => true,
      markSent,
    );

    expect(sent).to.deep.equal(['TelemetryDeck.Error.occurred']);
    expect(markSent).not.toHaveBeenCalled();
  });

  // [S-15] a non-success response must NOT be confirmed: the backend keeps the
  // marker unset so the event is retried on the next launch instead of being
  // silently lost.
  it('S-15 does not confirm a signal the server rejected', async () => {
    const markSent = vi.fn(async () => {});

    const sent = await runPlan(async () => plan([HEARTBEAT]), () => async () => false, markSent);

    expect(sent).to.deep.equal([]);
    expect(markSent).not.toHaveBeenCalled();
  });

  // [S-15] a throwing transport (offline, DNS failure) must not abort the rest
  // of the plan, and must not confirm the signal that failed.
  it('S-15 survives a throwing send and still delivers the rest', async () => {
    const markSent = vi.fn<(event: AnalyticsEvent) => Promise<void>>(async () => {});
    const signal = vi.fn<SignalFn>(async (s) => {
      if (s.type === 'TelemetryDeck.Acquisition.newInstallDetected') throw new Error('network down');
      return true;
    });

    const sent = await runPlan(async () => plan([INSTALL, HEARTBEAT]), () => signal, markSent);

    expect(sent).to.deep.equal(['app_heartbeat']);
    expect(markSent.mock.calls.map(c => c[0])).to.deep.equal(['app_heartbeat']);
  });

  // [S-15] a failure asking the backend for a plan is not fatal: analytics must
  // never take down app startup.
  it('S-15 returns empty when the backend plan call fails', async () => {
    const markSent = vi.fn();

    const sent = await runPlan(
      async () => { throw new Error('ipc unavailable'); },
      () => async () => true,
      markSent,
    );

    expect(sent).to.deep.equal([]);
    expect(markSent).not.toHaveBeenCalled();
  });

  // [S-15] the client is built once per plan, not once per signal.
  it('S-15 builds the SDK client once per plan', async () => {
    const makeSignal = vi.fn(() => async () => true);

    await runPlan(async () => plan([INSTALL, HEARTBEAT]), makeSignal, async () => {});

    expect(makeSignal).toHaveBeenCalledTimes(1);
  });
});

describe('analytics window screens', () => {
  // [S-23] auxiliary windows report a fixed screen name of ours. The widget
  // grid reports none: it IS the app, and its opening is the session signal.
  it('S-23 maps each window param to a fixed screen name', () => {
    const screen = (q: string) => windowScreen(new URLSearchParams(q));

    expect(screen('config=1')).to.equal('settings');
    expect(screen('connect=1')).to.equal('connect');
    expect(screen('trayflyout=1')).to.equal('tray-flyout');
    expect(screen('widgetwindow=1&widget=notes-1')).to.equal('widget-window');
    expect(screen('')).to.equal(null);
  });
});
