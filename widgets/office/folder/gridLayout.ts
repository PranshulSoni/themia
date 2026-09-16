// Pure grid layout math for the folder widget's grid / icon-grid views
// (issue #37).
//
// Columns stretch to fill the row width (`repeat(auto-fill, minmax(col, 1fr))`)
// so tiles stay evenly spread regardless of container width. Rows use a fixed
// pixel height unless the "stretch grid to fill height" option resolves true,
// in which case rows share the height equally (`1fr`). The caller passes the
// effective column width / row height (which the icon-grid view shrinks to drop
// the filename gutter), so this module stays a single source of template truth.

export interface GridTemplateInput {
  /** Effective column width (px). */
  minColWidth: number;
  /** Effective row height (px). */
  rowHeight: number;
  /** When the "stretch grid to fill height" option resolves true, rows share
   *  the height equally (`1fr`) instead of using a fixed pixel height. */
  stretchHeight: boolean;
}

export interface GridTemplate {
  /** value for `grid-template-columns` */
  columns: string;
  /** value for `grid-auto-rows` */
  autoRows: string;
}

/**
 * Compute the CSS grid template for a given column width + row height. Columns
 * stretch to fill the row (`minmax(col, 1fr)`); rows use a fixed pixel height
 * unless `stretchHeight` is true (then `1fr`).
 */
export function gridTemplate(input: GridTemplateInput): GridTemplate {
  const autoRows = input.stretchHeight ? '1fr' : `${input.rowHeight}px`;
  return { columns: `repeat(auto-fill, minmax(${input.minColWidth}px, 1fr))`, autoRows };
}
