import { useMemo, type Ref } from 'react';
import * as THREE from 'three';
import type { AircraftDef } from '../flight/aircraft';
import { getPanel } from '../instruments/panel';

/**
 * Procedural high-wing trainer, built to the flight model's geometry
 * (metres, origin at the centre of gravity, −z forward, +y up). Gear contact
 * points and wingtips line up with the physics contact points, so the wheels
 * sit on the runway and a wing strike looks like one.
 */
export interface AircraftParts {
  root: THREE.Group;
  elevator: THREE.Group;
  aileronLeft: THREE.Group;
  aileronRight: THREE.Group;
  rudder: THREE.Group;
  prop: THREE.Group;
  propDisc: THREE.Mesh;
  cockpit: THREE.Group;
  /** Fuselage and cabin shell, hidden in the cockpit view (the eye is inside it). */
  exterior: THREE.Group;
  yokeLeft: THREE.Group;
  yokeRight: THREE.Group;
  throttleKnob: THREE.Group;
  strobes: THREE.Mesh[];
  /** Live instrument canvas; mark `needsUpdate` after redrawing it. */
  panelTexture: THREE.CanvasTexture;
}

const YOKE_Z = -0.62;
const THROTTLE_Z = -0.9;

function useMaterials(look: AircraftDef['look']) {
  return useMemo(
    () => ({
      body: new THREE.MeshStandardMaterial({ color: look.body, roughness: 0.45, metalness: 0.05 }),
      stripe: new THREE.MeshStandardMaterial({ color: look.stripe, roughness: 0.5 }),
      accent: new THREE.MeshStandardMaterial({ color: look.accent, roughness: 0.5 }),
      glass: new THREE.MeshStandardMaterial({ color: '#1b2630', roughness: 0.08, metalness: 0.6 }),
      dark: new THREE.MeshStandardMaterial({ color: '#25282b', roughness: 0.8 }),
      tire: new THREE.MeshStandardMaterial({ color: '#151617', roughness: 0.95 }),
      metal: new THREE.MeshStandardMaterial({ color: '#9aa0a5', roughness: 0.35, metalness: 0.8 }),
      panel: new THREE.MeshStandardMaterial({ color: '#2b2e31', roughness: 0.9 }),
      disc: new THREE.MeshBasicMaterial({ color: '#2a2a2a', transparent: true, opacity: 0.18, depthWrite: false, side: THREE.DoubleSide }),
    }),
    [look],
  );
}

/** Fuselage as a lathe along z: nose at −2.2, tail at +5.9. Scaled to a boxy cross-section. */
function useFuselageGeometry() {
  return useMemo(() => {
    const profile: [number, number][] = [
      [0.0, -2.2],
      [0.42, -2.1],
      [0.56, -1.7],
      [0.62, -1.0],
      [0.66, -0.2],
      [0.64, 0.8],
      [0.52, 1.8],
      [0.36, 3.0],
      [0.22, 4.4],
      [0.14, 5.9],
      [0.0, 5.95],
    ];
    const g = new THREE.LatheGeometry(
      profile.map(([r, z]) => new THREE.Vector2(r, z)),
      24,
    );
    // Lathe axis is y; rotating +90° about x maps profile y → z (nose at −z).
    g.rotateX(Math.PI / 2);
    g.computeVertexNormals();
    return g;
  }, []);
}

function Wheel({ position, m }: { position: [number, number, number]; m: ReturnType<typeof useMaterials> }) {
  return (
    <mesh position={position} rotation={[0, 0, Math.PI / 2]} material={m.tire} castShadow>
      <cylinderGeometry args={[0.22, 0.22, 0.14, 18]} />
    </mesh>
  );
}

export function Aircraft({ ref, def }: { ref: Ref<AircraftParts>; def: AircraftDef }) {
  const m = useMaterials(def.look);
  // Geometry follows the flight model's definition so contact points line up.
  const high = def.look.wing === 'high';
  const wingY = high ? 1.0 : -0.32;
  const half = def.span / 2;
  const chord = def.chord;
  const trailZ = 0.1 + chord / 2;
  const mainX = Math.abs(def.gear[1].pos[1]);
  const mainZ = -def.gear[1].pos[0];
  const noseZ = -def.gear[0].pos[0];
  const fuselage = useFuselageGeometry();

  const parts = useMemo<AircraftParts>(
    () => ({
      root: new THREE.Group(),
      elevator: new THREE.Group(),
      aileronLeft: new THREE.Group(),
      aileronRight: new THREE.Group(),
      rudder: new THREE.Group(),
      prop: new THREE.Group(),
      propDisc: new THREE.Mesh(),
      cockpit: new THREE.Group(),
      exterior: new THREE.Group(),
      yokeLeft: new THREE.Group(),
      yokeRight: new THREE.Group(),
      throttleKnob: new THREE.Group(),
      strobes: [new THREE.Mesh(), new THREE.Mesh()],
      panelTexture: (() => {
        const t = new THREE.CanvasTexture(getPanel().canvas);
        t.colorSpace = THREE.SRGBColorSpace;
        t.anisotropy = 8;
        return t;
      })(),
    }),
    [],
  );
  if (typeof ref === 'function') ref(parts);
  else if (ref) ref.current = parts;

  return (
    <primitive object={parts.root}>
      <primitive object={parts.exterior}>
      {/* Fuselage, flattened sides, slightly taller than wide */}
      <mesh geometry={fuselage} material={m.body} scale={[0.85, 1.05, 1]} position={[0, 0.05, 0]} castShadow receiveShadow />
      {/* Cheat line along each side */}
      {[-1, 1].map((s) => (
        <mesh key={s} material={m.stripe} position={[s * 0.53, -0.05, 1.2]} rotation={[0, 0, 0]} castShadow>
          <boxGeometry args={[0.02, 0.12, 5.6]} />
        </mesh>
      ))}
      {high ? (
        <>
          {/* Cabin: fills the gap between the fuselage and the wing root */}
          <mesh material={m.body} position={[0, 0.72, 0.15]} castShadow>
            <boxGeometry args={[1.08, 0.46, 1.9]} />
          </mesh>
          {/* Cabin windows */}
          <mesh material={m.glass} position={[0, 0.72, -0.86]} rotation={[-0.95, 0, 0]}>
            <boxGeometry args={[1.0, 0.04, 0.55]} />
          </mesh>
          {[-1, 1].map((s) => (
            <mesh key={s} material={m.glass} position={[s * 0.55, 0.74, 0.1]}>
              <boxGeometry args={[0.02, 0.3, 1.45]} />
            </mesh>
          ))}
        </>
      ) : (
        <>
          {/* Bubble canopy over a low cockpit, with a turtle-deck behind it */}
          <mesh material={m.glass} position={[0, 0.62, -0.1]} scale={[0.52, 0.42, 1.05]} castShadow>
            <sphereGeometry args={[1, 24, 14, 0, Math.PI * 2, 0, Math.PI / 2]} />
          </mesh>
          <mesh material={m.body} position={[0, 0.6, 1.4]} rotation={[-0.12, 0, 0]} castShadow>
            <boxGeometry args={[0.7, 0.26, 1.6]} />
          </mesh>
        </>
      )}
      </primitive>

      {/* Wing on top of the cabin, with ailerons on the outer trailing edge */}
      <mesh material={m.body} position={[0, wingY, 0.1]} castShadow receiveShadow>
        <boxGeometry args={[def.span, 0.13, chord]} />
      </mesh>
      {[-1, 1].map((s) => (
        <mesh key={s} material={m.accent} position={[s * (half - 0.05), wingY, 0.1]} castShadow>
          <boxGeometry args={[0.1, 0.14, chord + 0.01]} />
        </mesh>
      ))}
      <primitive object={parts.aileronLeft} position={[-(half - 1.55), wingY, trailZ]}>
        <mesh material={m.body} position={[0, 0, 0.18]} castShadow>
          <boxGeometry args={[2.6, 0.06, 0.36]} />
        </mesh>
      </primitive>
      <primitive object={parts.aileronRight} position={[half - 1.55, wingY, trailZ]}>
        <mesh material={m.body} position={[0, 0, 0.18]} castShadow>
          <boxGeometry args={[2.6, 0.06, 0.36]} />
        </mesh>
      </primitive>
      {/* Wing struts (high wing only) */}
      {high && [-1, 1].map((s) => {
        const a = new THREE.Vector3(s * 0.5, -0.35, 0.1);
        const b = new THREE.Vector3(s * 2.7, 0.94, 0.15);
        const mid = a.clone().add(b).multiplyScalar(0.5);
        const len = a.distanceTo(b);
        const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), b.clone().sub(a).normalize());
        return (
          <mesh key={s} material={m.body} position={mid} quaternion={q} castShadow>
            <cylinderGeometry args={[0.035, 0.035, len, 8]} />
          </mesh>
        );
      })}

      {/* Horizontal tail with elevator */}
      <mesh material={m.body} position={[0, 0.12, 5.05]} castShadow>
        <boxGeometry args={[3.4, 0.08, 0.8]} />
      </mesh>
      <primitive object={parts.elevator} position={[0, 0.12, 5.45]}>
        <mesh material={m.body} position={[0, 0, 0.22]} castShadow>
          <boxGeometry args={[3.3, 0.05, 0.44]} />
        </mesh>
      </primitive>
      {/* Fin and rudder */}
      <mesh material={m.body} position={[0, 0.85, 5.15]} rotation={[0.35, 0, 0]} castShadow>
        <boxGeometry args={[0.07, 1.35, 0.95]} />
      </mesh>
      <primitive object={parts.rudder} position={[0, 0.85, 5.72]}>
        <mesh material={m.stripe} position={[0, 0, 0.2]} castShadow>
          <boxGeometry args={[0.06, 1.3, 0.4]} />
        </mesh>
      </primitive>

      {/* Engine: spinner and two-blade propeller */}
      <primitive object={parts.prop} position={[0, -0.02, -2.25]}>
        <mesh material={m.accent} rotation={[-Math.PI / 2, 0, 0]} castShadow>
          <coneGeometry args={[0.2, 0.42, 18]} />
        </mesh>
        <mesh material={m.dark} castShadow>
          <boxGeometry args={[0.12, 1.9, 0.05]} />
        </mesh>
      </primitive>
      <primitive object={parts.propDisc} position={[0, -0.02, -2.28]}>
        <circleGeometry args={[0.95, 32]} />
        <primitive object={m.disc} attach="material" />
      </primitive>

      {/* Fixed tricycle gear; contact points match the physics gear legs */}
      <mesh material={m.metal} position={[0, -0.55, noseZ]}>
        <cylinderGeometry args={[0.04, 0.04, 0.85, 8]} />
      </mesh>
      <Wheel position={[0, -0.83, noseZ]} m={m} />
      {[-1, 1].map((s) => (
        <group key={s}>
          <mesh material={m.metal} position={[s * (mainX - 0.35), -0.55, mainZ]} rotation={[0, 0, s * 0.55]}>
            <boxGeometry args={[0.05, 0.95, 0.12]} />
          </mesh>
          <Wheel position={[s * mainX, -0.83, mainZ]} m={m} />
        </group>
      ))}

      {/* Wheel fairings on the mains */}
      {[-1, 1].map((s) => (
        <mesh key={s} material={m.body} position={[s * mainX, -0.8, mainZ - 0.03]} scale={[0.16, 0.26, 0.52]} castShadow>
          <sphereGeometry args={[1, 16, 10]} />
        </mesh>
      ))}
      {/* Pitot tube under the left wing, exhaust under the cowl, landing light */}
      <mesh material={m.metal} position={[-(half * 0.48), wingY - 0.1, 0.1 - chord / 2 - 0.1]} rotation={[Math.PI / 2, 0, 0]}>
        <cylinderGeometry args={[0.012, 0.012, 0.42, 6]} />
      </mesh>
      <mesh material={m.metal} position={[0.18, -0.58, -1.55]} rotation={[Math.PI / 2 + 0.3, 0, 0]}>
        <cylinderGeometry args={[0.03, 0.035, 0.3, 8]} />
      </mesh>
      <mesh position={[-(half * 0.57), wingY, 0.1 - chord / 2 + 0.08]}>
        <boxGeometry args={[0.18, 0.06, 0.02]} />
        <meshBasicMaterial color="#fffbe8" />
      </mesh>

      {/* Position lights (red left, green right, white tail) and wingtip strobes */}
      <mesh position={[-(half + 0.03), wingY, -0.2]}>
        <sphereGeometry args={[0.045, 8, 6]} />
        <meshBasicMaterial color="#ff3b2f" />
      </mesh>
      <mesh position={[half + 0.03, wingY, -0.2]}>
        <sphereGeometry args={[0.045, 8, 6]} />
        <meshBasicMaterial color="#3dff6e" />
      </mesh>
      <mesh position={[0, 0.2, 5.98]}>
        <sphereGeometry args={[0.04, 8, 6]} />
        <meshBasicMaterial color="#ffffff" />
      </mesh>
      {[-1, 1].map((s, i) => (
        <primitive key={s} object={parts.strobes[i]} position={[s * (half + 0.03), wingY, 0.35]}>
          <sphereGeometry args={[0.06, 8, 6]} />
          <meshBasicMaterial color="#ffffff" />
        </primitive>
      ))}

      {/* Cockpit interior, shown only in the cockpit view */}
      <primitive object={parts.cockpit} visible={false}>
        {/* Cowling ahead of the windshield, so the nose attitude is visible */}
        <mesh material={m.body} position={[0, 0.44, -1.75]} rotation={[0.12, 0, 0]}>
          <boxGeometry args={[1.0, 0.12, 1.1]} />
        </mesh>
        {/* Instrument panel: live six-pack on the pilot's side, radio stack on the right */}
        <group position={[0, 0.42, -1.02]} rotation={[-0.2, 0, 0]}>
          <mesh material={m.panel}>
            <boxGeometry args={[1.12, 0.4, 0.05]} />
          </mesh>
          <mesh position={[-0.2, 0, 0.0265]}>
            <planeGeometry args={[0.68, 0.34]} />
            <meshBasicMaterial map={parts.panelTexture} toneMapped={false} />
          </mesh>
          {[0.08, 0.02, -0.04, -0.1].map((y, i) => (
            <mesh key={i} material={m.dark} position={[0.36, y, 0.03]}>
              <boxGeometry args={[0.3, 0.05, 0.01]} />
            </mesh>
          ))}
          {[0.08, 0.02, -0.04, -0.1].map((y, i) => (
            <mesh key={`d${i}`} position={[0.33, y, 0.036]}>
              <planeGeometry args={[0.1, 0.022]} />
              <meshBasicMaterial color={i === 0 ? '#5fd38a' : '#d89a3a'} toneMapped={false} />
            </mesh>
          ))}
        </group>
        {/* Glareshield */}
        <mesh material={m.dark} position={[0, 0.635, -1.1]} rotation={[-0.15, 0, 0]}>
          <boxGeometry args={[1.14, 0.04, 0.3]} />
        </mesh>
        {/* Control yokes (move with the actual aileron / elevator deflection) */}
        {[parts.yokeLeft, parts.yokeRight].map((yoke, i) => (
          <primitive key={i} object={yoke} position={[i === 0 ? -0.28 : 0.28, 0.3, YOKE_Z]}>
            <mesh material={m.metal} position={[0, 0, -0.18]} rotation={[Math.PI / 2, 0, 0]}>
              <cylinderGeometry args={[0.014, 0.014, 0.36, 8]} />
            </mesh>
            <mesh material={m.dark}>
              <boxGeometry args={[0.3, 0.035, 0.035]} />
            </mesh>
            {[-1, 1].map((s) => (
              <mesh key={s} material={m.dark} position={[s * 0.15, 0.05, 0]}>
                <boxGeometry args={[0.035, 0.12, 0.04]} />
              </mesh>
            ))}
            <mesh material={m.accent} position={[0, 0, 0.02]}>
              <cylinderGeometry args={[0.03, 0.03, 0.01, 12]} />
            </mesh>
          </primitive>
        ))}
        {/* Throttle plunger, centre of the panel: pushed in = full power */}
        <primitive object={parts.throttleKnob} position={[0.02, 0.27, THROTTLE_Z]}>
          <mesh material={m.metal} position={[0, 0, -0.06]} rotation={[Math.PI / 2, 0, 0]}>
            <cylinderGeometry args={[0.007, 0.007, 0.12, 6]} />
          </mesh>
          <mesh material={m.dark} rotation={[Math.PI / 2, 0, 0]}>
            <cylinderGeometry args={[0.022, 0.022, 0.03, 12]} />
          </mesh>
        </primitive>
        {/* Seats' top edges and the floor tunnel, for depth at the bottom of the view */}
        <mesh material={m.panel} position={[0, 0.02, -0.5]}>
          <boxGeometry args={[0.18, 0.12, 1.0]} />
        </mesh>
        {/* Windshield posts, door frames and cabin roof (a bubble canopy has none) */}
        {[-1, 1].map((s) => (
          <group key={s}>
            {high && (
              <>
                <mesh material={m.panel} position={[s * 0.52, 0.8, -0.95]} rotation={[0.75, 0, 0]}>
                  <boxGeometry args={[0.04, 0.5, 0.05]} />
                </mesh>
                <mesh material={m.panel} position={[s * 0.54, 0.75, 0.15]}>
                  <boxGeometry args={[0.03, 0.5, 0.05]} />
                </mesh>
              </>
            )}
            <mesh material={m.panel} position={[s * 0.56, 0.3, -0.2]}>
              <boxGeometry args={[0.03, 0.3, 2.0]} />
            </mesh>
          </group>
        ))}
        {high && (
          <mesh material={m.panel} position={[0, 0.96, -0.1]}>
            <boxGeometry args={[1.1, 0.03, 1.7]} />
          </mesh>
        )}
      </primitive>
    </primitive>
  );
}

export interface AircraftVisualState {
  surfaces: { elevator: number; aileron: number; rudder: number };
  engine: number;
  throttle: number;
  rpm: number;
  maxElevator: number;
  maxAileron: number;
  timeS: number;
}

/** Push the latest physics state into the model's moving parts. */
export function syncAircraft(p: AircraftParts, v: AircraftVisualState, dt: number) {
  const { surfaces } = v;
  p.elevator.rotation.x = surfaces.elevator; // + trailing edge down
  p.aileronRight.rotation.x = -surfaces.aileron; // + roll right: right TE up
  p.aileronLeft.rotation.x = surfaces.aileron;
  p.rudder.rotation.y = surfaces.rudder; // + trailing edge right
  p.prop.rotation.z += ((v.rpm / 60) * Math.PI * 2 * dt) % (Math.PI * 2);
  // Blades blur into a faint disc once the engine is running.
  (p.propDisc.material as THREE.MeshBasicMaterial).opacity = 0.08 + v.engine * 0.14;

  // Yokes mirror the real surface positions: turn for aileron, push/pull for elevator.
  const roll = surfaces.aileron / v.maxAileron;
  const pull = -surfaces.elevator / v.maxElevator;
  for (const yoke of [p.yokeLeft, p.yokeRight]) {
    yoke.rotation.z = -roll * 0.6;
    yoke.position.z = YOKE_Z + pull * 0.07;
  }
  p.throttleKnob.position.z = THROTTLE_Z - v.throttle * 0.07;

  // Wingtip strobes: double flash every 1.2 s.
  const phase = v.timeS % 1.2;
  const flash = phase < 0.05 || (phase > 0.15 && phase < 0.2);
  for (const s of p.strobes) s.visible = flash;
}
