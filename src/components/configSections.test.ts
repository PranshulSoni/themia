// @vitest-environment jsdom
//
// [S-22] A config panel must not re-create its whole body on every setting change.
//
// Regression (issue nwalser/themia-releases#80 follow-up). ConfigTabs rendered
// its sections with a plain `props.tabs.map(...)`. `props.tabs` is a getter, and
// a panel whose section list depends on its config — To Do drops "Refresh" in
// local mode ([S-47]) — re-evaluates it into a NEW array on every config write.
// So that one expression tracked the config, and a single setting change tore
// down and rebuilt every section: labels, dividers, and the entire `children`
// body.
//
// Measured in the real config window before the fix: one config write produced
// 20 teardown/rebuild mutations under `.settings-content.cfg`. The visible
// damage was worse than a flash — a control destroyed mid-gesture never saw its
// own pointerup, so a pressed NumberStepper leaked its 50ms repeat timer and ran
// the value from 1 to its max on its own, with nothing holding the button.
//
// These tests pin the memo contract that stops it: same sections in, no
// notification out. The section-identity half is pure; the reactive half is
// driven through a real memo, which is what ConfigTabs builds.

import { describe, it, expect } from 'vitest';
import { createSignal, createMemo, createEffect } from 'solid-js';
import { render } from 'solid-js/web';
import { sameSections, type ConfigSection } from './configSections.ts';

const SOURCE = { key: 'source', label: 'Source' };
const DISPLAY = { key: 'display', label: 'Display' };
const REFRESH = { key: 'refresh', label: 'Refresh' };

describe('sameSections [S-22]', () => {
  it('S-22_ a rebuilt array with the same sections is the same list', () => {
    expect(sameSections([SOURCE, DISPLAY], [{ ...SOURCE }, { ...DISPLAY }])).toBe(true);
  });

  it('S-22_ an added or removed section is a change', () => {
    expect(sameSections([SOURCE, DISPLAY], [SOURCE, DISPLAY, REFRESH])).toBe(false);
    expect(sameSections([SOURCE, DISPLAY, REFRESH], [SOURCE, DISPLAY])).toBe(false);
  });

  it('S-22_ a reordered list is a change', () => {
    expect(sameSections([SOURCE, DISPLAY], [DISPLAY, SOURCE])).toBe(false);
  });

  it('S-22_ a relabelled section is a change — the language switched', () => {
    expect(sameSections([SOURCE], [{ key: 'source', label: 'Quelle' }])).toBe(false);
  });

  it('S-22_ ignores extra fields, so an inline icon does not force a rebuild', () => {
    // Real tabs carry an `icon` component too; a panel that builds it inline
    // hands over a new function every read, which must not count as a change.
    const withIcon = [{ ...SOURCE, icon: () => null }];
    const withOther = [{ ...SOURCE, icon: () => null }];
    expect(sameSections(withIcon, withOther)).toBe(true);
  });

  it('S-22_ two empty lists are the same list', () => {
    expect(sameSections([], [])).toBe(true);
  });
});

describe('the section memo does not re-render on unrelated config writes [S-22]', () => {
  /** ConfigTabs' memo, over a tabs getter that rebuilds on every read — exactly
   *  how a config-derived `tabs()` behaves through a props getter. */
  function mount(initialLocal: boolean) {
    const [local, setLocal] = createSignal(initialLocal);
    const [otherSetting, setOtherSetting] = createSignal(0);
    let renders = 0;

    const host = document.createElement('div');
    document.body.appendChild(host);
    let sections!: () => ConfigSection[];
    const dispose = render(() => {
      // A fresh array per read, like `tabs={tabs()}` at the call site. It also
      // reads an unrelated setting, standing in for a panel that derives its
      // sections from the same config bag every other control writes to.
      const tabsGetter = (): ConfigSection[] => {
        void otherSetting();
        return local() ? [{ ...SOURCE }, { ...DISPLAY }] : [{ ...SOURCE }, { ...DISPLAY }, { ...REFRESH }];
      };
      sections = createMemo(tabsGetter, undefined, { equals: sameSections });
      createEffect(() => { sections(); renders++; }); // stands in for the rendered body
      return null;
    }, host);

    return { setLocal, setOtherSetting, sections: () => sections(), renders: () => renders, dispose, host };
  }

  it('S-22_ writing an unrelated setting does not re-render the sections', () => {
    const m = mount(false);
    expect(m.renders()).toBe(1);

    m.setOtherSetting(1);
    m.setOtherSetting(2);
    m.setOtherSetting(3);

    expect(m.renders()).toBe(1);
    m.dispose(); m.host.remove();
  });

  it('S-22_ …and keeps handing back the SAME array, so <For> rebuilds nothing', () => {
    const m = mount(false);
    const first = m.sections();
    m.setOtherSetting(1);
    expect(m.sections()).toBe(first);
    m.dispose(); m.host.remove();
  });

  it('S-22_ a real section change still re-renders', () => {
    const m = mount(false);
    expect(m.sections()).toHaveLength(3);

    m.setLocal(true); // To Do going local drops "Refresh" [S-47]

    expect(m.renders()).toBe(2);
    expect(m.sections().map(s => s.key)).toEqual(['source', 'display']);
    m.dispose(); m.host.remove();
  });

  it('S-22_ compared by reference instead, every write re-renders (the old bug)', () => {
    // The same setup under Solid's default `===` equality — what a plain
    // `props.tabs.map()` amounts to. Pinned so the contrast stays visible, and
    // so this file fails if `sameSections` is ever dropped from the memo.
    const host = document.createElement('div');
    document.body.appendChild(host);
    const [otherSetting, setOtherSetting] = createSignal(0);
    let renders = 0;
    const dispose = render(() => {
      const sections = createMemo(() => { void otherSetting(); return [{ ...SOURCE }]; });
      createEffect(() => { sections(); renders++; });
      return null;
    }, host);

    setOtherSetting(1);
    setOtherSetting(2);

    expect(renders).toBe(3); // initial + one per write
    dispose(); host.remove();
  });
});
