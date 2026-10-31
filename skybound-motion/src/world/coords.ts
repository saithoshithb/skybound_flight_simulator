import * as THREE from 'three';
import type { Quat, V3 } from '../flight/math';

/**
 * Physics uses NED world / FRD body; three.js uses x right (east), y up,
 * −z forward (north). The same fixed rotation M maps both:
 *   three = M · ned,  M = [[0,1,0],[0,0,−1],[−1,0,0]]
 * so an object quaternion is q₃ = M · q_ned · M⁻¹.
 */
const M = new THREE.Matrix4().set(0, 1, 0, 0, 0, 0, -1, 0, -1, 0, 0, 0, 0, 0, 0, 1);
const qM = new THREE.Quaternion().setFromRotationMatrix(M);
const qMinv = qM.clone().invert();
const tmp = new THREE.Quaternion();

export function nedToThree(v: V3, out = new THREE.Vector3()) {
  return out.set(v[1], -v[2], -v[0]);
}

export function quatToThree(q: Quat, out = new THREE.Quaternion()) {
  tmp.set(q[1], q[2], q[3], q[0]);
  return out.copy(qM).multiply(tmp).multiply(qMinv);
}
