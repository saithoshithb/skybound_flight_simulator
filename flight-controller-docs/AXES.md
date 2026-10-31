# Coordinate systems and sign conventions

Three frames are involved. All angles in the protocol and settings are **degrees**; three.js gets radians.

## 1. MPU-6050 chip frame (raw registers)

Right-handed, printed on the GY-521: +X and +Y in the board plane, +Z out of the component side. Gyro rates are positive **counter-clockwise** about each axis (right-hand rule). At rest, the accelerometer reads **+1 g on the axis pointing up**: it measures the reaction to gravity.

## 2. Controller frame (firmware, after `MOUNT_*` remap)

| Axis | Direction |
| --- | --- |
| x | forward (toward the screen) |
| y | left |
| z | up |

The firmware converts this to the **aerospace body frame** (x forward, y right, z down) for the Euler-rate equations: body rates are `p = gx`, `q = −gy` and `r = −gz`.

## 3. Aircraft attitude (protocol and game)

Standard aviation convention, applied as yaw, then pitch, then roll (intrinsic Z-Y-X):

| Angle | Positive when | Physical controller |
| --- | --- | --- |
| **pitch** θ | nose up | front edge raised |
| **roll** φ | right wing down | right edge lowered |
| **yaw** ψ | nose right | twisted clockwise seen from above |

From gravity alone (controller frame accelerations):

```
pitch = atan2(ax, sqrt(ay² + az²))
roll  = atan2(ay, az)
```

## Control commands

`src/control/mapping.ts` turns attitude *relative to neutral* into normalized stick commands in [−1, 1]:

| Command | Source | + means |
| --- | --- | --- |
| elevator | pitch − neutral pitch | nose-up demand (pull) |
| aileron | roll − neutral roll | roll-right demand |
| rudder | off / yaw washout / yaw rate | yaw-right demand |

The chain is: angle ÷ max-angle → dead zone (rescaled, no step) → expo → × sensitivity → clamp → invert → low-pass (time constant) → slew limit → × engagement ramp.

These are **inputs**, not attitude. The flight model (`src/flight/dynamics.ts`) turns elevator into pitching moment through the aircraft's dynamics: holding the controller tilted 10° nose-up keeps the aircraft pitching up, the way a real stick held back does. It does not freeze the aircraft at 10°.

With **bank assist** on (the default), the roll command becomes a target bank angle (±45° at full tilt). A PD law turns bank error into aileron, so the aircraft still rolls only through aileron moments. With it off, roll command is direct aileron, which commands a roll *rate*.

Physics frames: world NED (x north, y east, z down; ground at z = 0), body FRD. Rendering converts both to three.js with one fixed rotation (`src/world/coords.ts`, tested).

## three.js rendering

three.js uses +X right, +Y up and **−Z forward**. Aircraft attitude maps to an Euler with order `'YXZ'`:

```ts
new THREE.Euler(+pitch, −yaw, −roll, 'YXZ')
```

This was checked in the running app: pitch +30° sends the nose vector to (0, +0.5, −0.87), roll +30° sends the right wingtip to (0.87, −0.5, 0), and yaw +30° sends the nose to (+0.5, 0, −0.87).
