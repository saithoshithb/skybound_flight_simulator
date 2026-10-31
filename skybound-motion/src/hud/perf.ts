/**
 * Frame-rate monitor. Fed once per rendered frame; the FPS counter reads it,
 * and a one-time hint suggests a lower graphics tier when the frame rate stays
 * low. Ignores frames while the tab is hidden (browsers throttle those).
 */
export const perf = {
  fps: 0,
  /** Seconds the frame rate has been continuously below the threshold. */
  lowFor: 0,
  hinted: false,
};

const LOW_FPS = 32;
const LOW_FOR_S = 6;

export function perfFrame(dt: number, onLow: () => void) {
  if (dt <= 0 || dt > 0.5 || document.hidden) return;
  const fps = 1 / dt;
  perf.fps = perf.fps === 0 ? fps : perf.fps + (fps - perf.fps) * Math.min(1, dt * 2);
  perf.lowFor = perf.fps < LOW_FPS ? perf.lowFor + dt : 0;
  if (!perf.hinted && perf.lowFor > LOW_FOR_S) {
    perf.hinted = true;
    onLow();
  }
}
