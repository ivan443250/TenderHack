import { useEffect, useRef } from "react";

type Rgb = [number, number, number];
type GradientStop = [number, Rgb];

// Figma "Ellipse 3" (129:791): a 450px circle with a 50px layer blur and the "Shape-based
// particles" shader effect on top. At the node's settings (dot density 50% → 3.8px particles on a
// 3.8px noise grid) the particle field reads as a continuous surface, so it is reproduced as a
// per-cell colour field instead of visible dots.
const SIZE = 900; // well past the blurred disc, so the bulged glow never meets the canvas edge
const DISC_RADIUS = 225;
const BLUR_SIGMA = 50;
const EDGE_FADE_START = 350; // radius where the output starts fading to fully transparent
const EDGE_FADE_END = 435;
const CELL = 6; // simulation/render cell (px); the field is bilinearly upscaled onto the canvas
const GRID = SIZE / CELL;
const SOFTEN_BLUR = 12; // px of CSS blur that melts the colour rings into each other

// Shader parameters lifted from the Figma node.
const MOUSE_RADIUS = 220;
const MOUSE_PUSH = 62; // mouseStrength 50%: 300 * (220/400) / baseStiffness(300/112) px away from the cursor
const PERSPECTIVE = 600; // 1200 - mouseZOffset(150) * 4
const Z_LIFT = 118; // settled lift toward the camera at full response (mouseZOffset 150)
const SPRING_STIFFNESS = (300 / 112) * 0.99; // mouseSpring 33%
const SPRING_RETENTION = 1 - 50 * 0.00375; // mouseDamping 50%
const REST_COVERAGE = Math.PI / 4; // a resting particle is a circle inscribed in its grid cell
const SOURCE_MIX = 0.49;
const HOLE_EDGE = 45; // px of soft edge where the pushed-away particles open a hole at the cursor

// `particleGradient`: particle colour by cursor response (red at rest → magenta → cyan).
const PARTICLE_STOPS: GradientStop[] = [
  [0, [226, 29, 45]],
  [0.5, [245, 41, 242]],
  [1, [0, 255, 253]]
];
// The ellipse's own fill (top → bottom), shown through the particle gaps at `sourceMix`.
const SOURCE_STOPS: GradientStop[] = [
  [0, [149, 241, 255]],
  [0.5, [215, 121, 255]],
  [1, [226, 29, 45]]
];
// `backgroundColor`: what the particles sit on, visible through gaps and in the cursor hole.
const BACKGROUND: Rgb = [255, 47, 252];

function sampleGradient(stops: GradientStop[], t: number): Rgb {
  const clamped = Math.max(0, Math.min(1, t));
  for (let i = 1; i < stops.length; i++) {
    const [prevPos, prevColor] = stops[i - 1];
    const [pos, color] = stops[i];
    if (clamped <= pos) {
      const span = pos - prevPos || 1;
      const amount = (clamped - prevPos) / span;
      return [
        prevColor[0] + (color[0] - prevColor[0]) * amount,
        prevColor[1] + (color[1] - prevColor[1]) * amount,
        prevColor[2] + (color[2] - prevColor[2]) * amount
      ];
    }
  }
  return stops[stops.length - 1][1];
}

function buildLut(stops: GradientStop[]): Float32Array {
  const lut = new Float32Array(256 * 3);
  for (let i = 0; i < 256; i++) {
    const [r, g, b] = sampleGradient(stops, i / 255);
    lut[i * 3] = r;
    lut[i * 3 + 1] = g;
    lut[i * 3 + 2] = b;
  }
  return lut;
}

const PARTICLE_LUT = buildLut(PARTICLE_STOPS);
const SOURCE_LUT = buildLut(SOURCE_STOPS);

/** "Dome" mouse falloff from the shader: pow(1 - t², 1.5). */
function domeFalloff(distance: number): number {
  if (distance >= MOUSE_RADIUS) return 0;
  const t = distance / MOUSE_RADIUS;
  const u = 1 - t * t;
  return u * Math.sqrt(u);
}

/** Alpha of the 50px-blurred disc at `distance` from its centre (logistic ≈ Gaussian CDF). */
function discAlpha(distance: number): number {
  return 1 / (1 + Math.exp((1.702 * (distance - DISC_RADIUS)) / BLUR_SIGMA));
}

/** 1 inside the canvas, easing to 0 before its edge so nothing is ever clipped by the frame. */
function edgeFade(distance: number): number {
  const t = Math.max(0, Math.min(1, (distance - EDGE_FADE_START) / (EDGE_FADE_END - EDGE_FADE_START)));
  return 1 - t * t * (3 - 2 * t);
}

type Pointer = { x: number; y: number; hovering: boolean };

/**
 * Canvas2D reproduction of the Figma shader on the home-screen glow. The original is a WebGPU
 * particle field (unreleased "HTML-in-Canvas" API) whose dots are dense enough to look like a solid
 * gradient, so this renders the same thing as a smooth colour field: each cell runs the shader's
 * spring/damping response to the cursor, which drives the red→magenta→cyan colour, pushes the
 * surface away from the cursor (opening a hole onto the background colour), lifts it toward the
 * viewer, and bulges the blurred disc edge.
 */
export function InteractiveGlow({ className }: { className?: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const pointerRef = useRef<Pointer | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    const field = document.createElement("canvas");
    field.width = GRID;
    field.height = GRID;
    const fieldCtx = field.getContext("2d");
    if (!fieldCtx) return;

    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = SIZE * dpr;
    canvas.height = SIZE * dpr;
    ctx.scale(dpr, dpr);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";

    const image = fieldCtx.createImageData(GRID, GRID);
    const response = new Float32Array(GRID * GRID);
    const velocity = new Float32Array(GRID * GRID);

    let frame = 0;
    let lastTime = 0;

    function step(dt: number): boolean {
      const pointer = pointerRef.current;
      const frames = dt * 60; // shader physics is calibrated per 60fps frame
      const retention = Math.pow(SPRING_RETENTION, frames);
      const data = image.data;
      let active = false;

      for (let row = 0; row < GRID; row++) {
        const py = (row + 0.5) * CELL;
        const cy = py - SIZE / 2;
        for (let col = 0; col < GRID; col++) {
          const i = row * GRID + col;
          const px = (col + 0.5) * CELL;
          const cx = px - SIZE / 2;
          let r = response[i];
          let v = velocity[i];

          let dx = 0;
          let dy = 0;
          let dist = Infinity;
          let target = 0;
          if (pointer) {
            dx = px - pointer.x;
            dy = py - pointer.y;
            dist = Math.sqrt(dx * dx + dy * dy);
            if (pointer.hovering) target = domeFalloff(dist);
          }

          v = (v + (target - r) * SPRING_STIFFNESS * frames) * retention;
          r += v * dt;
          response[i] = r;
          velocity[i] = v;
          if (Math.abs(r) > 0.002 || Math.abs(v) > 0.02) active = true;

          const k = Math.max(0, Math.min(1, r));
          const o = i * 4;

          // Rest position of what's displayed at this cell: undo the perspective lift (about the
          // disc centre) and the push away from the cursor.
          const magnify = PERSPECTIVE / (PERSPECTIVE - Z_LIFT * k);
          let qx = cx / magnify;
          let qy = cy / magnify;
          let hole = 1;
          if (k > 0 && dist < Infinity) {
            const push = MOUSE_PUSH * k;
            if (dist > 0.001) {
              qx -= (dx / dist) * push;
              qy -= (dy / dist) * push;
            }
            const edge = Math.max(0, Math.min(1, (dist - push) / HOLE_EDGE));
            hole = edge * edge * (3 - 2 * edge);
          }

          const mask = discAlpha(Math.sqrt(qx * qx + qy * qy)) * edgeFade(Math.sqrt(cx * cx + cy * cy));
          if (mask < 0.002) {
            data[o + 3] = 0;
            continue;
          }

          const sourceIndex = Math.round(Math.max(0, Math.min(1, (qy + DISC_RADIUS) / (2 * DISC_RADIUS))) * 255) * 3;
          const sourceMix = SOURCE_MIX * mask;
          const baseR = SOURCE_LUT[sourceIndex] * sourceMix + BACKGROUND[0] * (1 - sourceMix);
          const baseG = SOURCE_LUT[sourceIndex + 1] * sourceMix + BACKGROUND[1] * (1 - sourceMix);
          const baseB = SOURCE_LUT[sourceIndex + 2] * sourceMix + BACKGROUND[2] * (1 - sourceMix);

          // Particles grow with the response (mouseScale 100%) and the lift, closing their gaps.
          const scale = magnify * (1 + k);
          const coverage = Math.min(1, REST_COVERAGE * scale * scale) * hole;
          const particleIndex = Math.round(k * 255) * 3;

          data[o] = PARTICLE_LUT[particleIndex] * coverage + baseR * (1 - coverage);
          data[o + 1] = PARTICLE_LUT[particleIndex + 1] * coverage + baseG * (1 - coverage);
          data[o + 2] = PARTICLE_LUT[particleIndex + 2] * coverage + baseB * (1 - coverage);
          data[o + 3] = mask * 255;
        }
      }

      fieldCtx!.putImageData(image, 0, 0);
      ctx!.clearRect(0, 0, SIZE, SIZE);
      ctx!.drawImage(field, 0, 0, SIZE, SIZE);
      return active;
    }

    function tick(now: number) {
      const dt = lastTime ? Math.min((now - lastTime) / 1000, 0.1) : 0;
      lastTime = now;
      const active = step(dt);
      // Keep animating while the cursor is over the page or springs are still settling; otherwise
      // idle on the last frame until the next pointer event.
      if (active || pointerRef.current?.hovering) {
        frame = requestAnimationFrame(tick);
      } else {
        frame = 0;
        lastTime = 0;
      }
    }

    function wake() {
      if (!frame) frame = requestAnimationFrame(tick);
    }

    // Listen on the window so the glow keeps reacting while the cursor is over the heading, the
    // composer or the chips that sit on top of it; the 220px dome gates the visible reaction.
    function onPointerMove(event: PointerEvent) {
      const rect = canvas!.getBoundingClientRect();
      pointerRef.current = { x: event.clientX - rect.left, y: event.clientY - rect.top, hovering: true };
      wake();
    }
    function onPointerLeave() {
      if (pointerRef.current) pointerRef.current.hovering = false;
      wake();
    }
    window.addEventListener("pointermove", onPointerMove);
    document.documentElement.addEventListener("pointerleave", onPointerLeave);
    window.addEventListener("blur", onPointerLeave);

    wake();
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("pointermove", onPointerMove);
      document.documentElement.removeEventListener("pointerleave", onPointerLeave);
      window.removeEventListener("blur", onPointerLeave);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      aria-hidden
      className={`pointer-events-none ${className ?? ""}`}
      style={{ width: SIZE, height: SIZE, filter: `blur(${SOFTEN_BLUR}px)` }}
    />
  );
}
