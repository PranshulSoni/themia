// Image side of the brand-mark pipeline: identify what a favicon actually is,
// and turn it into one tile-sized PNG.
//
// Build-time only (scripts/gen-brand-icons.mjs) — nothing here ships. It exists
// because favicons are published in whatever container the site chose: a .ico
// bundling six sizes as uncompressed bitmaps, or a 256px PNG for a 22px disc.
// Both work in an <img>, and both cost 20-30 KB of data URI apiece, so the one
// size the tile needs is unpacked and re-encoded here instead. Only the
// container changes — the published artwork is never redrawn.
//
// No image library: the decoding needed is narrow (8-bit non-interlaced PNG,
// uncompressed DIB) and a dependency that big for a script this small would be
// the larger cost. Anything outside that narrow set throws, and the caller
// keeps the original file, which still renders.
import { deflateSync, inflateSync } from 'node:zlib';

/** The tile is 22px; 64 survives a 2x display without carrying a 256px payload. */
export const TARGET_PX = 64;

// ── Format sniffing ─────────────────────────────────────────────────────────

const MAGIC = [
  { mime: 'image/png', test: (b) => b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47 },
  { mime: 'image/x-icon', test: (b) => b[0] === 0x00 && b[1] === 0x00 && b[2] === 0x01 && b[3] === 0x00 },
  { mime: 'image/gif', test: (b) => b[0] === 0x47 && b[1] === 0x49 && b[2] === 0x46 },
  { mime: 'image/webp', test: (b) => b[8] === 0x57 && b[9] === 0x45 && b[10] === 0x42 && b[11] === 0x50 },
];

/** Sniffed from the bytes, not from the server's word: a soft-404 answering
 *  `image/png` with an HTML body would otherwise be committed as a "mark".
 *  Returns null when the bytes are not an image at all. */
export function sniff(buf) {
  for (const { mime, test } of MAGIC) if (buf.length > 12 && test(buf)) return mime;
  const head = buf.subarray(0, 400).toString('utf8').trimStart().toLowerCase();
  if (head.startsWith('<svg') || (head.startsWith('<?xml') && head.includes('<svg'))) return 'image/svg+xml';
  return null;
}

// ── PNG encoding ────────────────────────────────────────────────────────────

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, body) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(body.length);
  const typed = Buffer.concat([Buffer.from(type, 'ascii'), body]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typed));
  return Buffer.concat([len, typed, crc]);
}

/** 8-bit RGBA PNG, one filter-none scanline per row. */
export function encodePng({ width, height, rgba }) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;   // bit depth
  ihdr[9] = 6;   // colour type: RGBA
  const raw = Buffer.alloc(height * (width * 4 + 1));
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0; // filter: none
    rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

// ── PNG decoding ────────────────────────────────────────────────────────────

/** 8-bit, non-interlaced PNG → RGBA. Throws on anything else. */
export function decodePng(buf) {
  let width = 0, height = 0, depth = 0, type = 0, palette = null, trns = null;
  const idat = [];
  for (let o = 8; o + 8 <= buf.length;) {
    const len = buf.readUInt32BE(o);
    const name = buf.toString('ascii', o + 4, o + 8);
    const body = buf.subarray(o + 8, o + 8 + len);
    if (name === 'IHDR') {
      width = body.readUInt32BE(0); height = body.readUInt32BE(4);
      depth = body[8]; type = body[9];
      if (body[12] !== 0) throw new Error('interlaced PNG');
    } else if (name === 'PLTE') palette = Buffer.from(body);
    else if (name === 'tRNS') trns = Buffer.from(body);
    else if (name === 'IDAT') idat.push(Buffer.from(body));
    else if (name === 'IEND') break;
    o += 12 + len;
  }
  if (depth !== 8) throw new Error(`${depth}-bit PNG`);
  const bpp = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[type];
  if (!bpp) throw new Error(`PNG colour type ${type}`);

  const stride = width * bpp;
  const raw = inflateSync(Buffer.concat(idat));
  const lines = Buffer.alloc(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const cur = lines.subarray(y * stride, (y + 1) * stride);
    const prev = y > 0 ? lines.subarray((y - 1) * stride, y * stride) : null;
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? cur[i - bpp] : 0;
      const b = prev ? prev[i] : 0;
      const c = prev && i >= bpp ? prev[i - bpp] : 0;
      let v = src[i];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) {
        const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      } else if (filter !== 0) throw new Error(`PNG filter ${filter}`);
      cur[i] = v & 0xff;
    }
  }

  const rgba = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    const s = i * bpp, d = i * 4;
    if (type === 6) { rgba[d] = lines[s]; rgba[d + 1] = lines[s + 1]; rgba[d + 2] = lines[s + 2]; rgba[d + 3] = lines[s + 3]; }
    else if (type === 2) { rgba[d] = lines[s]; rgba[d + 1] = lines[s + 1]; rgba[d + 2] = lines[s + 2]; rgba[d + 3] = 255; }
    else if (type === 0) { rgba[d] = rgba[d + 1] = rgba[d + 2] = lines[s]; rgba[d + 3] = 255; }
    else if (type === 4) { rgba[d] = rgba[d + 1] = rgba[d + 2] = lines[s]; rgba[d + 3] = lines[s + 1]; }
    else {
      if (!palette) throw new Error('indexed PNG without a palette');
      const p = lines[s] * 3;
      rgba[d] = palette[p]; rgba[d + 1] = palette[p + 1]; rgba[d + 2] = palette[p + 2];
      rgba[d + 3] = trns && lines[s] < trns.length ? trns[lines[s]] : 255;
    }
  }
  return { width, height, rgba };
}

// ── ICO decoding ────────────────────────────────────────────────────────────

/** The entry to keep: the one closest to tile size that still outresolves it,
 *  else the largest available (16px is all some sites publish). */
function pickEntry(buf) {
  const count = buf.readUInt16LE(4);
  const entries = [];
  for (let i = 0; i < count; i++) {
    const o = 6 + i * 16;
    entries.push({
      px: buf[o] || 256,                 // 0 means 256 in an ICO directory
      size: buf.readUInt32LE(o + 8),
      offset: buf.readUInt32LE(o + 12),
    });
  }
  if (!entries.length) return null;
  const usable = entries.filter(e => e.px >= 32 && e.px <= 256);
  if (!usable.length) return entries.sort((a, b) => b.px - a.px)[0];
  return usable.sort((a, b) =>
    Math.abs(a.px - TARGET_PX) - Math.abs(b.px - TARGET_PX) || b.px - a.px)[0];
}

/** RGBA pixels of an ICO's embedded DIB (bottom-up BGR(A) or palette indices). */
function dibToRgba(dib, px) {
  const headerSize = dib.readUInt32LE(0);
  const width = dib.readInt32LE(4);
  // An ICO's DIB height counts the colour bitmap AND the 1-bit AND mask.
  const height = Math.abs(dib.readInt32LE(8)) / 2;
  const bpp = dib.readUInt16LE(14);
  const compression = dib.readUInt32LE(16);
  if (compression !== 0) throw new Error(`compressed DIB (${compression})`);
  if (![1, 4, 8, 24, 32].includes(bpp)) throw new Error(`${bpp}bpp DIB`);
  if (width !== px || height !== px) throw new Error(`DIB is ${width}x${height}, directory says ${px}`);

  // Below 16bpp the pixels index a colour table sitting between the header and
  // the bitmap (microsoft.com still ships a 4bpp, 16-colour entry).
  const paletteCount = bpp <= 8 ? (dib.readUInt32LE(32) || 1 << bpp) : 0;
  const palette = dib.subarray(headerSize, headerSize + paletteCount * 4);

  const stride = ((width * bpp + 31) >> 5) << 2;      // rows are 4-byte aligned
  const maskStride = ((width + 31) >> 5) << 2;
  const colour = dib.subarray(headerSize + paletteCount * 4);
  const mask = colour.subarray(stride * height);
  const hasMask = mask.length >= maskStride * height;
  const out = Buffer.alloc(width * height * 4);

  // Some 32bpp entries leave the alpha channel zeroed and rely entirely on the
  // AND mask; reading their alpha bytes literally yields an invisible icon.
  let alphaChannelUsed = false;
  if (bpp === 32) {
    for (let i = 3; i < stride * height; i += 4) {
      if (colour[i] !== 0) { alphaChannelUsed = true; break; }
    }
  }

  const indexAt = (row, x) => {
    const perByte = 8 / bpp;
    const byte = colour[row + Math.floor(x / perByte)];
    const shift = (perByte - 1 - (x % perByte)) * bpp;
    return (byte >> shift) & ((1 << bpp) - 1);
  };

  for (let y = 0; y < height; y++) {
    const srcRow = (height - 1 - y) * stride;          // stored bottom-up
    for (let x = 0; x < width; x++) {
      const d = (y * width + x) * 4;
      let alpha;
      if (bpp <= 8) {
        const p = indexAt(srcRow, x) * 4;
        out[d] = palette[p + 2];
        out[d + 1] = palette[p + 1];
        out[d + 2] = palette[p];
        alpha = 255;
      } else {
        const s = srcRow + x * (bpp >> 3);
        out[d] = colour[s + 2];
        out[d + 1] = colour[s + 1];
        out[d + 2] = colour[s];
        alpha = bpp === 32 && alphaChannelUsed ? colour[s + 3] : 255;
      }
      if (hasMask) {
        const bit = mask[(height - 1 - y) * maskStride + (x >> 3)] >> (7 - (x & 7)) & 1;
        if (bit) alpha = 0;
      }
      out[d + 3] = alpha;
    }
  }
  if (!out.some((v, i) => i % 4 === 3 && v !== 0)) throw new Error('fully transparent after unpacking');
  return { width, height, rgba: out };
}

/** One PNG out of an ICO bundle. Throws on a container this cannot decode. */
export function icoToPng(buf) {
  const entry = pickEntry(buf);
  if (!entry) throw new Error('empty ICO directory');
  const body = buf.subarray(entry.offset, entry.offset + entry.size);
  // Vista-era icons store their large sizes as a whole PNG file; keep it as is.
  if (body[0] === 0x89 && body[1] === 0x50) return { png: body, px: entry.px, embedded: true };
  return { png: encodePng(dibToRgba(body, entry.px)), px: entry.px, embedded: false };
}

// ── Resampling ──────────────────────────────────────────────────────────────

/** Box filter, alpha-premultiplied so a transparent edge does not drag its
 *  (arbitrary) colour into the average and fringe the mark. */
export function downscale({ width, height, rgba }, factor) {
  const w = Math.max(1, Math.round(width / factor)), h = Math.max(1, Math.round(height / factor));
  const out = Buffer.alloc(w * h * 4);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const x0 = Math.floor((x * width) / w), x1 = Math.max(x0 + 1, Math.floor(((x + 1) * width) / w));
      const y0 = Math.floor((y * height) / h), y1 = Math.max(y0 + 1, Math.floor(((y + 1) * height) / h));
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let sy = y0; sy < y1; sy++) {
        for (let sx = x0; sx < x1; sx++) {
          const s = (sy * width + sx) * 4, al = rgba[s + 3];
          r += rgba[s] * al; g += rgba[s + 1] * al; b += rgba[s + 2] * al; a += al; n++;
        }
      }
      const d = (y * w + x) * 4;
      out[d] = a ? Math.round(r / a) : 0;
      out[d + 1] = a ? Math.round(g / a) : 0;
      out[d + 2] = a ? Math.round(b / a) : 0;
      out[d + 3] = Math.round(a / n);
    }
  }
  return { width: w, height: h, rgba: out };
}

// ── Pipeline ────────────────────────────────────────────────────────────────

/**
 * Whatever the site published → one tile-sized PNG, or the bytes untouched when
 * the container is not one this understands (SVG is already ideal; a WebP or an
 * exotic DIB is left alone and still renders in an <img>).
 *
 * Returns `{ buf, mime, note }`, where `note` names every transformation so the
 * generator's log says exactly what happened to each brand.
 */
export function toTilePng(buf, mime) {
  const notes = [];
  if (mime === 'image/x-icon') {
    try {
      const { png, px, embedded } = icoToPng(buf);
      notes.push(`${embedded ? 'unwrapped' : 'transcoded'} ${px}px from .ico`);
      buf = png;
      mime = 'image/png';
    } catch (e) {
      notes.push(`kept .ico as-is (${e.message})`);
    }
  }
  if (mime === 'image/png') {
    try {
      const img = decodePng(buf);
      if (img.width > TARGET_PX * 1.5) {
        buf = encodePng(downscale(img, img.width / TARGET_PX));
        notes.push(`resampled ${img.width}px → ${TARGET_PX}px`);
      }
    } catch (e) {
      notes.push(`left at source size (${e.message})`);
    }
  }
  return { buf, mime, note: notes.join(', ') };
}
