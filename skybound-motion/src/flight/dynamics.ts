import { TRAINER, type AircraftDef } from './aircraft';
import {
  add,
  clamp,
  cross,
  DEG,
  dot,
  fromEuler,
  length,
  qmul,
  qnormalize,
  rotate,
  rotateInv,
  scale,
  toEuler,
  type Quat,
  type V3,
} from './math';

/**
 * Six-degree-of-freedom rigid-body flight model.
 *
 * World frame NED with the ground plane at z = 0 (field elevation 0 ft MSL).
 * Forces: aerodynamics from stability derivatives with a smooth post-stall
 * lift break, propeller thrust from engine power, gravity, and spring-damper
 * landing gear with rolling/braking/side friction and nose-wheel steering.
 *
 * Pilot commands go through first-order surface actuators, so a step input
 * never produces an instantaneous rotation: the aircraft responds through
 * moments, inertia and damping.
 */

export interface PilotInput {
  /** −1…1, + = nose up */
  elevator: number;
  /** −1…1, + = roll right */
  aileron: number;
  /** −1…1, + = yaw right (also steers the nose wheel) */
  rudder: number;
  /** 0…1 */
  throttle: number;
  /** 0…1 */
  brake: number;
  /** −1…1 elevator trim, added to elevator before limits */
  trim: number;
}

export interface FlightState {
  pos: V3; // NED, m
  vel: V3; // NED, m/s
  q: Quat; // body → world
  w: V3; // body rates p, q, r (rad/s)
  engine: number; // 0…1, lags throttle
  surfaces: { elevator: number; aileron: number; rudder: number }; // actual deflections (rad, aerodynamic sign)
}

export type FlightPhase = 'ground' | 'airborne' | 'crashed';

export interface Telemetry {
  airspeedKt: number; // indicated
  groundSpeedKt: number;
  altitudeFt: number;
  /** Height of the CG above the terrain directly below. */
  aglFt: number;
  /** Rate of heading change (deg/s, + = right). */
  turnRateDps: number;
  /** Body lateral specific force in g (+ = pushed right), drives the slip ball. */
  lateralG: number;
  headingDeg: number; // 0–360
  verticalSpeedFpm: number;
  pitchDeg: number;
  rollDeg: number;
  /** Body roll rate p. */
  rollRateDps: number;
  /** Body pitch rate q. */
  pitchRateDps: number;
  aoaDeg: number;
  sideslipDeg: number;
  loadFactor: number;
  throttle: number;
  engine: number;
  trim: number;
  brake: number;
  phase: FlightPhase;
  stalled: boolean;
  overspeed: boolean;
  crashReason: string | null;
  /** Wheels in contact with the ground, for HUD/sounds. */
  wheelsOnGround: number;
  /** Tail skid touched the ground in the last 1.5 s. */
  tailStrike: boolean;
  timeS: number;
}

export const STEP_S = 1 / 240;
const G = 9.80665;
const RHO0 = 1.225;
const MS_TO_KT = 1.943844;
const M_TO_FT = 3.28084;

export function airDensity(altitudeM: number) {
  return RHO0 * Math.pow(Math.max(0.2, 1 - 2.25577e-5 * altitudeM), 4.2559);
}

export const emptyInput = (): PilotInput => ({ elevator: 0, aileron: 0, rudder: 0, throttle: 0, brake: 0, trim: 0 });

/** Lift coefficient vs angle of attack with a smooth stall (blended to flat-plate lift). */
export function liftCoefficient(ac: AircraftDef, alpha: number) {
  const sigma = stallBlend(ac, alpha);
  const linear = ac.CL0 + ac.CLalpha * alpha;
  const flatPlate = 2 * Math.sign(alpha) * Math.sin(alpha) ** 2 * Math.cos(alpha);
  return (1 - sigma) * linear + sigma * flatPlate;
}

function stallBlend(ac: AircraftDef, alpha: number) {
  const M = ac.stallSharpness;
  const a0 = ac.alphaStall;
  const e1 = Math.exp(-M * (alpha - a0));
  const e2 = Math.exp(M * (alpha + a0));
  return (1 + e1 + e2) / ((1 + e1) * (1 + e2));
}

/** Steady level-flight trim at a true airspeed: angle of attack, elevator (rad), throttle. */
export function trimLevel(ac: AircraftDef, speedMs: number, altitudeM: number) {
  const qbar = 0.5 * airDensity(altitudeM) * speedMs * speedMs;
  const CLreq = (ac.mass * G) / (qbar * ac.wingArea);
  // Solve CL(α) + CLde·δe = CLreq and Cm = 0 together (linear region).
  // δe = −(Cm0 + Cmα·α)/Cmδe  ⇒  CL0 + CLα·α − CLδe(Cm0 + Cmα·α)/Cmδe = CLreq
  const alpha = (CLreq - ac.CL0 + (ac.CLde * ac.Cm0) / ac.Cmde) / (ac.CLalpha - (ac.CLde * ac.Cmalpha) / ac.Cmde);
  const de = -(ac.Cm0 + ac.Cmalpha * alpha) / ac.Cmde;
  const CL = ac.CL0 + ac.CLalpha * alpha + ac.CLde * de;
  const CD = ac.CD0 + CL * CL / (Math.PI * ac.oswald * (ac.span ** 2 / ac.wingArea));
  const drag = qbar * ac.wingArea * CD;
  const thrustFull = Math.min(ac.staticThrustN, (ac.propEfficiency * ac.maxPowerW) / speedMs);
  return { alpha, elevatorRad: de, throttle: clamp(drag / thrustFull, 0, 1) };
}

export class FlightModel {
  ac: AircraftDef;
  state!: FlightState;
  phase: FlightPhase = 'ground';
  crashReason: string | null = null;
  telemetry!: Telemetry;
  private accumulator = 0;
  private time = 0;
  private lastLoad = 1;
  private lastLateralG = 0;
  private wheelsOnGround = 0;
  private tailStrikeUntil = -1;
  private lastInput: PilotInput = emptyInput();

  /** Angle of attack of maximum lift, and the stall-warning angle 2° before it. */
  alphaCLmax = 0;
  alphaWarn = 0;

  /** Terrain height (m above field) at a north/east position. Flat by default. */
  readonly terrain: (northM: number, eastM: number) => number;

  constructor(ac: AircraftDef = TRAINER, terrain: (northM: number, eastM: number) => number = () => 0) {
    this.ac = ac;
    this.terrain = terrain;
    this.setAircraft(ac);
  }

  /** Switch aircraft type; the new aircraft starts parked on the runway. */
  setAircraft(ac: AircraftDef) {
    this.ac = ac;
    let best = -Infinity;
    let at = 0;
    for (let a = 0; a < 35 * DEG; a += 0.1 * DEG) {
      const cl = liftCoefficient(ac, a);
      if (cl > best) [best, at] = [cl, a];
    }
    this.alphaCLmax = at;
    this.alphaWarn = at - 2 * DEG;
    this.resetOnRunway();
  }

  /** Parked at the runway threshold, engine idle, heading north (runway 36). */
  resetOnRunway(northM = 60) {
    const gearDepth = this.ac.gear[0].pos[2];
    this.state = {
      pos: [northM, 0, -gearDepth + 0.06],
      vel: [0, 0, 0],
      q: fromEuler(0, 0, 0),
      w: [0, 0, 0],
      engine: 0,
      surfaces: { elevator: 0, aileron: 0, rudder: 0 },
    };
    this.phase = 'ground';
    this.finishReset();
  }

  /**
   * Trimmed level flight on a long final for runway 36. Returns the trim
   * command the pilot must hold so the aircraft doesn't immediately pitch.
   */
  resetAirborne(distanceM = 4000, altitudeM = 300, speedMs = 50) {
    const t = trimLevel(this.ac, speedMs, altitudeM);
    this.state = {
      pos: [-distanceM, 0, -altitudeM],
      // Level flight path, nose raised by the trim angle of attack.
      vel: [speedMs, 0, 0],
      q: fromEuler(0, t.alpha, 0),
      w: [0, 0, 0],
      engine: t.throttle,
      surfaces: { elevator: t.elevatorRad, aileron: 0, rudder: 0 },
    };
    this.phase = 'airborne';
    this.finishReset();
    return { trim: -t.elevatorRad / this.ac.maxElevator, throttle: t.throttle };
  }

  private finishReset() {
    this.crashReason = null;
    this.accumulator = 0;
    this.time = 0;
    this.tailStrikeUntil = -1;
    this.lastLoad = 1;
    this.telemetry = this.computeTelemetry(this.lastInput);
  }

  /** Advance by a variable frame time using fixed sub-steps. */
  advance(input: PilotInput, frameDt: number) {
    this.lastInput = input;
    if (this.phase === 'crashed') return;
    this.accumulator += Math.min(frameDt, 0.1);
    while (this.accumulator >= STEP_S) {
      this.step(input, STEP_S);
      this.accumulator -= STEP_S;
      // step() may have crashed the aircraft; TS can't see that through the call.
      if ((this.phase as FlightPhase) === 'crashed') break;
    }
    this.telemetry = this.computeTelemetry(input);
  }

  step(input: PilotInput, dt: number) {
    const ac = this.ac;
    const s = this.state;
    this.time += dt;

    // --- actuators ---
    const k = 1 - Math.exp(-dt / ac.surfaceLagS);
    const target = {
      // Positive elevator command = trailing edge up = negative aerodynamic δe.
      elevator: -clamp(input.elevator + input.trim, -1, 1) * ac.maxElevator,
      aileron: clamp(input.aileron, -1, 1) * ac.maxAileron,
      rudder: clamp(input.rudder, -1, 1) * ac.maxRudder,
    };
    s.surfaces.elevator += (target.elevator - s.surfaces.elevator) * k;
    s.surfaces.aileron += (target.aileron - s.surfaces.aileron) * k;
    s.surfaces.rudder += (target.rudder - s.surfaces.rudder) * k;
    s.engine += (clamp(input.throttle, 0, 1) - s.engine) * (1 - Math.exp(-dt / ac.engineTimeConstantS));

    // --- aerodynamics (body frame) ---
    const vb = rotateInv(s.q, s.vel);
    const V = length(vb);
    const altitude = -s.pos[2];
    const rho = airDensity(altitude);
    let forceB: V3 = [0, 0, 0];
    let momentB: V3 = [0, 0, 0];
    if (V > 0.5) {
      const alpha = Math.atan2(vb[2], vb[0]);
      const beta = Math.asin(clamp(vb[1] / V, -1, 1));
      const qbar = 0.5 * rho * V * V;
      const [p, q, r] = s.w;
      const ph = (p * ac.span) / (2 * V);
      const qh = (q * ac.chord) / (2 * V);
      const rh = (r * ac.span) / (2 * V);
      const { elevator: de, aileron: da, rudder: dr } = s.surfaces;
      const AR = (ac.span * ac.span) / ac.wingArea;

      const CL = liftCoefficient(ac, alpha) + ac.CLq * qh + ac.CLde * de;
      const CLlin = ac.CL0 + ac.CLalpha * alpha;
      const sigma = stallBlend(ac, alpha);
      const CD = ac.CD0 + ((1 - sigma) * CLlin * CLlin) / (Math.PI * ac.oswald * AR) + sigma * 1.2 * Math.sin(alpha) ** 2;
      const CY = ac.CYbeta * beta + ac.CYdr * dr;
      const Cl = ac.Clbeta * beta + ac.Clp * ph + ac.Clr * rh + ac.Clda * da + ac.Cldr * dr;
      // Past the stall the tail loses some authority and the wing pitches the nose down.
      const Cm = ac.Cm0 + ac.Cmalpha * alpha + ac.Cmq * qh + ac.Cmde * de * (1 - 0.3 * sigma) - 0.25 * sigma * Math.sign(alpha);
      const Cn = ac.Cnbeta * beta + ac.Cnp * ph + ac.Cnr * rh + ac.Cnda * da + ac.Cndr * dr;

      const L = qbar * ac.wingArea * CL;
      const D = qbar * ac.wingArea * CD;
      const Y = qbar * ac.wingArea * CY;
      const ca = Math.cos(alpha), sa = Math.sin(alpha);
      forceB = [-D * ca + L * sa, Y, -D * sa - L * ca];
      momentB = [qbar * ac.wingArea * ac.span * Cl, qbar * ac.wingArea * ac.chord * Cm, qbar * ac.wingArea * ac.span * Cn];
    }

    // --- thrust along body x ---
    const thrust = s.engine * Math.min(ac.staticThrustN, (ac.propEfficiency * ac.maxPowerW) / Math.max(V, 1));
    forceB = add(forceB, [thrust, 0, 0]);

    // Load factor from non-gravity forces (what the pilot feels).
    const ground = this.groundForces(input, V);
    const totalNonGravBody = add(forceB, rotateInv(s.q, ground.force));
    this.lastLoad = -totalNonGravBody[2] / (ac.mass * G);
    this.lastLateralG = totalNonGravBody[1] / (ac.mass * G);

    // --- integrate translation ---
    const forceW = add(add(rotate(s.q, forceB), ground.force), [0, 0, ac.mass * G]);
    s.vel = add(s.vel, scale(forceW, dt / ac.mass));
    s.pos = add(s.pos, scale(s.vel, dt));

    // --- integrate rotation (diagonal inertia) ---
    const I = ac.inertia;
    const M = add(momentB, ground.moment);
    const Iw: V3 = [I[0] * s.w[0], I[1] * s.w[1], I[2] * s.w[2]];
    const gyro = cross(s.w, Iw);
    const wdot: V3 = [(M[0] - gyro[0]) / I[0], (M[1] - gyro[1]) / I[1], (M[2] - gyro[2]) / I[2]];
    s.w = add(s.w, scale(wdot, dt));
    const dq = qmul(s.q, [0, s.w[0], s.w[1], s.w[2]]);
    s.q = qnormalize([s.q[0] + 0.5 * dq[0] * dt, s.q[1] + 0.5 * dq[1] * dt, s.q[2] + 0.5 * dq[2] * dt, s.q[3] + 0.5 * dq[3] * dt]);

    if (!this.checkStructure(ground.maxSinkMs)) this.phase = ground.anyContact ? 'ground' : 'airborne';
  }

  private groundForces(input: PilotInput, airspeed: number) {
    const s = this.state;
    let force: V3 = [0, 0, 0];
    let moment: V3 = [0, 0, 0];
    let contacts = 0;
    let maxSinkMs = 0;
    let skidContact = false;
    // Nose-wheel steering fades out with speed, as rudder authority takes over.
    // Full authority while taxiing, ~10% by 40 kt: big nose-wheel angles at speed swerve the aircraft.
    const steer = clamp(input.rudder, -1, 1) * this.ac.maxNoseSteer * clamp(1 - (airspeed - 5) / 15, 0.1, 1);

    for (const leg of this.ac.gear) {
      const pw = add(s.pos, rotate(s.q, leg.pos));
      const penetration = pw[2] + this.terrain(pw[0], pw[1]); // ground at z = −h, z points down
      if (penetration <= 0) continue;
      if (!leg.skid) contacts++;
      const vp = add(s.vel, rotate(s.q, cross(s.w, leg.pos)));
      maxSinkMs = Math.max(maxSinkMs, vp[2]);
      const normal = Math.max(0, leg.spring * penetration + leg.damper * vp[2]);
      if (normal === 0) continue;

      const angle = leg.steerable ? steer : 0;
      const wheelFwd = rotate(s.q, [Math.cos(angle), Math.sin(angle), 0]);
      const h = Math.hypot(wheelFwd[0], wheelFwd[1]) || 1;
      const fwd: V3 = [wheelFwd[0] / h, wheelFwd[1] / h, 0];
      const lat: V3 = [-fwd[1], fwd[0], 0];
      const vLong = dot(vp, fwd);
      const vLat = dot(vp, lat);
      if (leg.skid) skidContact = true;
      const mu = leg.skid ? 0.45 : 0.025 + (leg.brakes ? clamp(input.brake, 0, 1) * 0.65 : 0);
      const fLong = -mu * normal * Math.tanh(vLong / 0.4);
      const fLat = -(leg.skid ? 0.45 : 0.85) * normal * Math.tanh(vLat / 0.25);
      const f: V3 = [fwd[0] * fLong + lat[0] * fLat, fwd[1] * fLong + lat[1] * fLat, -normal];
      force = add(force, f);
      moment = add(moment, cross(leg.pos, rotateInv(s.q, f)));
    }
    this.wheelsOnGround = contacts;
    if (skidContact) this.tailStrikeUntil = this.time + 1.5;
    return { force, moment, maxSinkMs, anyContact: contacts > 0 || skidContact };
  }

  /** Returns true if the aircraft crashed this step. */
  private checkStructure(sinkMs: number): boolean {
    if (sinkMs > 4.5) return this.crash(`Hard landing (${Math.round(sinkMs * 196.85)} fpm at touchdown)`);
    for (const p of this.ac.structure) {
      const pw = add(this.state.pos, rotate(this.state.q, p.pos));
      if (pw[2] + this.terrain(pw[0], pw[1]) > 0.02) return this.crash(`${p.name} struck the ground`);
    }
    return false;
  }

  private crash(reason: string) {
    this.phase = 'crashed';
    this.crashReason = reason;
    this.state.vel = [0, 0, 0];
    this.state.w = [0, 0, 0];
    return true;
  }

  private computeTelemetry(input: PilotInput): Telemetry {
    const s = this.state;
    const e = toEuler(s.q);
    const vb = rotateInv(s.q, s.vel);
    const V = length(vb);
    const alpha = V > 1 ? Math.atan2(vb[2], vb[0]) : 0;
    const beta = V > 1 ? Math.asin(clamp(vb[1] / V, -1, 1)) : 0;
    const altitudeM = -s.pos[2];
    const ias = V * Math.sqrt(airDensity(altitudeM) / RHO0);
    return {
      airspeedKt: ias * MS_TO_KT,
      groundSpeedKt: Math.hypot(s.vel[0], s.vel[1]) * MS_TO_KT,
      altitudeFt: altitudeM * M_TO_FT,
      aglFt: (altitudeM - this.terrain(s.pos[0], s.pos[1])) * M_TO_FT,
      turnRateDps: ((s.w[1] * Math.sin(e.roll) + s.w[2] * Math.cos(e.roll)) / Math.max(0.1, Math.cos(e.pitch))) / DEG,
      lateralG: this.lastLateralG,
      headingDeg: ((e.yaw / DEG) % 360 + 360) % 360,
      verticalSpeedFpm: -s.vel[2] * M_TO_FT * 60,
      pitchDeg: e.pitch / DEG,
      rollDeg: e.roll / DEG,
      rollRateDps: s.w[0] / DEG,
      pitchRateDps: s.w[1] / DEG,
      aoaDeg: alpha / DEG,
      sideslipDeg: beta / DEG,
      loadFactor: this.lastLoad,
      throttle: clamp(input.throttle, 0, 1),
      engine: s.engine,
      trim: input.trim,
      brake: input.brake,
      phase: this.phase,
      stalled: this.phase === 'airborne' && V > 5 && alpha > this.alphaWarn,
      overspeed: V > this.ac.vneMs,
      crashReason: this.crashReason,
      wheelsOnGround: this.wheelsOnGround,
      tailStrike: this.time < this.tailStrikeUntil,
      timeS: this.time,
    };
  }
}

/** 1-g stall speed (indicated, kt) at sea level, from the lift curve's maximum. */
export function stallSpeedKt(ac: AircraftDef) {
  let clMax = 0;
  for (let a = 0; a < 35 * DEG; a += 0.1 * DEG) clMax = Math.max(clMax, liftCoefficient(ac, a));
  return Math.sqrt((2 * ac.mass * G) / (RHO0 * ac.wingArea * clMax)) * MS_TO_KT;
}
