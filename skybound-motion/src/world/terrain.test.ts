import { describe, expect, it } from 'vitest';
import { TRAINER } from '../flight/aircraft';
import { emptyInput, FlightModel } from '../flight/dynamics';
import { fromEuler } from '../flight/math';
import { terrainHeight } from './terrain';

describe('terrain', () => {
  it('is flat around the airfield and along the final approach', () => {
    for (let n = -200; n <= 1400; n += 100) for (let e = -1500; e <= 1500; e += 250) expect(terrainHeight(n, e)).toBe(0);
    // Long final starts 4 km south at 300 m: terrain there stays far below.
    for (let n = -6000; n <= 0; n += 250) expect(terrainHeight(n, 0)).toBeLessThan(60);
  });

  it('has real hills further out, and is deterministic and smooth', () => {
    let max = 0;
    for (let n = -15000; n <= 15000; n += 500) for (let e = -15000; e <= 15000; e += 500) max = Math.max(max, terrainHeight(n, e));
    expect(max).toBeGreaterThan(150);
    expect(terrainHeight(7000, 6000)).toBe(terrainHeight(7000, 6000));
    // No cliffs: 10 m apart never differs by more than a few metres.
    for (let n = 3000; n < 12000; n += 777) expect(Math.abs(terrainHeight(n, 5000) - terrainHeight(n + 10, 5000))).toBeLessThan(4);
  });

  it('the flight model collides with hills, and AGL accounts for them', () => {
    // Find a hill.
    let spot = { n: 0, e: 0, h: 0 };
    for (let n = 4000; n <= 14000; n += 250) for (let e = -14000; e <= 14000; e += 250) {
      const h = terrainHeight(n, e);
      if (h > spot.h) spot = { n, e, h };
    }
    const m = new FlightModel(TRAINER, terrainHeight);
    m.resetAirborne();
    m.state.pos = [spot.n - 300, spot.e, -(spot.h - 30)]; // 30 m below the summit, 300 m short
    m.state.vel = [50, 0, 0];
    m.state.q = fromEuler(0, 0, 0);
    const startAgl = m.telemetry.aglFt;
    for (let i = 0; i < 60 * 12 && m.phase !== 'crashed'; i++) m.advance({ ...emptyInput(), throttle: 0.6 }, 1 / 60);
    expect(m.phase).toBe('crashed');
    expect(startAgl).toBeLessThan(m.telemetry.altitudeFt); // AGL below MSL over raised ground
  });
});
