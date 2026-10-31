/**
 * Glass-cockpit HUD instruments drawn with Canvas 2D: airspeed and altitude
 * tapes, a heading ribbon, a vertical-speed scale and a compact attitude ball.
 * Pure drawing functions; values come straight from the simulation.
 *
 * Canvas sizes are in CSS pixels; callers scale the context by devicePixelRatio.
 */
type C = CanvasRenderingContext2D;

const WHITE = '#eef3f8';
const DIM = 'rgba(238,243,248,0.55)';
const CYAN = '#4fd6ff';
const BOX_BG = 'rgba(6,11,19,0.86)';
const FONT = '"Barlow Condensed", "Inter Variable", sans-serif';

/** Dark translucent backing that fades out toward the tape ends. */
function tapeBacking(c: C, x: number, w: number, h: number) {
  const g = c.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(6,10,18,0)');
  g.addColorStop(0.18, 'rgba(6,10,18,0.42)');
  g.addColorStop(0.82, 'rgba(6,10,18,0.42)');
  g.addColorStop(1, 'rgba(6,10,18,0)');
  c.fillStyle = g;
  c.fillRect(x, 0, w, h);
}

/** Fade the drawn content toward the ends (applied last). */
function fadeEnds(c: C, w: number, h: number, horizontal = false) {
  c.save();
  c.globalCompositeOperation = 'destination-in';
  const g = horizontal ? c.createLinearGradient(0, 0, w, 0) : c.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(0,0,0,0)');
  g.addColorStop(0.16, 'rgba(0,0,0,1)');
  g.addColorStop(0.84, 'rgba(0,0,0,1)');
  g.addColorStop(1, 'rgba(0,0,0,0)');
  c.fillStyle = g;
  c.fillRect(0, 0, w, h);
  c.restore();
}

function text(c: C, s: string, x: number, y: number, size: number, color = WHITE, align: CanvasTextAlign = 'center', weight = 600) {
  c.fillStyle = color;
  c.font = `${weight} ${size}px ${FONT}`;
  c.textAlign = align;
  c.textBaseline = 'middle';
  c.fillText(s, x, y);
}

/** Pointer box: rectangle with an arrow toward `side`, cyan outline. */
function pointerBox(c: C, x: number, y: number, w: number, h: number, side: 'left' | 'right') {
  const a = 8;
  c.beginPath();
  if (side === 'right') {
    c.moveTo(x, y - h / 2);
    c.lineTo(x + w, y - h / 2);
    c.lineTo(x + w, y - 5);
    c.lineTo(x + w + a, y);
    c.lineTo(x + w, y + 5);
    c.lineTo(x + w, y + h / 2);
    c.lineTo(x, y + h / 2);
  } else {
    c.moveTo(x + w, y - h / 2);
    c.lineTo(x, y - h / 2);
    c.lineTo(x, y - 5);
    c.lineTo(x - a, y);
    c.lineTo(x, y + 5);
    c.lineTo(x, y + h / 2);
    c.lineTo(x + w, y + h / 2);
  }
  c.closePath();
  c.fillStyle = BOX_BG;
  c.fill();
  c.strokeStyle = CYAN;
  c.lineWidth = 1.2;
  c.stroke();
}

export interface SpeedBands {
  stallKt: number;
  /** Top of normal operating range (green). */
  normalMaxKt: number;
  /** Never exceed. */
  vneKt: number;
}

// ---------------------------------------------------------------- airspeed
export function drawSpeedTape(c: C, w: number, h: number, ias: number, targetKt: number | null, bands: SpeedBands, airborne: boolean) {
  c.clearRect(0, 0, w, h);
  const tapeW = w - 10;
  tapeBacking(c, 0, tapeW, h);
  const pxPerKt = h / 64;
  const yOf = (kt: number) => h / 2 - (kt - ias) * pxPerKt;
  const right = tapeW;

  // Speed bands on the right edge.
  const band = (from: number, to: number, color: string) => {
    const y0 = Math.min(h, Math.max(0, yOf(to)));
    const y1 = Math.min(h, Math.max(0, yOf(from)));
    if (y1 > y0) {
      c.fillStyle = color;
      c.fillRect(right - 4, y0, 4, y1 - y0);
    }
  };
  // The low-speed band only means something in flight; on the runway it would just glare.
  if (airborne) band(-50, bands.stallKt, 'rgba(255,90,79,0.85)');
  band(bands.stallKt, bands.normalMaxKt, 'rgba(79,209,139,0.75)');
  band(bands.normalMaxKt, bands.vneKt, 'rgba(242,177,61,0.85)');
  band(bands.vneKt, 400, 'rgba(255,90,79,0.85)');

  const lo = Math.floor((ias - 40) / 5) * 5;
  for (let kt = Math.max(0, lo); kt <= ias + 40; kt += 5) {
    const y = yOf(kt);
    const major = kt % 10 === 0;
    c.strokeStyle = major ? WHITE : DIM;
    c.lineWidth = 1.2;
    c.beginPath();
    c.moveTo(right - 6 - (major ? 10 : 6), y);
    c.lineTo(right - 6, y);
    c.stroke();
    if (major) text(c, String(kt), right - 22, y, 15, WHITE, 'right');
  }

  // Target-speed bug (Easy flight auto-throttle).
  if (targetKt !== null) {
    const y = Math.min(h - 8, Math.max(8, yOf(targetKt)));
    c.fillStyle = CYAN;
    c.beginPath();
    c.moveTo(right - 1, y);
    c.lineTo(right + 8, y - 6);
    c.lineTo(right + 8, y + 6);
    c.closePath();
    c.fill();
  }
  fadeEnds(c, w, h);

  pointerBox(c, 2, h / 2, tapeW - 16, 30, 'right');
  text(c, String(Math.max(0, Math.round(ias))), 2 + (tapeW - 16) / 2, h / 2 + 1, 22, WHITE, 'center', 600);
}

// ---------------------------------------------------------------- altitude
export function drawAltitudeTape(c: C, w: number, h: number, altFt: number, groundFt: number) {
  c.clearRect(0, 0, w, h);
  tapeBacking(c, 0, w, h);
  const pxPerFt = h / 640;
  const yOf = (ft: number) => h / 2 - (ft - altFt) * pxPerFt;

  // Terrain below: hatched strip from ground level down.
  const gy = yOf(groundFt);
  if (gy < h) {
    c.fillStyle = 'rgba(120,84,48,0.45)';
    c.fillRect(0, Math.max(0, gy), 6, h - Math.max(0, gy));
    c.strokeStyle = 'rgba(242,177,61,0.9)';
    c.lineWidth = 1.5;
    c.beginPath();
    c.moveTo(0, gy);
    c.lineTo(w, gy);
    c.stroke();
  }

  const lo = Math.floor((altFt - 360) / 20) * 20;
  for (let ft = lo; ft <= altFt + 360; ft += 20) {
    if (ft < groundFt - 10) continue; // nothing to read below the ground
    const y = yOf(ft);
    const major = ft % 100 === 0;
    c.strokeStyle = major ? WHITE : DIM;
    c.lineWidth = 1.2;
    c.beginPath();
    c.moveTo(8, y);
    c.lineTo(8 + (major ? 12 : 6), y);
    c.stroke();
    if (major) text(c, ft.toLocaleString('en-US'), 26, y, 14, WHITE, 'left');
  }
  fadeEnds(c, w, h);

  pointerBox(c, 12, h / 2, w - 14, 30, 'left');
  const shown = Math.round(altFt / 10) * 10;
  text(c, shown.toLocaleString('en-US'), 12 + (w - 14) / 2, h / 2 + 1, 21, WHITE, 'center', 600);
}

// ---------------------------------------------------------------- vertical speed
/** Non-linear scale: fine near zero, ±2,000 fpm at the ends. */
const vsiPos = (fpm: number) => Math.sign(fpm) * Math.sqrt(Math.min(2000, Math.abs(fpm)) / 2000);

export function drawVsi(c: C, w: number, h: number, fpm: number, targetFpm: number | null) {
  c.clearRect(0, 0, w, h);
  const mid = h / 2;
  const half = h / 2 - 10;
  c.fillStyle = 'rgba(6,10,18,0.35)';
  c.fillRect(0, 10, w, h - 20);
  for (const v of [-2000, -1000, -500, 0, 500, 1000, 2000]) {
    const y = mid - vsiPos(v) * half;
    c.strokeStyle = v === 0 ? WHITE : DIM;
    c.lineWidth = v === 0 ? 1.5 : 1;
    c.beginPath();
    c.moveTo(w - (v === 0 ? 10 : 6), y);
    c.lineTo(w, y);
    c.stroke();
  }
  if (targetFpm !== null) {
    const ty = mid - vsiPos(targetFpm) * half;
    c.strokeStyle = 'rgba(79,214,255,0.55)';
    c.lineWidth = 2;
    c.beginPath();
    c.moveTo(0, ty);
    c.lineTo(w - 2, ty);
    c.stroke();
  }
  const y = mid - vsiPos(fpm) * half;
  c.strokeStyle = CYAN;
  c.lineWidth = 3;
  c.lineCap = 'round';
  c.beginPath();
  c.moveTo(w - 1, mid);
  c.lineTo(2, y);
  c.stroke();
  c.lineCap = 'butt';
}

// ---------------------------------------------------------------- heading
export function drawHeadingTape(c: C, w: number, h: number, hdg: number) {
  c.clearRect(0, 0, w, h);
  const pxPerDeg = w / 70;
  const xOf = (d: number) => w / 2 + d * pxPerDeg;
  const base = h - 6;
  const g = c.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(6,10,18,0.0)');
  g.addColorStop(1, 'rgba(6,10,18,0.45)');
  c.fillStyle = g;
  c.fillRect(0, 0, w, h);
  const start = Math.floor((hdg - 40) / 5) * 5;
  for (let d = start; d <= hdg + 40; d += 5) {
    const x = xOf(d - hdg);
    const deg = ((d % 360) + 360) % 360;
    const major = deg % 10 === 0;
    c.strokeStyle = major ? WHITE : DIM;
    c.lineWidth = 1.2;
    c.beginPath();
    c.moveTo(x, base);
    c.lineTo(x, base - (major ? 10 : 5));
    c.stroke();
    if (deg % 30 === 0) {
      const card = deg === 0 ? 'N' : deg === 90 ? 'E' : deg === 180 ? 'S' : deg === 270 ? 'W' : String(deg / 10).padStart(2, '0');
      text(c, card, x, base - 20, card.length === 1 ? 16 : 14, card.length === 1 ? CYAN : WHITE);
    }
  }
  c.strokeStyle = 'rgba(238,243,248,0.25)';
  c.lineWidth = 1;
  c.beginPath();
  c.moveTo(0, base + 0.5);
  c.lineTo(w, base + 0.5);
  c.stroke();
  fadeEnds(c, w, h, true);
  // Lubber pointer.
  c.fillStyle = CYAN;
  c.beginPath();
  c.moveTo(w / 2, base + 2);
  c.lineTo(w / 2 - 5, h);
  c.lineTo(w / 2 + 5, h);
  c.closePath();
  c.fill();
}

// ---------------------------------------------------------------- attitude ball
export function drawAttitudeBall(c: C, size: number, pitchDeg: number, rollDeg: number) {
  const r = size / 2 - 2;
  const cx = size / 2;
  const cy = size / 2;
  c.clearRect(0, 0, size, size);
  c.save();
  c.beginPath();
  c.arc(cx, cy, r, 0, Math.PI * 2);
  c.clip();
  c.translate(cx, cy);
  c.rotate((-rollDeg * Math.PI) / 180);
  const pxPerDeg = r / 26;
  const hy = Math.max(-r * 1.8, Math.min(r * 1.8, pitchDeg * pxPerDeg));
  const sky = c.createLinearGradient(0, -r * 2, 0, hy);
  sky.addColorStop(0, '#0f3a6b');
  sky.addColorStop(1, '#2f78c4');
  c.fillStyle = sky;
  c.fillRect(-r * 2, -r * 3 + hy, r * 4, r * 3);
  const gnd = c.createLinearGradient(0, hy, 0, r * 2);
  gnd.addColorStop(0, '#6b4a2b');
  gnd.addColorStop(1, '#3a2716');
  c.fillStyle = gnd;
  c.fillRect(-r * 2, hy, r * 4, r * 3);
  c.strokeStyle = WHITE;
  c.lineWidth = 1.5;
  c.beginPath();
  c.moveTo(-r * 2, hy);
  c.lineTo(r * 2, hy);
  c.stroke();
  c.lineWidth = 1;
  for (const p of [-20, -10, 10, 20]) {
    const y = hy - p * pxPerDeg;
    const half = Math.abs(p) === 10 ? r * 0.28 : r * 0.42;
    c.strokeStyle = 'rgba(238,243,248,0.8)';
    c.beginPath();
    c.moveTo(-half, y);
    c.lineTo(half, y);
    c.stroke();
  }
  c.restore();
  // Bank scale (fixed) and pointer (rotates).
  for (const b of [-45, -30, -20, -10, 0, 10, 20, 30, 45]) {
    const a = ((-90 + b) * Math.PI) / 180;
    const big = b === 0 || Math.abs(b) === 30;
    c.strokeStyle = 'rgba(238,243,248,0.85)';
    c.lineWidth = big ? 1.6 : 1;
    c.beginPath();
    c.moveTo(cx + Math.cos(a) * (r - (big ? 9 : 6)), cy + Math.sin(a) * (r - (big ? 9 : 6)));
    c.lineTo(cx + Math.cos(a) * (r - 1), cy + Math.sin(a) * (r - 1));
    c.stroke();
  }
  c.save();
  c.translate(cx, cy);
  c.rotate((-rollDeg * Math.PI) / 180);
  c.fillStyle = CYAN;
  c.beginPath();
  c.moveTo(0, -r + 10);
  c.lineTo(-5, -r + 18);
  c.lineTo(5, -r + 18);
  c.closePath();
  c.fill();
  c.restore();
  // Fixed aircraft symbol.
  c.strokeStyle = CYAN;
  c.lineWidth = 2.6;
  c.lineCap = 'round';
  c.beginPath();
  c.moveTo(cx - r * 0.5, cy);
  c.lineTo(cx - r * 0.16, cy);
  c.lineTo(cx - r * 0.08, cy + r * 0.08);
  c.moveTo(cx + r * 0.5, cy);
  c.lineTo(cx + r * 0.16, cy);
  c.lineTo(cx + r * 0.08, cy + r * 0.08);
  c.stroke();
  c.lineCap = 'butt';
  c.fillStyle = CYAN;
  c.beginPath();
  c.arc(cx, cy, 2.2, 0, Math.PI * 2);
  c.fill();
  // Thin bezel.
  c.strokeStyle = 'rgba(120,190,255,0.35)';
  c.lineWidth = 1;
  c.beginPath();
  c.arc(cx, cy, r + 1, 0, Math.PI * 2);
  c.stroke();
}
