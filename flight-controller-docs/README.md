# Skybound motion flight controller: documentation

An ESP32 reads an MPU-6050 (GY-521 breakout), works out the controller's pitch, roll and yaw on the board, and streams them over USB to the Skybound flight simulator in the browser. Firmware version: **mpu-flight/1.1.2**, protocol version **1**.

## What's in this folder

| File | Read it for |
| --- | --- |
| [HARDWARE.md](HARDWARE.md) | Parts list, wiring, mounting, flashing the firmware, Windows drivers, what the firmware does, known limitations |
| [USING-THE-CONTROLLER.md](USING-THE-CONTROLLER.md) | Connecting in the browser, calibration and recentering, response settings, troubleshooting |
| [AXES.md](AXES.md) | Which way is positive pitch, roll and yaw, from the chip to the game |
| [PROTOCOL.md](PROTOCOL.md) | The NDJSON messages the board sends, the commands it accepts, link-health rules |
| [TEST-RESULTS.md](TEST-RESULTS.md) | What was measured on the real hardware, and what still hasn't been verified |

## Quick reference

**Wiring (GY-521 → ESP32 DevKit)**

| GY-521 | ESP32 |
| --- | --- |
| VCC | 3V3 (not 5V) |
| GND | GND |
| SDA | GPIO 21 |
| SCL | GPIO 22 |
| AD0, INT, XDA, XCL | not connected |

**Mounting:** board flat, components up, the printed **+X arrow pointing forward** (toward the screen).

**Firmware settings** (top of `mpu_flight_controller.ino`)

| Setting | Value |
| --- | --- |
| Serial baud | 115200 |
| I²C | 400 kHz, address 0x68 |
| Fusion loop | 200 Hz complementary filter, 0.5 s time constant |
| Output rate | 50 Hz |
| Gyro calibration | 2 s still at start-up and on `CAL`; up to 5 strict tries, then a tremor-only fallback (under 3 °/s) |
| Sensor ranges | ±4 g, ±500 °/s, 44 Hz low-pass |

**Commands you can send** (one per line): `CAL` re-measures gyro bias, `ZEROYAW` resets yaw to 0, `HELLO` asks the board to identify itself.

**Sign convention:** +pitch = nose up (front edge raised), +roll = right wing down (right edge lowered), +yaw = nose right.

## Files that go with these docs

- `firmware/mpu_flight_controller/mpu_flight_controller.ino`: the flight controller sketch (only needs `Wire`, which comes with the ESP32 core).
- `firmware/i2c_diagnose/i2c_diagnose.ino`: an I²C scanner to check the MPU-6050 is wired and answering.
- `tools/verify-device.ps1`: reads the board over USB without the browser and reports rate, drops, noise and drift.

These are in `skybound-flight-controller.zip`.
