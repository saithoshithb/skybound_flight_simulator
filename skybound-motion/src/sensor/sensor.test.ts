import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LineFramer } from './lineFramer';
import { encodeImu, parseLine, seqGap, type ImuSample } from './protocol';
import { SensorHub } from './sensorHub';
import type { SensorTransport, TransportEvents } from './transport/types';

// The exact line format the firmware's sendImu() printf produces.
const FIRMWARE_LINE =
  '{"t":"imu","s":42,"ms":12345,"p":-3.25,"r":10.50,"y":-170.00,"pr":1.5,"rr":-2.0,"yr":0.3,"a":1.002}';

describe('parseLine', () => {
  it('parses a firmware imu line', () => {
    const r = parseLine(FIRMWARE_LINE);
    expect(r).toEqual({
      ok: true,
      message: { kind: 'imu', seq: 42, deviceMs: 12345, pitch: -3.25, roll: 10.5, yaw: -170, pitchRate: 1.5, rollRate: -2, yawRate: 0.3, accelG: 1.002 },
    });
  });

  it('parses status, hello and ack', () => {
    expect(parseLine('{"t":"status","state":"ready","msg":"ok"}')).toMatchObject({ ok: true, message: { kind: 'status', state: 'ready' } });
    expect(parseLine('{"t":"hello","fw":"mpu-flight/1.0.0","proto":1,"rate":50}')).toMatchObject({
      ok: true,
      message: { kind: 'hello', firmware: 'mpu-flight/1.0.0', rateHz: 50 },
    });
    expect(parseLine('{"t":"ack","cmd":"?","err":"unknown command"}')).toMatchObject({ ok: true, message: { error: 'unknown command' } });
  });

  it.each([
    ['', 'empty line'],
    ['garbage', 'not a JSON object'],
    ['{"t":"imu","s":1', 'invalid JSON'],
    ['[1,2]', 'not a JSON object'],
    ['{"t":"nope"}', 'unknown message type'],
    ['{"t":"status","state":"flying"}', 'unknown status'],
  ])('rejects %j', (line, reason) => {
    const r = parseLine(line);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toContain(reason);
  });

  it('rejects out-of-range and non-numeric fields', () => {
    expect(parseLine(FIRMWARE_LINE.replace('"p":-3.25', '"p":95')).ok).toBe(false);
    expect(parseLine(FIRMWARE_LINE.replace('"a":1.002', '"a":"1"')).ok).toBe(false);
    expect(parseLine(FIRMWARE_LINE.replace('"s":42', '"s":4.2')).ok).toBe(false);
    expect(parseLine(FIRMWARE_LINE.replace(',"yr":0.3', '')).ok).toBe(false);
  });

  it('round-trips encodeImu', () => {
    const s: Omit<ImuSample, 'kind'> = { seq: 7, deviceMs: 1, pitch: 1.234, roll: -5, yaw: 0, pitchRate: 0, rollRate: 0, yawRate: 0, accelG: 1 };
    expect(parseLine(encodeImu(s))).toMatchObject({ ok: true, message: { seq: 7, pitch: 1.23 } });
  });
});

describe('seqGap', () => {
  it('counts losses and handles wrap and restarts', () => {
    expect(seqGap(1, 2)).toBe(0);
    expect(seqGap(1, 5)).toBe(3);
    expect(seqGap(0xffffffff, 0)).toBe(0);
    expect(seqGap(0xfffffffe, 1)).toBe(2);
    expect(seqGap(5000, 3)).toBe(0); // device rebooted
  });
});

describe('LineFramer', () => {
  it('reassembles lines split across chunks', () => {
    const f = new LineFramer();
    expect(f.push('{"a":1}\n{"b"')).toEqual(['{"a":1}']);
    expect(f.push(':2}\r\n\n')).toEqual(['{"b":2}', '']);
  });

  it('discards overlong lines without growing', () => {
    const f = new LineFramer();
    expect(f.push('x'.repeat(600))).toEqual([]);
    expect(f.push('y'.repeat(600))).toEqual([]);
    expect(f.push('tail\nok\n')).toEqual(['ok']);
    expect(f.overflowCount).toBe(1);
  });
});

class FakeTransport implements SensorTransport {
  readonly kind = 'simulated' as const;
  readonly label = 'fake';
  events: TransportEvents | null = null;
  sent: string[] = [];
  async open(e: TransportEvents) {
    this.events = e;
  }
  async close() {
    this.events?.onClose(null);
  }
  async send(l: string) {
    this.sent.push(l);
  }
  line(l: string) {
    this.events?.onLine(l);
  }
}

const imu = (seq: number, pitch = 0) =>
  encodeImu({ seq, deviceMs: seq * 20, pitch, roll: 0, yaw: 0, pitchRate: 0, rollRate: 0, yawRate: 0, accelG: 1 });

describe('SensorHub', () => {
  let now = 0;
  let hub: SensorHub;
  let t: FakeTransport;
  beforeEach(async () => {
    vi.useFakeTimers();
    now = 0;
    hub = new SensorHub(() => now);
    t = new FakeTransport();
    await hub.connect(t);
  });
  afterEach(() => vi.useRealTimers());

  const advance = (ms: number) => {
    now += ms;
    vi.advanceTimersByTime(ms);
  };

  it('waits for calibration before going live', () => {
    expect(hub.getStatus().link).toBe('awaiting');
    t.line('{"t":"status","state":"calibrating","msg":""}');
    t.line(imu(0));
    expect(hub.latest()).toBeNull();
    t.line('{"t":"status","state":"ready","msg":""}');
    t.line(imu(1, 5));
    expect(hub.getStatus().link).toBe('live');
    expect(hub.isUsable()).toBe(true);
    expect(hub.latest()?.sample.pitch).toBe(5);
  });

  it('goes stale then lost when samples stop, and recovers', () => {
    t.line('{"t":"status","state":"ready","msg":""}');
    t.line(imu(0));
    advance(300);
    expect(hub.getStatus().link).toBe('stale');
    expect(hub.isUsable()).toBe(false);
    advance(2000);
    expect(hub.getStatus().link).toBe('lost');
    t.line(imu(1));
    expect(hub.getStatus().link).toBe('live');
  });

  it('counts malformed lines and sequence gaps', () => {
    t.line('{"t":"status","state":"ready","msg":""}');
    t.line(imu(0));
    t.line('{"t":"imu","s":');
    t.line(imu(4));
    const st = hub.getStats();
    expect(st.malformed).toBe(1);
    expect(st.droppedSeq).toBe(3);
  });

  it('drops to disconnected when the transport dies', () => {
    t.events?.onClose('USB device was unplugged');
    expect(hub.getStatus()).toMatchObject({ link: 'disconnected', lastError: 'USB device was unplugged' });
    expect(hub.latest()).toBeNull();
  });

  it('flags a silent port', () => {
    advance(4100);
    expect(hub.getStatus().lastError).toMatch(/No data received/);
  });

  it('stops using data when the device recalibrates', () => {
    t.line('{"t":"status","state":"ready","msg":""}');
    t.line(imu(0));
    t.line('{"t":"status","state":"calibrating","msg":""}');
    expect(hub.isUsable()).toBe(false);
    expect(hub.getStatus().link).toBe('awaiting');
  });
});
