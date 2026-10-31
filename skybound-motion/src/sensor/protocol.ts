// Wire protocol v1: newline-delimited JSON from the controller. See docs/PROTOCOL.md.
// Every line is validated here; nothing downstream sees unvalidated data.

export const PROTOCOL_VERSION = 1;
export const MAX_LINE_LENGTH = 512;

export type DeviceState =
  | 'calibrating'
  | 'ready'
  | 'sensor_error'
  | 'motion_during_cal'
  | 'cal_failed';

const DEVICE_STATES: readonly DeviceState[] = [
  'calibrating',
  'ready',
  'sensor_error',
  'motion_during_cal',
  'cal_failed',
];

/** Fused attitude sample. Angles in degrees, aircraft convention (see docs/AXES.md). */
export interface ImuSample {
  kind: 'imu';
  seq: number;
  deviceMs: number;
  /** + nose up */
  pitch: number;
  /** + right wing down */
  roll: number;
  /** + nose right; relative to power-on / last ZEROYAW, drifts over time */
  yaw: number;
  pitchRate: number;
  rollRate: number;
  yawRate: number;
  /** Accelerometer magnitude in g (≈1 at rest). */
  accelG: number;
}

export interface StatusMessage {
  kind: 'status';
  state: DeviceState;
  message: string;
}

export interface HelloMessage {
  kind: 'hello';
  firmware: string;
  proto: number;
  rateHz: number;
}

export interface AckMessage {
  kind: 'ack';
  command: string;
  error?: string;
}

export type DeviceMessage = ImuSample | StatusMessage | HelloMessage | AckMessage;

export type HostCommand = 'CAL' | 'ZEROYAW' | 'HELLO';

export type ParseResult = { ok: true; message: DeviceMessage } | { ok: false; reason: string };

const fail = (reason: string): ParseResult => ({ ok: false, reason });

function num(o: Record<string, unknown>, key: string, min: number, max: number): number | string {
  const v = o[key];
  if (typeof v !== 'number' || !Number.isFinite(v)) return `"${key}" is not a finite number`;
  if (v < min || v > max) return `"${key}"=${v} outside [${min}, ${max}]`;
  return v;
}

function str(o: Record<string, unknown>, key: string, maxLen = 120): string | null {
  const v = o[key];
  return typeof v === 'string' && v.length <= maxLen ? v : null;
}

export function parseLine(raw: string): ParseResult {
  const line = raw.trim();
  if (!line) return fail('empty line');
  if (line.length > MAX_LINE_LENGTH) return fail('line too long');
  if (line[0] !== '{') return fail('not a JSON object');
  let data: unknown;
  try {
    data = JSON.parse(line);
  } catch {
    return fail('invalid JSON');
  }
  if (typeof data !== 'object' || data === null || Array.isArray(data)) return fail('not a JSON object');
  const o = data as Record<string, unknown>;

  switch (o.t) {
    case 'imu': {
      const fields = {
        seq: num(o, 's', 0, 0xffffffff),
        deviceMs: num(o, 'ms', 0, 0xffffffff),
        pitch: num(o, 'p', -90.5, 90.5),
        roll: num(o, 'r', -180.5, 180.5),
        yaw: num(o, 'y', -180.5, 180.5),
        pitchRate: num(o, 'pr', -2000, 2000),
        rollRate: num(o, 'rr', -2000, 2000),
        yawRate: num(o, 'yr', -2000, 2000),
        accelG: num(o, 'a', 0, 16),
      };
      for (const v of Object.values(fields)) if (typeof v === 'string') return fail(v);
      const f = fields as { [K in keyof typeof fields]: number };
      if (!Number.isInteger(f.seq)) return fail('"s" is not an integer');
      return { ok: true, message: { kind: 'imu', ...f } };
    }
    case 'status': {
      const state = str(o, 'state');
      if (!state || !DEVICE_STATES.includes(state as DeviceState)) return fail(`unknown status "${String(o.state)}"`);
      return { ok: true, message: { kind: 'status', state: state as DeviceState, message: str(o, 'msg') ?? '' } };
    }
    case 'hello': {
      const firmware = str(o, 'fw');
      const proto = num(o, 'proto', 1, 1000);
      const rateHz = num(o, 'rate', 1, 1000);
      if (!firmware) return fail('"fw" missing');
      if (typeof proto === 'string') return fail(proto);
      if (typeof rateHz === 'string') return fail(rateHz);
      return { ok: true, message: { kind: 'hello', firmware, proto, rateHz } };
    }
    case 'ack': {
      const command = str(o, 'cmd');
      if (!command) return fail('"cmd" missing');
      const error = str(o, 'err') ?? undefined;
      return { ok: true, message: { kind: 'ack', command, ...(error ? { error } : {}) } };
    }
    default:
      return fail(`unknown message type "${String(o.t)}"`);
  }
}

/** Serialize a sample exactly as the firmware does. Used by the simulator and tests. */
export function encodeImu(s: Omit<ImuSample, 'kind'>): string {
  return JSON.stringify({
    t: 'imu',
    s: s.seq,
    ms: s.deviceMs,
    p: +s.pitch.toFixed(2),
    r: +s.roll.toFixed(2),
    y: +s.yaw.toFixed(2),
    pr: +s.pitchRate.toFixed(1),
    rr: +s.rollRate.toFixed(1),
    yr: +s.yawRate.toFixed(1),
    a: +s.accelG.toFixed(3),
  });
}

/** Number of samples lost between two sequence numbers (handles uint32 wrap). */
export function seqGap(prev: number, next: number): number {
  const diff = (next - prev + 0x100000000) % 0x100000000;
  // A huge forward jump (or going backwards) means the device restarted, not loss.
  return diff === 0 || diff > 10000 ? 0 : diff - 1;
}
