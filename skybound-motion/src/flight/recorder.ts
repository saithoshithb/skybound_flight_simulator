import { controls } from '../control/controlLoop';
import { keyboardAxes } from '../input/keyboard';
import { sensorHub } from '../sensor/sensorHub';
import { getSettings } from '../settings/settingsStore';
import { flight, pilot, pilotInfo } from './session';

/**
 * Flight data recorder for diagnosing control problems on real hardware.
 * Keeps the last 60 s at 5 Hz: what the sensor reported, what the control
 * pipeline commanded, what Easy flight targeted, and what the aircraft did.
 * Exported as compact CSV the player can paste into a bug report.
 */
const RATE_HZ = 5;
const KEEP = 60 * RATE_HZ;

const COLUMNS = [
  't', // s since recording started
  'link', // sensor link state
  'ageMs', // age of the newest sensor sample
  'rxHz',
  'sPitch', // raw sensor pitch / roll (deg)
  'sRoll',
  'nPitch', // saved neutral
  'nRoll',
  'cPitch', // shaped controller command (−1…1)
  'cRoll',
  'engage', // controller authority 0…1
  'kRoll', // keyboard roll axis
  'tBank', // Easy-flight bank target (deg)
  'bank', // actual bank (deg)
  'hdg',
  'vs',
  'ias',
  'ail', // aileron command sent to the flight model
  'rud',
  'phase',
] as const;

const rows: string[] = [];
let clock = 0;
let elapsed = 0;

const f1 = (v: number) => (Number.isFinite(v) ? v.toFixed(1) : '');
const f2 = (v: number) => (Number.isFinite(v) ? v.toFixed(2) : '');

export function recordFrame(dt: number) {
  elapsed += dt;
  clock += dt;
  if (clock < 1 / RATE_HZ) return;
  clock = 0;
  const latest = sensorHub.latest();
  const status = sensorHub.getStatus();
  const stats = sensorHub.getStats();
  const { neutral } = getSettings();
  const t = flight.telemetry;
  rows.push(
    [
      f1(elapsed),
      status.link,
      Number.isFinite(stats.lastSampleAgeMs) ? Math.round(stats.lastSampleAgeMs) : '',
      stats.rxHz,
      f1(latest?.sample.pitch ?? NaN),
      f1(latest?.sample.roll ?? NaN),
      f1(neutral.pitch),
      f1(neutral.roll),
      f2(controls.elevator),
      f2(controls.aileron),
      f2(controls.engagement),
      f2(keyboardAxes.aileron),
      f1(pilotInfo.targetBankDeg),
      f1(t.rollDeg),
      Math.round(t.headingDeg),
      Math.round(t.verticalSpeedFpm),
      Math.round(t.airspeedKt),
      f2(pilot.aileron),
      f2(pilot.rudder),
      t.phase,
    ].join(','),
  );
  if (rows.length > KEEP) rows.shift();
}

export function flightLogCsv() {
  const s = getSettings();
  const header = `# skybound flight log · ${new Date().toISOString()} · easy=${s.easyFlight} · deadzone=${s.control.deadzone} expo=${s.control.expo} smooth=${s.control.smoothingMs}ms rollMax=${s.control.roll.maxAngleDeg} invertRoll=${s.control.roll.invert}`;
  return [header, COLUMNS.join(','), ...rows].join('\n');
}

export function clearFlightLog() {
  rows.length = 0;
  elapsed = 0;
}
