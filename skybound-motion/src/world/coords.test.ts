import { describe, expect, it } from 'vitest';
import * as THREE from 'three';
import { DEG, fromEuler } from '../flight/math';
import { nedToThree, quatToThree } from './coords';

const fwd = (q: THREE.Quaternion) => new THREE.Vector3(0, 0, -1).applyQuaternion(q);
const right = (q: THREE.Quaternion) => new THREE.Vector3(1, 0, 0).applyQuaternion(q);

describe('NED → three.js', () => {
  it('maps positions', () => {
    expect(nedToThree([100, 20, -50]).toArray()).toEqual([20, 50, -100]);
  });
  it('heading east points the model along +x', () => {
    const f = fwd(quatToThree(fromEuler(0, 0, 90 * DEG)));
    expect(f.x).toBeCloseTo(1);
    expect(f.z).toBeCloseTo(0);
  });
  it('pitch up raises the nose, right roll lowers the right wing', () => {
    expect(fwd(quatToThree(fromEuler(0, 30 * DEG, 0))).y).toBeCloseTo(0.5);
    expect(right(quatToThree(fromEuler(30 * DEG, 0, 0))).y).toBeCloseTo(-0.5);
  });
});
