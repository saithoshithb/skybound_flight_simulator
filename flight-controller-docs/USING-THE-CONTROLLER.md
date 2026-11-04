# Using the controller

## Browser requirements

- Web Serial works only in Chromium-based desktop browsers (Chrome, Edge, Opera 89+). Firefox, Safari and mobile browsers don't support it; the app offers **Simulated** instead.
- The page must be a **secure context**: `http://localhost` works, a LAN address like `http://192.168.x.x` does not. Use HTTPS for anything other than localhost.
- The port chooser only opens from a click. The browser remembers the permission per site, so **Reconnect** reopens a known port without prompting. If the same USB device is unplugged and plugged back in, the app reopens it automatically.
- Only one program can hold a COM port. Close the Arduino Serial Monitor (and `verify-device.ps1`) first.

## First connection

1. Wire and flash the board: [HARDWARE.md](../skybound-motion/docs/HARDWARE.md).
2. Put the controller on a still surface. In **Controller setup**, choose **ESP32 · USB**, click **Connect**, and pick the board's COM port.
3. Opening the port resets most ESP32 boards, which then calibrate the gyro for about 2 s. Keep the controller still. The setup screen says when data is live.
4. Hold the controller in your comfortable hands-off pose and click **Recenter** (or press **R**). This captures neutral pitch and roll over 1 s, and is rejected if you move.
5. Tilt it. The 3D model should follow: nose up shows **+pitch**, right edge down shows **+roll**. If an axis is reversed, fix the `MOUNT_*` constants in the firmware or use **Invert**.

**Calibrate gyro** sends `CAL` to the board to re-measure gyro bias. Use it if the attitude creeps while the controller sits still. **Recenter** only changes the browser-side neutral.

## Neutral at flight start

Every time a flight starts or restarts, the game captures the pose you're holding as "straight and level" (after a 1.2 s pause). Hold the controller the way you'll fly. If you're moving, the capture is rejected, the old neutral stays, and the game asks you to hold still and press **R**.

## Response settings

Set in **Controller setup → Response**. Saved in the browser.

| Setting | Default | What it does |
| --- | --- | --- |
| Pitch full deflection | 30° tilt | Tilt that gives a full climb or descent command |
| Roll full deflection | 35° tilt | Tilt that gives a full turn command |
| Sensitivity | 1.0 | Multiplies the tilt before shaping |
| Dead zone | 8% | Small tilts around neutral are ignored |
| Expo | 0.5 | Softens the response near center; full tilt is unchanged |
| Smoothing | 100 ms | Filters hand tremor |
| Slew limit | 3 per second | Caps how fast a command can change |
| Yaw | Off | Yaw drifts on this sensor, so it isn't used by default |
| Invert pitch / roll | Off | Flips an axis |

When the controller connects, its output ramps in over about 1 s. If the data stops, the commands fade to zero in about 300 ms.

## In flight

With **Easy flight** on (the default), tilt sideways to bank and turn (up to 35°), tilt forward or back to descend or climb (up to 1000 fpm), and let go to level out. Press **+** once on the runway to take off. **R** recenters at any time.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| No COM port appears | A charge-only USB cable, or a missing CP210x/CH340 driver (see [HARDWARE.md](../skybound-motion/docs/HARDWARE.md)). |
| Port picker never opens | The browser has no Web Serial (Firefox, Safari, or an embedded browser pane). Use Chrome or Edge. |
| The port won't open | Another program has it: close Arduino Serial Monitor or `verify-device.ps1`. |
| Plane turns or climbs by itself | Check the source shown in the HUD and setup screen says **ESP32 · USB**, not **Simulated**. Then recenter (**R**) while holding your normal pose. |
| Attitude creeps while the controller is still | Gyro bias has changed (often warm-up). Click **Calibrate gyro** with the controller resting. |
| Stuck on "Device is calibrating" | The board keeps detecting movement. Put it on a desk until it reports ready. |
| "MPU-6050 not found" | Wiring or address. Flash `i2c_diagnose.ino`: it should find the sensor at 0x68. |
| Connected but no data after opening | This board sometimes doesn't reset on connect. Run `verify-device.ps1 -Recalibrate`, or click **Calibrate gyro**. |

## Checking the board without the browser

```bash
powershell -ExecutionPolicy Bypass -File tools/verify-device.ps1
```

It finds the ESP32's port, prints calibration status live, and reports sample rate, dropped and malformed lines, out-of-range values, noise at rest and yaw drift. Options: `-Port COM8`, `-Seconds 30`, `-Recalibrate`, `-SaveTo log.ndjson`, `-File log.ndjson` (analyze a saved log).
