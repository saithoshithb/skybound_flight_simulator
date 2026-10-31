import { Bloom, EffectComposer, ToneMapping, Vignette } from '@react-three/postprocessing';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { ToneMappingMode } from 'postprocessing';
import { useMemo, useRef } from 'react';
import * as THREE from 'three';
import { aircraftById } from '../flight/aircraft';
import { recordFrame } from '../flight/recorder';
import { flash, flight, pilot, updatePilot } from '../flight/session';
import { perfFrame } from '../hud/perf';
import { engineRpm, getPanel } from '../instruments/panel';
import { GRAPHICS, type GraphicsProfile } from '../settings/graphics';
import { getSettings, useSettings } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { Aircraft, syncAircraft, type AircraftParts } from './Aircraft';
import { nedToThree, quatToThree } from './coords';
import { terrainHeight } from './terrain';
import { HAZE, SUN_DIR, World } from './World';

const CHASE_DISTANCE = 15;
const CHASE_HEIGHT = 3.6;
/** Pilot's eye in aircraft coordinates (left seat). */
const EYE = new THREE.Vector3(-0.28, 0.82, -0.25);
/** The pilot looks slightly down, toward the panel and the nose. */
const EYE_TILT = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -0.17);
const UP = new THREE.Vector3(0, 1, 0);
/** Frames rendered before the loading screen lifts (shaders compiled, shadows drawn). */
const READY_AFTER_FRAMES = 3;

/**
 * One frame, in order: pilot input → physics (fixed 240 Hz sub-steps) →
 * model transform and control surfaces → camera → shadow light.
 * Nothing here touches React state, and nothing allocates per frame.
 */
function FlightDriver({ profile, onReady }: { profile: GraphicsProfile; onReady?: () => void }) {
  const aircraftId = useSettings((s) => s.aircraftId);
  const parts = useRef<AircraftParts>(null);
  const sun = useRef<THREE.DirectionalLight>(null);
  const camera = useThree((s) => s.camera) as THREE.PerspectiveCamera;
  const scratch = useMemo(
    () => ({
      pos: new THREE.Vector3(),
      quat: new THREE.Quaternion(),
      fwd: new THREE.Vector3(),
      flatFwd: new THREE.Vector3(),
      desired: new THREE.Vector3(),
      chasePos: new THREE.Vector3(),
      chaseLook: new THREE.Vector3(),
      chaseQuat: new THREE.Quaternion(),
      eyePos: new THREE.Vector3(),
      eyeQuat: new THREE.Quaternion(),
      m: new THREE.Matrix4(),
      blend: 0,
      panelClock: 1,
      initialised: false,
      frames: 0,
    }),
    [],
  );

  useFrame((_, rawDt) => {
    const dt = Math.min(rawDt, 0.1);
    const s = scratch;
    if (s.frames <= READY_AFTER_FRAMES && ++s.frames === READY_AFTER_FRAMES) onReady?.();
    perfFrame(rawDt, () => {
      if (getSettings().graphics !== 'low') flash('Low frame rate · try Settings → Graphics: a lower quality', 6000);
    });

    const { paused, view } = useUi.getState();
    if (!paused) {
      updatePilot(dt);
      flight.advance(pilot, dt);
      recordFrame(dt);
    }
    const p = parts.current;
    if (!p) return;
    nedToThree(flight.state.pos, s.pos);
    quatToThree(flight.state.q, s.quat);
    p.root.position.copy(s.pos);
    p.root.quaternion.copy(s.quat);
    const t = flight.telemetry;
    const rpm = engineRpm(flight.state.engine, t.airspeedKt);
    syncAircraft(
      p,
      {
        surfaces: flight.state.surfaces,
        engine: flight.state.engine,
        throttle: t.throttle,
        rpm,
        maxElevator: flight.ac.maxElevator,
        maxAileron: flight.ac.maxAileron,
        timeS: performance.now() / 1000,
      },
      paused ? 0 : dt,
    );

    // Instruments: redraw ~30 Hz from the same telemetry the HUD shows.
    s.panelClock += rawDt;
    if (s.panelClock >= 1 / 30) {
      s.panelClock = 0;
      getPanel().draw(t, {
        rpm,
        stall: t.stalled,
        brakes: t.brake > 0 && t.phase === 'ground',
        easyFlight: getSettings().easyFlight,
        lowAltitude: t.phase === 'airborne' && t.aglFt < 200 && t.verticalSpeedFpm < -300,
      });
      p.panelTexture.needsUpdate = true;
    }

    // --- chase camera: follows heading and some pitch, never roll ---
    s.fwd.set(0, 0, -1).applyQuaternion(s.quat);
    s.flatFwd.set(s.fwd.x, 0, s.fwd.z);
    if (s.flatFwd.lengthSq() < 1e-4) s.flatFwd.set(0, 0, -1);
    s.flatFwd.normalize().lerp(s.fwd, 0.35).normalize();
    s.desired.copy(s.pos).addScaledVector(s.flatFwd, -CHASE_DISTANCE).addScaledVector(UP, CHASE_HEIGHT);
    // Never below the ground (or a hillside) behind the aircraft.
    s.desired.y = Math.max(s.desired.y, terrainHeight(-s.desired.z, s.desired.x) + 1.2);
    // First frame, or the aircraft was teleported (restart, final approach): cut, don't glide 4 km.
    if (!s.initialised || s.chasePos.distanceToSquared(s.desired) > 150 * 150) {
      s.chasePos.copy(s.desired);
      s.initialised = true;
    }
    s.chasePos.lerp(s.desired, 1 - Math.exp(-dt * 5));
    s.chaseLook.copy(s.pos).addScaledVector(s.flatFwd, 8).addScaledVector(UP, 1);
    s.m.lookAt(s.chasePos, s.chaseLook, THREE.Object3D.DEFAULT_UP);
    s.chaseQuat.setFromRotationMatrix(s.m);

    // --- cockpit camera: rigidly attached to the airframe ---
    s.eyePos.copy(EYE).applyQuaternion(s.quat).add(s.pos);
    s.eyeQuat.copy(s.quat).multiply(EYE_TILT);

    // --- smooth transition between views ---
    const target = view === 'cockpit' ? 1 : 0;
    s.blend += Math.sign(target - s.blend) * Math.min(Math.abs(target - s.blend), rawDt * 2.2);
    const e = s.blend * s.blend * (3 - 2 * s.blend);
    camera.position.lerpVectors(s.chasePos, s.eyePos, e);
    camera.quaternion.slerpQuaternions(s.chaseQuat, s.eyeQuat, e);
    const fov = THREE.MathUtils.lerp(55, 68, e);
    if (Math.abs(camera.fov - fov) > 0.01) {
      camera.fov = fov;
      camera.updateProjectionMatrix();
    }
    p.cockpit.visible = e > 0.5;
    p.exterior.visible = e <= 0.5;

    // --- sun shadow box follows the aircraft ---
    if (sun.current) {
      sun.current.position.copy(s.pos).addScaledVector(SUN_DIR, 400);
      sun.current.target.position.copy(s.pos);
      sun.current.target.updateMatrixWorld();
    }
  });

  return (
    <>
      {/* Remount on aircraft change so geometry and livery rebuild from the new definition. */}
      <Aircraft key={aircraftId} ref={parts} def={aircraftById(aircraftId)} />
      <directionalLight
        ref={sun}
        intensity={2.6}
        color="#fff3df"
        castShadow={profile.shadows}
        shadow-mapSize={[profile.shadowMapSize, profile.shadowMapSize]}
        shadow-camera-left={-40}
        shadow-camera-right={40}
        shadow-camera-top={40}
        shadow-camera-bottom={-40}
        shadow-camera-near={1}
        shadow-camera-far={900}
        shadow-bias={-0.0004}
      />
    </>
  );
}

/** Subtle and restrained: glow on lights and the sun, gently darker corners. */
function Effects() {
  return (
    <EffectComposer multisampling={4}>
      <Bloom mipmapBlur luminanceThreshold={0.92} luminanceSmoothing={0.15} intensity={0.35} />
      <Vignette offset={0.32} darkness={0.42} />
      <ToneMapping mode={ToneMappingMode.ACES_FILMIC} />
    </EffectComposer>
  );
}

export function FlightScene({ onReady }: { onReady?: () => void }) {
  const quality = useSettings((s) => s.graphics);
  const postFx = useSettings((s) => s.postFx);
  const profile = GRAPHICS[quality];
  const effects = profile.postFx && postFx;
  return (
    // Keyed by quality: antialiasing and shadow types can only be set when the GL context is created.
    <Canvas
      key={quality}
      shadows={profile.shadows}
      dpr={profile.dpr}
      gl={{ logarithmicDepthBuffer: true, antialias: profile.antialias && !effects, powerPreference: 'high-performance' }}
      camera={{ fov: 55, near: 0.1, far: 40000, position: [0, 4, 16] }}
      onCreated={({ gl }) => {
        gl.toneMapping = THREE.ACESFilmicToneMapping;
        gl.toneMappingExposure = 0.95;
      }}
    >
      <color attach="background" args={[HAZE]} />
      <World profile={profile} />
      <FlightDriver profile={profile} onReady={onReady} />
      {effects && <Effects />}
    </Canvas>
  );
}
