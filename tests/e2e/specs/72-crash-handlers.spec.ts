/**
 * Global crash handlers (1.18).
 *
 * main.tsx installs window 'error' + 'unhandledrejection' listeners (for every
 * window type) that route into the log pipeline. Here we dispatch synthetic
 * versions of each event in the config window and assert the entry shows up in
 * Settings > Logs under the expected source, proving an uncaught failure is no
 * longer swallowed by the webview console.
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab, enableDevMode, enableLogging,
} from '../helpers/app.js';

async function logSources(): Promise<string[]> {
  return browser.execute(() => {
    return Array.from(document.querySelectorAll<HTMLElement>(
      '.config-panel .logs-container .log-source',
    )).map(el => (el.textContent ?? '').trim());
  });
}

describe('global crash handlers', () => {
  beforeEach(async () => {
    await resetAppState();
    await enableDevMode(); // Logs tab only renders in devMode
    await enableLogging(); // [LOG-04] records only flow while the toggle is on
    await openSettings();
    await (await settingsTab('Logs')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('an uncaught error is routed to the log pipeline', async () => {
    await browser.execute(() => {
      window.dispatchEvent(new ErrorEvent('error', {
        error: new Error('synthetic uncaught error'),
        message: 'synthetic uncaught error',
        filename: 'spec.ts', lineno: 1, colno: 1,
      }));
    });
    await browser.waitUntil(async () => (await logSources()).includes('uncaught'), {
      timeout: 5_000, interval: 200,
      timeoutMsg: 'expected an "uncaught" log entry',
    });
  });

  it('an unhandled promise rejection is routed to the log pipeline', async () => {
    await browser.execute(() => {
      // Synthetic event: the `promise` field is a resolved promise so the test
      // itself does not create a real unhandled rejection.
      window.dispatchEvent(new PromiseRejectionEvent('unhandledrejection', {
        promise: Promise.resolve(),
        reason: new Error('synthetic rejection'),
      }));
    });
    await browser.waitUntil(async () => (await logSources()).includes('unhandledrejection'), {
      timeout: 5_000, interval: 200,
      timeoutMsg: 'expected an "unhandledrejection" log entry',
    });
  });
});
