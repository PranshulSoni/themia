// @vitest-environment jsdom
//
// Copying an AREA GROUP takes the widgets inside it ([S-41]). Runs against the
// real layout store: the whole point of the feature is what lands in the store
// after a paste, and a mocked store would pin nothing.
import { describe, it, expect, beforeEach } from 'vitest';
import {
  getViewLayoutStore, destroyViewLayoutStore, type Widget,
} from './layout.ts';
import { copyWidget, cutWidget, pasteWidget, clipboardWidgetCount, widgetClipboard } from './widgetClipboard.ts';
import { setViewStore } from './viewState.ts';

// jsdom's screen: the px round-trip is 1024×768 in and out, so offsets are exact.
const SCR = { w: 1024, h: 768 };

const groupW = (extra: Record<string, unknown> = {}) =>
  ({ id: 'g1', type: 'group', x: 0.2, y: 0.2, w: 0.4, h: 0.4, config: { collapsed: false }, ...extra }) as Widget;
const member = (id: string, x: number, y: number, extra: Record<string, unknown> = {}) =>
  ({ id, type: 'clock', x, y, w: 0.1, h: 0.1, parentId: 'g1', config: {}, ...extra }) as Widget;

let n = 0;
function freshView(widgets: Widget[]) {
  const id = `vitest-clip-${++n}`;
  const s = getViewLayoutStore(id);
  s._seedWidgets(widgets);
  // paste writes to the ACTIVE view's store, so point it at this one.
  setViewStore('activeId', id);
  return { s, dispose: () => destroyViewLayoutStore(id) };
}

const membersOf = (s: ReturnType<typeof getViewLayoutStore>, groupId: string) =>
  s.layout.widgets.filter(w => w.parentId === groupId);
const theGroup = (s: ReturnType<typeof getViewLayoutStore>) =>
  s.layout.widgets.filter(w => w.type === 'group');

describe('clipboard: an area group carries its members ([S-41])', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('S-41_copy captures each member as a px offset from the group corner', () => {
    const { s, dispose } = freshView([groupW(), member('m1', 0.25, 0.25), member('m2', 0.45, 0.35)]);
    copyWidget(s.layout.widgets.find(w => w.id === 'g1')!);
    const clip = widgetClipboard().widget!;
    expect(clip.members).to.have.length(2);
    // Offsets are relative, so the arrangement survives a paste anywhere.
    expect(clip.members![0].dxPx).to.be.closeTo(0.05 * SCR.w, 0.5);
    expect(clip.members![0].dyPx).to.be.closeTo(0.05 * SCR.h, 0.5);
    expect(clip.members![1].dxPx).to.be.closeTo(0.25 * SCR.w, 0.5);
    expect(clipboardWidgetCount()).to.equal(3); // the group + its two members
    dispose();
  });

  it('S-41_paste rebuilds the members inside the new group, at the same relative places', () => {
    const { s, dispose } = freshView([groupW(), member('m1', 0.25, 0.25), member('m2', 0.45, 0.35)]);
    copyWidget(s.layout.widgets.find(w => w.id === 'g1')!);
    expect(pasteWidget(0.5, 0.1)).to.equal(true);

    const groups = theGroup(s);
    expect(groups).to.have.length(2);
    const pasted = groups.find(g => g.id !== 'g1')!;
    expect(pasted.x).to.be.closeTo(0.5, 1e-9);
    const kids = membersOf(s, pasted.id);
    expect(kids).to.have.length(2);
    // Same offsets from the group's corner as the originals had.
    expect(kids[0].x - pasted.x).to.be.closeTo(0.05, 1e-3);
    expect(kids[0].y - pasted.y).to.be.closeTo(0.05, 1e-3);
    expect(kids[1].x - pasted.x).to.be.closeTo(0.25, 1e-3);
    // …and the originals are untouched by a COPY.
    expect(membersOf(s, 'g1')).to.have.length(2);
    dispose();
  });

  it('S-41_pasted members get fresh ids and keep their own config + label', () => {
    const { s, dispose } = freshView([
      groupW(),
      member('m1', 0.25, 0.25, { config: { style: 'analog' }, label: 'Desk clock' }),
    ]);
    copyWidget(s.layout.widgets.find(w => w.id === 'g1')!);
    pasteWidget(0.05, 0.05);
    const pasted = theGroup(s).find(g => g.id !== 'g1')!;
    const kid = membersOf(s, pasted.id)[0];
    expect(kid.id).to.not.equal('m1');
    expect(kid.config).to.deep.equal({ style: 'analog' });
    expect(kid.label).to.equal('Desk clock');
    dispose();
  });

  it('S-41_every widget of a pasted group gets a UNIQUE id (same-millisecond mint)', () => {
    const { s, dispose } = freshView([
      groupW(), member('m1', 0.25, 0.25), member('m2', 0.35, 0.25), member('m3', 0.45, 0.25),
    ]);
    copyWidget(s.layout.widgets.find(w => w.id === 'g1')!);
    pasteWidget(0.05, 0.5);
    const ids = s.layout.widgets.map(w => w.id);
    expect(new Set(ids).size).to.equal(ids.length);
    dispose();
  });

  it('S-41_cut removes the members with the group, instead of popping them loose', () => {
    const { s, dispose } = freshView([groupW(), member('m1', 0.25, 0.25), member('m2', 0.45, 0.35)]);
    cutWidget(s.layout.widgets.find(w => w.id === 'g1')!);
    // The cut source is removed by the placement path; this is the op it uses.
    s.removeWidgetTree('g1');
    expect(s.layout.widgets).to.have.length(0);
    // The clipboard still holds everything, so the paste restores all three.
    expect(clipboardWidgetCount()).to.equal(3);
    expect(pasteWidget(0.1, 0.1)).to.equal(true);
    const pasted = theGroup(s)[0];
    expect(membersOf(s, pasted.id)).to.have.length(2);
    dispose();
  });

  it('S-21_a plain delete still KEEPS the members (only a move takes them)', () => {
    const { s, dispose } = freshView([groupW(), member('m1', 0.25, 0.25)]);
    s.removeWidget('g1');
    expect(s.layout.widgets.map(w => w.id)).to.deep.equal(['m1']);
    expect(s.layout.widgets[0].parentId).to.equal(undefined);
    dispose();
  });

  it('S-41_a copied plain widget carries no members at all', () => {
    const { s, dispose } = freshView([{ id: 'c9', type: 'clock', x: 0.1, y: 0.1, w: 0.1, h: 0.1 } as Widget]);
    copyWidget(s.layout.widgets[0]);
    expect(widgetClipboard().widget!.members).to.equal(undefined);
    expect(clipboardWidgetCount()).to.equal(1);
    dispose();
  });
});
