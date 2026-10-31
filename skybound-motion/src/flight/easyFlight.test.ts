import { describe, expect, it } from 'vitest';
import { emptyInput, FlightModel } from './dynamics';
import { EASY, EasyFlight, type EasyCommand } from './easyFlight';
import { DEG, fromEuler } from './math';

/** Fly the real flight model with Easy flight in the loop at 60 Hz. */
function setup(airborne = true) {
  const m = new FlightModel();
  const easy = new EasyFlight();
  if (airborne) {
    const { trim, throttle } = m.resetAirborne(4000, 600, 49);
    easy.reset(true, -trim * 0 + trim, throttle);
  } else easy.reset(false);
  const input = emptyInput();
  const cmd: EasyCommand = { pitch: 0, roll: 0 };
  const trace: {
    t: number;
    bank: number;
    pitch: number;
    vs: number;
    ias: number;
    alt: number;
    hdg: number;
    stalled: boolean;
    tail: boolean;
    ground: boolean;
  }[] = [];
  let time = 0;
  const fly = (seconds: number, each?: () => void) => {
    for (let i = 0; i < seconds * 60; i++) {
      each?.();
      const out = easy.update(m.telemetry, cmd, 1 / 60, m.alphaWarn / DEG);
      Object.assign(input, { elevator: out.elevator, aileron: out.aileron, rudder: out.rudder, throttle: out.throttle, trim: 0 });
      m.advance(input, 1 / 60);
      time += 1 / 60;
      const t = m.telemetry;
      trace.push({
        t: time,
        bank: t.rollDeg,
        pitch: t.pitchDeg,
        vs: t.verticalSpeedFpm,
        ias: t.airspeedKt,
        alt: t.altitudeFt,
        hdg: t.headingDeg,
        stalled: t.stalled,
        tail: t.tailStrike,
        ground: t.phase === 'ground',
      });
    }
    return m.telemetry;
  };
  return { m, easy, cmd, fly, trace, input };
}

const last = <T,>(a: T[], seconds: number) => a.slice(-Math.round(seconds * 60));
const span = (xs: number[]) => Math.max(...xs) - Math.min(...xs);
/** Direction reversals bigger than `deadband`: counts real oscillation, ignores sub-0.2° numeric dither. */
function reversals(xs: number[], deadband = 0.2) {
  let n = 0;
  let dir = 0;
  let extreme = xs[0];
  for (const x of xs) {
    if (dir >= 0 && x > extreme) extreme = x;
    else if (dir <= 0 && x < extreme) extreme = x;
    if (dir >= 0 && x < extreme - deadband) {
      if (dir > 0) n++;
      dir = -1;
      extreme = x;
    } else if (dir <= 0 && x > extreme + deadband) {
      if (dir < 0) n++;
      dir = 1;
      extreme = x;
    }
  }
  return n;
}

describe('Easy flight', () => {
  it('hands-off: holds level cruise speed and altitude', () => {
    const { fly, trace } = setup();
    const t = fly(30);
    expect(Math.abs(t.rollDeg)).toBeLessThan(1);
    expect(Math.abs(t.verticalSpeedFpm)).toBeLessThan(100);
    expect(Math.abs(t.airspeedKt - EASY.cruiseKt)).toBeLessThan(5);
    expect(span(last(trace, 10).map((s) => s.alt))).toBeLessThan(30);
  });

  it('neutral input recovers from an upset smoothly, without oscillating', () => {
    const { m, fly, trace } = setup();
    m.state.q = fromEuler(40 * DEG, -12 * DEG, 0); // banked 40°, nose 12° down
    const t = fly(20);
    expect(Math.abs(t.rollDeg)).toBeLessThan(2);
    expect(Math.abs(t.verticalSpeedFpm)).toBeLessThan(150);
    expect(t.phase).toBe('airborne');
    expect(reversals(trace.map((s) => s.bank))).toBeLessThanOrEqual(2);
  });

  it('gentle turn: small input gives a small, steady bank and holds altitude', () => {
    const { cmd, fly, trace } = setup();
    cmd.roll = 0.25;
    const t = fly(20);
    expect(t.rollDeg).toBeGreaterThan(6);
    expect(t.rollDeg).toBeLessThan(11);
    expect(span(last(trace, 8).map((s) => s.alt))).toBeLessThan(40);
    expect(Math.abs(t.sideslipDeg)).toBeLessThan(3);
  });

  it('sharp turn: full input banks to the limit, no further, without stalling or losing height', () => {
    const { cmd, fly, trace } = setup();
    cmd.roll = -1;
    const start = trace.length;
    fly(25);
    const turn = trace.slice(start);
    expect(Math.min(...turn.map((s) => s.bank))).toBeGreaterThan(-(EASY.maxBankDeg + 3));
    expect(last(turn, 1)[0].bank).toBeLessThan(-30);
    expect(turn.some((s) => s.stalled)).toBe(false);
    expect(Math.min(...turn.map((s) => s.alt))).toBeGreaterThan(1968 - 120);
    // No steady sink once established in the turn.
    expect(Math.abs(last(turn, 1)[0].vs)).toBeLessThan(80);
    // Heading decreasing = turning left.
    expect(reversals(last(turn, 15).map((s) => s.bank))).toBeLessThanOrEqual(2);
  });

  it('climbs and descends at the commanded rate while auto-throttle holds speed', () => {
    const { cmd, fly } = setup();
    cmd.pitch = 0.5;
    let t = fly(15);
    expect(t.verticalSpeedFpm).toBeGreaterThan(400);
    expect(t.verticalSpeedFpm).toBeLessThan(650);
    expect(t.airspeedKt).toBeGreaterThan(80);
    cmd.pitch = -1;
    t = fly(15);
    expect(t.verticalSpeedFpm).toBeLessThan(-850);
    expect(t.verticalSpeedFpm).toBeGreaterThan(-1150);
    expect(t.pitchDeg).toBeGreaterThanOrEqual(EASY.minPitchDeg - 0.5);
    expect(t.airspeedKt).toBeLessThan(115);
    cmd.pitch = 0;
    t = fly(12);
    expect(Math.abs(t.verticalSpeedFpm)).toBeLessThan(150);
  });

  it('full back stick never stalls the aircraft', () => {
    const { cmd, easy, fly, trace } = setup();
    easy.speedTargetKt = EASY.minTargetKt;
    cmd.pitch = 1;
    fly(40);
    expect(trace.some((s) => s.stalled)).toBe(false);
    expect(Math.max(...trace.map((s) => s.pitch))).toBeLessThanOrEqual(EASY.maxPitchDeg + 1);
  });

  it('held full descent near the ground becomes a gentle arrival, not a crash', () => {
    const { m, cmd, easy, fly } = setup();
    m.resetAirborne(1500, 60, 40);
    easy.reset(true, 0, 0.3);
    easy.speedTargetKt = 65;
    cmd.pitch = -1;
    const t = fly(60);
    expect(t.phase).toBe('ground');
    expect(t.crashReason).toBeNull();
  });

  it.each([0, 0.3, 1])('takes off with one throttle press, hands-off or pulling (cmd %s)', (pull) => {
    const { m, easy, cmd, fly, trace } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    expect(easy.speedTargetKt).toBe(EASY.takeoffTargetKt);
    cmd.pitch = pull;
    const t = fly(50);
    expect(t.phase).toBe('airborne');
    expect(t.altitudeFt).toBeGreaterThan(350);
    expect(trace.some((s) => s.stalled)).toBe(false);
    expect(m.telemetry.crashReason).toBeNull();
    expect(trace.some((s) => s.tail)).toBe(false); // never over-rotates on the runway
    expect(Math.max(...trace.filter((s) => s.ground).map((s) => s.pitch))).toBeLessThan(11);
    // Lift-off within the 1,200 m runway (threshold at 60 m).
    const liftoff = trace.findIndex((s) => s.alt > 8);
    expect(liftoff).toBeGreaterThan(0);
  });

  it('take-off climb ends at 400 ft and then holds altitude hands-off', () => {
    const { easy, fly, trace } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    fly(80);
    expect(easy.takeoff).toBe(false);
    const alts = last(trace, 10).map((s) => s.alt);
    expect(Math.min(...alts)).toBeGreaterThan(380);
    expect(span(alts)).toBeLessThan(40);
  });

  it('taxiing (below 15 kt), sideways tilt steers without rolling the wings', () => {
    const { easy, cmd, fly, trace } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    cmd.roll = 0.6;
    const t = fly(2.5);
    expect(t.airspeedKt).toBeLessThan(15);
    expect(t.headingDeg).toBeGreaterThan(2);
    expect(t.headingDeg).toBeLessThan(180);
    expect(Math.max(...trace.map((s) => Math.abs(s.bank)))).toBeLessThan(3);
  });

  it('take-off roll holds its heading even with the controller held tilted (no spin-out)', () => {
    const { easy, cmd, fly, trace } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    cmd.roll = 0.6; // pilot holds a firm sideways tilt the whole roll
    const t = fly(25);
    const fromRoll = trace.findIndex((s) => s.ias >= 15);
    const hdgs = trace.slice(fromRoll).filter((s) => s.ground).map((s) => ((s.hdg + 180) % 360) - 180);
    expect(span(hdgs)).toBeLessThan(5);
    expect(t.crashReason).toBeNull();
    expect(trace.some((s) => !s.ground)).toBe(true); // still takes off
  });

  it('take-off roll stays straight with a wobbling hand', () => {
    const { easy, cmd, fly, trace } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    let k = 0;
    fly(30, () => {
      k++;
      cmd.roll = 0.45 * Math.sin(k / 23) + 0.25 * Math.sin(k / 7); // ±0.7 swaying
    });
    const groundHdgs = trace.filter((s) => s.ground).map((s) => ((s.hdg + 180) % 360) - 180);
    expect(Math.max(...groundHdgs.map(Math.abs))).toBeLessThan(6);
    expect(trace.some((s) => s.tail)).toBe(false);
    expect(trace.at(-1)!.alt).toBeGreaterThan(20);
  });

  it('a small leftover tilt (off-centre neutral) does not make the take-off roll veer', () => {
    const { easy, cmd, fly } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    cmd.roll = 0.12;
    cmd.pitch = -0.5; // stay on the runway for the check
    const t = fly(12);
    expect(t.phase).toBe('ground');
    const hdg = ((t.headingDeg + 180) % 360) - 180;
    expect(Math.abs(hdg)).toBeLessThan(1);
  });

  it('throttling back to zero on the runway aborts the take-off', () => {
    const { easy, cmd, fly } = setup(false);
    easy.adjustSpeed(1, 1 / 60, true);
    fly(8);
    for (let i = 0; i < 60 * 4; i++) easy.adjustSpeed(-1, 1 / 60, true);
    expect(easy.speedTargetKt).toBe(0);
    expect(easy.takeoff).toBe(false);
    cmd.pitch = 0;
    const t = fly(10);
    expect(t.phase).toBe('ground');
    expect(t.throttle).toBe(0);
  });

  it('sensor dropout (commands fading to zero) levels the aircraft', () => {
    const { cmd, fly } = setup();
    cmd.roll = 0.8;
    cmd.pitch = 0.4;
    fly(8);
    // The control pipeline fades a lost sensor to neutral over 0.35 s.
    let k = 1;
    fly(0.35, () => {
      k = Math.max(0, k - 1 / (0.35 * 60));
      cmd.roll = 0.8 * k;
      cmd.pitch = 0.4 * k;
    });
    const t = fly(15);
    expect(Math.abs(t.rollDeg)).toBeLessThan(2);
    expect(Math.abs(t.verticalSpeedFpm)).toBeLessThan(150);
  });
});
