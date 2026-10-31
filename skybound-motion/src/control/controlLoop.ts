import { getSettings } from '../settings/settingsStore';
import { sensorHub } from '../sensor/sensorHub';
import { ControlPipeline, type ControlCommand } from './mapping';

/**
 * Single per-frame control update shared by every consumer (calibration
 * screen now, flight model next). Runs on requestAnimationFrame, reads the
 * hub without touching React, and publishes into a mutable object.
 */
export const pipeline = new ControlPipeline();
export const controls: ControlCommand = { elevator: 0, aileron: 0, rudder: 0, engagement: 0 };

let raf = 0;
let last = 0;

function frame(t: number) {
  const dt = last ? (t - last) / 1000 : 0;
  last = t;
  const s = getSettings();
  const latest = sensorHub.isUsable() ? sensorHub.latest() : null;
  Object.assign(controls, pipeline.update(latest?.sample ?? null, s.neutral, s.control, dt));
  raf = requestAnimationFrame(frame);
}

export function startControlLoop() {
  if (!raf) raf = requestAnimationFrame(frame);
  return () => {
    cancelAnimationFrame(raf);
    raf = 0;
    last = 0;
  };
}

// Any reconnect starts from zero authority so the first samples can't jerk the aircraft.
sensorHub.subscribe((st) => {
  if (st.link === 'connecting' || st.link === 'awaiting') pipeline.reengage();
});
