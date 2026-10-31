import { LineFramer } from '../lineFramer';
import type { SensorTransport, TransportEvents } from './types';

export const SERIAL_BAUD = 115200;

export type SerialSupport = { ok: true } | { ok: false; reason: string };

export function serialSupport(): SerialSupport {
  if (typeof window === 'undefined') return { ok: false, reason: 'Not running in a browser.' };
  if (!window.isSecureContext)
    return { ok: false, reason: 'Web Serial needs a secure context: open the app on http://localhost or HTTPS.' };
  if (!('serial' in navigator))
    return {
      ok: false,
      reason: 'This browser has no Web Serial API. Use desktop Chrome, Edge, or Opera (version 89 or newer).',
    };
  return { ok: true };
}

/**
 * USB serial via the Web Serial API.
 *
 * Opening the port toggles DTR/RTS, which resets most ESP32 dev boards. The
 * firmware then re-runs gyro calibration, so SensorHub waits for a "ready"
 * status before treating data as usable.
 */
export class WebSerialTransport implements SensorTransport {
  readonly kind = 'serial' as const;
  label = 'USB serial';
  private port: SerialPort | null = null;
  private reader: ReadableStreamDefaultReader<Uint8Array> | null = null;
  private events: TransportEvents | null = null;
  private closing = false;
  private readonly onDisconnect = (e: Event) => {
    if ((e.target as SerialPort | null) === this.port || (e as Event & { port?: SerialPort }).port === this.port)
      this.finish('USB device was unplugged');
  };

  /** @param port reuse an already-granted port (reconnect) instead of prompting. */
  constructor(private readonly preselected?: SerialPort) {}

  /** Ports this origin was granted earlier; reconnecting to them needs no prompt. */
  static async grantedPorts(): Promise<SerialPort[]> {
    return serialSupport().ok ? navigator.serial.getPorts() : [];
  }

  get serialPort() {
    return this.port;
  }

  async open(events: TransportEvents) {
    const support = serialSupport();
    if (!support.ok) throw new Error(support.reason);
    // requestPort() must run inside a user gesture (a click). It throws
    // NotFoundError if the user dismisses the chooser.
    const port = this.preselected ?? (await navigator.serial.requestPort());
    try {
      await port.open({ baudRate: SERIAL_BAUD, bufferSize: 4096 });
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      throw new Error(
        /already open|Failed to open/i.test(msg)
          ? `Could not open the port. Close Arduino Serial Monitor or any other app using it. (${msg})`
          : msg,
      );
    }
    this.port = port;
    this.events = events;
    this.closing = false;
    const info = port.getInfo();
    if (info.usbVendorId !== undefined)
      this.label = `USB serial ${info.usbVendorId.toString(16).padStart(4, '0')}:${(info.usbProductId ?? 0).toString(16).padStart(4, '0')}`;
    navigator.serial.addEventListener('disconnect', this.onDisconnect);
    void this.readLoop(port);
  }

  private async readLoop(port: SerialPort) {
    const framer = new LineFramer();
    const decoder = new TextDecoder();
    // Non-fatal errors (framing/parity/buffer overrun) leave `readable` usable
    // again, so keep reading until it is null or we are closing.
    while (port.readable && !this.closing) {
      this.reader = port.readable.getReader();
      try {
        for (;;) {
          const { value, done } = await this.reader.read();
          if (done) break;
          if (value) for (const line of framer.push(decoder.decode(value, { stream: true }))) this.events?.onLine(line);
        }
      } catch (err) {
        if (this.closing) break;
        const name = err instanceof DOMException ? err.name : '';
        if (name === 'NetworkError' || name === 'NotFoundError') {
          this.finish('Serial device lost');
          return;
        }
        console.warn('Serial read error, continuing', err);
        framer.reset();
      } finally {
        this.reader?.releaseLock();
        this.reader = null;
      }
    }
    if (!this.closing) this.finish('Serial stream ended');
  }

  async send(line: string) {
    const writable = this.port?.writable;
    if (!writable) throw new Error('Serial port is not open');
    const writer = writable.getWriter();
    try {
      await writer.write(new TextEncoder().encode(line + '\n'));
    } finally {
      writer.releaseLock();
    }
  }

  async close() {
    if (!this.port) return;
    this.closing = true;
    const port = this.port;
    try {
      await this.reader?.cancel();
    } catch {
      /* already closed */
    }
    try {
      await port.close();
    } catch {
      /* unplugged ports throw on close */
    }
    this.finish(null);
  }

  private finish(reason: string | null) {
    if (!this.port) return;
    this.closing = true;
    navigator.serial.removeEventListener('disconnect', this.onDisconnect);
    const port = this.port;
    this.port = null;
    if (reason) void port.close().catch(() => {});
    const events = this.events;
    this.events = null;
    events?.onClose(reason);
  }
}
