import { describe, expect, it } from 'vitest';
import { emptyInput, FlightModel } from '../flight/dynamics';
import { asiAngle, tachAngle, vsiAngle } from './gauges';
import { engineRpm } from './panel';

const deg = (rad: number) => (rad * 180) / Math.PI;

describe('instrument scales', () => {
  it('airspeed: 40 kt at 12 o’clock, clockwise, pegs below the scale', () => {
    expect(deg(asiAngle(40))).toBeCloseTo(-90);
    expect(deg(asiAngle(120))).toBeGreaterThan(deg(asiAngle(100)));
    expect(asiAngle(0)).toBe(asiAngle(20)); // resting on the stop
  });

  it('VSI: level points at 9 o’clock, climb swings up, descent down', () => {
    expect(deg(vsiAngle(0))).toBeCloseTo(180);
    expect(deg(vsiAngle(1000))).toBeGreaterThan(180); // toward 12 o'clock (270°)
    expect(deg(vsiAngle(-1000))).toBeLessThan(180); // toward 6 o'clock (90°)
    expect(vsiAngle(5000)).toBe(vsiAngle(2000)); // pegged
  });

  it('tachometer reads idle and full power in sensible ranges', () => {
    expect(engineRpm(0, 0)).toBeGreaterThan(550);
    expect(engineRpm(0, 0)).toBeLessThan(800);
    expect(engineRpm(1, 100)).toBeGreaterThan(2400);
    expect(engineRpm(1, 100)).toBeLessThan(2700);
    expect(tachAngle(3000)).toBeGreaterThan(tachAngle(0));
  });
});

describe('instrument inputs from the flight model', () => {
  it('turn rate reads ~0 level and is positive in a right turn; slip ball centred when coordinated', () => {
    const m = new FlightModel();
    const { trim, throttle } = m.resetAirborne(4000, 600, 50);
    for (let i = 0; i < 120; i++) m.advance({ ...emptyInput(), trim, throttle }, 1 / 60);
    expect(Math.abs(m.telemetry.turnRateDps)).toBeLessThan(0.3);
    expect(Math.abs(m.telemetry.lateralG)).toBeLessThan(0.02);
    for (let i = 0; i < 60 * 4; i++) m.advance({ ...emptyInput(), trim, throttle, aileron: 0.3, rudder: 0.15 }, 1 / 60);
    expect(m.telemetry.turnRateDps).toBeGreaterThan(1);
  });

  it('AGL equals altitude over flat ground', () => {
    const m = new FlightModel();
    m.resetAirborne(4000, 300, 50);
    expect(m.telemetry.aglFt).toBeCloseTo(m.telemetry.altitudeFt);
  });
});
