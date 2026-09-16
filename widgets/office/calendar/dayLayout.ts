// Column packing for overlapping day-view events ([S-06]). Timed events that
// overlap in time are placed in side-by-side lanes so none hides another; a run
// of mutually-overlapping events shares one column count so their widths align.

export interface TimedSpan { start: number; end: number; }
export interface LaneAssignment { col: number; cols: number; }

/** Assign each span a column and the column count of its overlap cluster.
 *  Assignments are returned in the SAME order as the input array. Touching
 *  intervals (a.end === b.start) do NOT count as overlapping. */
export function assignEventLanes(spans: readonly TimedSpan[]): LaneAssignment[] {
  const n = spans.length;
  const result: LaneAssignment[] = new Array(n);
  // Walk in start order (ties broken by end) but remember the original index.
  const order = spans.map((_, i) => i).sort((a, b) =>
    spans[a].start - spans[b].start || spans[a].end - spans[b].end);

  let group: number[] = [];        // original indices in the current overlap cluster
  let colEnds: number[] = [];      // last end time per column in the current cluster
  const colOf = new Array<number>(n);
  let groupMaxEnd = -Infinity;

  const flush = () => {
    const cols = colEnds.length;
    for (const idx of group) result[idx] = { col: colOf[idx], cols };
    group = [];
    colEnds = [];
    groupMaxEnd = -Infinity;
  };

  for (const idx of order) {
    const s = spans[idx];
    if (group.length && s.start >= groupMaxEnd) flush(); // starts after the whole cluster
    let col = colEnds.findIndex(end => end <= s.start);   // first free column
    if (col === -1) { col = colEnds.length; colEnds.push(s.end); }
    else colEnds[col] = s.end;
    colOf[idx] = col;
    group.push(idx);
    groupMaxEnd = Math.max(groupMaxEnd, s.end);
  }
  flush();
  return result;
}
