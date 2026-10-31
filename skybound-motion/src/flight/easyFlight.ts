import type { AircraftDef } from './aircraft';
import { autoRudder, bankAssist } from './assists';
import type { Telemetry } from './dynamics';
import { clamp, DEG } from './math';

/**
 * Easy flight: a stability-augmentation layer for first-time pilots.
 *
 * It sits between the pilot's commands and the existing flight model. It only
 * produces ordinary control-surface and throttle commands, so the aircraft
 * still moves through the same aerodynamics, inertia and damping:
 *
 *  - roll command → target bank (±35°), rate-limited ailerons, auto-rudder
 *  - pitch command → target vertical speed (±1,000 fpm); centred = hold altitude
 *  - attitude limits: pitch −10…+15°, angle-of-attack (stall) protection,
 *    gentler sink near the ground
 *  - auto-throttle holds a target airspeed (throttle keys change the target)
 *  - an integrator acts as auto-trim, so hands-off flight is steady
 *
 * On the ground the wings are held level and sideways tilt steers the nose
 * wheel. One throttle press starts an automatic take-off: full power, rotate
 * to 7° at 55 kt (short of the 13° tail-strike angle), then climb at 700 fpm
 * to 400 ft unless the pilot pushes forward. Pilot input still shapes it.
 */
export interface EasyCommand {
  /** −1…1, + = climb / rotate */
  pitch: number;
  /** −1…1, + = bank right */
  roll: number;
}

export interface EasyOutput {
  elevator: number;
  aileron: number;
  rudder: number;
  throttle: number;
  /** For the HUD: what the assist is currently aiming for. */
  targetVsFpm: number;
  targetBankDeg: number;
}

export const EASY = {
  maxBankDeg: 35,
  maxAileron: 0.6, // caps roll rate to a calm ~15°/s
  maxClimbFpm: 1000,
  maxSinkFpm: 1000,
  vsSlewFpmPerS: 700,
  minPitchDeg: -10,
  maxPitchDeg: 15,
  rotatePitchDeg: 10,
  rotateMinKt: 50,
  autoRotateKt: 55,
  takeoffPitchDeg: 7,
  takeoffClimbFpm: 700,
  takeoffEndAglFt: 400,
  groundSteer: 0.5,
  groundSteerDeadzone: 0.15,
  /** Above this ground speed, tilt no longer steers: the runway heading is held instead. */
  taxiSteerMaxKt: 15,
  stallMarginDeg: 3,
  cruiseKt: 95,
  minTargetKt: 60,
  maxTargetKt: 115,
  takeoffTargetKt: 90,
  bankGains: { errorDeg: 20, rateDps: 35 },
  rudderGains: { fullAtSideslipDeg: 10, aileronFeedforward: 0.6 },
  /** Pitch error (deg) and pitch rate (deg/s) for full elevator. */
  pitchErrorDeg: 12,
  pitchRateDps: 12,
};

/** Height of the CG above the ground when the wheels touch (ft). */
const GEAR_HEIGHT_FT = 3.45;

export class EasyFlight {
  speedTargetKt = 0;
  /** Automatic take-off in progress (from the throttle press until 400 ft). */
  takeoff = false;
  /** Speeds for the current aircraft (kt). */
  speeds = { cruiseKt: EASY.cruiseKt, takeoffKt: EASY.takeoffTargetKt, minKt: EASY.minTargetKt, maxKt: EASY.maxTargetKt, rotateKt: EASY.autoRotateKt };

  /** Adopt an aircraft's speeds (cruise, take-off, range, rotation). */
  configure(ac: Pick<AircraftDef, 'easy'>) {
    this.speeds = { ...ac.easy };
  }
  /** Heading held during the take-off roll (null when taxiing or airborne). */
  private rollHeading: number | null = null;
  private vsTarget = 0;
  private vsI = 0;
  private pitchI = 0;
  private throttleI = 0;

  /** Start from rest on the runway (idle) or in flight (cruise). */
  reset(airborne: boolean, trimElevator = 0, throttle = 0) {
    this.takeoff = false;
    this.rollHeading = null;
    this.speedTargetKt = airborne ? this.speeds.cruiseKt : 0;
    this.vsTarget = 0;
    this.vsI = 0;
    this.pitchI = airborne ? trimElevator : 0;
    this.throttleI = airborne ? throttle : 0;
  }

  /** Throttle keys move the speed target. On the ground the first press means "take off". */
  adjustSpeed(dir: number, dt: number, onGround: boolean) {
    if (dir === 0) return;
    if (dir > 0 && onGround && this.speedTargetKt < this.speeds.takeoffKt) {
      this.speedTargetKt = this.speeds.takeoffKt;
      this.takeoff = true;
    } else this.speedTargetKt = clamp(this.speedTargetKt + dir * 25 * dt, onGround ? 0 : this.speeds.minKt, this.speeds.maxKt);
    if (dir < 0 && onGround && this.speedTargetKt < this.speeds.minKt) {
      this.speedTargetKt = 0; // throttle back on the runway = abort / taxi stop
      this.takeoff = false;
    }
  }

  update(
    t: Pick<
      Telemetry,
      'phase' | 'airspeedKt' | 'aglFt' | 'verticalSpeedFpm' | 'pitchDeg' | 'rollDeg' | 'rollRateDps' | 'pitchRateDps' | 'aoaDeg' | 'sideslipDeg' | 'headingDeg' | 'turnRateDps'
    >,
    cmd: EasyCommand,
    dt: number,
    alphaWarnDeg: number,
  ): EasyOutput {
    const airborne = t.phase === 'airborne';

    // ---- roll: bank target, gentle aileron, coordinated; on the ground, wings level + steer ----
    let aileron: number;
    let rudder: number;
    if (airborne) {
      this.rollHeading = null;
      aileron = bankAssist(cmd.roll, t, EASY.maxBankDeg, EASY.maxAileron, EASY.bankGains);
      rudder = autoRudder(t, aileron, EASY.rudderGains);
    } else {
      aileron = clamp(-t.rollDeg / EASY.bankGains.errorDeg - t.rollRateDps / EASY.bankGains.rateDps, -EASY.maxAileron, EASY.maxAileron);
      if (t.airspeedKt < EASY.taxiSteerMaxKt) {
        // Taxiing: tilt steers, with an extra dead zone so a slightly-off neutral can't creep.
        const steer = clamp(cmd.roll, -1, 1);
        const dz = EASY.groundSteerDeadzone;
        rudder = (Math.abs(steer) <= dz ? 0 : Math.sign(steer) * ((Math.abs(steer) - dz) / (1 - dz))) * EASY.groundSteer;
        this.rollHeading = null;
      } else {
        // Take-off roll: a hand-held controller always wobbles a little, and at speed
        // any steering from it swerves the aircraft. Hold the heading instead.
        this.rollHeading ??= t.headingDeg;
        const err = ((this.rollHeading - t.headingDeg + 540) % 360) - 180;
        rudder = clamp(err / 6 - t.turnRateDps / 5, -0.6, 0.6);
      }
    }

    // ---- pitch ----
    const agl = Math.max(0, t.aglFt - GEAR_HEIGHT_FT);
    let targetPitch: number;
    if (airborne) {
      let desiredVs = cmd.pitch >= 0 ? cmd.pitch * EASY.maxClimbFpm : cmd.pitch * EASY.maxSinkFpm;
      // Automatic take-off climb until a safe height, unless the pilot pushes forward.
      if (this.takeoff) {
        if (agl > EASY.takeoffEndAglFt || cmd.pitch < -0.2) this.takeoff = false;
        else desiredVs = Math.max(desiredVs, EASY.takeoffClimbFpm);
      }
      // Near the ground, limit sink so a held "descend" becomes a gentle arrival.
      if (agl < 60) desiredVs = Math.max(desiredVs, -Math.max(150, agl * 7));
      // Ease the target toward the request so stick changes never jerk the nose.
      const step = EASY.vsSlewFpmPerS * dt;
      this.vsTarget += clamp(desiredVs - this.vsTarget, -step, step);

      // Pitch ≈ flight-path angle + angle of attack × cos(bank), plus a correction on VS error.
      // (Banked, part of the angle of attack points sideways, so it raises the nose less.)
      const V = Math.max(t.airspeedKt * 0.5144, 20);
      const gammaDeg = Math.asin(clamp((this.vsTarget * 0.00508) / V, -0.5, 0.5)) / DEG;
      const vsError = this.vsTarget - t.verticalSpeedFpm;
      // Slow integral removes the steady sink in turns (and any model mismatch).
      this.vsI = clamp(this.vsI + (vsError / 1500) * dt, -4, 4);
      targetPitch = gammaDeg + t.aoaDeg * Math.cos(t.rollDeg * DEG) + clamp(vsError / 300, -5, 5) + this.vsI;
      // Stall protection: never ask for more angle of attack than the margin allows.
      const aoaLimit = alphaWarnDeg - EASY.stallMarginDeg;
      if (t.aoaDeg > aoaLimit) targetPitch = Math.min(targetPitch, t.pitchDeg - (t.aoaDeg - aoaLimit) * 1.5);
      targetPitch = clamp(targetPitch, EASY.minPitchDeg, EASY.maxPitchDeg);
    } else {
      // Start the airborne VS target at the take-off climb so lift-off doesn't sag.
      this.vsTarget = this.takeoff ? EASY.takeoffClimbFpm : 0;
      this.vsI = 0;
      const pull = Math.max(0, cmd.pitch);
      if (this.takeoff && t.airspeedKt >= this.speeds.rotateKt)
        targetPitch = cmd.pitch < -0.2 ? 0 : Math.min(EASY.rotatePitchDeg, EASY.takeoffPitchDeg + pull * 3);
      else targetPitch = t.airspeedKt > EASY.rotateMinKt ? pull * EASY.rotatePitchDeg : 0;
    }

    const pitchError = targetPitch - t.pitchDeg;
    if (airborne) this.pitchI = clamp(this.pitchI + pitchError * 0.015 * dt, -0.4, 0.4);
    else this.pitchI *= Math.exp(-dt / 0.5);
    let elevator = clamp(pitchError / EASY.pitchErrorDeg - t.pitchRateDps / EASY.pitchRateDps + this.pitchI, -0.7, 0.7);
    // Rolling slowly on the runway: leave the elevator alone.
    if (!airborne && targetPitch === 0 && t.airspeedKt < EASY.rotateMinKt) elevator = 0;

    // ---- auto-throttle ----
    let throttle: number;
    if (this.speedTargetKt <= 0) {
      throttle = 0;
      this.throttleI = 0;
    } else if (!airborne) {
      // Take-off roll: full power until near the target.
      throttle = t.airspeedKt < this.speedTargetKt - 5 ? 1 : 0.6;
    } else {
      const err = this.speedTargetKt - t.airspeedKt;
      this.throttleI = clamp(this.throttleI + err * 0.004 * dt, 0, 1);
      // Climbing costs power, descending gives it back.
      throttle = clamp(this.throttleI + err * 0.04 + this.vsTarget / 2500, 0, 1);
    }

    return {
      elevator,
      aileron,
      rudder,
      throttle,
      targetVsFpm: airborne ? this.vsTarget : 0,
      targetBankDeg: airborne ? clamp(cmd.roll, -1, 1) * EASY.maxBankDeg : 0,
    };
  }
}
