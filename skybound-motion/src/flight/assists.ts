import type { Telemetry } from './dynamics';
import { clamp } from './math';

/**
 * Auto-coordination ("auto-rudder"): rudder that drives sideslip toward zero,
 * standing in for the pilot's feet. Without it, aileron alone gives a light
 * aircraft several degrees of adverse yaw, which feels wrong when the motion
 * controller has no rudder axis. Inactive on the ground so nose-wheel steering
 * stays exactly what the pilot commands.
 *
 * β > 0 means the relative wind comes from the right (nose left of the flight
 * path), which needs right (+) rudder.
 */
// 5° drove a ±1.8° sideslip limit cycle (wing rock) through the rudder lag; 10° is clean.
const FULL_RUDDER_AT_SIDESLIP_DEG = 10;
/** Adverse yaw is proportional to aileron, so lead it rather than wait for sideslip. */
const AILERON_FEEDFORWARD = 0.6;

/**
 * Bank-angle assist: the roll command sets a target bank angle instead of a
 * roll rate. A PD law on bank error drives the ailerons, so the aircraft
 * still rolls only through aileron moments and its own inertia and damping.
 * This suits a hand-held controller that the pilot naturally holds tilted;
 * levelling the controller levels the wings. Off on the ground.
 */
export const MAX_ASSIST_BANK_DEG = 45;

export function bankAssist(
  rollCmd: number,
  t: Pick<Telemetry, 'rollDeg' | 'rollRateDps' | 'phase'>,
  maxBankDeg = MAX_ASSIST_BANK_DEG,
  maxAileron = 1,
  /** Bank error (deg) for full aileron, and roll rate (deg/s) that cancels it. */
  gains = { errorDeg: 20, rateDps: 35 },
) {
  if (t.phase !== 'airborne') return rollCmd;
  const target = clamp(rollCmd, -1, 1) * maxBankDeg;
  const error = ((target - t.rollDeg + 540) % 360) - 180;
  return clamp(error / gains.errorDeg - t.rollRateDps / gains.rateDps, -maxAileron, maxAileron);
}

export function autoRudder(
  t: Pick<Telemetry, 'sideslipDeg' | 'phase' | 'airspeedKt'>,
  aileron: number,
  gains = { fullAtSideslipDeg: FULL_RUDDER_AT_SIDESLIP_DEG, aileronFeedforward: AILERON_FEEDFORWARD },
) {
  if (t.phase !== 'airborne' || t.airspeedKt < 40) return 0;
  return clamp(t.sideslipDeg / gains.fullAtSideslipDeg + gains.aileronFeedforward * aileron, -1, 1);
}
