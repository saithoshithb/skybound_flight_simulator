# Hardware: wiring and assumptions

## Parts

| Part | Notes |
| --- | --- |
| ESP32 dev board (ESP32-WROOM-32 "DevKit V1" or similar) | Any board with the classic ESP32. ESP32-S3/C3 also work but change `PIN_SDA`/`PIN_SCL` and the board selected in Arduino. |
| GY-521 MPU-6050 breakout | Has its own 3.3 V regulator and I²C pull-ups. |
| 4 jumper wires, USB **data** cable | Charge-only cables are the most common reason the port doesn't appear. |
| Optional: a rigid base (perfboard, plastic box) | The sensor must not wobble relative to whatever you hold. |

## Wiring (GY-521 → ESP32)

```
GY-521        ESP32 DevKit
------        ------------
VCC   ─────── 3V3
GND   ─────── GND
SCL   ─────── GPIO 22
SDA   ─────── GPIO 21
AD0   ─────── (leave unconnected → I²C address 0x68; tie to 3V3 for 0x69)
INT   ─────── (unused)
XDA/XCL ───── (unused)
```

Power the GY-521 from **3V3**, not 5V/VIN: the breakout's pull-ups then sit at 3.3 V, which is what the ESP32's GPIOs expect.

## Mounting

Default firmware assumption: the GY-521 lies **flat, components facing up**, with the printed **+X arrow pointing forward** (away from you, toward the screen) and the board's +Y arrow pointing **left**.

If you mount it differently, edit the `MOUNT_*` constants at the top of `firmware/mpu_flight_controller/mpu_flight_controller.ino`. For example, a board rotated 90° so that +Y points forward:

```cpp
static const int MOUNT_FWD_AXIS = 1, MOUNT_FWD_SIGN = 1;    // chip Y is forward
static const int MOUNT_LEFT_AXIS = 0, MOUNT_LEFT_SIGN = -1; // chip −X is left
static const int MOUNT_UP_AXIS = 2, MOUNT_UP_SIGN = 1;
```

Minor sign mistakes can also be fixed with the browser's Invert switches, but a correct firmware mapping keeps the 3D preview honest.

## Flashing the firmware

The sketch uses only `Wire`, which comes with the ESP32 core. You don't need any extra libraries.

1. Arduino IDE → Boards Manager → install **esp32 by Espressif Systems** (tested to compile with core 3.3.12).
2. Open `firmware/mpu_flight_controller/mpu_flight_controller.ino`.
3. Tools → Board → **ESP32 Dev Module**; Tools → Port → your board's COM port.
4. Upload. If the upload stalls at `Connecting...`, hold the board's **BOOT** button until it starts.
5. Open Serial Monitor at **115200** baud to sanity-check. You should see a `hello`, then `calibrating`, then `ready`, then ~50 `imu` lines per second. **Close Serial Monitor before connecting the browser**: only one program can own the port.

Command-line alternative (the IDE ships arduino-cli):

```bash
arduino-cli compile --fqbn esp32:esp32:esp32 firmware/mpu_flight_controller
arduino-cli upload  --fqbn esp32:esp32:esp32 -p COM5 firmware/mpu_flight_controller
```

## Windows driver

Most DevKits use a CP2102 or CH340 USB-UART chip. If no new COM port appears in Device Manager when you plug in, install the Silicon Labs CP210x or WCH CH340 driver.

## What the firmware does

1. Configures the MPU-6050: ±4 g, ±500 °/s, 44 Hz digital low-pass, 200 Hz sample rate.
2. **Gyro calibration**: averages 2 s of gyro readings at power-up (and on the `CAL` command). If the board moved, it retries. After 5 tries it accepts the steadiest attempt only if that was hand tremor (under 3 °/s); during real movement it keeps waiting and sends no angles. It also measures how the accelerometer reads gravity at rest and treats that as 1 g, which corrects clone chips that read 10–20% off. Keep the controller resting on the desk for about 2 s after plugging in or connecting.
3. **Fusion**: complementary filter at 200 Hz (time constant 0.5 s) with proper Euler-rate kinematics. The accelerometer's influence fades to zero when the total acceleration differs from 1 g by more than 0.15 g, so shaking or swinging the controller doesn't corrupt the horizon.
4. Streams NDJSON at 50 Hz (see [PROTOCOL.md](PROTOCOL.md)).
5. If the sensor disappears (10 consecutive I²C failures), it reports `sensor_error`, stops sending attitude, and retries once a second.

## Known limitations

- **Yaw drifts.** The MPU-6050 has no magnetometer, so yaw is integrated gyro rate and wanders after bias calibration. Measured on the development board: 2.2° in 24 s sitting still (about 5 °/min), and more as temperature changes. The game therefore uses pitch and roll as primary controls, and yaw is off by default. An MPU-9250, ICM-20948, or a separate QMC5883L compass would give a usable absolute heading.
- **Opening the port resets most ESP32 dev boards** (DTR/RTS auto-reset), so every browser connect triggers a fresh 2 s calibration. The app waits for `ready` before using any data.
- Pitch beyond about ±80° approaches gimbal lock, where roll becomes ill-defined. A hand controller rarely goes there.
