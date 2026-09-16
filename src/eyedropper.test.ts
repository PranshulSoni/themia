import { describe, it, expect } from 'vitest';
import { normalizeEyedropperHex } from './eyedropper.ts';

// The EyeDropper API result (`sRGBHex`) is untrusted external input: a native
// picker value crossing the trust boundary into our color store. It must be
// normalized to our canonical `#rrggbb` lowercase form or rejected, never fed
// half-validated into props.onChange (themia-releases#46).
describe('normalizeEyedropperHex', () => {
  it('passes a canonical lowercase 6-digit hex through unchanged', () => {
    expect(normalizeEyedropperHex('#ffffff')).to.equal('#ffffff');
    expect(normalizeEyedropperHex('#60cdff')).to.equal('#60cdff');
  });

  it('lowercases uppercase / mixed-case hex', () => {
    expect(normalizeEyedropperHex('#FFFFFF')).to.equal('#ffffff');
    expect(normalizeEyedropperHex('#AbC123')).to.equal('#abc123');
  });

  it('trims surrounding whitespace', () => {
    expect(normalizeEyedropperHex('  #00ff00 ')).to.equal('#00ff00');
  });

  it('rejects values missing the leading #', () => {
    expect(normalizeEyedropperHex('ffffff')).to.equal(null);
  });

  it('rejects the wrong length (3-digit shorthand, 8-digit alpha)', () => {
    expect(normalizeEyedropperHex('#fff')).to.equal(null);
    expect(normalizeEyedropperHex('#ffffffff')).to.equal(null);
  });

  it('rejects non-hex characters', () => {
    expect(normalizeEyedropperHex('#gggggg')).to.equal(null);
    expect(normalizeEyedropperHex('rgb(0,0,0)')).to.equal(null);
  });

  it('rejects empty / garbage input', () => {
    expect(normalizeEyedropperHex('')).to.equal(null);
    expect(normalizeEyedropperHex('   ')).to.equal(null);
  });
});
