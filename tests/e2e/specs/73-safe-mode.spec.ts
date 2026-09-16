/**
 * Safe-mode boot recovery (1.19 B2).
 *
 * After SAFE_MODE_THRESHOLD (3) consecutive boots of one window that never reach
 * a stable mount, main.tsx renders the SafeMode recovery UI (`.safe-mode`) instead
 * of App; "Try again" clears that window's counter and recovers to the grid. The
 * counter is keyed PER WINDOW LABEL (the multi-monitor false-trip fix), so this
 * single-window e2e exercises the UI flow end-to-end; the per-window isolation
 * (many monitors booting once never trip each other) is covered by the unit test
 * src/safeMode.test.ts.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, attachToAppWindow, waitForReady } from '../helpers/app.js';

describe('safe-mode recovery', () => {
  beforeEach(async () => { await resetAppState(); });

  it('a normal boot does NOT trip safe mode', async () => {
    await waitForReady(); // .grid present
    expect(await $('.safe-mode').isExisting()).to.equal(false);
  });

  it('shows the recovery UI past the failure threshold, and Try again recovers', async () => {
    // Simulate this window failing to boot well past the threshold, then reload.
    // The app exposes its exact counter key as `__themiaWindowKey` under webdriver.
    const label = await browser.execute(
      () => (window as unknown as { __themiaWindowKey?: string }).__themiaWindowKey ?? 'widget',
    );
    await browser.execute((l: string) => {
      localStorage.setItem('themia-boot-attempts:' + l, '10');
      location.reload();
    }, label);
    await browser.pause(300);
    await attachToAppWindow();

    // Recovery UI appears instead of the widget grid.
    await $('.safe-mode').waitForExist({ timeout: 10_000 });
    expect(await $('.grid').isExisting()).to.equal(false);

    // "Try again" clears this window's counter, reloads, and recovers to the grid.
    await $('.safe-mode-retry').click();
    await browser.pause(300);
    await attachToAppWindow();
    await waitForReady(); // .grid back
    expect(await $('.safe-mode').isExisting()).to.equal(false);
  });
});
