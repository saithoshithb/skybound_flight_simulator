import { Sky } from '@react-three/drei';
import { useLayoutEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import type { GraphicsProfile } from '../settings/graphics';
import { terrainHeight } from './terrain';

/**
 * Airfield and surroundings. Runway 36/18: 1,200 m × 30 m, threshold of 36
 * at the origin, running north (three.js −z). Everything is procedural so the
 * app needs no downloaded assets.
 */
export const RUNWAY = { lengthM: 1200, widthM: 30 };
export const SUN_DIR = new THREE.Vector3(-0.45, 0.62, 0.35).normalize();
export const HAZE = '#c9d5df';

function rng(seed: number) {
  return () => {
    seed = (seed * 1664525 + 1013904223) >>> 0;
    return seed / 4294967296;
  };
}

function noiseTexture(size: number, base: [number, number, number], spread: number, seed: number, repeat: number) {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const ctx = c.getContext('2d')!;
  const img = ctx.createImageData(size, size);
  const r = rng(seed);
  for (let i = 0; i < size * size; i++) {
    const n = (r() - 0.5) * spread;
    img.data[i * 4] = base[0] + n;
    img.data[i * 4 + 1] = base[1] + n;
    img.data[i * 4 + 2] = base[2] + n * 0.7;
    img.data[i * 4 + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  // Large soft patches so the ground isn't uniform from altitude.
  for (let i = 0; i < 40; i++) {
    ctx.fillStyle = `rgba(${r() > 0.5 ? '70,95,45' : '120,130,70'},${0.05 + r() * 0.08})`;
    ctx.beginPath();
    ctx.arc(r() * size, r() * size, 8 + r() * size * 0.25, 0, Math.PI * 2);
    ctx.fill();
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(repeat, repeat);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

const TERRAIN_SIZE_M = 32000;

/**
 * Rolling terrain built from the same `terrainHeight` the flight model
 * collides with. Vertex colours shade by height and slope: meadow in the
 * valleys, darker woodland on the slopes, dry grass and rock on the tops.
 */
function Ground({ segments }: { segments: number }) {
  const tex = useMemo(() => {
    // Neutral-grey detail so vertex colours carry the hue.
    const t = noiseTexture(256, [150, 150, 150], 30, 7, 900);
    return t;
  }, []);
  const geometry = useMemo(() => {
    const g = new THREE.PlaneGeometry(TERRAIN_SIZE_M, TERRAIN_SIZE_M, segments, segments);
    g.rotateX(-Math.PI / 2); // now x = east, z = −north, y = up
    const pos = g.attributes.position as THREE.BufferAttribute;
    const colors = new Float32Array(pos.count * 3);
    const meadow = new THREE.Color('#6d8a4c');
    const wood = new THREE.Color('#3f5c36');
    const dry = new THREE.Color('#8f8a62');
    const rock = new THREE.Color('#8a8578');
    const c = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i);
      const z = pos.getZ(i);
      const h = terrainHeight(-z, x);
      pos.setY(i, h);
      const slope = Math.min(1, Math.hypot(terrainHeight(-z + 40, x) - h, terrainHeight(-z, x + 40) - h) / 18);
      const n = Math.sin(x * 0.0017) * Math.cos(z * 0.0021) * 0.5 + 0.5; // broad patchiness
      c.copy(meadow).lerp(wood, Math.min(1, slope * 1.4 + (h > 25 ? 0.35 : 0) + n * 0.25));
      if (h > 170) c.lerp(dry, Math.min(1, (h - 170) / 90));
      if (h > 250 || slope > 0.85) c.lerp(rock, Math.min(1, Math.max((h - 250) / 80, (slope - 0.85) / 0.15)));
      colors.set([c.r, c.g, c.b], i * 3);
    }
    g.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    g.computeVertexNormals();
    return g;
  }, [segments]);
  return (
    <>
      <mesh geometry={geometry} receiveShadow>
        <meshStandardMaterial map={tex} vertexColors roughness={1} />
      </mesh>
      {/* Beyond the terrain tile: flat plain under the haze to the horizon */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -2, 0]}>
        <planeGeometry args={[120000, 120000]} />
        <meshStandardMaterial color="#6d8a4c" roughness={1} />
      </mesh>
    </>
  );
}

function Runway() {
  const asphalt = useMemo(() => {
    const t = noiseTexture(128, [64, 66, 68], 22, 3, 1);
    t.repeat.set(4, 160);
    return t;
  }, []);
  const paint = useMemo(() => new THREE.MeshStandardMaterial({ color: '#e9e8e2', roughness: 0.8 }), []);
  const { lengthM: L, widthM: W } = RUNWAY;

  const marks = useMemo(() => {
    const m: { x: number; z: number; w: number; l: number }[] = [];
    // Centreline: 30 m stripes, 20 m gaps, clear of the threshold areas.
    for (let d = 90; d < L - 90; d += 50) m.push({ x: 0, z: -(d + 15), w: 0.9, l: 30 });
    // Distance from the runway-36 threshold (z = 0) to a marking's centre.
    for (const fromThreshold of [21, L - 21]) {
      // Threshold "piano keys": 30 m long, starting 6 m in from each end.
      for (let i = 0; i < 8; i++) m.push({ x: (i < 4 ? -1 : 1) * (2.5 + (i % 4) * 3.2), z: -fromThreshold, w: 1.8, l: 30 });
    }
    // Aiming-point blocks 300 m in from each end.
    for (const fromThreshold of [300, L - 300]) for (const s of [-1, 1]) m.push({ x: s * 6, z: -fromThreshold, w: 4, l: 45 });
    // Edge lines
    for (const s of [-1, 1]) m.push({ x: s * (W / 2 - 1), z: -L / 2, w: 0.9, l: L });
    return m;
  }, [L, W]);

  return (
    <group>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.03, -L / 2]} receiveShadow>
        <planeGeometry args={[W, L + 60]} />
        <meshStandardMaterial map={asphalt} roughness={0.92} polygonOffset polygonOffsetFactor={-1} />
      </mesh>
      {marks.map((k, i) => (
        <mesh key={i} rotation={[-Math.PI / 2, 0, 0]} position={[k.x, 0.06, k.z]} material={paint} receiveShadow>
          <planeGeometry args={[k.w, k.l]} />
        </mesh>
      ))}
      {/* Taxiway to the apron and two hangars on the east side */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[45, 0.03, -260]} receiveShadow>
        <planeGeometry args={[60, 15]} />
        <meshStandardMaterial map={asphalt} roughness={0.92} polygonOffset polygonOffsetFactor={-1} />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[110, 0.03, -250]} receiveShadow>
        <planeGeometry args={[80, 110]} />
        <meshStandardMaterial color="#6d6f70" roughness={0.95} polygonOffset polygonOffsetFactor={-1} />
      </mesh>
      {[
        [125, -210],
        [125, -290],
      ].map(([x, z], i) => (
        <group key={i} position={[x, 0, z]}>
          <mesh position={[0, 5, 0]} castShadow receiveShadow>
            <boxGeometry args={[26, 10, 32]} />
            <meshStandardMaterial color={i ? '#8b9196' : '#a39c8f'} roughness={0.85} />
          </mesh>
          <mesh position={[-13.05, 4, 0]}>
            <boxGeometry args={[0.1, 8, 26]} />
            <meshStandardMaterial color="#3a3e42" roughness={0.6} />
          </mesh>
        </group>
      ))}
      {/* Windsock */}
      <group position={[-30, 0, -150]}>
        <mesh position={[0, 3, 0]}>
          <cylinderGeometry args={[0.06, 0.06, 6, 8]} />
          <meshStandardMaterial color="#8a8f93" />
        </mesh>
        <mesh position={[0, 5.8, 1.1]} rotation={[Math.PI / 2 + 0.15, 0, 0]}>
          <coneGeometry args={[0.35, 2.2, 12, 1, true]} />
          <meshStandardMaterial color="#d26a2c" side={THREE.DoubleSide} />
        </mesh>
      </group>
    </group>
  );
}

function Trees({ count: COUNT }: { count: number }) {
  const trunks = useRef<THREE.InstancedMesh>(null);
  const crowns = useRef<THREE.InstancedMesh>(null);
  useLayoutEffect(() => {
    const r = rng(42);
    const m = new THREE.Matrix4();
    const color = new THREE.Color();
    let n = 0;
    while (n < COUNT) {
      // Clusters: pick a grove centre, then scatter around it.
      const cx = (r() - 0.5) * 9000;
      const cz = (r() - 0.5) * 9000 - 600;
      const groveSize = 20 + Math.floor(r() * 60);
      for (let i = 0; i < groveSize && n < COUNT; i++) {
        const x = cx + (r() - 0.5) * 260;
        const z = cz + (r() - 0.5) * 260;
        // Keep the runway strip, approach paths and apron clear.
        if (Math.abs(x) < 160 && z < 900 && z > -2100) continue;
        const h = 7 + r() * 9;
        const ground = terrainHeight(-z, x);
        m.compose(new THREE.Vector3(x, ground + h * 0.22, z), new THREE.Quaternion(), new THREE.Vector3(1, h * 0.44, 1));
        trunks.current!.setMatrixAt(n, m);
        m.compose(new THREE.Vector3(x, ground + h * 0.62, z), new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), r() * 6), new THREE.Vector3(h * 0.32, h * 0.75, h * 0.32));
        crowns.current!.setMatrixAt(n, m);
        crowns.current!.setColorAt(n, color.setHSL(0.25 + r() * 0.07, 0.35 + r() * 0.15, 0.2 + r() * 0.08));
        n++;
      }
    }
    trunks.current!.instanceMatrix.needsUpdate = true;
    crowns.current!.instanceMatrix.needsUpdate = true;
    if (crowns.current!.instanceColor) crowns.current!.instanceColor.needsUpdate = true;
  }, []);
  return (
    <>
      <instancedMesh ref={trunks} args={[undefined, undefined, COUNT]} castShadow>
        <cylinderGeometry args={[0.18, 0.25, 1, 5]} />
        <meshStandardMaterial color="#4b3a2a" roughness={1} />
      </instancedMesh>
      <instancedMesh ref={crowns} args={[undefined, undefined, COUNT]} castShadow receiveShadow>
        <icosahedronGeometry args={[1, 0]} />
        <meshStandardMaterial roughness={0.95} flatShading />
      </instancedMesh>
    </>
  );
}

function Hills() {
  const hills = useMemo(() => {
    const r = rng(9);
    return Array.from({ length: 46 }, (_, i) => {
      const a = (i / 46) * Math.PI * 2 + r() * 0.1;
      const d = 17000 + r() * 8000; // beyond the terrain tile
      return { x: Math.cos(a) * d, z: Math.sin(a) * d - 600, h: 600 + r() * 900, w: 2600 + r() * 3000 };
    });
  }, []);
  return (
    <>
      {hills.map((h, i) => (
        <mesh key={i} position={[h.x, h.h / 2 - 5, h.z]} scale={[h.w, h.h, h.w * 0.8]}>
          <coneGeometry args={[1, 1, 7, 1]} />
          <meshStandardMaterial color="#6f8162" roughness={1} flatShading />
        </mesh>
      ))}
    </>
  );
}

function cloudTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const ctx = c.getContext('2d')!;
  const g = ctx.createRadialGradient(64, 64, 4, 64, 64, 64);
  g.addColorStop(0, 'rgba(255,255,255,0.95)');
  g.addColorStop(0.5, 'rgba(250,250,252,0.55)');
  g.addColorStop(1, 'rgba(245,247,250,0)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function Clouds({ count }: { count: number }) {
  const sprites = useMemo(() => {
    const r = rng(17);
    const tex = cloudTexture();
    const out: { pos: [number, number, number]; s: number; mat: THREE.SpriteMaterial }[] = [];
    for (let c = 0; c < count; c++) {
      const cx = (r() - 0.5) * 14000;
      const cz = (r() - 0.5) * 14000;
      const cy = 900 + r() * 500;
      const puffs = 5 + Math.floor(r() * 6);
      for (let p = 0; p < puffs; p++) {
        const dy = r() * 120;
        // Shaded bases, bright tops: lower puffs are tinted grey-blue.
        const shade = new THREE.Color('#b9c2cc').lerp(new THREE.Color('#ffffff'), Math.min(1, dy / 90));
        const mat = new THREE.SpriteMaterial({ map: tex, color: shade, transparent: true, depthWrite: false, opacity: 0.6 + r() * 0.25, fog: true });
        const s = 220 + r() * 300 + (dy > 60 ? 80 : 0);
        out.push({ pos: [cx + (r() - 0.5) * 520, cy + dy, cz + (r() - 0.5) * 320], s, mat });
      }
    }
    return out;
  }, []);
  return (
    <>
      {sprites.map((p, i) => (
        <sprite key={i} position={p.pos} scale={[p.s, p.s * 0.55, 1]} material={p.mat} />
      ))}
    </>
  );
}

export function World({ profile }: { profile: GraphicsProfile }) {
  return (
    <>
      <Sky sunPosition={SUN_DIR.clone().multiplyScalar(1000).toArray()} turbidity={5} rayleigh={1.2} mieCoefficient={0.004} mieDirectionalG={0.8} distance={40000} />
      <fog attach="fog" args={[HAZE, 2500, profile.fogFar]} />
      <hemisphereLight args={['#dfe9f2', '#5d6b4a', 1.0]} />
      <Ground segments={profile.terrainSegments} />
      <Runway />
      <Trees key={profile.trees} count={profile.trees} />
      <Hills />
      <Clouds key={profile.clouds} count={profile.clouds} />
    </>
  );
}
