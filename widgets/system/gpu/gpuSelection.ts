/**
 * GPU source selection + multi-item layout (gpu [S-24], [S-25], [S-26]).
 *
 * The GPU widget generalises the original single-GPU picker to a multi-select:
 * a widget may monitor one or several GPUs. These helpers own the two pieces of
 * pure logic that need pinning by tests — migrating/validating the persisted
 * selection, and deciding whether the selected items stretch or overflow-scroll.
 */

/** Persisted config shape (unknown-typed: it crosses the JSON trust boundary). */
export interface GpuSelectionConfig {
  /** Multi-select GPU registry indexes (current field). */
  gpuIndexes?: unknown;
  /** Legacy single-select GPU index (migrated to `[gpuIndex]`). */
  gpuIndex?: unknown;
}

/** Keep only valid, deduped, non-negative integer indexes, preserving order. */
function normalizeIndexList(raw: unknown): number[] {
  if (!Array.isArray(raw)) return [];
  const out: number[] = [];
  for (const v of raw) {
    if (typeof v === 'number' && Number.isInteger(v) && v >= 0 && !out.includes(v)) {
      out.push(v);
    }
  }
  return out;
}

/**
 * Resolve the GPU indexes a widget monitors, migrating legacy config and
 * validating at the trust boundary (gpu [S-24], [S-25]).
 *
 * Precedence:
 *   1. `gpuIndexes` (new field) when it yields ≥1 valid index.
 *   2. `gpuIndex` (legacy single-select) migrated to a one-element array.
 *   3. `[0]` — the primary GPU — when the selection is absent or wholly corrupt.
 *
 * Distinguishes "absent" (→ default) from "corrupt" (invalid entries dropped;
 * a wholly-invalid `gpuIndexes` falls through to the legacy field, then default),
 * so a bad persisted value never crashes the widget or blanks the display.
 */
export function resolveGpuIndexes(cfg: GpuSelectionConfig | null | undefined): number[] {
  const c = cfg ?? {};
  const multi = normalizeIndexList(c.gpuIndexes);
  if (multi.length > 0) return multi;

  const legacy = c.gpuIndex;
  if (typeof legacy === 'number' && Number.isInteger(legacy) && legacy >= 0) {
    return [legacy];
  }
  return [0];
}

/**
 * The GPU selection the config picker should tick (gpu [S-28]).
 *
 * Like `resolveGpuIndexes` it migrates the legacy scalar and drops invalid
 * entries, but it does NOT apply the `[0]` runtime default: that default is a
 * *display* concern (the widget always renders the primary GPU, [S-25]), not a
 * *picker* one. Unticking the last GPU writes `gpuIndexes: []`; the picker must
 * then show nothing ticked, so an explicit array (empty or wholly-corrupt)
 * resolves to `[]` instead of re-ticking the primary GPU. Only a truly-absent
 * `gpuIndexes` field migrates the legacy `gpuIndex` so an un-migrated config
 * still shows its tick.
 */
export function selectedGpuIndexes(cfg: GpuSelectionConfig | null | undefined): number[] {
  const c = cfg ?? {};
  const multi = normalizeIndexList(c.gpuIndexes);
  if (multi.length > 0) return multi;
  // A present array is a deliberate selection (the user unticked everything) —
  // honour it as empty rather than falling through to the legacy field/default.
  if (Array.isArray(c.gpuIndexes)) return [];
  const legacy = c.gpuIndex;
  if (typeof legacy === 'number' && Number.isInteger(legacy) && legacy >= 0) {
    return [legacy];
  }
  return [];
}

export type ItemLayout = 'stretch' | 'overflow';

/**
 * The multi-item layout rule shared by "one-or-many" system widgets (gpu [S-26]):
 *   - one selected item  → `stretch` to fill the widget (unchanged single-item view);
 *   - two or more items  → `overflow`: stack them, each stretched, and let the body
 *     scroll when the stack exceeds the widget height.
 */
export function itemLayout(count: number): ItemLayout {
  return count >= 2 ? 'overflow' : 'stretch';
}
