/**
 * Terrain height (metres above the field, ≥ 0) as a pure function of
 * position. Shared by the renderer and the flight model, so hills you see are
 * hills you can hit. The airfield, its surroundings and the southern approach
 * to runway 36 are flat; rolling hills rise beyond them.
 *
 * Coordinates: north (m) and east (m) from the runway-36 threshold.
 */

const RUNWAY_END_N = 1200;

function hash(ix: number, iy: number) {
  let h = (ix * 374761393 + iy * 668265263) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

function valueNoise(x: number, y: number) {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  const a = hash(ix, iy);
  const b = hash(ix + 1, iy);
  const c = hash(ix, iy + 1);
  const d = hash(ix + 1, iy + 1);
  return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
}

function fbm(x: number, y: number) {
  let sum = 0;
  let amp = 0.5;
  let freq = 1;
  for (let o = 0; o < 5; o++) {
    sum += amp * valueNoise(x * freq, y * freq);
    freq *= 2.03;
    amp *= 0.5;
  }
  return sum; // ≈ 0…1
}

const smoothstep = (a: number, b: number, x: number) => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

/** Distance (m) from a point to the runway centreline segment. */
function distToRunway(n: number, e: number) {
  const cn = Math.min(RUNWAY_END_N, Math.max(0, n));
  return Math.hypot(n - cn, e);
}

export const TERRAIN = {
  flatRadiusM: 1800,
  rampM: 3500,
  maxHillM: 380,
};

export function terrainHeight(north: number, east: number) {
  let mask = smoothstep(TERRAIN.flatRadiusM, TERRAIN.flatRadiusM + TERRAIN.rampM, distToRunway(north, east));
  // Keep a wide, low corridor for the long final to runway 36 (south of the field).
  if (north < 0) mask *= 0.15 + 0.85 * smoothstep(900, 2600, Math.abs(east));
  if (mask === 0) return 0;
  const base = fbm(north / 4200 + 11.3, east / 4200 - 7.1);
  const ridges = 1 - Math.abs(2 * fbm(north / 2300 - 3.7, east / 2300 + 5.2) - 1);
  const h = Math.max(0, base - 0.32) * 1.5 + ridges * ridges * 0.35;
  return mask * h * TERRAIN.maxHillM;
}
