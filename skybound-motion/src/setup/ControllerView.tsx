import { Edges, Grid, OrbitControls, RoundedBox } from '@react-three/drei';
import { Canvas, useFrame } from '@react-three/fiber';
import { useMemo, useRef } from 'react';
import * as THREE from 'three';
import { wrap180 } from '../control/mapping';
import { useHubStatus } from '../sensor/hooks';
import { sensorHub } from '../sensor/sensorHub';
import { getSettings, useSettings } from '../settings/settingsStore';

const D2R = Math.PI / 180;

/**
 * Aircraft attitude (deg) -> three.js quaternion.
 * three.js: +X right, +Y up, -Z forward. Aircraft order yaw → pitch → roll is
 * Euler order 'YXZ'. Nose-up pitch is +X rotation; nose-right yaw is −Y;
 * right-wing-down roll is −Z.
 */
export function attitudeToQuaternion(pitch: number, roll: number, yaw: number, out = new THREE.Quaternion()) {
  return out.setFromEuler(new THREE.Euler(pitch * D2R, -yaw * D2R, -roll * D2R, 'YXZ'));
}

/** Breadboard-style controller: base plate, ESP32 dev board, GY-521 breakout, forward arrow. Units: cm. */
function ControllerModel({ ghost = false }: { ghost?: boolean }) {
  const m = useMemo(() => {
    if (ghost) {
      const g = new THREE.MeshBasicMaterial({ color: '#d9a441', transparent: true, opacity: 0.06, depthWrite: false });
      return { base: g, esp: g, shield: g, gy: g, chip: g, arrow: g, pins: g };
    }
    return {
      base: new THREE.MeshStandardMaterial({ color: '#2b2f33', roughness: 0.85 }),
      esp: new THREE.MeshStandardMaterial({ color: '#1c1f22', roughness: 0.6 }),
      shield: new THREE.MeshStandardMaterial({ color: '#b9bcbf', metalness: 0.85, roughness: 0.35 }),
      gy: new THREE.MeshStandardMaterial({ color: '#2a4f8f', roughness: 0.55 }),
      chip: new THREE.MeshStandardMaterial({ color: '#121212', roughness: 0.4 }),
      arrow: new THREE.MeshStandardMaterial({ color: '#e8e4da', roughness: 0.7 }),
      pins: new THREE.MeshStandardMaterial({ color: '#c8a352', metalness: 0.9, roughness: 0.3 }),
    };
  }, [ghost]);
  const edge = ghost ? <Edges color="#d9a441" threshold={20} /> : null;

  return (
    <group>
      <RoundedBox args={[7, 0.35, 11]} radius={0.15} material={m.base}>
        {edge}
      </RoundedBox>
      {!ghost && (
        <>
          {/* ESP32 dev board at the back, USB end toward the pilot */}
          <mesh material={m.esp} position={[0, 0.32, 2.4]}>
            <boxGeometry args={[2.8, 0.16, 5.2]} />
          </mesh>
          <mesh material={m.shield} position={[0, 0.5, 1.2]}>
            <boxGeometry args={[1.6, 0.25, 1.8]} />
          </mesh>
          <mesh material={m.shield} position={[0, 0.48, 4.85]}>
            <boxGeometry args={[0.8, 0.28, 0.6]} />
          </mesh>
          {[-1.25, 1.25].map((x) => (
            <mesh key={x} material={m.pins} position={[x, 0.26, 2.4]}>
              <boxGeometry args={[0.18, 0.1, 4.8]} />
            </mesh>
          ))}
          {/* GY-521 (MPU-6050) breakout, +X arrow pointing forward */}
          <mesh material={m.gy} position={[0, 0.3, -2.6]}>
            <boxGeometry args={[1.6, 0.12, 2.1]} />
          </mesh>
          <mesh material={m.chip} position={[0, 0.4, -2.5]}>
            <boxGeometry args={[0.4, 0.08, 0.4]} />
          </mesh>
          {/* Forward arrow */}
          <mesh material={m.arrow} position={[0, 0.2, -4.7]} rotation={[-Math.PI / 2, 0, 0]}>
            <coneGeometry args={[0.55, 1.1, 3]} />
          </mesh>
          {/* Right-side marker so roll direction is unambiguous */}
          <mesh position={[3.2, 0.2, 0]}>
            <boxGeometry args={[0.3, 0.12, 2]} />
            <meshStandardMaterial color="#d9a441" roughness={0.6} />
          </mesh>
        </>
      )}
    </group>
  );
}

function LiveController() {
  const live = useRef<THREE.Group>(null);
  const ghost = useRef<THREE.Group>(null);
  const target = useMemo(() => new THREE.Quaternion(), []);

  useFrame((_, dt) => {
    const { neutral, control } = getSettings();
    if (ghost.current) attitudeToQuaternion(neutral.pitch, neutral.roll, 0, ghost.current.quaternion);
    const latest = sensorHub.latest();
    if (!latest || !live.current) return;
    const s = latest.sample;
    // Yaw has no absolute reference, so only show it relative to neutral, and only when used.
    const yaw = control.yawMode === 'off' ? 0 : wrap180(s.yaw - neutral.yaw);
    attitudeToQuaternion(s.pitch, s.roll, yaw, target);
    // Display-only smoothing so 50 Hz samples look fluid at 60–144 fps.
    live.current.quaternion.slerp(target, 1 - Math.exp(-dt * 30));
  });

  return (
    <>
      <group ref={ghost}>
        <ControllerModel ghost />
      </group>
      <group ref={live}>
        <ControllerModel />
      </group>
    </>
  );
}

export function ControllerView() {
  const status = useHubStatus();
  const hasNeutral = useSettings((s) => s.neutral.capturedAt > 0);
  const live = status.link === 'live' && status.device === 'ready';
  const overlay =
    status.link === 'disconnected'
      ? 'Connect a controller or the simulator'
      : status.device === 'calibrating'
        ? 'Device is calibrating — keep it still'
        : status.link === 'awaiting' || status.link === 'connecting'
          ? 'Waiting for sensor data…'
          : status.link === 'lost'
            ? 'Sensor data stopped'
            : null;

  return (
    <div className="relative h-full min-h-[280px] overflow-hidden rounded-lg border border-line bg-[#0a111c]">
      <Canvas camera={{ position: [9, 9, 15], fov: 35 }} dpr={[1, 2]}>
        <color attach="background" args={['#0a111c']} />
        <hemisphereLight args={['#dfe6ec', '#1a1d20', 1.1]} />
        <directionalLight position={[6, 12, 4]} intensity={2.2} />
        <LiveController />
        <Grid position={[0, -3, 0]} args={[40, 40]} cellSize={1} cellColor="#20252a" sectionSize={5} sectionColor="#2c3238" fadeDistance={40} infiniteGrid />
        <OrbitControls enablePan={false} minDistance={10} maxDistance={40} />
      </Canvas>
      <div className="pointer-events-none absolute top-3 left-3 flex gap-3 text-[11px] text-ink-3">
        <span>Solid: live sensor</span>
        <span className="text-accent/80">Outline: neutral{hasNeutral ? '' : ' (not set)'}</span>
      </div>
      <div className="pointer-events-none absolute right-3 bottom-3 text-[11px] text-ink-3">Arrow = forward · amber bar = right side · drag to orbit</div>
      {!live && overlay && (
        <div className="pointer-events-none absolute inset-0 grid place-items-center bg-bg/40">
          <span className="rounded-md border border-line bg-panel px-3 py-1.5 text-[13px] text-ink-2">{overlay}</span>
        </div>
      )}
    </div>
  );
}
