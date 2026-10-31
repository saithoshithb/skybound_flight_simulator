import type { Neutral } from '../calibration/neutral';
import type { ImuSample } from '../sensor/protocol';

/**
 * Turns controller attitude into normalized pilot commands in [-1, 1].
 * These are control-surface commands (stick position), not aircraft attitude:
 * the flight model decides how the aircraft responds to them.
 *
 *   elevator + = pull / nose-up command   (controller front edge raised)
 *   aileron  + = roll-right command       (controller right edge lowered)
 *   rudder   + = yaw-right command        (controller twisted clockwise from above)
 */
export interface ControlCommand {
  elevator: number;
  aileron: number;
  rudder: number;
  /** 0–1 authority given to the sensor; ramps in/out on connect, recenter and dropouts. */
  engagement: number;
}

export type YawMode = 'off' | 'washout' | 'rate';

export interface AxisSettings {
  /** Controller tilt (deg) that produces full deflection before sensitivity. */
  maxAngleDeg: number;
  invert: boolean;
}

export interface ControlSettings {
  pitch: AxisSettings;
  roll: AxisSettings;
  yaw: AxisSettings;
  yawMode: YawMode;
  /** Yaw rate (deg/s) for full rudder in 'rate' mode. */
  yawFullRateDps: number;
  /** Seconds for a held twist to wash out to zero rudder in 'washout' mode. */
  yawWashoutS: number;
  /** Multiplier applied after the curve. 1 = maxAngle gives full deflection. */
  sensitivity: number;
  /** Fraction of the range around neutral that produces no output (0–0.5). */
  deadzone: number;
  /** 0 = linear, 1 = fully cubic: finer control near center. */
  expo: number;
  /** Low-pass time constant (ms). Removes hand tremor and sensor noise. */
  smoothingMs: number;
  /** Max command change per second. Stops instantaneous jumps. */
  slewPerSec: number;
}

export const DEFAULT_CONTROL_SETTINGS: ControlSettings = {
  pitch: { maxAngleDeg: 30, invert: false },
  roll: { maxAngleDeg: 35, invert: false },
  yaw: { maxAngleDeg: 30, invert: false },
  yawMode: 'off',
  yawFullRateDps: 90,
  yawWashoutS: 3,
  sensitivity: 1,
  // Tuned for Easy flight: hand tremor (±1–2°) produces nothing, small tilts
  // give gentle turns, and smoothing hides sensor noise without feeling laggy.
  deadzone: 0.08,
  expo: 0.5,
  smoothingMs: 100,
  slewPerSec: 3,
};

/** Defaults shipped before Easy flight, for migrating untouched settings. */
export const PREVIOUS_SHAPING_DEFAULTS = { deadzone: 0.06, expo: 0.3, smoothingMs: 60, slewPerSec: 4 };

/** Seconds to fade sensor authority in after (re)engaging, and out after a dropout. */
export const ENGAGE_RAMP_S = 1.0;
export const DISENGAGE_RAMP_S = 0.35;

export const clamp = (v: number, lo = -1, hi = 1) => Math.min(hi, Math.max(lo, v));
export const wrap180 = (d: number) => ((((d + 180) % 360) + 360) % 360) - 180;

/** Dead zone that rescales so output still reaches ±1 (no step at the edge). */
export function applyDeadzone(x: number, dz: number) {
  const a = Math.abs(x);
  if (a <= dz) return 0;
  return Math.sign(x) * Math.min(1, (a - dz) / (1 - dz));
}

export function applyExpo(x: number, expo: number) {
  return (1 - expo) * x + expo * x * x * x;
}

/** Static part of the shaping chain: normalized deflection → curve → sensitivity. */
export function shapeAxis(deltaDeg: number, axis: AxisSettings, s: ControlSettings) {
  const x = clamp(deltaDeg / axis.maxAngleDeg);
  const y = clamp(applyExpo(applyDeadzone(x, s.deadzone), s.expo) * s.sensitivity);
  return axis.invert ? -y : y;
}

/**
 * Stateful part: smoothing, slew limiting, yaw washout and engagement ramp.
 * Call `update` once per simulation frame (not per sensor sample) with the
 * latest sample or null when the link isn't usable.
 */
export class ControlPipeline {
  private out = { elevator: 0, aileron: 0, rudder: 0 };
  private engagement = 0;
  private yawRef: number | null = null;
  /** Raw (pre-smoothing) targets, for the UI. */
  target = { elevator: 0, aileron: 0, rudder: 0 };

  /** Drop authority to zero immediately; it ramps back in. Use after recenter/connect. */
  reengage() {
    this.engagement = 0;
    this.yawRef = null;
  }

  update(sample: ImuSample | null, neutral: Neutral, s: ControlSettings, dt: number): ControlCommand {
    dt = Math.min(Math.max(dt, 0), 0.1);
    const usable = sample !== null;

    if (usable) {
      this.engagement = Math.min(1, this.engagement + dt / ENGAGE_RAMP_S);
      const dYaw = this.yawDelta(sample, neutral, s, dt);
      this.target = {
        elevator: shapeAxis(sample.pitch - neutral.pitch, s.pitch, s),
        aileron: shapeAxis(wrap180(sample.roll - neutral.roll), s.roll, s),
        rudder: dYaw,
      };
    } else {
      // Link stale/lost: fade to neutral instead of freezing the last deflection.
      this.engagement = Math.max(0, this.engagement - dt / DISENGAGE_RAMP_S);
      this.target = { elevator: 0, aileron: 0, rudder: 0 };
    }

    const alpha = s.smoothingMs > 0 ? 1 - Math.exp(-(dt * 1000) / s.smoothingMs) : 1;
    const maxStep = s.slewPerSec * dt;
    for (const k of ['elevator', 'aileron', 'rudder'] as const) {
      const smoothed = this.out[k] + (this.target[k] - this.out[k]) * alpha;
      this.out[k] = this.out[k] + clamp(smoothed - this.out[k], -maxStep, maxStep);
    }
    const e = smoothstep(this.engagement);
    return {
      elevator: this.out.elevator * e,
      aileron: this.out.aileron * e,
      rudder: this.out.rudder * e,
      engagement: e,
    };
  }

  private yawDelta(sample: ImuSample, neutral: Neutral, s: ControlSettings, dt: number) {
    switch (s.yawMode) {
      case 'off':
        return 0;
      case 'rate': {
        // Yaw *rate* is drift-free enough to use directly: twist to turn, hold still to stop.
        const y = clamp(applyDeadzone(clamp(sample.yawRate / s.yawFullRateDps), s.deadzone) * s.sensitivity);
        return s.yaw.invert ? -y : y;
      }
      case 'washout': {
        // Relative yaw with a high-pass: the reference slowly follows the
        // controller, so gyro drift and a held twist both decay to zero rudder.
        if (this.yawRef === null) this.yawRef = neutral.capturedAt ? neutral.yaw : sample.yaw;
        const delta = wrap180(sample.yaw - this.yawRef);
        this.yawRef = wrap180(this.yawRef + delta * (1 - Math.exp(-dt / Math.max(0.2, s.yawWashoutS))));
        return shapeAxis(delta, s.yaw, s);
      }
    }
  }
}

function smoothstep(x: number) {
  return x * x * (3 - 2 * x);
}
