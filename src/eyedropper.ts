// Trust-boundary normalizer for the color-picker pipette (themia-releases#46).
//
// The EyeDropper API hands back an `sRGBHex` string picked from anywhere on
// screen. This is external input that must be validated before it reaches the color
// store. Return our canonical `#rrggbb` lowercase form, or `null` when the
// value isn't a plain 6-digit hex (short-hand, alpha, or garbage), so the
// caller can reject it loudly instead of feeding a half-valid color through.

const HEX6 = /^#[0-9a-f]{6}$/;

export function normalizeEyedropperHex(sRGBHex: string): string | null {
  const hex = sRGBHex.trim().toLowerCase();
  return HEX6.test(hex) ? hex : null;
}
