import type { ImuSample } from '../sensor/protocol';
import type { SensorHub } from '../sensor/sensorHub';

/** The controller pose the pilot calls "hands-off". Degrees, aircraft convention. */
export interface Neutral {
  pitch: number;
  roll: number;
  yaw: number;
  capturedAt: number;
}

export const ZERO_NEUTRAL: Neutral = { pitch: 0, roll: 0, yaw: 0, capturedAt: 0 };

export const NEUTRAL_CAPTURE_MS = 1000;
export const NEUTRAL_MIN_SAMPLES = 20;
/** Max standard deviation (deg) of pitch/roll while capturing; more means the hand moved. */
export const NEUTRAL_MAX_SPREAD_DEG = 0.8;
/** Neutral beyond this tilt is almost certainly a mistake (controller on its side). */
export const NEUTRAL_MAX_TILT_DEG = 45;

export type NeutralResult =
  | { ok: true; neutral: Neutral; spreadDeg: number }
  | { ok: false; error: 'no_data' | 'moving' | 'too_tilted' | 'link_lost'; detail: string };

const D2R = Math.PI / 180;

/** Angle-safe mean: roll and yaw can straddle ±180°. */
function circularMean(deg: number[]) {
  let s = 0;
  let c = 0;
  for (const d of deg) {
    s += Math.sin(d * D2R);
    c += Math.cos(d * D2R);
  }
  return Math.atan2(s, c) / D2R;
}

function spread(deg: number[], mean: number) {
  let sum = 0;
  for (const d of deg) {
    const diff = ((((d - mean + 180) % 360) + 360) % 360) - 180;
    sum += diff * diff;
  }
  return Math.sqrt(sum / deg.length);
}

export function computeNeutral(samples: readonly ImuSample[], capturedAt = Date.now()): NeutralResult {
  if (samples.length < NEUTRAL_MIN_SAMPLES)
    return { ok: false, error: 'no_data', detail: `Only ${samples.length} samples arrived; need ${NEUTRAL_MIN_SAMPLES}.` };
  const pitches = samples.map((s) => s.pitch);
  const rolls = samples.map((s) => s.roll);
  const pitch = pitches.reduce((a, b) => a + b, 0) / pitches.length;
  const roll = circularMean(rolls);
  const yaw = circularMean(samples.map((s) => s.yaw));
  const worst = Math.max(spread(pitches, pitch), spread(rolls, roll));
  if (worst > NEUTRAL_MAX_SPREAD_DEG)
    return { ok: false, error: 'moving', detail: `Controller moved during capture (±${worst.toFixed(1)}°). Hold it still and retry.` };
  if (Math.abs(pitch) > NEUTRAL_MAX_TILT_DEG || Math.abs(roll) > NEUTRAL_MAX_TILT_DEG)
    return {
      ok: false,
      error: 'too_tilted',
      detail: `Neutral pose is tilted ${pitch.toFixed(0)}° pitch / ${roll.toFixed(0)}° roll. Hold it near level, or check the mounting.`,
    };
  return { ok: true, neutral: { pitch, roll, yaw, capturedAt }, spreadDeg: worst };
}

/** Collect samples from the hub for `durationMs`, then validate them. */
export function captureNeutral(hub: SensorHub, durationMs = NEUTRAL_CAPTURE_MS): Promise<NeutralResult> {
  return new Promise((resolve) => {
    const samples: ImuSample[] = [];
    const off = hub.onSample(({ sample }) => samples.push(sample));
    setTimeout(() => {
      off();
      if (!hub.isUsable()) {
        resolve({ ok: false, error: 'link_lost', detail: 'Sensor link dropped during capture.' });
        return;
      }
      resolve(computeNeutral(samples));
    }, durationMs);
  });
}
