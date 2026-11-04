import { parseLine, seqGap, type DeviceState, type HostCommand, type ImuSample } from './protocol';
import type { SensorTransport } from './transport/types';

/**
 * Link state, from the browser's point of view.
 *  - disconnected: no transport open
 *  - connecting:   opening the port / waiting for the user to pick one
 *  - awaiting:     port open, no valid attitude yet (device booting or calibrating)
 *  - live:         valid samples arriving on time
 *  - stale:        samples late (> STALE_MS); controls fade to neutral
 *  - lost:         no samples for LOST_MS while the port is still open
 */
export type LinkState = 'disconnected' | 'connecting' | 'awaiting' | 'live' | 'stale' | 'lost';

export const STALE_MS = 250;
export const LOST_MS = 2000;
/** Port open but not a single line received: probably wrong port/baud or no firmware. */
export const SILENT_PORT_MS = 4000;

/** Coarse status, changes a few times a second at most. Safe to put in React state. */
export interface HubStatus {
  link: LinkState;
  device: DeviceState | 'unknown';
  deviceMessage: string;
  transport: SensorTransport['kind'] | null;
  transportLabel: string;
  firmware: string | null;
  lastError: string | null;
}

/** Samples can drive controls. A fallback calibration (cal_failed) still streams usable data, with a warning. */
export function isUsableStatus(s: HubStatus): boolean {
  return s.link === 'live' && (s.device === 'ready' || s.device === 'cal_failed');
}

/** Diagnostics counters, read by polling. */
export interface HubStats {
  rxHz: number;
  lastSampleAgeMs: number;
  samples: number;
  droppedSeq: number;
  malformed: number;
  lastMalformedReason: string;
  restarts: number;
}

export interface TimedSample {
  sample: ImuSample;
  /** performance.now() when the line arrived. */
  receivedAt: number;
}

type Clock = () => number;
type SampleListener = (s: TimedSample) => void;
type StatusListener = (s: HubStatus) => void;

const LOG_LINES = 40;

/**
 * Owns the transport and is the single source of truth for sensor data.
 * High-rate data never goes through React: consumers either call `latest()`
 * from a render loop or register an `onSample` listener.
 */
export class SensorHub {
  private transport: SensorTransport | null = null;
  private status: HubStatus = {
    link: 'disconnected',
    device: 'unknown',
    deviceMessage: '',
    transport: null,
    transportLabel: '',
    firmware: null,
    lastError: null,
  };
  private stats: HubStats = this.freshStats();
  private last: TimedSample | null = null;
  private lastSeq: number | null = null;
  private rateWindow: number[] = [];
  private log: string[] = [];
  private sampleListeners = new Set<SampleListener>();
  private statusListeners = new Set<StatusListener>();
  private watchdog: ReturnType<typeof setInterval> | null = null;
  private generation = 0;
  private openedAt = 0;

  constructor(private readonly now: Clock = () => performance.now()) {}

  // ----- public API -----

  getStatus(): HubStatus {
    return this.status;
  }

  getStats(): HubStats {
    const t = this.now();
    while (this.rateWindow.length && t - this.rateWindow[0] > 1000) this.rateWindow.shift();
    return {
      ...this.stats,
      rxHz: this.rateWindow.length,
      lastSampleAgeMs: this.last ? t - this.last.receivedAt : Infinity,
    };
  }

  /** Most recent valid sample, or null. Only meaningful while link is live. */
  latest(): TimedSample | null {
    return this.last;
  }

  /** True when the newest sample can drive controls. */
  isUsable(): boolean {
    return isUsableStatus(this.status);
  }

  recentLines(): readonly string[] {
    return this.log;
  }

  onSample(fn: SampleListener) {
    this.sampleListeners.add(fn);
    return () => void this.sampleListeners.delete(fn);
  }

  subscribe(fn: StatusListener) {
    this.statusListeners.add(fn);
    return () => void this.statusListeners.delete(fn);
  }

  async connect(transport: SensorTransport) {
    await this.disconnect();
    const gen = ++this.generation;
    this.transport = transport;
    this.stats = this.freshStats();
    this.last = null;
    this.lastSeq = null;
    this.rateWindow = [];
    this.log = [];
    this.setStatus({
      link: 'connecting',
      device: 'unknown',
      deviceMessage: '',
      transport: transport.kind,
      transportLabel: transport.label,
      firmware: null,
      lastError: null,
    });
    try {
      await transport.open({
        onLine: (line) => gen === this.generation && this.handleLine(line),
        onClose: (reason) => gen === this.generation && this.handleClose(reason),
      });
    } catch (err) {
      if (gen !== this.generation) return;
      this.transport = null;
      const e = err as Error;
      // Dismissing the port chooser is not an error worth shouting about.
      const cancelled = e?.name === 'NotFoundError';
      this.setStatus({ link: 'disconnected', transport: null, lastError: cancelled ? null : e?.message ?? String(err) });
      if (!cancelled) throw err;
      return;
    }
    if (gen !== this.generation) return;
    this.openedAt = this.now();
    this.setStatus({ link: 'awaiting', transportLabel: transport.label });
    this.watchdog = setInterval(() => this.checkTimeouts(), 50);
  }

  async disconnect() {
    const t = this.transport;
    this.generation++;
    this.transport = null;
    this.stopWatchdog();
    if (t) await t.close().catch(() => {});
    this.last = null;
    if (this.status.link !== 'disconnected')
      this.setStatus({ link: 'disconnected', device: 'unknown', deviceMessage: '', transport: null });
  }

  async send(cmd: HostCommand) {
    if (!this.transport) throw new Error('Not connected');
    await this.transport.send(cmd);
  }

  /** The active transport, for transport-specific UI (e.g. simulator fault buttons). */
  activeTransport(): SensorTransport | null {
    return this.transport;
  }

  // ----- internals -----

  private freshStats(): HubStats {
    return { rxHz: 0, lastSampleAgeMs: Infinity, samples: 0, droppedSeq: 0, malformed: 0, lastMalformedReason: '', restarts: 0 };
  }

  private handleLine(line: string) {
    this.log.push(line);
    if (this.log.length > LOG_LINES) this.log.shift();

    const result = parseLine(line);
    if (!result.ok) {
      if (result.reason !== 'empty line') {
        this.stats.malformed++;
        this.stats.lastMalformedReason = result.reason;
      }
      return;
    }
    const msg = result.message;
    switch (msg.kind) {
      case 'hello':
        // A hello mid-stream means the board rebooted (e.g. DTR reset or brown-out).
        if (this.status.firmware) this.stats.restarts++;
        this.lastSeq = null;
        this.setStatus({ firmware: msg.firmware });
        if (msg.proto !== 1) this.setStatus({ lastError: `Firmware speaks protocol v${msg.proto}; this app expects v1.` });
        return;
      case 'status':
        this.setStatus({ device: msg.state, deviceMessage: msg.message });
        if (msg.state !== 'ready') {
          // Never keep driving controls from pre-calibration data.
          this.last = null;
          if (this.status.link === 'live' || this.status.link === 'stale') this.setStatus({ link: 'awaiting' });
        }
        return;
      case 'ack':
        if (msg.error) this.setStatus({ lastError: `Device rejected ${msg.command}: ${msg.error}` });
        return;
      case 'imu': {
        const t = this.now();
        if (this.lastSeq !== null) this.stats.droppedSeq += seqGap(this.lastSeq, msg.seq);
        this.lastSeq = msg.seq;
        this.stats.samples++;
        this.rateWindow.push(t);
        // Older firmware may not send status; treat streaming attitude as ready.
        if (this.status.device === 'unknown') this.setStatus({ device: 'ready' });
        if (this.status.device !== 'ready' && this.status.device !== 'cal_failed') return;
        this.last = { sample: msg, receivedAt: t };
        if (this.status.link !== 'live') this.setStatus({ link: 'live' });
        for (const fn of this.sampleListeners) fn(this.last);
        return;
      }
    }
  }

  private handleClose(reason: string | null) {
    this.transport = null;
    this.stopWatchdog();
    this.last = null;
    this.setStatus({ link: 'disconnected', device: 'unknown', transport: null, lastError: reason });
  }

  private checkTimeouts() {
    const link = this.status.link;
    if (link === 'awaiting' && this.log.length === 0 && this.now() - this.openedAt > SILENT_PORT_MS && !this.status.lastError) {
      this.setStatus({
        lastError: `No data received after ${SILENT_PORT_MS / 1000} s. Check that the firmware is flashed, the baud rate is 115200, and you picked the ESP32's port.`,
      });
      return;
    }
    if (link !== 'live' && link !== 'stale' && link !== 'lost') return;
    const age = this.last ? this.now() - this.last.receivedAt : Infinity;
    const next: LinkState = age > LOST_MS ? 'lost' : age > STALE_MS ? 'stale' : 'live';
    if (next !== link) this.setStatus({ link: next });
  }

  private stopWatchdog() {
    if (this.watchdog) clearInterval(this.watchdog);
    this.watchdog = null;
  }

  private setStatus(patch: Partial<HubStatus>) {
    const next = { ...this.status, ...patch };
    const changed = (Object.keys(next) as (keyof HubStatus)[]).some((k) => next[k] !== this.status[k]);
    if (!changed) return;
    this.status = next;
    for (const fn of this.statusListeners) fn(next);
  }
}

/** App-wide instance. */
export const sensorHub = new SensorHub();
