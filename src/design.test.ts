// @vitest-environment jsdom
//
// Characterization of the global-design schema: per-field validation. The bug
// class it kills: a single corrupt field (e.g. uiScale:"oops") flowing into the
// store and poisoning the theme. Each field is validated independently; an
// invalid one falls back to its default while the valid ones are kept.

import { describe, it, expect } from 'vitest';
import { claimLegacyDesktopIconRepair, globalDesignSchema, hexToRgba, migrateV6TransitionAnim, migrateV7TransitionAnimMs, migrateV8UnlockAnim, migrateV9DropExtreme, setGlobalDesign, resolveFontStack, FONT_STACKS } from './design.ts';
import { loadPersisted, type ReadableStorage } from './persistedStore.ts';
import { detectLanguage } from './i18n.ts';

function fakeStorage(initial: Record<string, string> = {}): ReadableStorage {
  const map = new Map<string, string>(Object.entries(initial));
  return { getItem: (k: string) => (map.has(k) ? map.get(k)! : null) };
}
function stored(state: unknown): ReadableStorage {
  return fakeStorage({ k: JSON.stringify(state) });
}

describe('global design schema: per-field validation', () => {
  it('absent key → defaults', () => {
    const got = loadPersisted('k', globalDesignSchema, fakeStorage());
    expect(got.theme).toBe('dark');
    expect(got.uiScale).toBe(1);
    expect(got.allowPrivateNetworkAccess).toBe(false);
  });

  it('a corrupt scalar field falls back to default; valid siblings are kept', () => {
    const got = loadPersisted('k', globalDesignSchema, stored({ theme: 'light', uiScale: 'oops', gridSnap: false }));
    expect(got.theme).toBe('light'); // valid → kept
    expect(got.uiScale).toBe(1); // wrong type → default (theme not poisoned)
    expect(got.gridSnap).toBe(false); // valid → kept
  });

  it('an out-of-set enum value falls back to its default', () => {
    const got = loadPersisted('k', globalDesignSchema, stored({ theme: 'rainbow', widgetBorderStyle: 'zigzag' }));
    expect(got.theme).toBe('dark');
    expect(got.widgetBorderStyle).toBe('solid');
  });

  it('keeps a fully-valid object', () => {
    const got = loadPersisted(
      'k',
      globalDesignSchema,
      stored({ theme: 'light', uiScale: 1.5, allowPrivateNetworkAccess: true, gridSnap: false, widgetGap: 20 }),
    );
    expect(got.theme).toBe('light');
    expect(got.uiScale).toBe(1.5);
    expect(got.allowPrivateNetworkAccess).toBe(true);
    expect(got.widgetGap).toBe(20);
  });

  // [LOG-04] (src-tauri/SPEC.md, Logging pipeline) the log pipeline is gated
  // by a dev-only toggle, default OFF; logging is opt-in on user machines.
  it('LOG-04 loggingEnabled defaults to false and validates as a boolean', () => {
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).loggingEnabled).toBe(false);
    const got = loadPersisted('k', globalDesignSchema, stored({ loggingEnabled: 'yes', gridSnap: false }));
    expect(got.loggingEnabled).toBe(false); // wrong type → default
    expect(got.gridSnap).toBe(false); // valid sibling kept
    expect(loadPersisted('k', globalDesignSchema, stored({ loggingEnabled: true })).loggingEnabled).toBe(true);
  });

  // The header surface is an independent pair of fields (headerBgColor /
  // headerBgOpacity). Same per-field validation contract as the body surface:
  // a corrupt header value must not poison the body, and vice versa.
  it('header surface fields default when absent', () => {
    const got = loadPersisted('k', globalDesignSchema, fakeStorage());
    expect(got.headerBgColor).toBe('');
    expect(got.headerBgOpacity).toBe(0.90);
  });

  it('a corrupt headerBgOpacity falls back without poisoning the body surface', () => {
    const got = loadPersisted(
      'k',
      globalDesignSchema,
      stored({ widgetBgColor: '#123456', widgetBgOpacity: 0.5, headerBgColor: '#abcdef', headerBgOpacity: 'oops' }),
    );
    expect(got.widgetBgColor).toBe('#123456'); // body untouched
    expect(got.widgetBgOpacity).toBe(0.5);
    expect(got.headerBgColor).toBe('#abcdef'); // valid header colour kept
    expect(got.headerBgOpacity).toBe(0.90); // wrong type → default
  });

  it('widgetBorderOpacity defaults when absent and validates per-field', () => {
    const def = loadPersisted('k', globalDesignSchema, fakeStorage());
    expect(def.widgetBorderOpacity).toBe(1);
    const got = loadPersisted(
      'k',
      globalDesignSchema,
      stored({ widgetBorderColor: '#ff0000', widgetBorderOpacity: 'oops' }),
    );
    expect(got.widgetBorderColor).toBe('#ff0000'); // valid → kept
    expect(got.widgetBorderOpacity).toBe(1); // wrong type → default
  });

  // [S-15] startup intro animation: an enum (none/fade/scale/slide/flip/blur),
  // default 'scale'. Out-of-set values (including the legacy boolean) fall back
  // to the default rather than poisoning the store.
  it('widgetStartupAnimation defaults to scale and validates as an enum', () => {
    const def = loadPersisted('k', globalDesignSchema, fakeStorage());
    expect(def.widgetStartupAnimation).toBe('scale');
    // out-of-set string → default, valid sibling kept
    const corrupt = loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'oops', gridSnap: false }));
    expect(corrupt.widgetStartupAnimation).toBe('scale');
    expect(corrupt.gridSnap).toBe(false);
    // a legacy persisted boolean is not a valid enum member → default (the v5
    // migration is what converts true→'scale'/false→'none' before load)
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: true })).widgetStartupAnimation).toBe('scale');
    // valid members kept
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'none' })).widgetStartupAnimation).toBe('none');
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'flip' })).widgetStartupAnimation).toBe('flip');
    // [S-28] both sheet styles are valid enum members
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'sides' })).widgetStartupAnimation).toBe('sides');
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'topbottom' })).widgetStartupAnimation).toBe('topbottom');
    // [S-16]/[S-23] the overlay + hacker styles are valid enum members
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'hyperspace' })).widgetStartupAnimation).toBe('hyperspace');
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'hacker' })).widgetStartupAnimation).toBe('hacker');
    // 'hyperspace-extreme' was DROPPED, no longer a valid member, so a raw
    // value falls back to the default (the v9 migration remaps it → 'hyperspace'
    // before load).
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimation: 'hyperspace-extreme' })).widgetStartupAnimation).toBe('scale');
  });

  // [S-24] view-transition animation: same enum as the startup style, but an
  // INDEPENDENT field: it styles the per-widget intro on view switches.
  it('S-24 widgetTransitionAnimation defaults to scale and validates as an enum', () => {
    const def = loadPersisted('k', globalDesignSchema, fakeStorage());
    expect(def.widgetTransitionAnimation).toBe('scale');
    // out-of-set string → default, valid sibling kept
    const corrupt = loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimation: 'oops', widgetStartupAnimation: 'flip' }));
    expect(corrupt.widgetTransitionAnimation).toBe('scale');
    expect(corrupt.widgetStartupAnimation).toBe('flip');
    // valid members kept; the overlay styles are valid here too (they play
    // only their per-widget intro on a view switch, never the boot overlay)
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimation: 'none' })).widgetTransitionAnimation).toBe('none');
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimation: 'hyperspace' })).widgetTransitionAnimation).toBe('hyperspace');
    // dropped value → default (v9 remaps it before load)
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimation: 'hyperspace-extreme' })).widgetTransitionAnimation).toBe('scale');
  });

  // [S-16] unlock intro animation: its OWN enum (was a replay-on-unlock boolean
  // before the split), styling the per-widget intro on screen unlock. Default
  // 'scale' (a fresh install replays the same default as startup); same per-field
  // validation as the other intro styles.
  it('S-16 widgetUnlockAnimation defaults to scale and validates as an enum', () => {
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).widgetUnlockAnimation).toBe('scale');
    // out-of-set string → default, valid sibling kept
    const corrupt = loadPersisted('k', globalDesignSchema, stored({ widgetUnlockAnimation: 'oops', gridSnap: false }));
    expect(corrupt.widgetUnlockAnimation).toBe('scale');
    expect(corrupt.gridSnap).toBe(false);
    // valid members kept (incl. the overlay styles, since unlock can play the overlay)
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetUnlockAnimation: 'none' })).widgetUnlockAnimation).toBe('none');
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetUnlockAnimation: 'hyperspace' })).widgetUnlockAnimation).toBe('hyperspace');
  });

  // [S-16] unlock intro duration, independent of the startup duration.
  it('S-16 widgetUnlockAnimationMs defaults to 500 and validates as a number', () => {
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).widgetUnlockAnimationMs).toBe(500);
    // wrong type → default, valid sibling kept
    const got = loadPersisted('k', globalDesignSchema, stored({ widgetUnlockAnimationMs: 'fast', widgetUnlockAnimation: 'flip' }));
    expect(got.widgetUnlockAnimationMs).toBe(500);
    expect(got.widgetUnlockAnimation).toBe('flip');
    // valid number kept
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetUnlockAnimationMs: 250 })).widgetUnlockAnimationMs).toBe(250);
  });

  it('widgetStartupAnimationMs defaults to 500 and validates as a number', () => {
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).widgetStartupAnimationMs).toBe(500);
    // wrong type → default, valid sibling kept
    const got = loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimationMs: 'fast', widgetStartupAnimation: 'flip' }));
    expect(got.widgetStartupAnimationMs).toBe(500);
    expect(got.widgetStartupAnimation).toBe('flip');
    // valid number kept
    expect(loadPersisted('k', globalDesignSchema, stored({ widgetStartupAnimationMs: 200 })).widgetStartupAnimationMs).toBe(200);
  });

  // [S-24] each intro mode has its OWN duration: the transition duration is
  // independent of the startup one.
  it('S-24 widgetTransitionAnimationMs defaults to 500 and validates as a number', () => {
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).widgetTransitionAnimationMs).toBe(500);
    // wrong type → default, valid sibling kept
    const got = loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimationMs: 'slow', widgetStartupAnimationMs: 900 }));
    expect(got.widgetTransitionAnimationMs).toBe(500);
    expect(got.widgetStartupAnimationMs).toBe(900);
    // valid number kept, independent of the startup duration
    const both = loadPersisted('k', globalDesignSchema, stored({ widgetTransitionAnimationMs: 200, widgetStartupAnimationMs: 1500 }));
    expect(both.widgetTransitionAnimationMs).toBe(200);
    expect(both.widgetStartupAnimationMs).toBe(1500);
  });

  it('keeps independent header and body surfaces', () => {
    const got = loadPersisted(
      'k',
      globalDesignSchema,
      stored({ widgetBgColor: '#000000', widgetBgOpacity: 1, headerBgColor: '#ffffff', headerBgOpacity: 0.2 }),
    );
    expect(got.widgetBgColor).toBe('#000000');
    expect(got.widgetBgOpacity).toBe(1);
    expect(got.headerBgColor).toBe('#ffffff');
    expect(got.headerBgOpacity).toBe(0.2);
  });
});

// issue #44 (themia-releases): "Widgets body and header colors set in style
// settings do not match the actual colors" (e.g. set #171717, got #111111).
//
// The chosen hex flows store → applyGlobalDesign() → `--card`/`--header-bg` as
// `rgba(r,g,b,opacity)`. The reported #111111 is the GLASS widget composited
// over the desktop at opacity < 1 (alpha compositing, not a parse bug; the
// hex→rgb conversion is lossless). But the inline parser that built the rgba
// string (`parseInt(hex.slice(1,3),16)` ×3, duplicated four times in
// applyGlobalDesign) had a REAL latent defect: any non-canonical stored hex
// (shorthand `#fff`, a missing `#`, or junk) silently produced NaN / wrong
// channels, i.e. a colour that genuinely does NOT match the one set. The fix is
// one validated pure helper. These pin the lossless round-trip AND the
// normalisation so a malformed value can never paint the wrong colour again.
describe('issue_44 hexToRgba: lossless, validated colour conversion', () => {
  it('issue_44 round-trips a canonical 6-digit hex losslessly (the #171717 case)', () => {
    // The exact value from the bug report: #171717 = rgb(23,23,23). At full
    // opacity the rendered channels must equal the input byte-for-byte; there
    // is no quantization step in the conversion.
    expect(hexToRgba('#171717', 1)).toBe('rgba(23,23,23,1)');
    // …and the opacity is passed through verbatim (the 0.9 default is what makes
    // the composited glass pixel read darker than the literal hex, by design).
    expect(hexToRgba('#171717', 0.9)).toBe('rgba(23,23,23,0.9)');
  });

  it('issue_44 normalises non-canonical hex instead of emitting NaN/wrong channels', () => {
    // Uppercase is already case-insensitive, but assert it explicitly.
    expect(hexToRgba('#FFFFFF', 1)).toBe('rgba(255,255,255,1)');
    // Shorthand #rgb: the old inline parser produced rgba(255,15,NaN,…).
    expect(hexToRgba('#fff', 1)).toBe('rgba(255,255,255,1)');
    expect(hexToRgba('#abc', 1)).toBe('rgba(170,187,204,1)');
    // Missing leading '#': the old parser sliced off the first hex digit and
    // produced an entirely different colour (rgba(113,113,7,…) for '171717').
    expect(hexToRgba('171717', 1)).toBe('rgba(23,23,23,1)');
  });

  it('issue_44 returns a typed default for unparseable input (never NaN in CSS)', () => {
    // Junk must not paint rgba(NaN,NaN,NaN,…) (invalid → widget renders with no
    // fill). Fall back to a defined, opaque-at-given-alpha black.
    expect(hexToRgba('#GGGGGG', 1)).toBe('rgba(0,0,0,1)');
    expect(hexToRgba('', 0.5)).toBe('rgba(0,0,0,0.5)');
    expect(hexToRgba('not-a-color', 1)).toBe('rgba(0,0,0,1)');
  });
});

// [S-24] v6 migration: the transition style is new; seed it from the user's
// existing startup style so a view switch keeps animating exactly as before
// the split. Without it every upgrader silently falls back to 'scale'.
describe('S-24 v6 migration: seed widgetTransitionAnimation from the startup style', () => {
  function writableStorage(initial: Record<string, string> = {}) {
    const map = new Map<string, string>(Object.entries(initial));
    return {
      getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
      setItem: (k: string, v: string) => { map.set(k, v); },
      dump: () => map,
    };
  }
  const KEY = 'widget-global-design';

  it('copies the existing startup style into the new transition field', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimation: 'hacker' }) });
    migrateV6TransitionAnim(s);
    const blob = JSON.parse(s.dump().get(KEY)!);
    expect(blob.widgetTransitionAnimation).toBe('hacker');
    expect(blob.widgetStartupAnimation).toBe('hacker');
  });

  it('never overwrites an already-present transition value, and is one-shot', () => {
    const s = writableStorage({
      [KEY]: JSON.stringify({ widgetStartupAnimation: 'hacker', widgetTransitionAnimation: 'fade' }),
    });
    migrateV6TransitionAnim(s);
    expect(JSON.parse(s.dump().get(KEY)!).widgetTransitionAnimation).toBe('fade');
    // one-shot: mutate startup, run again → transition untouched
    s.setItem(KEY, JSON.stringify({ widgetStartupAnimation: 'flip' }));
    migrateV6TransitionAnim(s);
    expect('widgetTransitionAnimation' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
  });

  it('skips invalid startup values (the schema default applies instead)', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimation: true }) });
    migrateV6TransitionAnim(s);
    expect('widgetTransitionAnimation' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
  });

  it('tolerates an absent blob (fresh install): just marks itself done', () => {
    const s = writableStorage();
    migrateV6TransitionAnim(s);
    expect(s.dump().get(KEY)).toBeUndefined();
  });

  // [S-24] v7, same contract for the duration split: seed the new transition
  // duration from the user's existing (shared) duration so a view switch keeps
  // its exact speed.
  describe('v7: seed widgetTransitionAnimationMs from the shared duration', () => {
    it('copies the existing duration into the new transition field', () => {
      const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimationMs: 1200 }) });
      migrateV7TransitionAnimMs(s);
      const blob = JSON.parse(s.dump().get(KEY)!);
      expect(blob.widgetTransitionAnimationMs).toBe(1200);
      expect(blob.widgetStartupAnimationMs).toBe(1200);
    });

    it('never overwrites an already-present value, and is one-shot', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({ widgetStartupAnimationMs: 1200, widgetTransitionAnimationMs: 300 }),
      });
      migrateV7TransitionAnimMs(s);
      expect(JSON.parse(s.dump().get(KEY)!).widgetTransitionAnimationMs).toBe(300);
      s.setItem(KEY, JSON.stringify({ widgetStartupAnimationMs: 700 }));
      migrateV7TransitionAnimMs(s);
      expect('widgetTransitionAnimationMs' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
    });

    it('skips a non-numeric duration (the schema default applies instead)', () => {
      const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimationMs: 'fast' }) });
      migrateV7TransitionAnimMs(s);
      expect('widgetTransitionAnimationMs' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
    });
  });

  // [S-16] v8: the replay-on-unlock BOOLEAN split into an independent unlock
  // animation + duration. Seed them from the startup values so the behavior is
  // preserved: before the split, unlock REPLAYED the startup animation when the
  // boolean was on (default on); off → no unlock animation ('none').
  describe('v8: seed widgetUnlockAnimation from the replay-on-unlock boolean', () => {
    it('boolean on (default): copies the startup style + duration into the unlock fields', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({
          widgetStartupAnimationOnUnlock: true,
          widgetStartupAnimation: 'hyperspace',
          widgetStartupAnimationMs: 1200,
        }),
      });
      migrateV8UnlockAnim(s);
      const blob = JSON.parse(s.dump().get(KEY)!);
      expect(blob.widgetUnlockAnimation).toBe('hyperspace');
      expect(blob.widgetUnlockAnimationMs).toBe(1200);
    });

    it('boolean ABSENT (legacy default on): still seeds from the startup style', () => {
      const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimation: 'flip', widgetStartupAnimationMs: 700 }) });
      migrateV8UnlockAnim(s);
      const blob = JSON.parse(s.dump().get(KEY)!);
      expect(blob.widgetUnlockAnimation).toBe('flip');
      expect(blob.widgetUnlockAnimationMs).toBe(700);
    });

    it('boolean off: maps to no unlock animation (none)', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({ widgetStartupAnimationOnUnlock: false, widgetStartupAnimation: 'hyperspace' }),
      });
      migrateV8UnlockAnim(s);
      expect(JSON.parse(s.dump().get(KEY)!).widgetUnlockAnimation).toBe('none');
    });

    it('never overwrites an already-present unlock value, and is one-shot', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({ widgetStartupAnimation: 'hacker', widgetUnlockAnimation: 'fade' }),
      });
      migrateV8UnlockAnim(s);
      expect(JSON.parse(s.dump().get(KEY)!).widgetUnlockAnimation).toBe('fade');
      // one-shot: mutate startup, run again → unlock untouched
      s.setItem(KEY, JSON.stringify({ widgetStartupAnimation: 'flip' }));
      migrateV8UnlockAnim(s);
      expect('widgetUnlockAnimation' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
    });

    it('skips an invalid startup style (the schema default applies instead)', () => {
      const s = writableStorage({ [KEY]: JSON.stringify({ widgetStartupAnimationOnUnlock: true, widgetStartupAnimation: true }) });
      migrateV8UnlockAnim(s);
      expect('widgetUnlockAnimation' in JSON.parse(s.dump().get(KEY)!)).toBe(false);
    });

    it('tolerates an absent blob (fresh install): just marks itself done', () => {
      const s = writableStorage();
      migrateV8UnlockAnim(s);
      expect(s.dump().get(KEY)).toBeUndefined();
    });
  });

  // [S-16] v9: the 'hyperspace' (standard) and 'hyperspace-extreme' styles
  // collapsed into a single 'hyperspace' = the violent flavour. Remap every
  // persisted 'hyperspace-extreme' (startup / transition / unlock) → 'hyperspace'
  // so a user who picked it keeps a hyperspace warp instead of silently falling
  // back to the default.
  describe('v9: remap dropped hyperspace-extreme → hyperspace', () => {
    it('remaps every intro field that held the dropped value', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({
          widgetStartupAnimation: 'hyperspace-extreme',
          widgetTransitionAnimation: 'hyperspace-extreme',
          widgetUnlockAnimation: 'hyperspace-extreme',
        }),
      });
      migrateV9DropExtreme(s);
      const blob = JSON.parse(s.dump().get(KEY)!);
      expect(blob.widgetStartupAnimation).toBe('hyperspace');
      expect(blob.widgetTransitionAnimation).toBe('hyperspace');
      expect(blob.widgetUnlockAnimation).toBe('hyperspace');
    });

    it('leaves other values untouched, and is one-shot', () => {
      const s = writableStorage({
        [KEY]: JSON.stringify({ widgetStartupAnimation: 'hyperspace-extreme', widgetTransitionAnimation: 'flip' }),
      });
      migrateV9DropExtreme(s);
      const blob = JSON.parse(s.dump().get(KEY)!);
      expect(blob.widgetStartupAnimation).toBe('hyperspace');
      expect(blob.widgetTransitionAnimation).toBe('flip');
      // one-shot: reintroduce the dropped value, rerun → untouched
      s.setItem(KEY, JSON.stringify({ widgetStartupAnimation: 'hyperspace-extreme' }));
      migrateV9DropExtreme(s);
      expect(JSON.parse(s.dump().get(KEY)!).widgetStartupAnimation).toBe('hyperspace-extreme');
    });

    it('tolerates an absent blob (fresh install): just marks itself done', () => {
      const s = writableStorage();
      migrateV9DropExtreme(s);
      expect(s.dump().get(KEY)).toBeUndefined();
    });
  });
});

// A single-field design write must MERGE onto the latest persisted blob, not
// overwrite it with this window's (possibly stale) in-memory copy. This is what
// keeps Developer Mode (toggled in the config window) from being reverted when
// another window later persists an unrelated design change, i.e. what makes
// devMode actually survive a restart. Regression: whole-blob save clobbered it.
describe('single-field design write never clobbers a sibling changed elsewhere', () => {
  const KEY = 'widget-global-design';

  it('preserves a persisted devMode=true when an unrelated field is saved', () => {
    // Simulate another window having enabled Developer Mode straight to storage;
    // this module's in-memory store still holds the import-time default
    // (devMode=false).
    localStorage.setItem(KEY, JSON.stringify({ devMode: true }));

    setGlobalDesign('widgetGap', 20);

    const blob = JSON.parse(localStorage.getItem(KEY)!);
    expect(blob.devMode).toBe(true);  // sibling merged from storage, not clobbered
    expect(blob.widgetGap).toBe(20);  // our change landed
  });
});

// issue #63 (themia-releases): add Simplified Chinese (zh-Hans / zh-CN). The
// Language union + the persisted-design schema guard must accept 'zh', and the
// system-locale auto-detect must map a zh-* browser locale to 'zh'.
describe('issue_63 Simplified Chinese language support', () => {
  it('issue_63 zh passes the language schema guard (is a valid Language)', () => {
    expect(loadPersisted('k', globalDesignSchema, stored({ language: 'zh' })).language).toBe('zh');
    // sibling locales still validate
    expect(loadPersisted('k', globalDesignSchema, stored({ language: 'ja' })).language).toBe('ja');
    // an unknown code still falls back to the default (absent-vs-corrupt: corrupt → default)
    expect(loadPersisted('k', globalDesignSchema, stored({ language: 'xx' })).language).toBe('en');
    // absent → default
    expect(loadPersisted('k', globalDesignSchema, fakeStorage()).language).toBe('en');
  });

  it('issue_63 auto-detect maps a zh-CN / zh-* system locale to zh', () => {
    expect(detectLanguage('zh-CN')).toBe('zh');
    expect(detectLanguage('zh-Hans')).toBe('zh');
    expect(detectLanguage('zh')).toBe('zh');
    expect(detectLanguage('ZH-cn')).toBe('zh'); // case-insensitive
    // other locales are unaffected, and unknown / empty → en
    expect(detectLanguage('de-DE')).toBe('de');
    expect(detectLanguage('ja-JP')).toBe('ja');
    expect(detectLanguage('en-US')).toBe('en');
    expect(detectLanguage('')).toBe('en');
    expect(detectLanguage(null)).toBe('en');
  });
});

// issue #62 (themia-releases): configurable global font family + size. Font is
// now a design token: a numeric fontScale plus an OPEN font family (4 curated
// presets + ANY installed system font, issue #64). fontFamily is a free-form
// string: a preset id resolves to a curated stack, any other value is treated as
// a single custom family with a safe fallback tail. applied as CSS vars on :root
// by applyGlobalDesign so the app root cascades them. Old persisted designs (no
// font fields) must still load, defaulting the new fields.
describe('issue_62 configurable font family + size', () => {
  it('issue_62 applyGlobalDesign writes --font-family and --font-scale from config', () => {
    setGlobalDesign('fontFamily', 'serif');
    setGlobalDesign('fontScale', 1.25);
    const el = document.documentElement;
    // serif maps to a serif stack (Georgia …); the var must carry it verbatim
    expect(el.style.getPropertyValue('--font-family')).toContain('Georgia');
    expect(el.style.getPropertyValue('--font-family')).toContain('serif');
    expect(el.style.getPropertyValue('--font-scale')).toBe('1.25');
    // mono is a distinct preset resolving to a monospace stack
    setGlobalDesign('fontFamily', 'mono');
    expect(el.style.getPropertyValue('--font-family')).toContain('monospace');
    // system restores the original Segoe stack
    setGlobalDesign('fontFamily', 'system');
    expect(el.style.getPropertyValue('--font-family')).toContain('Segoe UI');
    setGlobalDesign('fontScale', 1); // reset for any later test
  });

  it('issue_64 applyGlobalDesign quotes a custom installed family with a fallback tail', () => {
    setGlobalDesign('fontFamily', 'Comic Sans MS');
    const el = document.documentElement;
    const v = el.style.getPropertyValue('--font-family');
    expect(v).toContain('"Comic Sans MS"'); // custom family, quoted
    expect(v).toContain('sans-serif');       // still ends in the safe fallback tail
    setGlobalDesign('fontFamily', 'system'); // reset
  });

  it('issue_62 an old persisted design without font fields loads the defaults', () => {
    const got = loadPersisted('k', globalDesignSchema, stored({ theme: 'light', uiScale: 1.2 }));
    expect(got.fontFamily).toBe('system'); // absent → default
    expect(got.fontScale).toBe(1);          // absent → default
    expect(got.theme).toBe('light');        // unrelated field untouched
    expect(got.uiScale).toBe(1.2);
  });

  it('issue_64 a custom family string survives load; only a non-string falls back', () => {
    // fontFamily is now an OPEN set: any installed family name is a valid value,
    // so an unknown string is KEPT (it resolves to a fallback stack at apply
    // time), unlike the old closed FontFamily enum which reset it to 'system'.
    const got = loadPersisted('k', globalDesignSchema, stored({ fontFamily: 'Comic Sans MS', fontScale: 'huge', gridSnap: false }));
    expect(got.fontFamily).toBe('Comic Sans MS'); // custom family → kept
    expect(got.fontScale).toBe(1);                 // wrong type → default
    expect(got.gridSnap).toBe(false);              // valid sibling kept, not poisoned
  });

  it('issue_64 a non-string fontFamily falls back to the default', () => {
    const got = loadPersisted('k', globalDesignSchema, stored({ fontFamily: 123 }));
    expect(got.fontFamily).toBe('system'); // wrong type → default
  });

  it('issue_62 keeps valid font fields', () => {
    const got = loadPersisted('k', globalDesignSchema, stored({ fontFamily: 'mono', fontScale: 1.5 }));
    expect(got.fontFamily).toBe('mono');
    expect(got.fontScale).toBe(1.5);
  });
});

// issue #64: resolveFontStack maps the OPEN fontFamily value to a concrete CSS
// font stack. A preset id → its curated stack; any other non-empty value → that
// family, QUOTED, with the system stack appended as a fallback tail so a missing
// font still renders; a value carrying CSS-injection metacharacters is refused
// and falls back to the system stack entirely.
describe('issue_64 resolveFontStack (open font family)', () => {
  it('resolves each preset id to its curated stack', () => {
    expect(resolveFontStack('system')).toContain('Segoe UI');
    expect(resolveFontStack('sans')).toContain('Arial');
    expect(resolveFontStack('serif')).toContain('Georgia');
    expect(resolveFontStack('mono')).toContain('monospace');
  });

  it('quotes a custom family and appends the system fallback tail', () => {
    const s = resolveFontStack('Comic Sans MS');
    expect(s.startsWith('"Comic Sans MS"')).toBe(true);
    expect(s).toContain('sans-serif'); // fallback tail present
  });

  it('empty / whitespace → the system stack', () => {
    expect(resolveFontStack('')).toBe(FONT_STACKS.system);
    expect(resolveFontStack('   ')).toBe(FONT_STACKS.system);
  });

  it('refuses a family carrying CSS-injection metacharacters → system stack', () => {
    for (const evil of ['Foo"; background:red', 'a}b{c', "x';color:red", 'a\\b', 'a;b']) {
      expect(resolveFontStack(evil)).toBe(FONT_STACKS.system);
    }
  });
});

// [DI-06] One-shot repair of a pre-0.15 `ShowWindow(SW_HIDE)` desktop icon hide.
describe('claimLegacyDesktopIconRepair', () => {
  const KEY = 'widget-global-design';
  const MARKER = 'widget-design-migration-v10';

  function writableStorage(initial: Record<string, string> = {}) {
    const map = new Map<string, string>(Object.entries(initial));
    return {
      getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
      setItem: (k: string, v: string) => { map.set(k, v); },
      dump: () => map,
    };
  }

  it('claims the repair when Themia is the one that hid the icons', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ desktopIconsVisible: false }) });
    expect(claimLegacyDesktopIconRepair(s)).toBe(true);
  });

  it('is one-shot: a later shell-owned hide looks identical and must NOT be repaired', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ desktopIconsVisible: false }) });
    expect(claimLegacyDesktopIconRepair(s)).toBe(true);
    // Same stored state, second run: repairing again would post a toggle that
    // SHOWS the icons the user is deliberately keeping hidden.
    expect(claimLegacyDesktopIconRepair(s)).toBe(false);
    expect(s.dump().get(MARKER)).toBe('1');
  });

  it('does not claim when the icons are shown — a hide then belongs to the user', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ desktopIconsVisible: true }) });
    expect(claimLegacyDesktopIconRepair(s)).toBe(false);
  });

  it('consumes the check even when it does not claim, so it can never fire later', () => {
    const s = writableStorage({ [KEY]: JSON.stringify({ desktopIconsVisible: true }) });
    expect(claimLegacyDesktopIconRepair(s)).toBe(false);
    // User hides icons after upgrading: that hide went through the shell and is
    // already persisted, so the migration must stay silent.
    s.setItem(KEY, JSON.stringify({ desktopIconsVisible: false }));
    expect(claimLegacyDesktopIconRepair(s)).toBe(false);
  });

  it('tolerates an absent blob (fresh install) and a corrupt one', () => {
    expect(claimLegacyDesktopIconRepair(writableStorage())).toBe(false);
    expect(claimLegacyDesktopIconRepair(writableStorage({ [KEY]: '{not json' }))).toBe(false);
  });
});
