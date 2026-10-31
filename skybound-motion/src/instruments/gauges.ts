/**
 * Round-dial flight instruments drawn with Canvas 2D. Each draw function takes
 * the gauge centre and radius plus the value(s) it shows. Static parts (faces,
 * scales) are cached per gauge; only needles and moving cards redraw per frame.
 *
 * Markings follow the conventions of a light trainer's "six-pack".
 */
export type Ctx = CanvasRenderingContext2D;

const FACE = '#141516';
const BEZEL = '#2b2d2f';
const INK = '#ecebe6';
const DIM = '#9a9b97';
const NEEDLE = '#f4f3ee';
const D2R = Math.PI / 180;

export const V_SPEEDS = { whiteLow: 45, whiteHigh: 85, greenLow: 51, greenHigh: 129, red: 163 };

function face(c: Ctx, x: number, y: number, r: number) {
  c.save();
  c.fillStyle = BEZEL;
  c.beginPath();
  c.arc(x, y, r, 0, Math.PI * 2);
  c.fill();
  const g = c.createRadialGradient(x, y - r * 0.3, r * 0.1, x, y, r * 0.92);
  g.addColorStop(0, '#1d1f20');
  g.addColorStop(1, FACE);
  c.fillStyle = g;
  c.beginPath();
  c.arc(x, y, r * 0.9, 0, Math.PI * 2);
  c.fill();
  // mounting screws
  c.fillStyle = '#4a4c4e';
  for (const a of [45, 135, 225, 315]) {
    c.beginPath();
    c.arc(x + Math.cos(a * D2R) * r * 0.965, y + Math.sin(a * D2R) * r * 0.965, r * 0.025, 0, Math.PI * 2);
    c.fill();
  }
  c.restore();
}

function tick(c: Ctx, x: number, y: number, r0: number, r1: number, angle: number, width: number, color = INK) {
  c.strokeStyle = color;
  c.lineWidth = width;
  c.beginPath();
  c.moveTo(x + Math.cos(angle) * r0, y + Math.sin(angle) * r0);
  c.lineTo(x + Math.cos(angle) * r1, y + Math.sin(angle) * r1);
  c.stroke();
}

function label(c: Ctx, text: string, x: number, y: number, size: number, color = INK, weight = 600) {
  c.fillStyle = color;
  c.font = `${weight} ${size}px "Inter", "Segoe UI", system-ui, sans-serif`;
  c.textAlign = 'center';
  c.textBaseline = 'middle';
  c.fillText(text, x, y);
}

function needle(c: Ctx, x: number, y: number, angle: number, length: number, width: number, tail = 0.18) {
  c.save();
  c.translate(x, y);
  c.rotate(angle);
  c.fillStyle = NEEDLE;
  c.beginPath();
  c.moveTo(-length * tail, -width * 0.6);
  c.lineTo(length * 0.82, -width * 0.5);
  c.lineTo(length, 0);
  c.lineTo(length * 0.82, width * 0.5);
  c.lineTo(-length * tail, width * 0.6);
  c.closePath();
  c.fill();
  c.restore();
  c.fillStyle = '#3a3c3e';
  c.beginPath();
  c.arc(x, y, width * 1.3, 0, Math.PI * 2);
  c.fill();
}

function arc(c: Ctx, x: number, y: number, r: number, a0: number, a1: number, width: number, color: string) {
  c.strokeStyle = color;
  c.lineWidth = width;
  c.beginPath();
  c.arc(x, y, r, a0, a1);
  c.stroke();
}

// ---------------------------------------------------------------- airspeed
const ASI_MIN = 40;
const ASI_MAX = 200;
/** Needle angle for a speed: 40 kt at 12 o'clock, sweeping 330° clockwise. */
export const asiAngle = (kt: number) => (-90 + (Math.min(Math.max(kt, ASI_MIN - 6), ASI_MAX) - ASI_MIN) * (330 / (ASI_MAX - ASI_MIN))) * D2R;

export function drawAirspeedFace(c: Ctx, x: number, y: number, r: number) {
  face(c, x, y, r);
  const R = r * 0.82;
  const v = V_SPEEDS;
  arc(c, x, y, R * 0.93, asiAngle(v.whiteLow), asiAngle(v.whiteHigh), r * 0.035, '#e8e8e2');
  arc(c, x, y, R, asiAngle(v.greenLow), asiAngle(v.greenHigh), r * 0.05, '#3f9a52');
  arc(c, x, y, R, asiAngle(v.greenHigh), asiAngle(v.red), r * 0.05, '#d6b23c');
  tick(c, x, y, R * 0.9, R * 1.04, asiAngle(v.red), r * 0.03, '#d24a3a');
  for (let kt = ASI_MIN; kt <= ASI_MAX; kt += 10) {
    const big = kt % 20 === 0;
    tick(c, x, y, R * (big ? 0.82 : 0.88), R * 0.97, asiAngle(kt), big ? r * 0.022 : r * 0.012);
    if (big) label(c, String(kt), x + Math.cos(asiAngle(kt)) * R * 0.66, y + Math.sin(asiAngle(kt)) * R * 0.66, r * 0.13);
  }
  label(c, 'AIRSPEED', x, y - r * 0.28, r * 0.08, DIM, 500);
  label(c, 'KNOTS', x, y + r * 0.3, r * 0.08, DIM, 500);
}

export function drawAirspeedNeedle(c: Ctx, x: number, y: number, r: number, kt: number) {
  needle(c, x, y, asiAngle(kt), r * 0.74, r * 0.06);
}

// ---------------------------------------------------------------- attitude
export function drawAttitude(c: Ctx, x: number, y: number, r: number, pitchDeg: number, rollDeg: number) {
  face(c, x, y, r);
  const R = r * 0.88;
  const pxPerDeg = R / 28;
  c.save();
  c.beginPath();
  c.arc(x, y, R, 0, Math.PI * 2);
  c.clip();
  c.translate(x, y);
  c.rotate(-rollDeg * D2R);
  const horizon = Math.max(-R * 1.6, Math.min(R * 1.6, pitchDeg * pxPerDeg));
  c.fillStyle = '#3d76b0';
  c.fillRect(-R * 2, -R * 3 + horizon, R * 4, R * 3);
  c.fillStyle = '#7a5532';
  c.fillRect(-R * 2, horizon, R * 4, R * 3);
  c.strokeStyle = INK;
  c.lineWidth = r * 0.018;
  c.beginPath();
  c.moveTo(-R * 2, horizon);
  c.lineTo(R * 2, horizon);
  c.stroke();
  // pitch ladder every 5°, labelled every 10°
  for (let p = -30; p <= 30; p += 5) {
    if (p === 0) continue;
    const yy = horizon - p * pxPerDeg;
    if (Math.abs(yy) > R * 0.75) continue;
    const w = p % 10 === 0 ? R * 0.28 : R * 0.14;
    c.lineWidth = r * 0.012;
    c.beginPath();
    c.moveTo(-w, yy);
    c.lineTo(w, yy);
    c.stroke();
    if (p % 10 === 0) {
      label(c, String(Math.abs(p)), -w - R * 0.1, yy, r * 0.075);
      label(c, String(Math.abs(p)), w + R * 0.1, yy, r * 0.075);
    }
  }
  c.restore();
  // fixed bank scale and pointer
  for (const b of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
    const a = (-90 + b) * D2R;
    const big = b === 0 || Math.abs(b) === 30 || Math.abs(b) === 60;
    tick(c, x, y, R * (big ? 0.84 : 0.89), R * 0.98, a, big ? r * 0.024 : r * 0.014);
  }
  c.save();
  c.translate(x, y);
  c.rotate(-rollDeg * D2R);
  c.fillStyle = '#f0a43a';
  c.beginPath();
  c.moveTo(0, -R * 0.83);
  c.lineTo(-R * 0.05, -R * 0.74);
  c.lineTo(R * 0.05, -R * 0.74);
  c.closePath();
  c.fill();
  c.restore();
  // miniature aircraft (fixed)
  c.strokeStyle = '#f0a43a';
  c.lineWidth = r * 0.04;
  c.lineCap = 'round';
  c.beginPath();
  c.moveTo(x - R * 0.42, y);
  c.lineTo(x - R * 0.14, y);
  c.lineTo(x - R * 0.07, y + R * 0.07);
  c.moveTo(x + R * 0.42, y);
  c.lineTo(x + R * 0.14, y);
  c.lineTo(x + R * 0.07, y + R * 0.07);
  c.stroke();
  c.fillStyle = '#f0a43a';
  c.beginPath();
  c.arc(x, y, r * 0.03, 0, Math.PI * 2);
  c.fill();
  c.lineCap = 'butt';
}

// ---------------------------------------------------------------- altimeter
export function drawAltimeterFace(c: Ctx, x: number, y: number, r: number) {
  face(c, x, y, r);
  const R = r * 0.82;
  for (let i = 0; i < 50; i++) {
    const a = (-90 + i * 7.2) * D2R;
    const big = i % 5 === 0;
    tick(c, x, y, R * (big ? 0.82 : 0.9), R * 0.98, a, big ? r * 0.022 : r * 0.011);
    if (big) label(c, String(i / 5), x + Math.cos(a) * R * 0.66, y + Math.sin(a) * R * 0.66, r * 0.15);
  }
  label(c, 'ALT', x, y - r * 0.3, r * 0.08, DIM, 500);
  label(c, 'FEET', x, y + r * 0.34, r * 0.075, DIM, 500);
}

export function drawAltimeterNeedles(c: Ctx, x: number, y: number, r: number, feet: number) {
  const ft = Math.max(0, feet);
  // Digital counter for readability, like a modern altimeter's drum.
  c.fillStyle = '#0b0c0c';
  c.fillRect(x + r * 0.14, y - r * 0.09, r * 0.5, r * 0.18);
  label(c, Math.round(ft / 10) * 10 + '', x + r * 0.39, y + r * 0.005, r * 0.12, INK, 600);
  needle(c, x, y, (-90 + ((ft / 10000) % 1) * 360) * D2R, r * 0.46, r * 0.075, 0.1);
  needle(c, x, y, (-90 + ((ft / 1000) % 1) * 360) * D2R, r * 0.76, r * 0.05);
}

// ------------------------------------------------------- turn coordinator
export function drawTurnCoordinatorFace(c: Ctx, x: number, y: number, r: number) {
  face(c, x, y, r);
  const R = r * 0.82;
  // standard-rate (3°/s) marks at ±20° below the wings-level marks
  for (const a of [180, 0, 160, 20]) tick(c, x, y, R * 0.78, R * 0.95, a * D2R, r * 0.03);
  label(c, 'L', x - R * 0.72, y + R * 0.42, r * 0.12);
  label(c, 'R', x + R * 0.72, y + R * 0.42, r * 0.12);
  label(c, 'TURN COORDINATOR', x, y - r * 0.42, r * 0.065, DIM, 500);
  label(c, '2 MIN', x, y + r * 0.66, r * 0.07, DIM, 500);
  // ball tube
  c.strokeStyle = '#3c3e40';
  c.lineWidth = r * 0.14;
  c.beginPath();
  c.arc(x, y - R * 1.15, R * 1.55, (90 - 16) * D2R, (90 + 16) * D2R);
  c.stroke();
  c.strokeStyle = INK;
  c.lineWidth = r * 0.012;
  for (const dx of [-0.08, 0.08]) {
    c.beginPath();
    c.moveTo(x + R * dx, y + R * 0.3);
    c.lineTo(x + R * dx, y + R * 0.47);
    c.stroke();
  }
}

export function drawTurnCoordinatorDynamic(c: Ctx, x: number, y: number, r: number, turnRateDps: number, lateralG: number) {
  const R = r * 0.82;
  // Aircraft symbol: 3°/s turn = 20° deflection; limited at ~2× standard rate.
  const deflect = Math.max(-40, Math.min(40, (turnRateDps / 3) * 20)) * D2R;
  c.save();
  c.translate(x, y);
  c.rotate(deflect);
  c.strokeStyle = NEEDLE;
  c.fillStyle = NEEDLE;
  c.lineWidth = r * 0.05;
  c.lineCap = 'round';
  c.beginPath();
  c.moveTo(-R * 0.72, 0);
  c.lineTo(R * 0.72, 0);
  c.moveTo(0, -R * 0.16);
  c.lineTo(0, 0);
  c.moveTo(-R * 0.18, R * 0.12);
  c.lineTo(R * 0.18, R * 0.12);
  c.stroke();
  c.beginPath();
  c.arc(0, 0, r * 0.07, 0, Math.PI * 2);
  c.fill();
  c.restore();
  c.lineCap = 'butt';
  // Slip ball: thrown opposite to the lateral acceleration.
  const off = Math.max(-1, Math.min(1, -lateralG / 0.15));
  const a = (90 - off * 13) * D2R;
  c.fillStyle = '#0a0a0a';
  c.beginPath();
  c.arc(x + Math.cos(a) * R * 1.55, y - R * 1.15 + Math.sin(a) * R * 1.55, r * 0.06, 0, Math.PI * 2);
  c.fill();
  c.strokeStyle = '#6a6c6e';
  c.lineWidth = 1;
  c.stroke();
}

// ------------------------------------------------------- heading indicator
export function drawHeading(c: Ctx, x: number, y: number, r: number, headingDeg: number) {
  face(c, x, y, r);
  const R = r * 0.84;
  c.save();
  c.translate(x, y);
  c.rotate(-headingDeg * D2R);
  for (let d = 0; d < 360; d += 5) {
    const a = (d - 90) * D2R;
    const big = d % 30 === 0;
    const mid = d % 10 === 0;
    tick(c, 0, 0, R * (big ? 0.8 : mid ? 0.85 : 0.9), R * 0.98, a, big ? r * 0.022 : r * 0.011);
    if (big) {
      const txt = d === 0 ? 'N' : d === 90 ? 'E' : d === 180 ? 'S' : d === 270 ? 'W' : String(d / 10);
      c.save();
      c.rotate(d * D2R);
      label(c, txt, 0, -R * 0.64, r * (txt.length === 1 && /[NESW]/.test(txt) ? 0.15 : 0.12), /[NESW]/.test(txt) ? '#f0a43a' : INK);
      c.restore();
    }
  }
  c.restore();
  // lubber line and aircraft silhouette (fixed)
  c.fillStyle = '#f0a43a';
  c.beginPath();
  c.moveTo(x, y - R * 0.99);
  c.lineTo(x - r * 0.05, y - R * 1.08);
  c.lineTo(x + r * 0.05, y - R * 1.08);
  c.closePath();
  c.fill();
  c.strokeStyle = '#f0a43a';
  c.lineWidth = r * 0.035;
  c.lineCap = 'round';
  c.beginPath();
  c.moveTo(x, y - R * 0.3);
  c.lineTo(x, y + R * 0.3);
  c.moveTo(x - R * 0.28, y - R * 0.02);
  c.lineTo(x + R * 0.28, y - R * 0.02);
  c.moveTo(x - R * 0.11, y + R * 0.25);
  c.lineTo(x + R * 0.11, y + R * 0.25);
  c.stroke();
  c.lineCap = 'butt';
}

// ------------------------------------------------------- vertical speed
const VSI_MAX = 2000;
/** 0 fpm at 9 o'clock; climb swings the needle up (clockwise), descent down. */
export const vsiAngle = (fpm: number) => (180 + (Math.max(-VSI_MAX, Math.min(VSI_MAX, fpm)) / VSI_MAX) * 170) * D2R;

export function drawVsiFace(c: Ctx, x: number, y: number, r: number) {
  face(c, x, y, r);
  const R = r * 0.82;
  for (let v = -2000; v <= 2000; v += 100) {
    const big = v % 500 === 0;
    tick(c, x, y, R * (big ? 0.82 : 0.9), R * 0.98, vsiAngle(v), big ? r * 0.022 : r * 0.011);
    if (big && v % 1000 === 0 && Math.abs(v) !== 0) label(c, String(Math.abs(v) / 100), x + Math.cos(vsiAngle(v)) * R * 0.66, y + Math.sin(vsiAngle(v)) * R * 0.66, r * 0.14);
    if (big && Math.abs(v) === 500) label(c, '5', x + Math.cos(vsiAngle(v)) * R * 0.66, y + Math.sin(vsiAngle(v)) * R * 0.66, r * 0.12);
  }
  label(c, '0', x - R * 0.66, y, r * 0.14);
  label(c, 'UP', x - R * 0.25, y - R * 0.32, r * 0.08, DIM, 500);
  label(c, 'DOWN', x - R * 0.25, y + R * 0.32, r * 0.08, DIM, 500);
  label(c, 'VERTICAL SPEED', x + R * 0.2, y - r * 0.12, r * 0.062, DIM, 500);
  label(c, '100 FT/MIN', x + R * 0.2, y + r * 0.12, r * 0.062, DIM, 500);
}

export function drawVsiNeedle(c: Ctx, x: number, y: number, r: number, fpm: number) {
  needle(c, x, y, vsiAngle(fpm), r * 0.74, r * 0.06);
}

// ------------------------------------------------------- tachometer
export const tachAngle = (rpm: number) => (135 + (Math.max(0, Math.min(3000, rpm)) / 3000) * 270) * D2R;

export function drawTachFace(c: Ctx, x: number, y: number, r: number) {
  face(c, x, y, r);
  const R = r * 0.82;
  arc(c, x, y, R, tachAngle(2100), tachAngle(2700), r * 0.05, '#3f9a52');
  tick(c, x, y, R * 0.88, R * 1.03, tachAngle(2700), r * 0.03, '#d24a3a');
  for (let rpm = 0; rpm <= 3000; rpm += 100) {
    const big = rpm % 500 === 0;
    tick(c, x, y, R * (big ? 0.82 : 0.9), R * 0.98, tachAngle(rpm), big ? r * 0.022 : r * 0.011);
    if (rpm % 500 === 0) label(c, String(rpm / 100), x + Math.cos(tachAngle(rpm)) * R * 0.66, y + Math.sin(tachAngle(rpm)) * R * 0.66, r * 0.12);
  }
  label(c, 'RPM', x, y - r * 0.28, r * 0.08, DIM, 500);
  label(c, '×100', x, y + r * 0.42, r * 0.075, DIM, 500);
}

export function drawTachNeedle(c: Ctx, x: number, y: number, r: number, rpm: number) {
  needle(c, x, y, tachAngle(rpm), r * 0.74, r * 0.06);
}
