import { createEffect, on, onCleanup, onMount } from 'solid-js';
import {
  hackerVariant,
  hackerTileTiming,
  hackerPlayNonce,
  type HackerPanelKind,
} from '../hacker.ts';
import './HackerTile.css';

// Per-widget hacker animation ([S-23]). Rendered INSIDE each widget tile while
// its hacker intro plays: a canvas that fills the tile and draws ONE
// movie-hacker animation: a scrolling exploit/tcpdump console or memory
// hex-dump, a rotating wireframe protein, a CCTV feed with a tracking reticle,
// a radar/geometry scope, or an oscilloscope, picked by widget index and
// cycled so adjacent tiles differ (hackerVariant). It is opaque (covering the
// live widget content) while it churns, then fades out to reveal the widget.
// Its start-to-finish lifetime equals the configured duration (hackerTileTiming).
//
// Because the canvas is the widget's OWN child it needs NO coordinate math and
// no full-screen overlay; it simply fills the tile (transform/scale-agnostic),
// and it plays on EVERY widget mount (boot AND view switches), since the widget
// subtree remounts on a view change. The settings ▶ preview (same view, no
// remount) restarts it via the `hackerPlayNonce` signal. pointer-events:none so
// clicks pass through. Self-stops when the show is over (no idle rAF).
//
// The green-phosphor palette is self-contained (this style's identity).

const HEX = '0123456789ABCDEF';

interface TileState {
  kind: HackerPanelKind;
  hex: boolean;
  title: string;
  lines?: string[]; scroll?: number; speed?: number; addr?: { v: number };
  pts?: { x: number; y: number; z: number }[]; edges?: [number, number][]; angle?: number;
  sweep?: number; poly?: number; blips?: { ang: number; r: number }[];
  phase?: number;
}

export default function HackerTile(props: { index: number; durationMs: number }) {
  let canvasEl: HTMLCanvasElement | undefined;
  let raf: number | null = null;

  onMount(() => {
    const cnv = canvasEl;
    const ctx = cnv?.getContext('2d');
    if (!cnv || !ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const U = dpr;
    const green = (a: number) => `rgba(54, 240, 138, ${a})`;
    const greenBright = (a: number) => `rgba(200, 255, 214, ${a})`;
    const ri = (a: number, b: number) => a + ((Math.random() * (b - a + 1)) | 0);
    const hx = (n: number) => { let s = ''; for (let i = 0; i < n; i++) s += HEX[(Math.random() * 16) | 0]; return s; };
    const pick = <T,>(arr: T[]): T => arr[(Math.random() * arr.length) | 0];

    let w = 1, h = 1;
    function measure() {
      const r = cnv!.getBoundingClientRect();
      const bw = Math.max(1, Math.round(r.width * dpr));
      const bh = Math.max(1, Math.round(r.height * dpr));
      if (bw !== w || bh !== h) { w = bw; h = bh; cnv!.width = w; cnv!.height = h; }
    }

    function genConsoleLine(st: TileState): string {
      if (st.hex) {
        const a = (st.addr!.v >>> 0).toString(16).toUpperCase().padStart(8, '0');
        st.addr!.v += 16;
        let bytes = '', ascii = '';
        for (let i = 0; i < 8; i++) {
          const b = hx(2); bytes += b + ' ';
          const c = parseInt(b, 16);
          ascii += c >= 33 && c < 127 ? String.fromCharCode(c) : '.';
        }
        return `${a}  ${bytes} |${ascii}|`;
      }
      return pick([
        `$ nmap -sS 10.0.${ri(0, 9)}.${ri(0, 255)}`,
        `[+] ${ri(1, 65535)}/tcp open  ${pick(['ssh', 'http', 'smb', 'rpc', 'ldap', 'vnc'])}`,
        `[*] sending stage (${ri(180, 9999)} bytes)`,
        `[+] session ${ri(1, 9)} opened`,
        `brute ${pick(['admin', 'root', 'toor', 'hunter2'])} -> ${pick(['FAIL', 'FAIL', 'FAIL', 'OK'])}`,
        `xor 0x${hx(2)} @ 0x${hx(8)}`,
        `10.0.${ri(0, 9)}.${ri(1, 254)} > 192.168.${ri(0, 9)}.${ri(1, 254)}: ${pick(['[S]', '[.]', '[P.]', '[F.]'])} seq ${ri(0, 999999)}`,
        `GET /${pick(['admin', 'api/keys', '.env'])} ${pick(['200', '403', '301', '500'])}`,
        `[!] bypassing ${pick(['WAF', 'ASLR', '2FA'])}`,
      ]);
    }

    function makeState(): TileState {
      const v = hackerVariant(props.index);
      const st: TileState = { kind: v.kind, hex: !!v.hex, title: `${v.tag}-${String(props.index + 1).padStart(2, '0')}` };
      if (st.kind === 'console') {
        st.lines = []; st.scroll = 0; st.speed = 0.5 + Math.random() * 1.1; st.addr = { v: 0x7ffe0000 };
      } else if (st.kind === 'molecule') {
        const N = 16;
        const pts: { x: number; y: number; z: number }[] = [];
        for (let i = 0; i < N; i++) {
          const a = i * 0.62;
          pts.push({ x: Math.cos(a) * 0.55, y: (i / (N - 1) - 0.5) * 1.4, z: Math.sin(a) * 0.55 });
        }
        const edges: [number, number][] = [];
        for (let i = 0; i < N - 1; i++) edges.push([i, i + 1]);
        for (let i = 0; i < N - 2; i += 4) edges.push([i, i + 2]);
        for (let i = 2; i < N; i += 3) {
          const a = i * 0.62 + 1.3;
          pts.push({ x: Math.cos(a) * 0.95, y: (i / (N - 1) - 0.5) * 1.4, z: Math.sin(a) * 0.95 });
          edges.push([i, pts.length - 1]);
        }
        st.pts = pts; st.edges = edges; st.angle = Math.random() * Math.PI * 2;
      } else if (st.kind === 'radar') {
        st.sweep = 0; st.poly = 0;
        st.blips = [{ ang: 0.6, r: 0.5 }, { ang: 2.1, r: 0.82 }, { ang: 3.5, r: 0.34 }, { ang: 4.6, r: 0.7 }, { ang: 5.6, r: 0.55 }];
      } else {
        st.phase = Math.random() * Math.PI * 2;
      }
      return st;
    }

    type Body = { bx: number; by: number; bw: number; bh: number };
    function clipBody(b: Body) { ctx!.beginPath(); ctx!.rect(b.bx, b.by, b.bw, b.bh); ctx!.clip(); }

    function drawConsole(s: TileState, b: Body, alpha: number, dt: number) {
      const lineH = 14 * U;
      s.scroll = (s.scroll ?? 0) + (s.speed ?? 1) * dt;
      while (s.scroll! >= lineH) {
        s.scroll! -= lineH;
        s.lines!.push(genConsoleLine(s));
        if (s.lines!.length > 200) s.lines!.shift();
      }
      ctx!.save();
      clipBody(b);
      ctx!.font = `${Math.round(11 * U)}px "Cascadia Code", "Consolas", monospace`;
      ctx!.textBaseline = 'alphabetic';
      const lines = s.lines!;
      for (let k = 0; ; k++) {
        const idx = lines.length - 1 - k;
        if (idx < 0) break;
        const yy = b.by + b.bh - (s.scroll ?? 0) - k * lineH;
        if (yy < b.by - lineH) break;
        const fade = Math.max(0.12, 1 - k * 0.06);
        ctx!.fillStyle = k === 0 ? greenBright(0.95 * alpha) : green(fade * 0.85 * alpha);
        ctx!.fillText(lines[idx], b.bx + 2 * U, yy);
      }
      ctx!.restore();
    }

    function drawMolecule(s: TileState, b: Body, alpha: number, dt: number) {
      s.angle = (s.angle ?? 0) + 0.022 * dt;
      const cx = b.bx + b.bw / 2, cy = b.by + b.bh / 2;
      const R = Math.min(b.bw, b.bh) * 0.36;
      const cosX = Math.cos(0.4), sinX = Math.sin(0.4);
      const ca = Math.cos(s.angle), sa = Math.sin(s.angle);
      const proj = (p: { x: number; y: number; z: number }) => {
        const x = p.x * ca - p.z * sa;
        const z = p.x * sa + p.z * ca;
        const y2 = p.y * cosX - z * sinX;
        const z2 = p.y * sinX + z * cosX;
        const f = 2.6, sc = f / (f - z2);
        return { sx: cx + x * sc * R, sy: cy + y2 * sc * R, depth: z2 };
      };
      const pr = s.pts!.map(proj);
      ctx!.save();
      clipBody(b);
      for (const [i, j] of s.edges!) {
        const a = pr[i], c = pr[j];
        const d = (a.depth + c.depth) / 2;
        ctx!.strokeStyle = green((0.18 + (d + 1) * 0.22) * alpha);
        ctx!.lineWidth = (0.6 + (d + 1) * 0.5) * U;
        ctx!.beginPath(); ctx!.moveTo(a.sx, a.sy); ctx!.lineTo(c.sx, c.sy); ctx!.stroke();
      }
      pr.forEach((p, i) => {
        const r = (1.4 + (p.depth + 1) * 1.3) * U;
        ctx!.fillStyle = i % 5 === 0 ? greenBright(0.9 * alpha) : green((0.4 + (p.depth + 1) * 0.3) * alpha);
        ctx!.beginPath(); ctx!.arc(p.sx, p.sy, r, 0, Math.PI * 2); ctx!.fill();
      });
      ctx!.restore();
    }

    function drawRadar(s: TileState, b: Body, alpha: number, dt: number) {
      s.sweep = (s.sweep ?? 0) + 0.035 * dt;
      s.poly = (s.poly ?? 0) - 0.018 * dt;
      const cx = b.bx + b.bw / 2, cy = b.by + b.bh / 2;
      const maxR = Math.min(b.bw, b.bh) * 0.46;
      ctx!.save();
      clipBody(b);
      ctx!.strokeStyle = green(0.25 * alpha);
      ctx!.lineWidth = 1 * U;
      for (let i = 1; i <= 4; i++) { ctx!.beginPath(); ctx!.arc(cx, cy, (maxR * i) / 4, 0, Math.PI * 2); ctx!.stroke(); }
      ctx!.beginPath();
      ctx!.moveTo(cx - maxR, cy); ctx!.lineTo(cx + maxR, cy);
      ctx!.moveTo(cx, cy - maxR); ctx!.lineTo(cx, cy + maxR);
      ctx!.stroke();
      const drawPoly = (sides: number, rad: number, rot: number, a: number) => {
        ctx!.strokeStyle = green(a * alpha);
        ctx!.beginPath();
        for (let i = 0; i <= sides; i++) {
          const ang = rot + (i / sides) * Math.PI * 2;
          const px = cx + Math.cos(ang) * rad, py = cy + Math.sin(ang) * rad;
          if (i === 0) ctx!.moveTo(px, py); else ctx!.lineTo(px, py);
        }
        ctx!.stroke();
      };
      drawPoly(6, maxR * 0.78, s.poly!, 0.35);
      drawPoly(3, maxR * 0.5, -s.poly! * 1.6, 0.3);
      for (let t = 0; t < 10; t++) {
        const ang = s.sweep! - t * 0.06;
        ctx!.strokeStyle = green((0.5 - t * 0.05) * alpha);
        ctx!.lineWidth = 1.5 * U;
        ctx!.beginPath(); ctx!.moveTo(cx, cy); ctx!.lineTo(cx + Math.cos(ang) * maxR, cy + Math.sin(ang) * maxR); ctx!.stroke();
      }
      for (const bl of s.blips!) {
        let diff = ((s.sweep! - bl.ang) % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
        if (diff > Math.PI) diff = Math.PI * 2 - diff;
        const flash = Math.max(0, 1 - diff * 1.6);
        const bx = cx + Math.cos(bl.ang) * maxR * bl.r, by = cy + Math.sin(bl.ang) * maxR * bl.r;
        ctx!.fillStyle = greenBright((0.25 + flash * 0.7) * alpha);
        ctx!.beginPath(); ctx!.arc(bx, by, (2 + flash * 3) * U, 0, Math.PI * 2); ctx!.fill();
      }
      ctx!.restore();
    }

    function drawCamera(s: TileState, b: Body, alpha: number, dt: number, elapsed: number) {
      s.phase = (s.phase ?? 0) + 0.02 * dt;
      const t = s.phase!;
      ctx!.save();
      clipBody(b);
      ctx!.fillStyle = `rgba(2, 10, 6, ${0.5 * alpha})`;
      ctx!.fillRect(b.bx, b.by, b.bw, b.bh);
      ctx!.strokeStyle = `rgba(54, 240, 138, ${0.06 * alpha})`;
      ctx!.lineWidth = 1 * U;
      for (let y = b.by; y < b.by + b.bh; y += 3 * U) { ctx!.beginPath(); ctx!.moveTo(b.bx, y); ctx!.lineTo(b.bx + b.bw, y); ctx!.stroke(); }
      for (let i = 0; i < 10; i++) {
        ctx!.fillStyle = green(0.08 * alpha + Math.random() * 0.12 * alpha);
        ctx!.fillRect(b.bx + Math.random() * b.bw, b.by + Math.random() * b.bh, U, U);
      }
      const tx = b.bx + b.bw / 2 + Math.sin(t * 1.7) * b.bw * 0.28;
      const ty = b.by + b.bh / 2 + Math.cos(t * 1.3) * b.bh * 0.26;
      const box = Math.min(22 * U, b.bw * 0.3);
      ctx!.strokeStyle = greenBright(0.85 * alpha);
      ctx!.lineWidth = 1.4 * U;
      ctx!.strokeRect(tx - box / 2, ty - box / 2, box, box);
      ctx!.beginPath();
      ctx!.moveTo(tx - box, ty); ctx!.lineTo(tx + box, ty);
      ctx!.moveTo(tx, ty - box); ctx!.lineTo(tx, ty + box);
      ctx!.stroke();
      const cm = 6 * U, cl = 12 * U;
      ctx!.strokeStyle = green(0.6 * alpha);
      ctx!.lineWidth = 1.5 * U;
      const corner = (x: number, y: number, sx: number, sy: number) => { ctx!.beginPath(); ctx!.moveTo(x + sx * cl, y); ctx!.lineTo(x, y); ctx!.lineTo(x, y + sy * cl); ctx!.stroke(); };
      corner(b.bx + cm, b.by + cm, 1, 1);
      corner(b.bx + b.bw - cm, b.by + cm, -1, 1);
      corner(b.bx + cm, b.by + b.bh - cm, 1, -1);
      corner(b.bx + b.bw - cm, b.by + b.bh - cm, -1, -1);
      if (Math.sin(elapsed * 0.008) > 0) {
        ctx!.fillStyle = `rgba(255, 70, 70, ${0.9 * alpha})`;
        ctx!.beginPath(); ctx!.arc(b.bx + b.bw - 12 * U, b.by + 10 * U, 3 * U, 0, Math.PI * 2); ctx!.fill();
      }
      ctx!.restore();
    }

    function drawWave(s: TileState, b: Body, alpha: number, dt: number) {
      s.phase = (s.phase ?? 0) + 0.18 * dt;
      const cy = b.by + b.bh / 2;
      ctx!.save();
      clipBody(b);
      ctx!.strokeStyle = green(0.12 * alpha);
      ctx!.lineWidth = 1 * U;
      for (let x = b.bx; x < b.bx + b.bw; x += 22 * U) { ctx!.beginPath(); ctx!.moveTo(x, b.by); ctx!.lineTo(x, b.by + b.bh); ctx!.stroke(); }
      ctx!.beginPath(); ctx!.moveTo(b.bx, cy); ctx!.lineTo(b.bx + b.bw, cy); ctx!.stroke();
      ctx!.strokeStyle = greenBright(0.85 * alpha);
      ctx!.lineWidth = 1.5 * U;
      ctx!.beginPath();
      const step = 2 * U;
      for (let x = 0; x <= b.bw; x += step) {
        const v = Math.sin(x * 0.05 + s.phase!) * 0.5 + Math.sin(x * 0.13 + s.phase! * 1.7) * 0.25 + (Math.random() - 0.5) * 0.14;
        const yy = cy + v * b.bh * 0.34;
        if (x === 0) ctx!.moveTo(b.bx + x, yy); else ctx!.lineTo(b.bx + x, yy);
      }
      ctx!.stroke();
      ctx!.restore();
    }

    function drawTile(st: TileState, alpha: number, dt: number, elapsed: number) {
      const titleH = h > 56 * U && w > 70 * U ? 13 * U : 0;
      ctx!.save();
      ctx!.beginPath(); ctx!.rect(0, 0, w, h); ctx!.clip();
      // Near-opaque so the (live) widget content under the tile is hidden during
      // the churn; as alpha fades the content is revealed.
      ctx!.fillStyle = `rgba(3, 14, 8, ${0.97 * alpha})`;
      ctx!.fillRect(0, 0, w, h);
      if (titleH) {
        ctx!.fillStyle = green(0.1 * alpha);
        ctx!.fillRect(0, 0, w, titleH);
        ctx!.fillStyle = greenBright(0.85 * alpha);
        ctx!.font = `${Math.round(9 * U)}px "Cascadia Code", monospace`;
        ctx!.textBaseline = 'middle';
        ctx!.fillText(st.title, 5 * U, titleH / 2);
        if (Math.sin(elapsed * 0.01 + props.index) > 0) {
          ctx!.fillStyle = green(0.85 * alpha);
          ctx!.beginPath(); ctx!.arc(w - 7 * U, titleH / 2, 2 * U, 0, Math.PI * 2); ctx!.fill();
        }
      }
      const pad = 5 * U;
      const body = { bx: pad, by: titleH + pad, bw: w - 2 * pad, bh: h - titleH - 2 * pad };
      if (body.bw > 10 * U && body.bh > 10 * U) {
        switch (st.kind) {
          case 'console': drawConsole(st, body, alpha, dt); break;
          case 'molecule': drawMolecule(st, body, alpha, dt); break;
          case 'radar': drawRadar(st, body, alpha, dt); break;
          case 'camera': drawCamera(st, body, alpha, dt, elapsed); break;
          case 'wave': drawWave(st, body, alpha, dt); break;
        }
      }
      ctx!.restore();
      ctx!.strokeStyle = green(0.65 * alpha);
      ctx!.lineWidth = 1.5 * U;
      ctx!.strokeRect(0.75 * U, 0.75 * U, w - 1.5 * U, h - 1.5 * U);
    }

    // ── play lifecycle ──
    let state = makeState();
    let start: number | null = null, last: number | null = null;
    let revealMs = 0, fadeMs = 1, endMs = 0;
    function computeTiming() {
      // start-to-finish === the configured duration ([S-23]).
      ({ revealMs, fadeMs, endMs } = hackerTileTiming(props.durationMs, props.index));
    }
    // Opaque from frame 0 (no content flash); fades out after the reveal point.
    const alphaAt = (elapsed: number) =>
      elapsed < revealMs ? 1 : Math.max(0, 1 - (elapsed - revealMs) / fadeMs);

    const frame = (now: number) => {
      if (start == null) start = now;
      if (last == null) last = now;
      const elapsed = now - start;
      const dt = Math.min(2.5, (now - last) / 16.667);
      last = now;
      measure();
      try {
        ctx.clearRect(0, 0, w, h);
        const alpha = alphaAt(elapsed);
        if (alpha > 0.01) drawTile(state, alpha, dt, elapsed);
      } catch {
        /* ignore: a cosmetic draw glitch must never keep the rAF spinning */
      }
      if (elapsed >= endMs) { ctx.clearRect(0, 0, w, h); raf = null; return; } // done, stop
      raf = requestAnimationFrame(frame);
    };

    function play() {
      // [S-23]/[S-15]: suppress the movie-hacker intro under prefers-reduced-motion.
      // The canvas only covers the (already-rendered) widget while it draws, so
      // skipping the draw loop reveals the widget immediately with no animation.
      if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
        if (raf != null) { cancelAnimationFrame(raf); raf = null; }
        return;
      }
      computeTiming();
      state = makeState();
      start = null; last = null;
      if (raf == null) raf = requestAnimationFrame(frame);
    }

    play(); // initial play on mount (boot or view switch, the widget remounted)
    // Settings ▶ replay (same view, no remount) restarts the canvas.
    createEffect(on(hackerPlayNonce, () => play(), { defer: true }));
  });

  onCleanup(() => { if (raf != null) cancelAnimationFrame(raf); });

  return <canvas ref={canvasEl} class="hacker-tile" aria-hidden="true" />;
}
