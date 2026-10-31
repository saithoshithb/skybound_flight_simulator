import { controls } from '../control/controlLoop';
import { keyboardAxes, updateKeyboard } from '../input/keyboard';
import { getSettings, useSettings } from '../settings/settingsStore';
import { autoRudder, bankAssist } from './assists';
import { aircraftById } from './aircraft';
import { emptyInput, FlightModel, type PilotInput } from './dynamics';
import { terrainHeight } from '../world/terrain';
import { EasyFlight } from './easyFlight';
import { clamp, DEG } from './math';

/**
 * The live flight: one FlightModel plus the pilot input it flies with.
 * Mutable singletons, read every frame by the scene and HUD without React.
 */
export const flight = new FlightModel(aircraftById(getSettings().aircraftId), terrainHeight);
export const pilot: PilotInput = emptyInput();
export const easy = new EasyFlight();
easy.configure(flight.ac);

export type ControlSource = 'motion' | 'keyboard';
export const pilotInfo = {
  source: 'keyboard' as ControlSource,
  message: '',
  messageUntil: 0,
  /** Easy flight targets, for the HUD. */
  targetVsFpm: 0,
  targetBankDeg: 0,
};

const THROTTLE_RATE = 0.5; // full range in 2 s
const TRIM_RATE = 0.25;

/**
 * Mix motion-controller and keyboard commands. They add, so the keyboard can
 * supply rudder (or correct) while the controller flies pitch and roll; the
 * controller's own engagement ramp already fades it in and out smoothly.
 *
 * With Easy flight on, those same commands mean "bank this much" and "climb or
 * descend this fast", and the throttle keys set a target speed.
 */
export function updatePilot(dt: number) {
  updateKeyboard(dt);
  const k = keyboardAxes;
  const settings = getSettings();
  const pitchCmd = clamp(controls.elevator + k.elevator, -1, 1);
  const rollCmd = clamp(controls.aileron + k.aileron, -1, 1);
  pilot.brake = k.brake;
  pilotInfo.source = controls.engagement > 0.5 ? 'motion' : 'keyboard';

  if (settings.easyFlight) {
    const onGround = flight.telemetry.phase !== 'airborne';
    const before = easy.speedTargetKt;
    easy.adjustSpeed(k.throttleDir, dt, onGround);
    if (before === 0 && easy.speedTargetKt > 0) flash(`Taking off · it lifts off and climbs to 400 ft by itself · tilt sideways to steer`, 4500);
    const out = easy.update(flight.telemetry, { pitch: pitchCmd, roll: rollCmd }, dt, flight.alphaWarn / DEG);
    pilot.elevator = out.elevator;
    pilot.aileron = out.aileron;
    // Q/E still steer the nose wheel on the ground and add to the auto-rudder in the air.
    pilot.rudder = clamp(controls.rudder + k.rudder + out.rudder, -1, 1);
    pilot.throttle = out.throttle;
    pilot.trim = 0;
    pilotInfo.targetVsFpm = out.targetVsFpm;
    pilotInfo.targetBankDeg = out.targetBankDeg;
    return;
  }

  pilot.elevator = pitchCmd;
  pilot.aileron = settings.bankAssist ? bankAssist(rollCmd, flight.telemetry) : rollCmd;
  const rudderAssist = settings.autoRudder ? autoRudder(flight.telemetry, pilot.aileron) : 0;
  pilot.rudder = clamp(controls.rudder + k.rudder + rudderAssist, -1, 1);
  pilot.throttle = clamp(pilot.throttle + k.throttleDir * THROTTLE_RATE * dt, 0, 1);
  pilot.trim = clamp(pilot.trim + k.trimDir * TRIM_RATE * dt, -1, 1);
}

export function flash(message: string, ms = 2500) {
  pilotInfo.message = message;
  pilotInfo.messageUntil = performance.now() + ms;
}

export function resetOnRunway() {
  flight.resetOnRunway();
  Object.assign(pilot, emptyInput());
  easy.reset(false);
  flash(getSettings().easyFlight ? 'On runway 36 · press + (or PgUp) once to take off' : 'On runway 36 · add power with + or PgUp');
}

export function resetAirborne() {
  const { trim, throttle } = flight.resetAirborne();
  Object.assign(pilot, emptyInput(), { trim, throttle });
  easy.reset(true, trim, throttle);
  flash('Long final, runway 36 · 1,000 ft · trimmed');
}

// Switching modes mid-flight hands over without a jump: each side starts from
// what the aircraft is doing now.
useSettings.subscribe((s, prev) => {
  if (s.aircraftId !== prev.aircraftId) {
    const ac = aircraftById(s.aircraftId);
    flight.setAircraft(ac);
    easy.configure(ac);
    easy.reset(false);
    Object.assign(pilot, emptyInput());
    return;
  }
  if (s.easyFlight === prev.easyFlight) return;
  const airborne = flight.telemetry.phase === 'airborne';
  if (s.easyFlight) {
    easy.reset(airborne, pilot.elevator + pilot.trim, pilot.throttle);
    if (airborne) easy.speedTargetKt = clamp(Math.round(flight.telemetry.airspeedKt / 5) * 5, easy.speeds.minKt, easy.speeds.maxKt);
  } else {
    pilot.trim = clamp(pilot.elevator, -1, 1);
  }
});
