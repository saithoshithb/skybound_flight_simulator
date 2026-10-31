// A transport moves text lines between the browser and a controller.
// It knows nothing about the protocol; SensorHub parses what it delivers.
// Adding Wi-Fi/WebSocket or BLE later means adding another implementation.

export type TransportKind = 'serial' | 'simulated';

export interface TransportEvents {
  onLine(line: string): void;
  /** Called once when the link closes. `reason` is null for a user-initiated close. */
  onClose(reason: string | null): void;
}

export interface SensorTransport {
  readonly kind: TransportKind;
  readonly label: string;
  /** Opens the link. May prompt the user (serial) and may throw. */
  open(events: TransportEvents): Promise<void>;
  close(): Promise<void>;
  /** Sends one command line; the transport appends the newline. */
  send(line: string): Promise<void>;
}
