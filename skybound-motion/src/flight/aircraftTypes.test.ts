import { describe, expect, it } from 'vitest';
import { AIRCRAFT, SPORT, TRAINER } from './aircraft';
import { emptyInput, FlightModel, trimLevel } from './dynamics';
import { EasyFlight } from './easyFlight';
import { DEG } from './math';

const run = (m: FlightModel, input: ReturnType<typeof emptyInput>, s: number, each?: () => void) => {
  for (let i = 0; i < s * 60; i++) {
    each?.();
    m.advance(input, 1 / 60);
  }
  return m.telemetry;
};

describe('aircraft catalogue', () => {
  it('every aircraft has unique id, sane trim at cruise, and sits still when parked', () => {
    expect(new Set(AIRCRAFT.map((a) => a.id)).size).toBe(AIRCRAFT.length);
    for (const ac of AIRCRAFT) {
      const t = trimLevel(ac, ac.easy.cruiseKt * 0.5144, 600);
      expect(t.alpha / DEG).toBeGreaterThan(-2);
      expect(t.alpha / DEG).toBeLessThan(8);
      expect(t.throttle).toBeLessThan(0.95);
      const m = new FlightModel(ac);
      const p = run(m, { ...emptyInput(), brake: 1 }, 4);
      expect(p.phase).toBe('ground');
      expect(p.groundSpeedKt).toBeLessThan(0.3);
    }
  });

  it('the sport plane is faster and rolls quicker than the trainer', () => {
    const top = (ac: typeof TRAINER) => {
      const m = new FlightModel(ac);
      const tr = m.resetAirborne(4000, 600, ac.easy.cruiseKt * 0.5144);
      const input = { ...emptyInput(), throttle: 1, trim: tr.trim };
      run(m, input, 90, () => {
        input.elevator = Math.max(-1, Math.min(1, -m.telemetry.verticalSpeedFpm / 600 - m.state.w[1] * 2));
      });
      return m.telemetry.airspeedKt;
    };
    const rollRate = (ac: typeof TRAINER) => {
      const m = new FlightModel(ac);
      const tr = m.resetAirborne(4000, 600, 55);
      run(m, { ...emptyInput(), trim: tr.trim, throttle: tr.throttle, aileron: 1 }, 1);
      return m.telemetry.rollRateDps;
    };
    expect(top(SPORT)).toBeGreaterThan(top(TRAINER) + 10);
    expect(rollRate(SPORT)).toBeGreaterThan(rollRate(TRAINER));
  });

  it('the sport plane takes off manually with full power and a gentle pull', () => {
    const m = new FlightModel(SPORT);
    const input = { ...emptyInput(), throttle: 1 };
    let liftoff = 0;
    run(m, input, 45, () => {
      const t = m.telemetry;
      input.elevator = t.airspeedKt > 62 ? (t.pitchDeg > 8 ? 0.05 : 0.3) : 0;
      if (!liftoff && t.phase === 'airborne' && t.altitudeFt > 3) liftoff = t.airspeedKt;
    });
    expect(m.telemetry.phase).toBe('airborne');
    expect(liftoff).toBeGreaterThan(58);
    expect(liftoff).toBeLessThan(85);
  });

  it('Easy flight flies the sport plane too: one-key take-off, cruise speed, sharp turn', () => {
    const m = new FlightModel(SPORT);
    const easy = new EasyFlight();
    easy.configure(SPORT);
    easy.reset(false);
    easy.adjustSpeed(1, 1 / 60, true);
    expect(easy.speedTargetKt).toBe(SPORT.easy.takeoffKt);
    const input = emptyInput();
    const cmd = { pitch: 0, roll: 0 };
    let stalled = false;
    let tail = false;
    const fly = (s: number) =>
      run(m, input, s, () => {
        const o = easy.update(m.telemetry, cmd, 1 / 60, m.alphaWarn / DEG);
        Object.assign(input, { elevator: o.elevator, aileron: o.aileron, rudder: o.rudder, throttle: o.throttle, trim: 0 });
        stalled ||= m.telemetry.stalled;
        tail ||= m.telemetry.tailStrike;
      });
    fly(80);
    expect(m.telemetry.phase).toBe('airborne');
    expect(m.telemetry.crashReason).toBeNull();
    expect(Math.abs(m.telemetry.airspeedKt - SPORT.easy.takeoffKt)).toBeLessThan(8);
    cmd.roll = 1;
    const t = fly(20);
    expect(t.rollDeg).toBeGreaterThan(30);
    expect(t.rollDeg).toBeLessThan(38);
    expect(stalled).toBe(false);
    expect(tail).toBe(false);
  });

  it('switching aircraft resets to the runway with the new type', () => {
    const m = new FlightModel(TRAINER);
    m.resetAirborne();
    m.setAircraft(SPORT);
    expect(m.ac.id).toBe('sport');
    expect(m.telemetry.phase).toBe('ground');
  });
});
