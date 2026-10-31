import { sensorHub } from './sensorHub';
import { SimulatedTransport } from './transport/simulated';
import type { TransportKind } from './transport/types';
import { serialSupport, WebSerialTransport } from './transport/webSerial';

/**
 * Connection actions the UI calls. Remembers the last serial port so it can
 * reconnect without a prompt, and reconnects automatically if that port is
 * plugged back in after an unexpected loss (not after a user Disconnect).
 */
let lastPort: SerialPort | null = null;
let autoReconnect = false;

export async function connect(kind: TransportKind) {
  if (kind === 'simulated') {
    autoReconnect = false;
    return sensorHub.connect(new SimulatedTransport());
  }
  const support = serialSupport();
  if (!support.ok) throw new Error(support.reason);
  // requestPort must be the first await inside the click handler so the
  // browser still sees the user gesture.
  let port: SerialPort;
  try {
    port = await navigator.serial.requestPort();
  } catch (err) {
    if ((err as Error)?.name === 'NotFoundError') return; // user closed the chooser
    throw err;
  }
  lastPort = port;
  autoReconnect = true;
  await sensorHub.connect(new WebSerialTransport(port));
}

/** Reopen the last-used (already permitted) port without the chooser. */
export async function reconnect() {
  const port = lastPort ?? (await WebSerialTransport.grantedPorts())[0];
  if (!port) throw new Error('No previously permitted port. Use Connect to choose one.');
  lastPort = port;
  autoReconnect = true;
  await sensorHub.connect(new WebSerialTransport(port));
}

export async function disconnect() {
  autoReconnect = false;
  await sensorHub.disconnect();
}

export function canReconnect() {
  return lastPort !== null;
}

if (serialSupport().ok) {
  navigator.serial.addEventListener('connect', (e) => {
    const port = e.target as SerialPort;
    if (!autoReconnect || port !== lastPort || sensorHub.getStatus().link !== 'disconnected') return;
    // Give the board a moment to enumerate before opening.
    setTimeout(() => void sensorHub.connect(new WebSerialTransport(port)).catch(() => {}), 500);
  });
}
