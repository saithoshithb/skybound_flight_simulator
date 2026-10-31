import { encodeImu } from '../protocol';
import type { SensorTransport, TransportEvents } from './types';

export type SimMotion = 'wave' | 'manual' | 'still';

/** Live-tunable simulator parameters. Mutated directly by the UI. */
export interface SimParams {
  motion: SimMotion;
  /** Manual target attitude in degrees, aircraft convention. */
  target: { pitch: number; roll: number; yaw: number };
  /** Gaussian noise on angles, degrees (1σ). */
  noiseDeg: number;
  /** Yaw gyro drift, deg/s, as a real MPU-6050 shows without a magnetometer. */
  yawDriftDps: number;
  /** Fraction of samples silently dropped (0–1). */
  dropRate: number;
  /** Fraction of lines corrupted (0–1). */
  malformedRate: number;
  /** Simulated mounting offset: the "desk" isn't level. */
  biasDeg: { pitch: number; roll: number };
}

export const simParams: SimParams = {
  motion: 'wave',
  target: { pitch: 0, roll: 0, yaw: 0 },
  noiseDeg: 0.25,
  yawDriftDps: 0.3,
  dropRate: 0,
  malformedRate: 0,
  biasDeg: { pitch: 2.5, roll: -1.5 },
};

const RATE_HZ = 50;
const CAL_MS = 1500;

function gauss() {
  return Math.sqrt(-2 * Math.log(1 - Math.random())) * Math.cos(2 * Math.PI * Math.random());
}

const wrap180 = (d: number) => ((((d + 180) % 360) + 360) % 360) - 180;

/**
 * Stands in for the ESP32 when no hardware is attached. It speaks the exact
 * line protocol (including hello/status/ack, calibration delay and command
 * handling) so the parser, hub and calibration code run unmodified.
 */
export class SimulatedTransport implements SensorTransport {
  readonly kind = 'simulated' as const;
  readonly label = 'Simulated controller';
  private events: TransportEvents | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private calTimer: ReturnType<typeof setTimeout> | null = null;
  private seq = 0;
  private startMs = 0;
  private calibrating = true;
  private att = { pitch: 0, roll: 0, yaw: 0 };
  private prev = { pitch: 0, roll: 0, yaw: 0 };
  private yawDrift = 0;

  async open(events: TransportEvents) {
    this.events = events;
    this.startMs = performance.now();
    this.emit(JSON.stringify({ t: 'hello', fw: 'simulator/1.0.0', proto: 1, rate: RATE_HZ }));
    this.beginCalibration();
    this.timer = setInterval(() => this.tick(), 1000 / RATE_HZ);
  }

  async close() {
    this.stop();
    const events = this.events;
    this.events = null;
    events?.onClose(null);
  }

  /** Fault injection: behave like a yanked USB cable. */
  unplug() {
    this.stop();
    const events = this.events;
    this.events = null;
    events?.onClose('Simulated cable unplugged');
  }

  async send(line: string) {
    const cmd = line.trim();
    if (cmd === 'CAL') {
      this.emit('{"t":"ack","cmd":"CAL"}');
      this.beginCalibration();
    } else if (cmd === 'ZEROYAW') {
      this.yawDrift = 0;
      simParams.target.yaw = 0;
      this.emit('{"t":"ack","cmd":"ZEROYAW"}');
    } else if (cmd === 'HELLO') {
      this.emit(JSON.stringify({ t: 'hello', fw: 'simulator/1.0.0', proto: 1, rate: RATE_HZ }));
    } else {
      this.emit('{"t":"ack","cmd":"?","err":"unknown command"}');
    }
  }

  private beginCalibration() {
    this.calibrating = true;
    this.emit('{"t":"status","state":"calibrating","msg":"hold the controller still"}');
    if (this.calTimer) clearTimeout(this.calTimer);
    this.calTimer = setTimeout(() => {
      this.calibrating = false;
      this.yawDrift = 0;
      this.emit('{"t":"status","state":"ready","msg":"gyro bias calibrated"}');
    }, CAL_MS);
  }

  private stop() {
    if (this.timer) clearInterval(this.timer);
    if (this.calTimer) clearTimeout(this.calTimer);
    this.timer = this.calTimer = null;
  }

  private emit(line: string) {
    this.events?.onLine(line);
  }

  private tick() {
    // Like the firmware, no attitude is streamed while calibrating.
    if (this.calibrating) return;
    const p = simParams;
    const dt = 1 / RATE_HZ;
    const tSec = (performance.now() - this.startMs) / 1000;
    let target = p.target;
    if (p.motion === 'wave')
      target = { pitch: 18 * Math.sin(tSec * 0.7), roll: 28 * Math.sin(tSec * 0.45 + 1), yaw: 0 };
    else if (p.motion === 'still') target = { pitch: 0, roll: 0, yaw: 0 };

    // A hand doesn't teleport: ease toward the target like a real wrist.
    const k = 1 - Math.exp(-dt * 8);
    this.att.pitch += (target.pitch - this.att.pitch) * k;
    this.att.roll += (target.roll - this.att.roll) * k;
    this.att.yaw += wrap180(target.yaw - this.att.yaw) * k;
    this.yawDrift += p.yawDriftDps * dt;

    const pitch = Math.max(-90, Math.min(90, this.att.pitch + p.biasDeg.pitch + gauss() * p.noiseDeg));
    const roll = wrap180(this.att.roll + p.biasDeg.roll + gauss() * p.noiseDeg);
    const yaw = wrap180(this.att.yaw + this.yawDrift + gauss() * p.noiseDeg * 0.5);
    const rates = {
      pitchRate: (pitch - this.prev.pitch) / dt,
      rollRate: wrap180(roll - this.prev.roll) / dt,
      yawRate: wrap180(yaw - this.prev.yaw) / dt,
    };
    this.prev = { pitch, roll, yaw };

    const seq = this.seq++ >>> 0;
    if (Math.random() < p.dropRate) return;
    let line = encodeImu({
      seq,
      deviceMs: Math.round(tSec * 1000),
      pitch,
      roll,
      yaw,
      ...rates,
      accelG: 1 + gauss() * 0.01,
    });
    if (Math.random() < p.malformedRate) line = line.slice(0, Math.floor(Math.random() * line.length));
    this.emit(line);
  }
}
