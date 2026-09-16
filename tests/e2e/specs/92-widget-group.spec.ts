/**
 * Group (area) widget: the container whose members stay on the desktop grid
 * (group SPEC [S-01]..[S-09], src/SPEC.md [S-36]/[S-37]/[S-38]).
 *
 * Covers:
 *  - an expanded group's members are the GRID's own tiles, at their own
 *    geometry -- the group never lays them out ([S-36])
 *  - collapsing hides every member and shrinks the group to its title bar,
 *    while the stored area is left untouched, so expanding restores it ([S-37])
 *  - the title bar toggles at rest, and the chevron toggles in edit mode too,
 *    so members are never unreachable where they are rearranged ([S-03])
 *  - the bar carries no member count, in either state ([S-05])
 *  - the switch between the two states is animated ([S-16] / src [S-43])
 *  - an EMPTY area stays visible and offers the creation path ([S-07])
 *  - removing the group leaves its members on the grid ([S-21])
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, waitForReady, stubConfirm, rightClick, ctxMenuItem,
  enterEditMode, leaveEditMode, clickGridCell, waitForPersist,
} from '../helpers/app.js';

interface SeedMember { id: string; type: string; x: number; y: number }

/**
 * Press on `selector` and drag by (dx, dy), leaving the button DOWN so the
 * mid-drag chrome can be inspected. Release with `dragRelease`.
 *
 * Synthetic MouseEvents rather than a WebDriver pointer action, for the reason
 * 02-drag-resize-persist documents: in WebView2 a pointer action does not
 * reliably reach the window-level `mousemove` listeners `gridDragManager`
 * hangs the drag off, so the drag never starts and there is no ghost to look
 * at. Dispatching on the element (which bubbles to the tile) plus window and
 * document covers every listener target.
 */
async function dragHold(selector: string, dx: number, dy: number, steps = 20): Promise<void> {
  await browser.execute((sel: string, ddx: number, ddy: number, n: number) => {
    const el = document.querySelector(sel) as HTMLElement | null;
    if (!el) throw new Error(`dragHold: no element for "${sel}"`);
    const r = el.getBoundingClientRect();
    const sx = Math.round(r.left + r.width / 2);
    const sy = Math.round(r.top + r.height / 2);
    const fire = (type: string, x: number, y: number, tgt: EventTarget, buttons: number) => {
      tgt.dispatchEvent(new MouseEvent(type, {
        bubbles: true, cancelable: true, view: window,
        clientX: x, clientY: y, button: 0, buttons,
      }));
    };
    fire('mousedown', sx, sy, el, 1);
    for (let i = 1; i <= n; i++) {
      const x = sx + Math.round((ddx * i) / n);
      const y = sy + Math.round((ddy * i) / n);
      fire('mousemove', x, y, window, 1);
      fire('mousemove', x, y, document, 1);
    }
    (window as any).__e2eDragEnd = { x: sx + ddx, y: sy + ddy };
  }, selector, dx, dy, steps);
}

/** Let go where `dragHold` left the pointer. */
async function dragRelease(): Promise<void> {
  await browser.execute(() => {
    const p = (window as any).__e2eDragEnd ?? { x: 0, y: 0 };
    const fire = (tgt: EventTarget) => tgt.dispatchEvent(new MouseEvent('mouseup', {
      bubbles: true, cancelable: true, view: window,
      clientX: p.x, clientY: p.y, button: 0, buttons: 0,
    }));
    fire(window);
    fire(document);
  });
  await browser.pause(100);
}

/** Seed an area group + members in PERCENT shape (parentId passes through the
 *  seed hook untouched). The area is x 0.2..0.7, y 0.1..0.7. */
async function seedGroup(
  cfg: Record<string, unknown>,
  members: SeedMember[],
): Promise<void> {
  await browser.execute((groupCfg: Record<string, unknown>, kids: SeedMember[]) => {
    const seed = (window as any).__themiaSeedWidgets;
    if (typeof seed !== 'function') throw new Error('__themiaSeedWidgets not exposed');
    seed([
      { id: 'group-e2e', type: 'group', x: 0.2, y: 0.1, w: 0.5, h: 0.6, config: groupCfg },
      ...kids.map(m => ({
        id: m.id, type: m.type, x: m.x, y: m.y, w: 0.15, h: 0.15,
        parentId: 'group-e2e', config: {},
      })),
    ]);
  }, cfg, members);
  await browser.pause(100);
  await $('.group-widget').waitForDisplayed({ timeout: 3_000 });
}

const gridWidgetCount = () =>
  browser.execute(() => document.querySelectorAll('.grid > .widget').length);

/** Rendered height of the group's own grid tile, in px. */
const groupTileHeight = () => browser.execute(() => {
  const el = document.querySelector('.group-widget')?.closest('.widget');
  return el ? Math.round(el.getBoundingClientRect().height) : -1;
});

const TWO_MEMBERS: SeedMember[] = [
  { id: 'clock-e2e', type: 'clock', x: 0.25, y: 0.2 },
  { id: 'notes-e2e', type: 'notes', x: 0.45, y: 0.2 },
];

describe('widget: Group (area)', () => {
  beforeEach(async () => {
    await resetAppState();
    await waitForReady();
  });

  it('S-36_an expanded group renders its members as the grid\'s own tiles', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    // 1 group + 2 members: unlike a stack, the members are grid rows ([S-36]).
    expect(await gridWidgetCount()).to.equal(3);
    expect(await $$('.grid > .widget .clock-widget').length).to.equal(1);
    expect(await $$('.grid > .widget .notes-widget').length).to.equal(1);
    // They sit where they were placed, not inside the group's DOM.
    expect(await $$('.group-widget .clock-widget').length).to.equal(0);
  });

  it('S-37_collapsing hides every member and shrinks the group to its title bar', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    const openHeight = await groupTileHeight();
    expect(openHeight).to.be.greaterThan(100);

    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, {
      timeout: 3_000, timeoutMsg: 'members stayed on the grid after collapse',
    });
    // The tile is now the title bar alone (32px + the widget-gap inset). Read
    // through waitUntil: the tile TRANSITIONS to the bar ([S-43]), so a single
    // read can land mid-animation.
    await browser.waitUntil(async () => {
      const h = await groupTileHeight();
      return h >= 24 && h <= 44;
    }, { timeout: 3_000, timeoutMsg: 'the tile never settled on the title bar' });
    expect(await $$('.grid > .widget .clock-widget').length).to.equal(0);
  });

  it('S-37_expanding restores the SAME area and every member', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    const openHeight = await groupTileHeight();

    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, { timeout: 3_000 });
    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 3, {
      timeout: 3_000, timeoutMsg: 'members did not come back',
    });
    // The stored rect was never touched, so the area comes back identical —
    // once the open animation has finished travelling there ([S-43]).
    await browser.waitUntil(async () => (await groupTileHeight()) === openHeight, {
      timeout: 3_000, timeoutMsg: 'the area did not come back to its stored height',
    });
  });

  it('S-03_the title bar toggles at rest; the chevron still toggles in edit mode', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    // At rest: the whole band is the target.
    await $('.group-head').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, {
      timeout: 3_000, timeoutMsg: 'band click did not collapse the group',
    });

    // In edit mode the band is the drag handle, so the chevron BUTTON is the
    // way back -- otherwise the members would be unreachable exactly where the
    // user goes to rearrange them.
    await enterEditMode();
    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 3, {
      timeout: 3_000, timeoutMsg: 'chevron did not expand the group in edit mode',
    });
    await leaveEditMode();
  });

  it('S-05_the bar carries no member count, collapsed or not', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    expect(await $$('.group-count').length).to.equal(0);
    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, { timeout: 3_000 });
    // Collapsed is exactly where a badge used to appear.
    expect(await $$('.group-count').length).to.equal(0);
  });

  it('S-16_collapsing is animated: the members fade with the area before they go', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    // The duration is published once, by the grid, from the same constant the
    // phase timer runs on ([S-43]) — CSS and the JS clock cannot drift.
    const ms = await browser.execute(() => getComputedStyle(
      document.querySelector('.grid') as HTMLElement,
    ).getPropertyValue('--group-collapse-ms').trim());
    expect(ms).to.match(/^\d+ms$/);

    // Click and read in ONE execute: the fade window is ~200ms, far too short
    // to catch over two webdriver round-trips. Solid flushes the toggle and the
    // phase effect before `click()` returns, which is the contract — if the
    // phase landed a tick late the members would blink out and back in.
    const onToggle = await browser.execute(() => {
      (document.querySelector('.group-chevron') as HTMLElement).click();
      return {
        tiles: document.querySelectorAll('.grid > .widget').length,
        leaving: document.querySelectorAll('.grid > .widget.group-leaving').length,
      };
    });
    // Still mounted, and fading out with the area rather than blinking away a
    // frame before the area under them moves.
    expect(onToggle.tiles).to.equal(3);
    expect(onToggle.leaving).to.equal(2);

    // Gone once the phase ends, with no class left stuck on anything.
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, {
      timeout: 3_000, timeoutMsg: 'members never unmounted after the close',
    });
    expect(await $$('.widget.group-leaving').length).to.equal(0);
  });

  it('S-07_an empty area stays visible and offers Add widget', async () => {
    await seedGroup({ collapsed: false }, []);
    expect(await $('.group-empty').isDisplayed()).to.equal(true);
    expect(await $('.group-empty-add').isDisplayed()).to.equal(true);
    // The drag hint is an edit-mode affordance only.
    expect(await $$('.group-empty-hint').length).to.equal(0);
    await enterEditMode();
    await $('.group-empty-hint').waitForDisplayed({ timeout: 3_000 });
    await leaveEditMode();
  });

  it('S-11_the header sits on the configured edge, and a side header turns vertical', async () => {
    await seedGroup({ collapsed: false, headerSide: 'bottom' }, TWO_MEMBERS);
    const geom = () => browser.execute(() => {
      const root = document.querySelector('.group-widget') as HTMLElement;
      const head = document.querySelector('.group-head') as HTMLElement;
      const area = document.querySelector('.group-area') as HTMLElement;
      return {
        headTop: Math.round(head.getBoundingClientRect().top),
        areaTop: Math.round(area.getBoundingClientRect().top),
        headW: Math.round(head.getBoundingClientRect().width),
        writing: getComputedStyle(head).writingMode,
        cls: root.className,
      };
    });
    // Bottom: the bar is BELOW the area.
    let g = await geom();
    expect(g.cls).to.contain('side-bottom');
    expect(g.headTop).to.be.greaterThan(g.areaTop);

    // Left: the bar becomes a 32px vertical band.
    await seedGroup({ collapsed: false, headerSide: 'left' }, TWO_MEMBERS);
    g = await geom();
    expect(g.cls).to.contain('side-left');
    expect(g.headW).to.equal(32);
    // Bottom-to-top, the way a side tab label reads ([S-11]).
    expect(g.writing).to.equal('sideways-lr');
  });

  it('S-15_the border is the grid tile\'s own, and can be switched off', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    const b = await browser.execute(() => {
      const g = document.querySelector('.group-widget') as HTMLElement;
      const edge = (el: Element) => {
        const s = getComputedStyle(el);
        return [s.borderTopWidth, s.borderTopStyle, s.borderTopColor, s.borderTopLeftRadius];
      };
      return {
        groupTile: edge(g.closest('.widget')!),
        memberTile: edge(document.querySelector('.clock-widget')!.closest('.widget')!),
        ownBorder: getComputedStyle(g).borderTopWidth,
      };
    });
    // The SAME rule paints both, so a group lines up with the widgets in it.
    expect(b.groupTile).to.deep.equal(b.memberTile);
    // …and the group draws no second border inside the tile's slot.
    expect(b.ownBorder).to.equal('0px');

    await seedGroup({ collapsed: false, borderMode: 'none' }, TWO_MEMBERS);
    const off = await browser.execute(() => {
      // A visible width, so the ring is measurable: the token defaults to 0,
      // which paints nothing whichever mode the group is in.
      document.documentElement.style.setProperty('--widget-border-width', '2px');
      const el = document.querySelector('.group-head') as HTMLElement;
      const head = getComputedStyle(el);
      const tile = getComputedStyle((document.querySelector('.group-widget') as HTMLElement)
        .closest('.widget')!);
      return {
        tile: tile.borderTopColor,
        tileBorderPx: parseFloat(tile.borderTopWidth),
        headTop: [head.borderTopWidth, head.borderTopStyle],
        // The ring closes on the body-facing edge too, replacing the divider:
        // stopping short of it read as a bar missing its bottom border.
        headBottom: [head.borderBottomWidth, head.borderBottomStyle],
        headRadius: head.borderTopLeftRadius,
        headBox: Math.round(el.getBoundingClientRect().height),
      };
    });
    // The CONTENT loses the border…
    expect(off.tile).to.match(/rgba\(0, 0, 0, 0\)|transparent/);
    // …and the title bar keeps one ([S-15]), at the widget border width, rounded
    // to follow the tile's clip, without changing the 32px the collapsed rect is
    // cut to ([S-04]).
    expect(off.headTop).to.deep.equal(['2px', 'solid']);
    expect(off.headBottom).to.deep.equal(['2px', 'solid']);
    expect(parseFloat(off.headRadius)).to.be.greaterThan(0);
    // The tile keeps its border SLOT (only the colour goes transparent) and the
    // ring is drawn one border in on both sides, so the bar's own box gives up
    // that width top and bottom. The band the user sees is still the 32px the
    // collapsed rect is cut to ([S-04]) — measure it, not the inner box.
    expect(off.headBox + 2 * off.tileBorderPx).to.equal(32);
  });

  it('S-04_the collapsed bar fits the tile, so its far border is not clipped away', async () => {
    // Regression: the bar kept a fixed 32px while GROUP_HEADER_PX is the tile's
    // OUTER size, so it overflowed the content box by one border width per side
    // and `overflow: hidden` ate the far edge — the ring's bottom border on a
    // top header, its left on a right-hand one.
    for (const side of ['top', 'left', 'bottom', 'right']) {
      await seedGroup({ collapsed: true, headerSide: side, borderMode: 'none' }, TWO_MEMBERS);
      const m = await browser.execute((s: string) => {
        document.documentElement.style.setProperty('--widget-border-width', '2px');
        const tile = (document.querySelector('.group-widget') as HTMLElement).closest('.widget') as HTMLElement;
        const head = document.querySelector('.group-head') as HTMLElement;
        const r = tile.getBoundingClientRect(), hr = head.getBoundingClientRect();
        const cs = getComputedStyle(tile), h = getComputedStyle(head);
        const b = (v: string) => parseFloat(v);
        return {
          side: s,
          // Every edge of the bar sits inside the tile's border, none past it.
          overflow: [
            Math.round((r.left + b(cs.borderLeftWidth)) - hr.left),
            Math.round(hr.right - (r.right - b(cs.borderRightWidth))),
            Math.round((r.top + b(cs.borderTopWidth)) - hr.top),
            Math.round(hr.bottom - (r.bottom - b(cs.borderBottomWidth))),
          ],
          ring: [h.borderTopWidth, h.borderRightWidth, h.borderBottomWidth, h.borderLeftWidth],
          // …and the collapsed tile is still the 32px band ([S-37]).
          band: Math.round(Math.min(r.width, r.height)),
        };
      }, side);
      expect(m.overflow, `${side}: bar overflows the tile`).to.deep.equal([0, 0, 0, 0]);
      expect(m.ring, `${side}: the ring closes on all four sides`)
        .to.deep.equal(['2px', '2px', '2px', '2px']);
      expect(m.band, `${side}: still the 32px band`).to.equal(32);
    }
  });

  it('S-04_the bar is pixel-identical open and shut', async () => {
    // The 32px is measured from the TILE's outer edge in both states, so the
    // bar's content box lands in the same place either way. Before: expanded it
    // was a full 32px BELOW the tile's border (plus a divider row of its own),
    // collapsed it was 32 minus both borders — the band and the title shifted
    // as the group opened.
    const bar = () => browser.execute(() => {
      document.documentElement.style.setProperty('--widget-border-width', '2px');
      const tile = (document.querySelector('.group-widget') as HTMLElement).closest('.widget') as HTMLElement;
      const head = document.querySelector('.group-head') as HTMLElement;
      const icon = document.querySelector('.group-icon') as HTMLElement;
      const r = tile.getBoundingClientRect(), hr = head.getBoundingClientRect();
      const ir = icon.getBoundingClientRect(), h = getComputedStyle(head);
      const n = (v: string) => parseFloat(v);
      // Everything relative to the tile's outer edge, across the bar's thin axis.
      const vertical = head.classList.contains('vertical');
      return vertical ? {
        near:   +(hr.left + n(h.borderLeftWidth) - r.left).toFixed(2),
        far:    +(hr.right - n(h.borderRightWidth) - r.left).toFixed(2),
        centre: +(ir.left + ir.width / 2 - r.left).toFixed(2),
      } : {
        near:   +(hr.top + n(h.borderTopWidth) - r.top).toFixed(2),
        far:    +(hr.bottom - n(h.borderBottomWidth) - r.top).toFixed(2),
        centre: +(ir.top + ir.height / 2 - r.top).toFixed(2),
      };
    });

    for (const side of ['top', 'left']) {
      for (const borderMode of ['theme', 'none']) {
        await seedGroup({ collapsed: false, headerSide: side, borderMode }, TWO_MEMBERS);
        const open = await bar();
        await seedGroup({ collapsed: true, headerSide: side, borderMode }, TWO_MEMBERS);
        const shut = await bar();
        expect(shut, `${side}/${borderMode}: the bar moves when the group opens`)
          .to.deep.equal(open);
        // …and it really is the 32px band, not two matching-but-wrong bars.
        expect(open.far - open.near, `${side}/${borderMode}: bar thickness`)
          .to.equal(borderMode === 'none' ? 24 : 28); // 32 − 2×2 (− 2×2 for the ring)
      }
    }
  });

  it('S-13a_a see-through area masks its title bar only, not the whole tile', async () => {
    // Painted area: the tile masks its own box, like any other widget.
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    const tileEl = () => browser.execute(() => {
      const el = (document.querySelector('.group-widget') as HTMLElement).closest('.widget') as HTMLElement;
      return {
        band: el.dataset.maskRect ?? null,
        h: Math.round(el.getBoundingClientRect().height),
      };
    });
    expect((await tileEl()).band).to.equal(null);

    // Transparent: the area shows the desktop, so only the bar may be masked —
    // otherwise an invisible window swallows the clicks meant for the icons
    // under it ([S-13a]).
    await seedGroup({ collapsed: false, transparentBg: true }, TWO_MEMBERS);
    const t = await tileEl();
    expect(t.band, 'the tile publishes a band').to.be.a('string');
    const [, , , bandH] = t.band!.split(',').map(Number);
    expect(bandH).to.be.within(24, 44);      // the title bar, not the area
    expect(t.h).to.be.greaterThan(bandH * 2); // …while the tile stays big

    // An EMPTY transparent area keeps the whole rect: its "add a widget"
    // placeholder is real paint, and a clipped placeholder hides the way in.
    await seedGroup({ collapsed: false, transparentBg: true }, []);
    expect((await tileEl()).band).to.equal(null);
  });

  it('S-12_the group shows its own title and icon', async () => {
    await seedGroup({ collapsed: false, title: 'Work stuff', icon: 'briefcase' }, TWO_MEMBERS);
    expect((await $('.group-title').getText()).trim()).to.equal('Work stuff');
    // The picked icon replaces the default group glyph.
    expect(await $$('.group-icon svg').length).to.equal(1);
  });

  it('S-14_the area carries no padding, so widgets can sit flush with its edge', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    const pad = await browser.execute(() => {
      const cs = getComputedStyle(document.querySelector('.group-area') as HTMLElement);
      return [cs.paddingTop, cs.paddingRight, cs.paddingBottom, cs.paddingLeft];
    });
    expect(pad).to.deep.equal(['0px', '0px', '0px', '0px']);
  });

  it('S-40_dragging a group shows where its header splits from the body', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    await enterEditMode();

    await dragHold('.group-head', 120, 120);
    // Mid-drag: the ghost marks the outer bound AND the header split.
    expect(await $$('.ghost').length).to.equal(1);
    expect(await $$('.ghost .ghost-split').length).to.equal(1);
    await dragRelease();

    // A plain widget's ghost has no split line. `:not(.ghost-guide)` because
    // this clock is a MEMBER of the group, so dragging it also raises the
    // area's own guide — which does carry a split, and is the other half of
    // [S-40]. The question here is only what the DRAGGED widget's ghost shows.
    await dragHold('.grid > .widget .clock-widget', 120, 120);
    expect(await $$('.ghost:not(.ghost-guide) .ghost-split').length).to.equal(0);
    await dragRelease();

    await leaveEditMode();
  });

  it('S-41_copying a group copies the widgets inside it', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    await enterEditMode();
    await rightClick('.group-head');
    await (await ctxMenuItem('Copy widget')).click();
    await $('.grid.placing').waitForDisplayed({ timeout: 3_000 });
    await clickGridCell(8, 6);
    await waitForPersist();

    // Two groups, four members: the copy brought its contents, the original
    // kept its own.
    await browser.waitUntil(async () => (await $$('.group-widget').length) === 2, {
      timeout: 3_000, timeoutMsg: 'the group was not copied',
    });
    expect(await gridWidgetCount()).to.equal(6); // 2 groups + 2×2 members
    expect(await $$('.grid > .widget .clock-widget').length).to.equal(2);
    expect(await $$('.grid > .widget .notes-widget').length).to.equal(2);
    await leaveEditMode();
  });

  it('S-41_cutting a group MOVES it with its widgets — nothing is left behind', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    await enterEditMode();
    await rightClick('.group-head');
    await (await ctxMenuItem('Cut widget')).click();
    await $('.grid.placing').waitForDisplayed({ timeout: 3_000 });
    await clickGridCell(8, 6);
    await waitForPersist();

    // Still exactly one group with two members: the source went with it rather
    // than leaving its widgets loose on the desktop.
    expect(await $$('.group-widget').length).to.equal(1);
    expect(await gridWidgetCount()).to.equal(3);
    // Collapsing proves the pasted members really belong to the pasted group.
    await $('.group-chevron').click();
    await browser.waitUntil(async () => (await gridWidgetCount()) === 1, {
      timeout: 3_000, timeoutMsg: 'pasted members are not parented to the pasted group',
    });
    await leaveEditMode();
  });

  it('S-21_removing the group leaves its members on the grid', async () => {
    await seedGroup({ collapsed: false }, TWO_MEMBERS);
    await stubConfirm(true);
    await rightClick('.group-head');
    await (await ctxMenuItem('Remove widget')).click();
    await browser.waitUntil(async () => (await $$('.group-widget').length) === 0, {
      timeout: 3_000, timeoutMsg: 'group was not removed',
    });
    // Both members survive, top-level, where they were.
    expect(await gridWidgetCount()).to.equal(2);
    expect(await $$('.grid > .widget .clock-widget').length).to.equal(1);
    expect(await $$('.grid > .widget .notes-widget').length).to.equal(1);
  });
});
