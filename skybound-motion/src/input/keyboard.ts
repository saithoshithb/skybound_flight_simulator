/**
 * Keyboard fallback controls. Axes ease toward the key target instead of
 * snapping, so a tap gives a small, smooth deflection like a real stick.
 *
 *   ↑ / W   push (nose down)        ↓ / S   pull (nose up)
 *   ← / A   roll left               → / D   roll right
 *   Q / E   rudder left / right     B       brakes (hold)
 *   + / PgUp  throttle up           − / PgDn  throttle down
 *   [ / ]   trim nose down / up
 */
export interface KeyboardAxes {
  elevator: number;
  aileron: number;
  rudder: number;
  /** −1, 0 or +1 while a throttle key is held. */
  throttleDir: number;
  /** −1, 0 or +1 while a trim key is held. */
  trimDir: number;
  brake: number;
}

const AXIS_RATE = 2.5; // full deflection in 0.4 s
const RETURN_RATE = 4; // back to centre in 0.25 s
// Pitch is far more sensitive than roll on a light aircraft: a quick tap
// should nudge the nose, not slam the elevator (full deflection in ~1.1 s).
const ELEVATOR_RATE = 0.9;
const ELEVATOR_RETURN = 2.5;

const held = new Set<string>();
export const keyboardAxes: KeyboardAxes = { elevator: 0, aileron: 0, rudder: 0, throttleDir: 0, trimDir: 0, brake: 0 };

const FLIGHT_KEYS = new Set([
  'arrowup', 'arrowdown', 'arrowleft', 'arrowright', 'w', 'a', 's', 'd', 'q', 'e', 'b',
  '+', '=', '-', '_', 'pageup', 'pagedown', '[', ']', ' ',
]);

function isTyping(t: EventTarget | null) {
  return t instanceof HTMLInputElement || t instanceof HTMLTextAreaElement || (t instanceof HTMLElement && t.isContentEditable);
}

const onDown = (e: KeyboardEvent) => {
  if (isTyping(e.target) || e.ctrlKey || e.metaKey || e.altKey) return;
  const k = e.key.toLowerCase();
  if (FLIGHT_KEYS.has(k)) e.preventDefault(); // no page scrolling while flying
  held.add(k);
};
const onUp = (e: KeyboardEvent) => held.delete(e.key.toLowerCase());
const onBlur = () => held.clear();

export function attachKeyboard() {
  window.addEventListener('keydown', onDown);
  window.addEventListener('keyup', onUp);
  window.addEventListener('blur', onBlur);
  return () => {
    window.removeEventListener('keydown', onDown);
    window.removeEventListener('keyup', onUp);
    window.removeEventListener('blur', onBlur);
    held.clear();
  };
}

const any = (...keys: string[]) => keys.some((k) => held.has(k));
const dir = (pos: boolean, neg: boolean) => (pos ? 1 : 0) - (neg ? 1 : 0);

function approach(value: number, target: number, dt: number, rates: [number, number] = [AXIS_RATE, RETURN_RATE]) {
  const rate = target === 0 ? rates[1] : rates[0];
  const step = rate * dt;
  return Math.abs(target - value) <= step ? target : value + Math.sign(target - value) * step;
}

export function updateKeyboard(dt: number) {
  const a = keyboardAxes;
  a.elevator = approach(a.elevator, dir(any('arrowdown', 's'), any('arrowup', 'w')), dt, [ELEVATOR_RATE, ELEVATOR_RETURN]);
  a.aileron = approach(a.aileron, dir(any('arrowright', 'd'), any('arrowleft', 'a')), dt);
  a.rudder = approach(a.rudder, dir(any('e'), any('q')), dt);
  a.throttleDir = dir(any('+', '=', 'pageup'), any('-', '_', 'pagedown'));
  a.trimDir = dir(any(']'), any('['));
  a.brake = any('b') ? 1 : 0;
}
