# Hardware test results

Tested 2026-10-29/30 on an ESP32 DevKit (CH340 USB chip, COM8) with a GY-521 clone, firmware 1.1.2.

## Measured

- **Stream:** 2,102 samples at 50.0 Hz, 0 sequence gaps, 0 malformed lines, 0 values out of range.
- **Gyro calibration** passes on the first try when resting: 0.06–0.50 °/s noise against a 0.8 °/s limit.
- **Noise at rest:** pitch and roll ±0.01–0.02°. Yaw drift 0.27° over 42 s.
- **Accelerometer scale:** this GY-521 reads gravity 12–16% high near level. The firmware's rest-magnitude normalization brings it to 1.00 g, so the accelerometer correction stays active.
- **Past vertical:** tipping past vertical stays within the protocol's ±90° pitch (tested on 1.1.1, which had the same fusion code).
- **In Edge with Web Serial:** connect, calibration, recenter, and all four axis signs (pitch ±, roll ±) were confirmed by hand. The 3D model follows.
- **In flight:** tilt control for turning and climbing was confirmed working after the auto-recenter fix.
- **Port reset:** opening the port usually resets this board (DTR). Once, it didn't. `verify-device.ps1 -Recalibrate` covers that case.

## Tested without hardware

- 29 unit tests cover the browser side: protocol parsing and rejection, a firmware-format line, sequence-wrap and restart handling, line framing, link timing (`awaiting → live → stale → lost → live`), dead zone, expo, inversion, slew limit, engage ramp, fade on link loss, the ±180° roll wrap, yaw washout, and neutral capture.
- Against the simulated controller: with 30% corrupted lines plus 30% dropped lines the link stayed live; unplugging at full deflection faded the command from 1.0 to 0 in about 300 ms; connecting while tilted held output at 0 through calibration, then ramped in over 1 s.
- The firmware compiles for `esp32:esp32:esp32` with core 3.3.12 (23% of flash).

## Known hardware limitations

- The accelerometer has a Z-axis offset of about +0.12 g. Rest-magnitude normalization only corrects it near the calibration pose. Far from level (upside down), the accelerometer is ignored and attitude runs on the gyro alone until the controller comes back. A 6-face accelerometer calibration would fix this and was deferred.
- Yaw drifts (no magnetometer), so yaw is off by default.

## Not yet verified

- Unplug and replug auto-reconnect with real USB.
- Long sessions (thermal drift).
- The tremor-only calibration fallback (every real run calibrated cleanly, so it never triggered).
- Whether the 8% dead zone and 100 ms smoothing are the best fit for this sensor's noise over a long flight.
