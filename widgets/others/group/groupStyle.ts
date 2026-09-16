// Pure style resolution for the group's area surface ([S-13]). Kept out of the
// component so the mode fallback + the CSS string are unit-testable, and so a
// junk config can never reach the DOM as `background: undefined`.

/** How the area behind the member widgets is painted. "Nothing at all" is NOT
 *  a mode here: that is the shared transparent-background switch ([S-32]),
 *  which clears the title bar with the area instead of leaving a frosted bar
 *  floating over a see-through region. */
export type GroupBgMode = 'default' | 'solid';
const MODES: readonly GroupBgMode[] = ['default', 'solid'];

export interface GroupBgConfig {
  bgMode?: unknown;
  bgColor?: unknown;
  bgAlpha?: unknown;
}

export const GROUP_BG_DEFAULTS = {
  color: '#60cdff',
  alpha: 0.12,
} as const;

/** The configured mode, defaulting to the design-token wash. A dropped mode
 *  (an old `gradient` or `none` config) resolves here like any other junk. */
export function groupBgMode(v: unknown): GroupBgMode {
  return MODES.includes(v as GroupBgMode) ? v as GroupBgMode : 'default';
}

/** The configured alpha, or the default. Out of range counts as unset. */
function resolveAlpha(v: unknown): number {
  return typeof v === 'number' && v >= 0 && v <= 1 ? v : GROUP_BG_DEFAULTS.alpha;
}

function hexToRgba(hex: unknown, alpha: unknown, fallbackHex: string): string {
  const h = typeof hex === 'string' && /^#[0-9a-fA-F]{6}$/.test(hex) ? hex : fallbackHex;
  const a = resolveAlpha(alpha);
  return `rgba(${parseInt(h.slice(1, 3), 16)},${parseInt(h.slice(3, 5), 16)},${parseInt(h.slice(5, 7), 16)},${a})`;
}

/**
 * The `background` value for the group's area, or **null** to keep the
 * stylesheet's token wash (`--card-hover`). Returning null rather than a copy of
 * the token keeps the default themeable in ONE place: a user who never touches
 * the setting follows their design settings, as every other widget does — and
 * it is also what lets the transparent switch clear the wash from CSS ([S-13]),
 * since an inline value would outrank the stylesheet.
 */
export function groupAreaBackground(cfg: GroupBgConfig): string | null {
  switch (groupBgMode(cfg.bgMode)) {
    case 'solid':
      return hexToRgba(cfg.bgColor, cfg.bgAlpha, GROUP_BG_DEFAULTS.color);
    default:
      return null;
  }
}

/**
 * Does the area paint ANYTHING? ([S-13a])
 *
 * The window hit-mask follows paint, not layout (src/SPEC [S-17]): a group whose
 * area is see-through must contribute only its title bar to the region, or it
 * covers the desktop it is supposed to be showing — the wallpaper reads through,
 * but every click over the area lands on an invisible window instead of the
 * desktop icons under it. Pure, and the one place that decides it.
 *
 * `solid` at alpha 0 is see-through too: the user got there with the alpha
 * slider, and the mask has to follow what they see, not which mode they picked.
 */
export function groupAreaPaints(cfg: GroupBgConfig & { transparentBg?: unknown }): boolean {
  if (groupBgMode(cfg.bgMode) === 'solid') return resolveAlpha(cfg.bgAlpha) > 0;
  // The token wash, unless the transparent switch has cleared it ([S-32]).
  return cfg.transparentBg !== true;
}
