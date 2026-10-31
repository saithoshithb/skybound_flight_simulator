import { describe, expect, it } from 'vitest';
import { TRAINER } from './aircraft';
import { autoRudder, bankAssist } from './assists';
import { emptyInput, FlightModel, liftCoefficient, trimLevel, type PilotInput } from './dynamics';
import { clamp, DEG, fromEuler, rotate, toEuler } from './math';

const run = (m: FlightModel, input: PilotInput, seconds: number, each?: (t: number) => void) => {
  for (let t = 0; t < seconds; t += 1 / 60) {
    m.advance(input, 1 / 60);
    each?.(t);
  }
  return m.telemetry;
};

describe('math', () => {
  it('Euler round-trip and frame directions', () => {
    const q = fromEuler(10 * DEG, 20 * DEG, 30 * DEG);
    const e = toEuler(q);
    expect(e.roll / DEG).toBeCloseTo(10);
    expect(e.pitch / DEG).toBeCloseTo(20);
    expect(e.yaw / DEG).toBeCloseTo(30);
    // Yaw 90° points the nose east; pitch up points it at −z (up in NED).
    expect(rotate(fromEuler(0, 0, 90 * DEG), [1, 0, 0])[1]).toBeCloseTo(1);
    expect(rotate(fromEuler(0, 30 * DEG, 0), [1, 0, 0])[2]).toBeCloseTo(-0.5);
  });
});

describe('aerodynamics', () => {
  it('lift breaks down past the stall angle', () => {
    const m = new FlightModel();
    const clMax = liftCoefficient(TRAINER, m.alphaCLmax);
    expect(clMax).toBeGreaterThan(1.4);
    expect(clMax).toBeLessThan(1.7);
    expect(m.alphaCLmax / DEG).toBeGreaterThan(14);
    expect(m.alphaCLmax / DEG).toBeLessThan(18);
    expect(liftCoefficient(TRAINER, 5 * DEG)).toBeCloseTo(0.31 + 5.143 * 5 * DEG, 2);
    expect(liftCoefficient(TRAINER, 26 * DEG)).toBeLessThan(clMax * 0.4);
  });

  it('trim gives plausible cruise numbers', () => {
    const t = trimLevel(TRAINER, 50, 300);
    expect(t.alpha / DEG).toBeGreaterThan(0);
    expect(t.alpha / DEG).toBeLessThan(6);
    expect(t.throttle).toBeGreaterThan(0.3);
    expect(t.throttle).toBeLessThan(0.9);
  });
});

describe('FlightModel', () => {
  it('sits still on the runway without bouncing or creeping', () => {
    const m = new FlightModel();
    const t = run(m, { ...emptyInput(), brake: 1 }, 5);
    expect(t.phase).toBe('ground');
    expect(t.groundSpeedKt).toBeLessThan(0.2);
    expect(Math.abs(t.pitchDeg)).toBeLessThan(2);
    expect(Math.abs(t.verticalSpeedFpm)).toBeLessThan(5);
    expect(t.wheelsOnGround).toBe(3);
  });

  it('holds trimmed level flight hands-off', () => {
    const m = new FlightModel();
    const { trim, throttle } = m.resetAirborne(4000, 300, 50);
    const start = m.telemetry.altitudeFt;
    const t = run(m, { ...emptyInput(), trim, throttle }, 20);
    expect(t.phase).toBe('airborne');
    expect(Math.abs(t.altitudeFt - start)).toBeLessThan(60);
    expect(Math.abs(t.airspeedKt - 97)).toBeLessThan(5);
    expect(Math.abs(t.rollDeg)).toBeLessThan(1);
  });

  it('takes off: full power and a gentle pull lifts off around 55–70 kt', () => {
    const m = new FlightModel();
    let liftoffKt = 0;
    let groundRollM = 0;
    const input = { ...emptyInput(), throttle: 1 };
    run(m, input, 45, () => {
      const t = m.telemetry;
      input.elevator = t.airspeedKt > 55 ? 0.35 : 0;
      if (!liftoffKt && t.phase === 'airborne' && t.altitudeFt > 3) {
        liftoffKt = t.airspeedKt;
        groundRollM = m.state.pos[0] - 60;
      }
      // Ease the pull once climbing so the test doesn't stall it.
      if (t.pitchDeg > 8) input.elevator = 0.05;
    });
    expect(m.telemetry.phase).toBe('airborne');
    expect(liftoffKt).toBeGreaterThan(50);
    expect(liftoffKt).toBeLessThan(72);
    expect(groundRollM).toBeGreaterThan(200);
    expect(groundRollM).toBeLessThan(700);
    expect(m.telemetry.verticalSpeedFpm).toBeGreaterThan(200);
  });

  it('rolls right with right aileron and turns right when banked', () => {
    const m = new FlightModel();
    const { trim, throttle } = m.resetAirborne(4000, 600, 50);
    run(m, { ...emptyInput(), trim, throttle, aileron: 0.4 }, 1.5);
    expect(m.telemetry.rollDeg).toBeGreaterThan(10);
    const heading0 = m.telemetry.headingDeg;
    run(m, { ...emptyInput(), trim, throttle, aileron: 0 }, 5);
    const turned = ((m.telemetry.headingDeg - heading0 + 540) % 360) - 180;
    expect(turned).toBeGreaterThan(5);
  });

  it('auto-rudder keeps an aileron-only turn coordinated', () => {
    const fly = (assist: boolean) => {
      const m = new FlightModel();
      const { trim } = m.resetAirborne(4000, 600, 51);
      const input = { ...emptyInput(), trim, throttle: 1, aileron: 0.5 };
      let maxBeta = 0;
      let minHeadingDelta = 0;
      run(m, input, 3, () => {
        input.rudder = assist ? autoRudder(m.telemetry, input.aileron) : 0;
        maxBeta = Math.max(maxBeta, Math.abs(m.telemetry.sideslipDeg));
        minHeadingDelta = Math.min(minHeadingDelta, ((m.telemetry.headingDeg + 180) % 360) - 180);
      });
      return { maxBeta, minHeadingDelta };
    };
    const off = fly(false);
    const on = fly(true);
    expect(off.maxBeta).toBeGreaterThan(8); // adverse yaw without feet
    expect(on.maxBeta).toBeLessThan(4);
    expect(on.minHeadingDelta).toBeGreaterThan(-4); // nose no longer swings left first
  });

  it('bank assist: held tilt gives a steady bank and turn; levelling rolls wings level', () => {
    const m = new FlightModel();
    const { trim, throttle } = m.resetAirborne(4000, 600, 50);
    const input = { ...emptyInput(), trim, throttle };
    let cmd = 0.5; // half tilt → 22.5° target
    let maxBank = 0;
    const fly = (s: number) =>
      run(m, input, s, () => {
        input.aileron = bankAssist(cmd, m.telemetry);
        input.rudder = autoRudder(m.telemetry, input.aileron);
        // Hold altitude roughly, like a pilot adding back pressure in the turn.
        input.elevator = clamp(-m.telemetry.verticalSpeedFpm / 1500, -0.3, 0.4);
        maxBank = Math.max(maxBank, m.telemetry.rollDeg);
      });
    const h0 = m.telemetry.headingDeg;
    fly(8);
    expect(m.telemetry.rollDeg).toBeGreaterThan(19);
    expect(m.telemetry.rollDeg).toBeLessThan(26);
    expect(maxBank).toBeLessThan(28); // little overshoot
    expect(((m.telemetry.headingDeg - h0 + 360) % 360)).toBeGreaterThan(15); // turning right
    cmd = 0;
    fly(6);
    expect(Math.abs(m.telemetry.rollDeg)).toBeLessThan(3);
    expect(m.telemetry.phase).toBe('airborne');
  });

  it('stall warning comes at a trainer-like speed in a slow level deceleration', () => {
    const m = new FlightModel();
    const { trim } = m.resetAirborne(4000, 1200, 45);
    const input = { ...emptyInput(), trim, throttle: 0 };
    let warnKt = 0;
    run(m, input, 90, () => {
      const vs = m.telemetry.verticalSpeedFpm;
      input.elevator = Math.max(-1, Math.min(1, input.elevator + (-vs / 400 - m.state.w[1] * 3) * 0.004));
      if (!warnKt && m.telemetry.stalled) warnKt = m.telemetry.airspeedKt;
    });
    expect(warnKt).toBeGreaterThan(45);
    expect(warnKt).toBeLessThan(62);
  });

  it('stalls when held nose-high at idle, then recovers when released', () => {
    const m = new FlightModel();
    const { trim } = m.resetAirborne(4000, 1200, 45);
    let stalled = false;
    run(m, { ...emptyInput(), trim, throttle: 0, elevator: 1 }, 12, () => {
      stalled ||= m.telemetry.stalled;
    });
    expect(stalled).toBe(true);
    run(m, { ...emptyInput(), trim, throttle: 1, elevator: -0.2 }, 6);
    expect(m.telemetry.stalled).toBe(false);
    expect(m.telemetry.phase).toBe('airborne');
  });

  it('responds through dynamics, not instantly', () => {
    const m = new FlightModel();
    const { trim, throttle } = m.resetAirborne(4000, 600, 50);
    const p0 = m.telemetry.pitchDeg;
    run(m, { ...emptyInput(), trim, throttle, elevator: 1 }, 1 / 30);
    expect(Math.abs(m.telemetry.pitchDeg - p0)).toBeLessThan(0.5);
  });

  it('over-rotating on the runway scrapes the tail without ending the flight', () => {
    const m = new FlightModel();
    const input = { ...emptyInput(), throttle: 1 };
    let scraped = false;
    run(m, input, 25, () => {
      input.elevator = m.telemetry.airspeedKt > 50 ? 1 : 0;
      scraped ||= m.telemetry.tailStrike;
    });
    expect(scraped).toBe(true);
    expect(m.telemetry.phase).not.toBe('crashed');
  });

  it('lands gently from a stabilised descent and brakes to a stop', () => {
    const m = new FlightModel();
    const { trim } = m.resetAirborne(800, 25, 36);
    const input = { ...emptyInput(), trim, throttle: 0.25 };
    let touchdownFpm: number | null = null;
    run(m, input, 60, () => {
      const t = m.telemetry;
      const agl = -m.state.pos[2] - 1.05;
      // Descend at 400 fpm, flare to 100 fpm in the last 3 m, idle and brake once down.
      // Like a pilot: vertical-speed error picks a pitch attitude, elevator holds that attitude.
      const targetVs = agl > 3 ? -400 : -100;
      if (t.phase === 'airborne') {
        const targetPitch = clamp(1 + (targetVs - t.verticalSpeedFpm) / 250, -5, 9);
        input.elevator = clamp((targetPitch - t.pitchDeg) / 12 - (m.state.w[1] / DEG) / 15, -0.5, 0.5);
      }
      else {
        touchdownFpm ??= t.verticalSpeedFpm;
        input.throttle = 0;
        input.elevator = 0;
        if (t.groundSpeedKt < 50) input.brake = 1;
      }
    });
    expect(m.telemetry.phase).toBe('ground');
    expect(touchdownFpm).not.toBeNull();
    expect(m.telemetry.groundSpeedKt).toBeLessThan(1);
    expect(m.telemetry.wheelsOnGround).toBe(3);
  });

  it('crashes on a steep dive into the ground', () => {
    const m = new FlightModel();
    const { trim } = m.resetAirborne(500, 40, 50);
    const t = run(m, { ...emptyInput(), trim, throttle: 1, elevator: -1 }, 10);
    expect(t.phase).toBe('crashed');
    expect(t.crashReason).toBeTruthy();
  });
});
