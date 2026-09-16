/**
 * System-stats widgets: CPU, GPU, RAM, Disk, Net, Battery, Bluetooth.
 *
 * Each system widget pulls its data via the wrapped invoke in
 * `src/demo/invoke.ts`, which exposes a `window.__themiaInvoke.setInvoke`
 * hook under WebDriver. We use `stubThemiaInvoke` (helpers/app.ts) to feed
 * deterministic fixtures.
 *
 * NOTE: `Ping` is commented out of `src/widgets/registry.tsx`, so its
 * describe block stays skipped until the widget is restored.
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, stubThemiaInvoke, seedWidgets,
} from '../helpers/app.js';

// ── Shared fixture ───────────────────────────────────────────────────────────
// `get_system_stats` is consumed by CPU, RAM, Net, Disk. Returning a single
// object with all fields keeps each spec self-contained.
const SYSTEM_STATS = {
  cpu_usage: 42,
  cpu_cores: [40, 44, 41, 43],
  cpu_freq_mhz: 3200,
  cpu_temp_c: 65,
  cpu_processes_total: 200,
  cpu_threads_total: 2800,
  cpu_top: [{ name: 'test.exe', pid: 1, cpu_pct: 30 }],
  cpu_uptime_s: 3600,
  ram_used: 8 * 1024 * 1024 * 1024,
  ram_total: 16 * 1024 * 1024 * 1024,
  ram_available: 7 * 1024 * 1024 * 1024,
  swap_used: 0,
  swap_total: 0,
  ram_top: [{ name: 'test.exe', pid: 1, mem_bytes: 500_000_000 }],
  net_rx_per_sec: 2 * 1024 * 1024,
  net_tx_per_sec: 512 * 1024,
  net_interfaces: [{ name: 'Wi-Fi', rx_per_sec: 2 * 1024 * 1024, tx_per_sec: 512 * 1024, is_up: true }],
  net_session_rx: 100_000_000,
  net_session_tx: 30_000_000,
  public_ip: '203.0.113.99',
  disk_read_per_sec: 1024 * 1024,
  disk_write_per_sec: 256 * 1024,
  disk_read_ops_per_sec: 0,
  disk_write_ops_per_sec: 0,
  disks: [{ mount: 'C:\\', used: 250 * 1024 * 1024 * 1024, total: 500 * 1024 * 1024 * 1024, read_per_sec: 0, write_per_sec: 0 }],
};

const GPU_STATS = {
  name: 'Test GPU',
  utilization: 55,
  vram_used_mb: 2048,
  vram_total_mb: 8192,
  shared_used_mb: 0,
  shared_total_mb: 0,
  temp_c: null,
  power_w: null,
  fan_pct: null,
  encoder_pct: null,
  decoder_pct: null,
  core_clock_mhz: null,
  mem_clock_mhz: null,
};

const BATTERY_STATS = {
  percent: 77,
  is_charging: false,
  is_on_ac: false,
  seconds_remaining: 3_600 * 2,
  has_battery: true,
  design_capacity_mwh: 50_000,
  full_charge_capacity_mwh: 45_000,
  health_pct: 90,
  cycle_count: 100,
  voltage_mv: 11_800,
  temp_c: 31,
};

const BT_DEVICES = [
  { name: 'AirPods Pro', id: 'bt-1', is_connected: true,  device_type: 'audio', battery_pct: 80, rssi: null },
  { name: 'Logi Mouse',  id: 'bt-2', is_connected: false, device_type: 'mouse', battery_pct: null, rssi: null },
];

/** Polling has a 0–2s stagger before the first refresh; allow ~8s total. */
async function waitForWidgetText(fragment: string, timeout = 8_000): Promise<void> {
  await browser.waitUntil(async () => {
    const txt = await browser.execute(() => {
      const el = document.querySelector('.widget') as HTMLElement | null;
      // textContent (not innerText): the compact header uppercases the title via
      // CSS text-transform, which innerText reflects ("TEST GPU"); textContent
      // keeps the real DOM text ("Test GPU") so case-sensitive checks match.
      return el ? (el.textContent ?? '') : '';
    });
    return typeof txt === 'string' && txt.includes(fragment);
  }, { timeout, timeoutMsg: `expected widget to contain "${fragment}"` });
}

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: CPU', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': SYSTEM_STATS, '*': null });
    await seedWidgets([{ type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders cpuw-widget and shows CPU usage percentage', async () => {
    await $('.cpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('42');
  });

  it('renders sparkline when showGraph is default-on', async () => {
    await $('.cpuw-widget').waitForExist({ timeout: 4_000 });
    await browser.waitUntil(async () => (await $$('.cpuw-spark').length) > 0,
      { timeout: 8_000, timeoutMsg: 'expected .cpuw-spark sparkline to render' });
  });

  it('hides temperature row by default (visibleMetrics excludes it)', async () => {
    await $('.cpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('42');
    // Fixture provides cpu_temp_c=65, but `temperature` isn't in canonical
    // visibleMetrics so the °C row must not render.
    const txt = await browser.execute(() => {
      const el = document.querySelector('.cpuw-widget') as HTMLElement | null;
      return el ? (el.innerText ?? el.textContent ?? '') : '';
    });
    expect(txt as string).to.not.include('°C');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// cpu [S-20]: temperature row visibility follows sensor availability.
describe('system stats: CPU temperature (S-20)', () => {
  async function cpuWidgetText(): Promise<string> {
    const txt = await browser.execute(() => {
      const el = document.querySelector('.cpuw-widget') as HTMLElement | null;
      return el ? (el.innerText ?? el.textContent ?? '') : '';
    });
    return txt as string;
  }

  it('S-20: row hidden entirely when sensor unavailable (no "—°C" placeholder)', async () => {
    await resetAppState();
    await stubThemiaInvoke({
      'get_system_stats': { ...SYSTEM_STATS, cpu_temp_c: null },
      '*': null,
    });
    await seedWidgets([{
      type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'temperature'] },
    }]);
    await $('.cpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('42');
    expect(await cpuWidgetText()).to.not.include('°C');
  });

  it('S-20: row shows the value when the sensor reports one', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': SYSTEM_STATS, '*': null });
    await seedWidgets([{
      type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'temperature'] },
    }]);
    await $('.cpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('65°C');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// cpu [S-25]/[S-26]: top-process rows show the resolved friendly name (falling
// back to the exe image name) and honour the configured row count.
describe('system stats: CPU top processes (S-25/S-26)', () => {
  const PROC_STATS = {
    ...SYSTEM_STATS,
    cpu_top: [
      { name: 'chrome.exe',  pid: 1, cpu_pct: 30, desc: 'Google Chrome' },
      { name: 'code.exe',    pid: 2, cpu_pct: 20, desc: 'Visual Studio Code' },
      { name: 'mystery.exe', pid: 3, cpu_pct: 10, desc: null },
      { name: 'd.exe',       pid: 4, cpu_pct: 5,  desc: 'App D' },
      { name: 'e.exe',       pid: 5, cpu_pct: 2,  desc: 'App E' },
    ],
  };

  async function procNames(): Promise<string[]> {
    return await browser.execute(() =>
      Array.from(document.querySelectorAll('.cpuw-proc-row .cpuw-proc-name'))
        .map((el) => el.textContent ?? '')
    ) as string[];
  }

  it('S-25: rows show the friendly desc, falling back to the exe name', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': PROC_STATS, '*': null });
    await seedWidgets([{
      type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'topProcs'] },
    }]);
    await waitForWidgetText('Google Chrome');
    expect(await procNames()).to.deep.equal(
      ['Google Chrome', 'Visual Studio Code', 'mystery.exe'],
      'default count is 3; desc-less row falls back to image name');
  });

  it('S-26: topProcsCount config sets the number of rows', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': PROC_STATS, '*': null });
    await seedWidgets([{
      type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'topProcs'], topProcsCount: 5 },
    }]);
    await waitForWidgetText('App E');
    expect((await procNames()).length).to.equal(5);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// cpu [S-21] / ram [S-16]: expensive backend work is requested only when a
// visible metric consumes it. Record the args the widget actually sends.
describe('system stats: get_system_stats arg gating (S-21)', () => {
  async function stubRecordingArgs(): Promise<void> {
    await browser.execute((stats: any) => {
      const hook = (window as any).__themiaInvoke;
      if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed');
      (window as any).__statsArgs = [];
      hook.setInvoke(async (cmd: string, args: any) => {
        if (cmd === 'get_system_stats') {
          (window as any).__statsArgs.push(args ?? null);
          return stats;
        }
        return null;
      });
    }, SYSTEM_STATS as any);
  }

  async function recordedArgs(): Promise<any[]> {
    return await browser.execute(() => (window as any).__statsArgs) as any[];
  }

  it('S-21: CPU widget with default metrics requests neither procs nor temp', async () => {
    await resetAppState();
    await stubRecordingArgs();
    await seedWidgets([{ type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
    await waitForWidgetText('42');
    const args = await recordedArgs();
    expect(args.length).to.be.greaterThan(0);
    expect(args[0]).to.deep.equal({ includeProcs: false, includeTemp: false });
  });

  it('S-21: CPU widget with topProcs+temperature visible requests both', async () => {
    await resetAppState();
    await stubRecordingArgs();
    await seedWidgets([{
      type: 'cpu', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'topProcs', 'temperature'] },
    }]);
    await waitForWidgetText('42');
    const args = await recordedArgs();
    expect(args.length).to.be.greaterThan(0);
    expect(args[0]).to.deep.equal({ includeProcs: true, includeTemp: true });
  });

  it('ram S-16: RAM widget with topProcs visible requests procs but never temp', async () => {
    await resetAppState();
    await stubRecordingArgs();
    await seedWidgets([{
      type: 'ram', col: 2, row: 2, colSpan: 3, rowSpan: 2,
      config: { visibleMetrics: ['usage', 'topProcs'] },
    }]);
    await waitForWidgetText('8');
    const args = await recordedArgs();
    expect(args.length).to.be.greaterThan(0);
    expect(args[0]).to.deep.equal({ includeProcs: true, includeTemp: false });
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: GPU', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({
      'get_gpu_stats':    GPU_STATS,
      'list_gpus':        [{ index: 0, name: 'Test GPU' }],
      'get_system_stats': SYSTEM_STATS,
      '*': null,
    });
    await seedWidgets([{ type: 'gpu', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders gpuw-widget and shows utilization percentage', async () => {
    await $('.gpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('55');
  });

  it('shows GPU name from stub', async () => {
    await $('.gpuw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Test GPU');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: RAM', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': SYSTEM_STATS, '*': null });
    await seedWidgets([{ type: 'ram', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders ramw-widget', async () => {
    await $('.ramw-widget').waitForExist({ timeout: 4_000 });
  });

  it('shows used / total memory in GB', async () => {
    await waitForWidgetText('8');
    await waitForWidgetText('16');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: Disk', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': SYSTEM_STATS, '*': null });
    await seedWidgets([{ type: 'disk', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders diskw-widget', async () => {
    await $('.diskw-widget').waitForExist({ timeout: 4_000 });
  });

  it('shows disk usage percentage and sizes', async () => {
    await waitForWidgetText('50%');
    await waitForWidgetText('250');
    await waitForWidgetText('500');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: Net', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_system_stats': SYSTEM_STATS, '*': null });
    await seedWidgets([{ type: 'net', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders netw-widget', async () => {
    await $('.netw-widget').waitForExist({ timeout: 4_000 });
  });

  it('shows rx/tx speeds from stub', async () => {
    // RX 2 MB/s, TX 512 KB/s; formatter output varies by locale ("2.0 MB/s",
    // "2,0 MB/s"); assert on the digits + unit substring.
    await waitForWidgetText('2');
    await waitForWidgetText('MB/s');
    await waitForWidgetText('512');
    await waitForWidgetText('KB/s');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: Battery', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_battery_stats': BATTERY_STATS, '*': null });
    await seedWidgets([{ type: 'battery', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders battery-widget', async () => {
    await $('.battery-widget').waitForExist({ timeout: 4_000 });
  });

  it('shows percent and remaining-time status', async () => {
    await waitForWidgetText('77%');
    await waitForWidgetText('2h');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('system stats: Bluetooth', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'get_bluetooth_devices': BT_DEVICES, '*': null });
    await seedWidgets([{ type: 'bluetooth', col: 2, row: 2, colSpan: 3, rowSpan: 2 }]);
  });

  it('renders bt-widget with list of devices', async () => {
    await $('.bt-widget').waitForExist({ timeout: 4_000 });
    await browser.waitUntil(async () => (await $$('.bt-widget__item').length) >= 2,
      { timeout: 8_000, timeoutMsg: 'expected 2 bluetooth device rows' });
  });

  it('shows device names from stub', async () => {
    await waitForWidgetText('AirPods Pro');
    await waitForWidgetText('Logi Mouse');
  });

  it('renders battery percent for LE devices that report it', async () => {
    await $('.bt-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('80%');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Ping is registered again. TODO: port a real ping e2e (host config + latency
// render); backend command is `ping_hosts`.
describe.skip('system stats: Ping', () => {
  it('placeholder', () => { /* TODO: stub ping_hosts and assert latency render */ });
});
