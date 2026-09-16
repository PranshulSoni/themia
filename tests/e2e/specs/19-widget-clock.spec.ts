/**
 * Clock widget: timezone-aware clock (the former World Clock, merged in).
 *
 * Covers the behaviors tagged in widgets/others/clock/SPEC.md. The Clock defaults
 * to the current (local) timezone but can render any IANA zone, so it doubles as
 * a world clock. Seeded directly via localStorage for determinism.
 *
 * Notable regressions guarded here:
 *  - [S-07] The label config key is named `customLabel`, NOT `label`.
 *    WidgetGrid.tsx spreads `widget.config` then explicitly overrides
 *    `label={widget.label ?? ''}`; any config key called `label` is shadowed.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import { resetAppState, LS_KEYS } from '../helpers/app.js';

interface ClockConfig {
  timezone?: string;
  customLabel?: string;
  format?: '24h' | '12h';
  showSeconds?: boolean;
  showDate?: boolean;
  offsetMode?: 'none' | 'utc' | 'local';
  style?: 'digital' | 'analog' | 'stacked';
  align?: 'left' | 'center' | 'right';
}

async function seedClock(config: ClockConfig, type = 'clock'): Promise<void> {
  await browser.execute((key: string, cfg: ClockConfig, widgetType: string) => {
    const layout = {
      widgets: [{
        id: 'clock-1', type: widgetType, x: 0.05, y: 0.05, w: 0.55, h: 0.6,
        config: {
          timezone: '',
          customLabel: '',
          format: '24h',
          showSeconds: true,
          showDate: true,
          offsetMode: 'none',
          style: 'digital',
          align: 'center',
          ...cfg,
        },
      }],
      widgetGap: 12, bgImage: '', bgImageFit: 'cover',
    };
    const serialized = JSON.stringify(layout);
    const prior = localStorage.getItem(key) ?? '{}';
    localStorage.setItem(key, serialized);
    window.dispatchEvent(new StorageEvent('storage', {
      key, newValue: serialized, oldValue: prior, storageArea: localStorage,
    }));
  }, LS_KEYS.layoutRoot, config, type);
  await $('.widget').waitForExist({ timeout: 5_000 });
  await $('.clock-widget').waitForExist({ timeout: 5_000 });
}

describe('widget: Clock', () => {
  beforeEach(async () => { await resetAppState(); });

  // ── Display ────────────────────────────────────────────────────────────────

  it('[S-01][S-04] renders time formatted in the configured timezone (HH:MM:SS, zero-padded)', async () => {
    await seedClock({ timezone: 'UTC', showSeconds: true, format: '24h' });
    const text = (await $('.clock-widget__time').getText()).trim();
    expect(text).to.match(/^\d{2}:\d{2}:\d{2}$/);
  });

  it('[S-17] defaults to the local timezone (empty timezone still renders a time)', async () => {
    await seedClock({ timezone: '' });
    const text = (await $('.clock-widget__time').getText()).trim();
    expect(text).to.match(/^\d{2}:\d{2}(:\d{2})?$/);
  });

  it('[S-01] different timezones produce different displayed times', async () => {
    await seedClock({ timezone: 'Asia/Tokyo', showSeconds: false });
    const tokyo = (await $('.clock-widget__time').getText()).trim();

    await browser.execute(() => {
      const raw = localStorage.getItem('widget-layout') ?? '{}';
      const layout = JSON.parse(raw);
      layout.widgets[0].config.timezone = 'UTC';
      localStorage.setItem('widget-layout', JSON.stringify(layout));
      window.dispatchEvent(new StorageEvent('storage', {
        key: 'widget-layout', newValue: JSON.stringify(layout),
      }));
    });
    await browser.pause(150);
    const utc = (await $('.clock-widget__time').getText()).trim();
    expect(tokyo).to.not.equal(utc);
  });

  it('[S-03] in 12h mode renders a separate AM/PM indicator', async () => {
    await seedClock({ timezone: 'UTC', format: '12h', showSeconds: false });
    const ampm = await $('.clock-widget__ampm');
    await ampm.waitForExist({ timeout: 2_000 });
    expect((await ampm.getText()).trim()).to.match(/^(AM|PM)$/);
  });

  it('[S-07] non-local timezone label defaults to the city derived from the IANA zone', async () => {
    await seedClock({ timezone: 'America/New_York', customLabel: '' });
    const text = (await $('.clock-widget__label').getText()).trim();
    expect(text).to.equal('New York');
  });

  it('[S-07] customLabel overrides the derived city, guards label-shadowing regression', async () => {
    await seedClock({ timezone: 'America/New_York', customLabel: 'Office' });
    const text = (await $('.clock-widget__label').getText()).trim();
    expect(text).to.equal('Office');
  });

  it('[S-18] local clock shows no label by default (classic clean look)', async () => {
    await seedClock({ timezone: '', customLabel: '' });
    expect(await $$('.clock-widget__label').length).to.equal(0);
  });

  it('[S-05] showDate=true renders the date line; false hides it', async () => {
    await seedClock({ timezone: 'UTC', showDate: true });
    expect(await $$('.clock-widget__date').length).to.equal(1);

    await seedClock({ timezone: 'UTC', showDate: false });
    expect(await $$('.clock-widget__date').length).to.equal(0);
  });

  it('[S-08] offsetMode=utc renders the UTC offset; offsetMode=none hides it', async () => {
    await seedClock({ timezone: 'UTC', offsetMode: 'none' });
    expect(await $$('.clock-widget__offset').length).to.equal(0);

    await seedClock({ timezone: 'Asia/Tokyo', offsetMode: 'utc' });
    const off = await $('.clock-widget__offset');
    await off.waitForExist({ timeout: 2_000 });
    const text = (await off.getText()).trim();
    expect(text).to.match(/^UTC[+-]\d+(?::\d{2})?$/);
  });

  it('[S-16] offsetMode=local renders relative offset suffixed with "local"', async () => {
    await seedClock({ timezone: 'Asia/Tokyo', offsetMode: 'local' });
    const off = await $('.clock-widget__offset');
    await off.waitForExist({ timeout: 2_000 });
    const text = (await off.getText()).trim();
    expect(text).to.match(/^(same as local|[+−]\d+(?::\d{2})?h local)$/);
  });

  // ── Styles ───────────────────────────────────────────────────────────────

  it('[S-12] analog style renders an SVG clock face', async () => {
    await seedClock({ timezone: 'UTC', style: 'analog' });
    await $('.clock-widget--analog .clock-widget__face').waitForExist({ timeout: 2_000 });
    expect(await $$('.clock-widget--analog .clock-widget__hand').length).to.be.greaterThan(0);
  });

  it('[S-13] stacked style renders the hours/minutes stack', async () => {
    await seedClock({ timezone: 'UTC', style: 'stacked' });
    await $('.clock-widget--stacked .clock-widget__stack').waitForExist({ timeout: 2_000 });
    const lines = await $$('.clock-widget--stacked .clock-widget__stack-line').length;
    expect(lines).to.be.greaterThan(1);
  });

  it('[S-25] align config sets the alignment root class (left / center / right)', async () => {
    await seedClock({ timezone: 'UTC', align: 'left' });
    expect(await $$('.clock-widget--align-left').length).to.equal(1);

    await seedClock({ timezone: 'UTC', align: 'right' });
    expect(await $$('.clock-widget--align-right').length).to.equal(1);

    await seedClock({ timezone: 'UTC', align: 'center' });
    expect(await $$('.clock-widget--align-center').length).to.equal(1);
  });

  // ── Chrome ──────────────────────────────────────────────────────────────────

  it('[S-06] renders without a widget header (registry noHeader)', async () => {
    await seedClock({ timezone: 'UTC' });
    const headers = await $$('.widget .widget-header');
    expect(await headers.length).to.equal(0);
  });
});
