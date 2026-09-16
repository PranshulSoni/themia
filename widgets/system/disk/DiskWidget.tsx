import { createSignal, createMemo, For, Index } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoDiskHistory } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { resolveMetrics, canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { activeDisk, selectedDiskIo } from './diskIo.ts';
import { resolveMounts, diskLayout, selectedDisks } from './diskMounts.ts';
import { createDiskHistory } from './diskHistory.ts';
import { METRIC_DEFS } from './metrics.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './DiskWidget.css';
import type { JSX } from 'solid-js';
import { refreshInterval } from '../refreshInterval.ts';
import { graphSpanSec, windowCapacity, sampleX, GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';

export interface DiskWidgetProps {
  headerMode?: HeaderMode;
  interval?: number;
  /** @deprecated legacy single-mount config; migrated to `mounts` on load (#67, [S-23]). */
  mount?: string;
  mounts?: string[];
  showGraph?: boolean;
  label?: string;
  visibleMetrics?: unknown;
  graphSpanSec?: number;
}

interface DiskInfo {
  mount: string;
  used: number;
  total: number;
  read_per_sec?: number;
  write_per_sec?: number;
}

interface SystemStats {
  disk_read_ops_per_sec?: number;
  disk_write_ops_per_sec?: number;
  disks?: DiskInfo[];
}

interface SparkPaths {
  line: string;
  area: string;
}

const uid = Math.random().toString(36).slice(2, 8);

function fmtSpeed(b: number): string {
  if (b >= 1_073_741_824) return (b / 1_073_741_824).toFixed(2) + ' GB/s';
  if (b >= 1_048_576)     return (b / 1_048_576).toFixed(1)     + ' MB/s';
  if (b >= 1_024)         return (b / 1_024).toFixed(0)         + ' KB/s';
  return b + ' B/s';
}

function fmtSize(b: number): string {
  if (b >= 1_073_741_824) return (b / 1_073_741_824).toFixed(0) + ' GB';
  return (b / 1_048_576).toFixed(0) + ' MB';
}

function buildSpark(pts: number[], maxVal: number, capacity: number): SparkPaths {
  if (pts.length < 2 || maxVal === 0) return { line: '', area: '' };
  const n = pts.length;
  const coords = pts.map((v, i) => [
    sampleX(i, n, capacity),
    +(100 - (v / maxVal) * 100).toFixed(2),
  ]);
  const line = 'M ' + coords.map(([x, y]) => `${x} ${y}`).join(' L ');
  const area = `M ${coords[0][0]} 100 ` +
               coords.map(([x, y]) => `L ${x} ${y}`).join(' ') +
               ` L ${coords.at(-1)![0]} 100 Z`;
  return { line, area };
}

function barColor(p: number): string {
  if (p >= 90) return 'var(--destructive)';
  if (p >= 80) return '#f59e0b';
  return 'var(--primary)';
}

export default function DiskWidget(props: DiskWidgetProps): JSX.Element {
  const interval  = () => refreshInterval(props.interval, 'disk');
  const label     = () => props.label ?? '';
  const capacity  = createMemo(() =>
    windowCapacity(graphSpanSec(props.graphSpanSec, GRAPH_SPAN_DEFAULTS.disk), interval()));

  // #67: `mounts[]` (migrated from legacy `mount`) drives both selection and
  // layout. 0/1 selected => single-disk view (stretch to fill, as before);
  // 2+ => stacked per-drive bars that overflow-scroll ([S-23], [S-24]).
  const mounts       = createMemo<string[]>(() => resolveMounts(props));
  const layout       = createMemo(() => diskLayout(mounts().length));
  const primaryMount = () => mounts()[0] ?? '';

  // #64: open a drive in Explorer via the shared command.
  function openDrive(mountPath: string): void {
    if (!mountPath) return;
    invoke('open_in_explorer', { path: mountPath })
      .catch(e => logger.error('disk', `open_in_explorer failed: ${e}`));
  }

  const visible = createMemo<string[]>(() => {
    const raw = props.visibleMetrics;
    if (raw === undefined || raw === null) {
      const out = canonicalVisible(METRIC_DEFS).slice();
      if (props.showGraph === false) {
        const i = out.indexOf('graph');
        if (i >= 0) out.splice(i, 1);
      }
      return out;
    }
    return resolveMetrics(raw, METRIC_DEFS).visible;
  });
  const shows = (k: string) => visible().includes(k);

  const [stats, setStats] = createSignal<SystemStats | null>(null);

  // Per-partition sparkline history: push() appends the *selected* disk's rate,
  // and switching mount clears the series ([S-16], [S-22], #49).
  const hist = createDiskHistory(primaryMount, {
    max: capacity,
    seed: isDemo() ? demoDiskHistory(capacity()) : undefined,
  });

  async function refresh() {
    try {
      const s = await invoke('get_system_stats', {});
      setStats(s);
      hist.push(s);
    } catch (e) { logger.error('disk', `get_system_stats failed: ${e}`); }
  }

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'disk');

  const readWin    = createMemo(() => hist.read().slice(-capacity()));
  const writeWin   = createMemo(() => hist.write().slice(-capacity()));
  const scaleMax   = createMemo(() => Math.max(1024, ...readWin(), ...writeWin()));
  const readSpark  = createMemo(() => buildSpark(readWin(), scaleMax(), capacity()));
  const writeSpark = createMemo(() => buildSpark(writeWin(), scaleMax(), capacity()));

  const disk    = createMemo(() => activeDisk(stats(), primaryMount()));
  const io      = createMemo(() => selectedDiskIo(stats(), primaryMount()));
  const selected = createMemo(() => selectedDisks(stats(), mounts()));
  const diskPct = createMemo(() => {
    const d = disk();
    return d?.total ? (d.used / d.total) * 100 : 0;
  });

  const gradReadId  = `disk-r-${uid}`;
  const gradWriteId = `disk-w-${uid}`;

  return (
    <div class="diskw-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        title={label() || (layout() === 'multi' ? 'Disks' : (disk()?.mount || 'Disk'))}
        secondary={
          <Show when={stats() && shows('ioBytes') && layout() === 'single'}>
            <span class="diskw-info-read">R {fmtSpeed(io().read)}</span>
            <span class="diskw-info-write">W {fmtSpeed(io().write)}</span>
          </Show>
        }
      />

      <div class="diskw-body widget-body">
      <Show when={layout() === 'single'}>
        <Show when={shows('graph')}>
          <div class="diskw-spark-wrap">
            <Show when={stats()}>
              <svg class="diskw-spark" viewBox="0 0 100 100" preserveAspectRatio="none">
                <defs>
                  <linearGradient id={gradReadId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                    <stop offset="0%" stop-color="var(--success)" stop-opacity="0.25"/>
                    <stop offset="100%" stop-color="var(--success)" stop-opacity="0"/>
                  </linearGradient>
                  <linearGradient id={gradWriteId} x1="0" y1="0" x2="0" y2="100" gradientUnits="userSpaceOnUse">
                    <stop offset="0%" stop-color="var(--primary)" stop-opacity="0.25"/>
                    <stop offset="100%" stop-color="var(--primary)" stop-opacity="0"/>
                  </linearGradient>
                </defs>
                <path d={readSpark().area}  fill={`url(#${gradReadId})`} />
                <path d={readSpark().line}  fill="none" stroke="var(--success)" stroke-width="1.5"
                      stroke-linecap="round" stroke-linejoin="round"
                      vector-effect="non-scaling-stroke" />
                <path d={writeSpark().area} fill={`url(#${gradWriteId})`} />
                <path d={writeSpark().line} fill="none" stroke="var(--primary)" stroke-width="1.5"
                      stroke-linecap="round" stroke-linejoin="round"
                      vector-effect="non-scaling-stroke" />
              </svg>
            </Show>
          </div>
        </Show>

        <Show when={disk() && shows('space')}>
          <div class="diskw-drive diskw-drive-single"
               title={`Open ${disk()!.mount} in Explorer`}
               onClick={() => openDrive(disk()!.mount)}>
            <div class="diskw-space-row">
              <span class="diskw-used">{fmtSize(disk()!.used)}</span>
              <span class="diskw-sep">/</span>
              <span class="diskw-total">{fmtSize(disk()!.total)}</span>
              <span class="diskw-pct" style={{ color: barColor(diskPct()) }}>{diskPct().toFixed(0)}%</span>
            </div>
            <div class="diskw-bar-track">
              <div class="diskw-bar-fill" style={{ width: `${diskPct()}%`, background: barColor(diskPct()) }} />
            </div>
          </div>
        </Show>

        <Show when={shows('iops') && (stats()?.disk_read_ops_per_sec != null || stats()?.disk_write_ops_per_sec != null)}>
          <div class="diskw-aux-row">
            <span class="diskw-aux-label">IOPS</span>
            <span class="diskw-aux-val">
              R {stats()!.disk_read_ops_per_sec ?? 0} / W {stats()!.disk_write_ops_per_sec ?? 0}
            </span>
          </div>
        </Show>

        <Show when={shows('perDisk') && (stats()?.disks?.length ?? 0) > 0}>
          <div class="diskw-procs">
            <For each={stats()!.disks!}>
              {(d) => (
                <Show when={d.read_per_sec != null || d.write_per_sec != null}>
                  <div class="diskw-proc-row">
                    <span class="diskw-proc-name">{d.mount}</span>
                    <span class="diskw-proc-val">R {fmtSpeed(d.read_per_sec ?? 0)} W {fmtSpeed(d.write_per_sec ?? 0)}</span>
                  </div>
                </Show>
              )}
            </For>
          </div>
        </Show>
      </Show>

      <Show when={layout() === 'multi'}>
        <div class="diskw-multi">
          <Index each={selected()}>
            {(d) => {
              const pct = () => (d().total ? (d().used / d().total) * 100 : 0);
              return (
                <div class="diskw-drive"
                     title={`Open ${d().mount} in Explorer`}
                     onClick={() => openDrive(d().mount)}>
                  <div class="diskw-drive-row">
                    <span class="diskw-drive-name">{d().mount}</span>
                    <span class="diskw-used">{fmtSize(d().used)}</span>
                    <span class="diskw-sep">/</span>
                    <span class="diskw-total">{fmtSize(d().total)}</span>
                    <span class="diskw-pct" style={{ color: barColor(pct()) }}>{pct().toFixed(0)}%</span>
                  </div>
                  <div class="diskw-bar-track">
                    <div class="diskw-bar-fill" style={{ width: `${pct()}%`, background: barColor(pct()) }} />
                  </div>
                </div>
              );
            }}
          </Index>
        </div>
      </Show>
      </div>
    </div>
  );
}
