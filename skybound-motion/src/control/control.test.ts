import { describe, expect, it } from 'vitest';
import { computeNeutral, ZERO_NEUTRAL } from '../calibration/neutral';
import type { ImuSample } from '../sensor/protocol';
import { applyDeadzone, ControlPipeline, DEFAULT_CONTROL_SETTINGS as S, fitToRanges, shapeAxis } from './mapping';

const sample = (p: Partial<ImuSample> = {}): ImuSample => ({
  kind: 'imu',
  seq: 0,
  deviceMs: 0,
  pitch: 0,
  roll: 0,
  yaw: 0,
  pitchRate: 0,
  rollRate: 0,
  yawRate: 0,
  accelG: 1,
  ...p,
});

describe('saved settings', () => {
  it('are brought back inside the slider ranges', () => {
    const c = fitToRanges({ ...S, sensitivity: 50, deadzone: Number.NaN, smoothingMs: -5, roll: { ...S.roll, maxAngleDeg: 0 } });
    expect(c.roll.maxAngleDeg).toBe(10);
    expect(c.sensitivity).toBe(2);
    expect(c.deadzone).toBe(S.deadzone);
    expect(c.smoothingMs).toBe(0);
    // The case that went wrong: held exactly at neutral with a 0° max angle gave 0 / 0.
    expect(shapeAxis(0, c.roll, c)).toBe(0);
  });
});

describe('shaping', () => {
  it('dead zone is continuous and still reaches full scale', () => {
    expect(applyDeadzone(0.05, 0.1)).toBe(0);
    expect(applyDeadzone(0.1001, 0.1)).toBeCloseTo(0, 3);
    expect(applyDeadzone(1, 0.1)).toBe(1);
    expect(applyDeadzone(-1, 0.1)).toBe(-1);
  });

  it('maps max angle to full deflection, respects sign and inversion', () => {
    expect(shapeAxis(30, S.pitch, S)).toBeCloseTo(1);
    expect(shapeAxis(-60, S.pitch, S)).toBe(-1);
    expect(shapeAxis(15, S.pitch, S)).toBeGreaterThan(0);
    expect(shapeAxis(15, { ...S.pitch, invert: true }, S)).toBeLessThan(0);
    expect(shapeAxis(1, S.pitch, S)).toBe(0); // inside dead zone
  });
});

describe('easy-flight tilt curve (defaults)', () => {
  it('ignores tremor, gives small output for small tilts, full output at full tilt', () => {
    expect(shapeAxis(2, S.roll, S)).toBe(0); // hand tremor
    const small = shapeAxis(8, S.roll, S);
    const medium = shapeAxis(18, S.roll, S);
    expect(small).toBeGreaterThan(0);
    expect(small).toBeLessThan(0.12); // ≈ 3–4° of bank with the 35° limit
    expect(medium).toBeGreaterThan(small * 3); // progressive, not linear
    expect(shapeAxis(35, S.roll, S)).toBeCloseTo(1);
  });
});

describe('ControlPipeline', () => {
  const run = (p: ControlPipeline, s: ImuSample | null, seconds: number, neutral = ZERO_NEUTRAL) => {
    let out = p.update(s, neutral, S, 0);
    for (let t = 0; t < seconds; t += 1 / 60) out = p.update(s, neutral, S, 1 / 60);
    return out;
  };

  it('ramps in instead of jumping on first sample', () => {
    const p = new ControlPipeline();
    const first = p.update(sample({ pitch: 30 }), ZERO_NEUTRAL, S, 1 / 60);
    expect(first.elevator).toBeLessThan(0.01);
    const later = run(p, sample({ pitch: 30 }), 2);
    expect(later.engagement).toBe(1);
    expect(later.elevator).toBeCloseTo(1, 2);
  });

  it('never changes faster than the slew limit', () => {
    const p = new ControlPipeline();
    run(p, sample(), 2);
    let prev = p.update(sample({ roll: -40 }), ZERO_NEUTRAL, S, 1 / 60).aileron;
    for (let i = 0; i < 60; i++) {
      const a = p.update(sample({ roll: -40 }), ZERO_NEUTRAL, S, 1 / 60).aileron;
      expect(Math.abs(a - prev)).toBeLessThanOrEqual(S.slewPerSec / 60 + 1e-9);
      prev = a;
    }
  });

  it('fades to neutral when the link drops', () => {
    const p = new ControlPipeline();
    run(p, sample({ pitch: 30 }), 2);
    const out = run(p, null, 1);
    expect(out.engagement).toBe(0);
    expect(out.elevator).toBe(0);
  });

  it('subtracts neutral, wrapping roll across ±180', () => {
    const p = new ControlPipeline();
    const n = { pitch: 5, roll: 179, yaw: 0, capturedAt: 1 };
    const out = run(p, sample({ pitch: 5, roll: -179 }), 2, n);
    expect(out.elevator).toBe(0);
    expect(out.aileron).toBe(0); // 2° apart, inside dead zone
  });

  it('washout yaw decays a held twist to zero', () => {
    const p = new ControlPipeline();
    const s = { ...S, yawMode: 'washout' as const, yawWashoutS: 1 };
    const n = { ...ZERO_NEUTRAL, capturedAt: 1 };
    let out = p.update(sample(), n, s, 0);
    for (let i = 0; i < 90; i++) out = p.update(sample(), n, s, 1 / 60);
    for (let i = 0; i < 20; i++) out = p.update(sample({ yaw: 30 }), n, s, 1 / 60);
    expect(out.rudder).toBeGreaterThan(0.3);
    for (let i = 0; i < 600; i++) out = p.update(sample({ yaw: 30 }), n, s, 1 / 60);
    expect(Math.abs(out.rudder)).toBeLessThan(0.05);
  });
});

describe('computeNeutral', () => {
  const still = (n: number, p = 2, r = -1) => Array.from({ length: n }, (_, i) => sample({ pitch: p + (i % 2) * 0.1, roll: r }));

  it('averages a still capture', () => {
    const r = computeNeutral(still(50));
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.neutral.pitch).toBeCloseTo(2.05, 2);
  });

  it('averages roll across the ±180 seam', () => {
    const r = computeNeutral(Array.from({ length: 40 }, (_, i) => sample({ roll: i % 2 ? 179.5 : -179.5 })), 0);
    // Too tilted to accept, but the mean must be ~180, not ~0.
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.error).toBe('too_tilted');
  });

  it('rejects movement, missing data and extreme tilt', () => {
    const moving = Array.from({ length: 50 }, (_, i) => sample({ pitch: i * 0.5 }));
    expect(computeNeutral(moving)).toMatchObject({ ok: false, error: 'moving' });
    expect(computeNeutral(still(5))).toMatchObject({ ok: false, error: 'no_data' });
    expect(computeNeutral(still(50, 60))).toMatchObject({ ok: false, error: 'too_tilted' });
  });
});
