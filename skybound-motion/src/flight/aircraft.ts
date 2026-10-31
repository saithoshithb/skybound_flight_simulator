import { DEG, type V3 } from './math';

/**
 * Aircraft definition: geometry, mass properties, engine, and nondimensional
 * stability/control derivatives (aerospace body axes). Values are in the
 * range of a four-seat, high-wing, 160 hp trainer, taken from textbook
 * light-aircraft data, not any manufacturer's data. Units SI.
 *
 * Control sign convention used here (and by ControlCommand):
 *   elevator + → nose up, aileron + → roll right, rudder + → yaw right.
 */
export interface GearLeg {
  name: string;
  /** Contact point, body FRD (m from CG). */
  pos: V3;
  spring: number; // N/m
  damper: number; // N·s/m
  steerable: boolean;
  brakes: boolean;
  /** A skid, not a wheel: high sliding friction, flagged as a strike. */
  skid?: boolean;
}

export interface AircraftDef {
  id: string;
  name: string;
  mass: number; // kg
  inertia: V3; // Ixx, Iyy, Izz (kg·m²)
  wingArea: number; // S, m²
  span: number; // b, m
  chord: number; // c̄, m
  oswald: number;
  maxPowerW: number;
  propEfficiency: number;
  staticThrustN: number;
  engineTimeConstantS: number;
  // Lift and drag
  CL0: number;
  CLalpha: number;
  CLq: number;
  CLde: number;
  CD0: number;
  /** Centre (rad) and sharpness of the lift-to-flat-plate blend. Peak CL comes a few degrees earlier. */
  alphaStall: number;
  stallSharpness: number;
  // Side force
  CYbeta: number;
  CYdr: number;
  // Roll
  Clbeta: number;
  Clp: number;
  Clr: number;
  Clda: number;
  Cldr: number;
  // Pitch
  Cm0: number;
  Cmalpha: number;
  Cmq: number;
  Cmde: number;
  // Yaw
  Cnbeta: number;
  Cnp: number;
  Cnr: number;
  Cnda: number;
  Cndr: number;
  // Control surface limits (rad) and actuator lag (s)
  maxElevator: number;
  maxAileron: number;
  maxRudder: number;
  surfaceLagS: number;
  maxNoseSteer: number;
  gear: GearLeg[];
  /** Points that must never touch the ground (crash if they do). */
  structure: { name: string; pos: V3 }[];
  vneMs: number;
  /** Menu copy. */
  description: string;
  /** Visual style for the procedural model. */
  look: { wing: 'high' | 'low'; body: string; stripe: string; accent: string };
  /** Easy-flight speeds (kt): cruise, take-off target, and the adjustable range. */
  easy: { cruiseKt: number; takeoffKt: number; minKt: number; maxKt: number; rotateKt: number };
}

export const TRAINER: AircraftDef = {
  id: 'trainer',
  name: 'Highwing 160 trainer',
  mass: 1043,
  inertia: [1285, 1825, 2667],
  wingArea: 16.2,
  span: 10.9,
  chord: 1.49,
  oswald: 0.75,
  maxPowerW: 119000,
  propEfficiency: 0.8,
  staticThrustN: 2600,
  engineTimeConstantS: 0.6,
  CL0: 0.31,
  CLalpha: 5.143,
  CLq: 3.9,
  CLde: 0.43,
  CD0: 0.031,
  // Blend centre and sharpness: gives CLmax ≈ 1.55 at ≈16° with a gradual break.
  alphaStall: 20 * DEG,
  stallSharpness: 28,
  CYbeta: -0.31,
  CYdr: -0.187,
  Clbeta: -0.089,
  Clp: -0.47,
  Clr: 0.096,
  Clda: 0.178,
  Cldr: -0.0147,
  Cm0: -0.015,
  Cmalpha: -0.89,
  Cmq: -12.4,
  Cmde: -1.28,
  Cnbeta: 0.065,
  Cnp: -0.03,
  Cnr: -0.099,
  Cnda: -0.053,
  Cndr: 0.0657,
  maxElevator: 25 * DEG,
  maxAileron: 20 * DEG,
  maxRudder: 20 * DEG,
  surfaceLagS: 0.06,
  maxNoseSteer: 25 * DEG,
  gear: [
    { name: 'nose', pos: [1.25, 0, 1.05], spring: 35000, damper: 4500, steerable: true, brakes: false },
    { name: 'left main', pos: [-0.35, -1.25, 1.05], spring: 55000, damper: 6500, steerable: false, brakes: true },
    { name: 'right main', pos: [-0.35, 1.25, 1.05], spring: 55000, damper: 6500, steerable: false, brakes: true },
    // Over-rotation scrapes the tail (≈13° nose-up on the mains) instead of ending the flight.
    { name: 'tail', pos: [-5.2, 0, -0.1], spring: 90000, damper: 9000, steerable: false, brakes: false, skid: true },
  ],
  structure: [
    { name: 'Left wingtip', pos: [-0.2, -5.45, -1.0] },
    { name: 'Right wingtip', pos: [-0.2, 5.45, -1.0] },
    { name: 'Propeller', pos: [2.3, 0, 0.75] },
    { name: 'Fuselage', pos: [0, 0, 0.55] },
  ],
  vneMs: 84,
  description: 'Forgiving four-seat trainer. Stable, slow to stall, easy to land.',
  look: { wing: 'high', body: '#efede6', stripe: '#24405a', accent: '#a8412f' },
  easy: { cruiseKt: 95, takeoffKt: 90, minKt: 60, maxKt: 115, rotateKt: 55 },
};

/**
 * A livelier two-seat low-wing sport aircraft: more power, a smaller wing
 * (faster, higher stall speed), quicker roll and lighter pitch damping.
 * Generic textbook-range numbers, not any manufacturer's data.
 */
export const SPORT: AircraftDef = {
  ...TRAINER,
  id: 'sport',
  name: 'Lowwing 200 sport',
  mass: 1100,
  inertia: [1150, 1750, 2600],
  wingArea: 14.2,
  span: 9.4,
  chord: 1.52,
  oswald: 0.78,
  maxPowerW: 150000,
  staticThrustN: 3400,
  CL0: 0.26,
  CLalpha: 5.0,
  CD0: 0.027,
  Clp: -0.42,
  Clda: 0.23,
  Cmq: -10.5,
  Cm0: -0.02,
  maxAileron: 22 * DEG,
  gear: [
    { name: 'nose', pos: [1.3, 0, 1.05], spring: 36000, damper: 4600, steerable: true, brakes: false },
    { name: 'left main', pos: [-0.3, -1.4, 1.05], spring: 58000, damper: 6800, steerable: false, brakes: true },
    { name: 'right main', pos: [-0.3, 1.4, 1.05], spring: 58000, damper: 6800, steerable: false, brakes: true },
    { name: 'tail', pos: [-5.0, 0, -0.1], spring: 90000, damper: 9000, steerable: false, brakes: false, skid: true },
  ],
  structure: [
    // Low wing: tips sit below the CG, so bank on the runway is limited to about 9°.
    { name: 'Left wingtip', pos: [-0.1, -4.7, 0.3] },
    { name: 'Right wingtip', pos: [-0.1, 4.7, 0.3] },
    { name: 'Propeller', pos: [2.3, 0, 0.75] },
    { name: 'Fuselage', pos: [0, 0, 0.55] },
  ],
  vneMs: 100,
  description: 'Faster two-seat sport plane. More power and a crisper roll; lands faster.',
  look: { wing: 'low', body: '#e9e6df', stripe: '#7b2f2a', accent: '#2f3e4f' },
  easy: { cruiseKt: 120, takeoffKt: 110, minKt: 70, maxKt: 140, rotateKt: 66 },
};

export const AIRCRAFT: AircraftDef[] = [TRAINER, SPORT];

export const aircraftById = (id: string) => AIRCRAFT.find((a) => a.id === id) ?? TRAINER;
