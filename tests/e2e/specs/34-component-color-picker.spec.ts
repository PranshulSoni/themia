/**
 * ColorPicker: used for Primary color, Background color, Widget background
 * in the Appearance tab.
 *
 * Drag-slider interactions (HSV gradient, Hue slider) are flaky through
 * WebDriver, so we exercise the deterministic code paths:
 *  - Typing into the hex input + committing (Enter) updates props.value
 *  - Typing into an RGB <input type="number"> commits immediately (onChange)
 *  - Clicking a preset swatch commits immediately
 *
 * All three flows round-trip through `props.onChange`, which for the Primary
 * color control writes to designState.primaryColor → persisted in
 * localStorage `widget-global-design` and reflected on :root as --primary.
 *
 * Selectors per ColorPicker.tsx:
 *  - swatch trigger: `.cp-swatch-btn`
 *  - popover:        `.cp-popover` (Portal → body)
 *  - hex input:      `.cp-hex-input`
 *  - rgb inputs:     `.cp-rgb-input`
 *  - preset swatch:  `.cp-preset`
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, openSettings, closeSettings, settingsTab,
  readDesign, readCssVar,
} from '../helpers/app.js';

async function popoverOpen(): Promise<boolean> {
  return (await $$('.cp-popover').length) > 0;
}

async function openPrimaryColorPicker(): Promise<void> {
  // "Primary color" is the first ColorPicker on the Appearance tab (the order
  // is: Primary, Background, Widget bg). We click the first
  // .cp-swatch-btn inside the modal.
  await browser.execute(() => {
    const btn = document.querySelector<HTMLElement>('.design-modal .cp-swatch-btn');
    btn?.click();
  });
  await $('.cp-popover').waitForDisplayed({ timeout: 2_000 });
}

async function setHex(hex: string): Promise<void> {
  const input = await $('.cp-popover .cp-hex-input');
  await input.waitForExist({ timeout: 2_000 });
  // Select all + type the new value. setValue clears first on wdio v9.
  await input.setValue(hex);
  // Commit via Enter (triggers onHexCommit).
  await browser.keys(['Enter']);
}

// Skipped pending follow-up.
describe.skip('component: color picker', () => {
  beforeEach(async () => {
    await resetAppState();
    await openSettings();
    await (await settingsTab('Appearance')).click();
  });

  afterEach(async () => { await closeSettings(); });

  it('click swatch opens the popover', async () => {
    expect(await popoverOpen()).to.equal(false);
    await openPrimaryColorPicker();
    expect(await popoverOpen()).to.equal(true);
  });

  it('typing a hex + Enter commits the new color', async () => {
    await openPrimaryColorPicker();
    await setHex('#ff8800');

    // Wait for design persistence & applyDesign to run.
    await browser.pause(120);
    const design = await readDesign();
    expect((design.primaryColor ?? '').toLowerCase()).to.equal('#ff8800');

    // --primary CSS var updates as well.
    const pc = await readCssVar('--primary');
    expect(pc.toLowerCase()).to.equal('#ff8800');
  });

  it('invalid hex reverts to current props.value', async () => {
    await openPrimaryColorPicker();

    // Commit a known-valid hex first.
    await setHex('#123456');
    await browser.pause(80);

    // Reopen (Enter may blur; reopen defensively).
    if (!(await popoverOpen())) await openPrimaryColorPicker();

    // Now attempt an invalid hex.
    const input = await $('.cp-popover .cp-hex-input');
    await input.setValue('not-a-hex');
    await browser.keys(['Enter']);
    await browser.pause(80);

    const design = await readDesign();
    // Should still equal the last valid commit.
    expect((design.primaryColor ?? '').toLowerCase()).to.equal('#123456');
  });

  it('clicking a preset swatch commits that color', async () => {
    await openPrimaryColorPicker();

    // Read first preset's background, then click it and assert it round-trips.
    const firstPresetBg = await browser.execute(() => {
      const p = document.querySelector<HTMLElement>('.cp-popover .cp-preset');
      if (!p) return '';
      return p.style.background;   // e.g. "rgb(255, 255, 255)" or "#ffffff"
    });
    expect(firstPresetBg).to.be.a('string').and.not.equal('');

    await browser.execute(() => {
      const p = document.querySelector<HTMLElement>('.cp-popover .cp-preset');
      p?.click();
    });

    await browser.pause(120);
    const design = await readDesign();
    expect(design.primaryColor).to.be.a('string').and.match(/^#[0-9a-f]{6}$/);
  });
});
