# Skybound Motion

A browser flight simulator flown by tilting an MPU-6050 on an ESP32. React, TypeScript, Three.js / React Three Fiber, Zustand, Tailwind.

**Current milestone (2 of 4):** a flyable aircraft.
- Milestone 1 delivered the sensor pipeline and controller setup screen: firmware fusion over USB, Web Serial, calibration and live readouts.
- This milestone adds a 6-DOF physics flight model, keyboard fallback, and a first airfield scene with chase and cockpit cameras and a flight-data HUD.
- Next: cockpit instruments and a more detailed world.

## Quick start

Requirements: Node.js 20+ (installed: 26 LTS), and desktop **Chrome, Edge or Opera** for Web Serial.

```bash
npm install
npm run dev
```

Open <http://localhost:5173>. Choose **Simulated** to try everything without hardware, or **ESP32 · USB** with the board plugged in.

| Script | |
| --- | --- |
| `npm run dev` | Dev server on localhost:5173 |
| `npm test` | Unit tests (protocol, framing, hub timeouts, shaping, calibration) |
| `npm run typecheck` | `tsc` strict |
| `npm run build` | Production build to `dist/` |

## Using the controller

1. Wire and flash the board: [docs/HARDWARE.md](docs/HARDWARE.md).
2. Place it on a still surface, select **ESP32 · USB**, click **Connect**, and pick the board's COM port in the browser prompt.
3. The board resets and calibrates its gyro (about 2 s). Keep it still. The view says when data is live.
4. Hold the controller in your comfortable "hands-off" pose and click **Recenter** (or press **R**). This captures neutral pitch and roll and is rejected if you moved during the 1 s capture.
5. Tilt it. The 3D model should follow, nose-up should show **+pitch**, and right-edge-down should show **+roll**. If an axis is reversed, fix the mounting constants in the firmware or use **Invert**.
6. Tune sensitivity, dead zone, expo and smoothing in **Response**. Settings and neutral persist in the browser.

**Calibrate gyro** sends `CAL` to the device to re-measure gyro bias. Use it if the attitude creeps while the controller sits still. **Recenter** only changes the browser-side neutral.

## Flying

Click **Fly →**. You start on the threshold of runway 36 (1,200 m, heading north) with the engine idling.

### Easy flight (default)

For first-time pilots, Easy flight is on by default; toggle it in the pause menu.

| Do this | And the aircraft |
| --- | --- |
| Press **+** (or PgUp) once | takes off by itself: full power, lifts off at about 70 kt, climbs to 400 ft and levels off |
| Tilt left / right | banks and turns; small tilt = gentle turn, full tilt = 35° bank (never more) |
| Tilt back / forward | climbs / descends; full tilt = 1,000 fpm, with pitch kept within −10…+15° |
| Let go (or the sensor drops out) | levels the wings and holds altitude within a few seconds |
| **+ / −** in the air | changes the held speed (60–115 kt; cruise 95) |
| Tilt sideways (or Q/E) on the ground | steers; the wings stay level |
| **−** on the runway, down to idle | aborts the takeoff (B brakes) |
| **R** or the HUD's **Recenter** button | sets the controller's neutral pose |
| Start or restart a flight | neutral is captured automatically from the pose you hold; the HUD shows "Controller · turn … · climb …" so an off-centre neutral is easy to spot |

An off-centre neutral, such as a pose saved with a different grip or with the controller resting on the desk, used to make the aircraft turn by itself. Auto-recenter at flight start fixed it (confirmed on the real controller, 2026-10-31). On the runway, ground steering also ignores the first 15% of tilt.

It's a stabilization layer (`src/flight/easyFlight.ts`) on top of the unchanged flight model. It turns your input into a target bank and a target climb rate, then flies the normal elevator, ailerons, rudder and throttle toward them. Its safeguards:
- **Stall protection:** it never asks for more angle of attack than 3° short of the stall warning.
- **Near the ground:** it limits sink rate, so a held descent becomes a gentle arrival.
- **Speed:** auto-throttle holds the target speed.
- **Trim:** an integrator trims out steady errors.
- **Smoothness:** roll rate is capped at about 15°/s, and climb-rate targets ramp gradually, so nothing jerks.

The motion-controller defaults were also softened: dead zone 8%, expo 0.5, smoothing 100 ms, max rate 3/s. Saved settings still on the old defaults were upgraded automatically; values you changed were kept.

### Manual flying (Easy flight off) The motion controller, if live, and the keyboard both work, and their inputs add, so you can use the keyboard for rudder or corrections while the controller flies pitch and roll.

| Key | Action |
| --- | --- |
| ↓ / S | Pull (nose up) |
| ↑ / W | Push (nose down) |
| ← → / A D | Roll |
| Q / E | Rudder; nose-wheel steering on the ground |
| + − / PgUp PgDn | Throttle |
| [ ] | Trim nose down / up |
| B | Brakes (hold) |
| V | Chase / cockpit view |
| I | Show / hide the on-screen instrument panel (chase view) |
| R | Recenter the motion controller |
| Esc / P | Pause menu: resume, restart, start on final approach, assists, setup |
| Backspace | Restart on the runway |
| H | Hide or show the key card |

**Takeoff:** go to full power, keep straight with Q/E, and gently pull at about 55 kt. Liftoff comes at about 60–65 kt, after roughly 280 m of ground roll. Pull too hard on the runway and you'll scrape the tail. **Landing:** use the pause menu's *Start on final approach* for a trimmed final at 1,000 ft, about 4 km out.

**Assists** (pause menu, saved):
- **Bank assist** (on): roll input sets a *target bank angle* (full tilt = 45°), and a small control law drives the ailerons to reach it. The aircraft still rolls only through aileron moments and its own dynamics, but a held tilt gives a steady turn, and leveling the controller levels the wings. Turn it off and roll input is a direct aileron, which commands a roll *rate*, like a real stick.
- **Auto-rudder** (on): adds rudder to cancel sideslip while airborne, standing in for the pilot's feet. Without it, the aileron alone gives about 9° of adverse yaw. That's true to the aircraft, but awkward with a controller that has no rudder axis.

Pitch is always direct: tilt is elevator, and the nose follows through angle of attack, inertia and damping, never by setting the model's angle.

**Flight model** (`src/flight/dynamics.ts`), integrated at a fixed 240 Hz regardless of frame rate:
- **Aerodynamics:** stability and control derivatives for a generic 160 hp high-wing trainer, with lift from angle of attack and a smooth stall break (peak lift coefficient about 1.55 at 16°, stall warning 2° before).
- **Engine:** a propeller thrust model with engine lag.
- **Controls:** first-order control-surface actuators.
- **Ground:** spring-damper gear with rolling, braking and side friction, nose-wheel steering, and a tail skid.
- **Crashes:** a crash is a touchdown faster than about 890 fpm, or a wingtip, propeller or fuselage touching the ground.

## Browser requirements for Web Serial

- Chromium-based desktop browsers only (Chrome, Edge, Opera 89+). Firefox and Safari don't implement it, and neither does any mobile browser. The app says so and offers Simulated instead.
- A **secure context** is required: `http://localhost` counts, but a LAN IP like `http://192.168.x.x` does not. Use HTTPS for anything other than localhost.
- The port chooser only opens from a click. The browser remembers the permission per site, so **Reconnect** reopens a known port without prompting. If that same USB device is unplugged and plugged back in, the app reopens it automatically.
- Only one program can hold a COM port. Close Arduino Serial Monitor first.

## Architecture

```
firmware/mpu_flight_controller/   ESP32: MPU-6050 read, gyro-bias cal, complementary filter, NDJSON @ 50 Hz
src/sensor/
  protocol.ts          message types, strict validation, encoder (shared by simulator/tests)
  lineFramer.ts        byte chunks → lines, overflow protection
  transport/types.ts   SensorTransport interface (serial today; WebSocket/BLE later)
  transport/webSerial.ts
  transport/simulated.ts   protocol-exact fake device + fault injection
  sensorHub.ts         owns the link: parse, sequence/drop stats, live/stale/lost watchdog
  serialSession.ts     connect / reconnect / auto-reconnect on replug
  hooks.ts             coarse React bindings (status changes, 4 Hz diagnostics)
src/calibration/neutral.ts   1 s still-capture with motion/tilt rejection
src/control/
  mapping.ts           dead zone, expo, sensitivity, inversion, smoothing, slew, engage ramp, yaw modes
  controlLoop.ts       one rAF loop → mutable `controls` object (the flight model's input)
src/settings/settingsStore.ts   Zustand + persist (control settings, neutral, source, flight assists)
src/setup/             calibration screen: connection, 3D controller view, live readouts, response, diagnostics
src/flight/
  math.ts              vectors, quaternions (NED world, FRD body)
  aircraft.ts          aircraft definition: mass, inertia, derivatives, engine, gear, structure points
  dynamics.ts          6-DOF FlightModel, fixed 240 Hz steps, ground contact, crash/stall/tail-strike detection, trim solver
  assists.ts           bank-angle assist, auto-rudder
  session.ts           the live FlightModel + pilot input mixing (motion + keyboard + assists)
src/input/keyboard.ts  smoothed keyboard axes
src/world/
  coords.ts            NED/FRD → three.js conversion
  Aircraft.tsx         procedural trainer with moving elevator, ailerons, rudder, propeller
  World.tsx            sky, haze, ground, runway 36/18 with markings, apron, hangars, trees, hills, clouds
  FlightScene.tsx      per-frame driver: input → physics → model → chase/cockpit camera → shadow light
src/hud/               flight-data HUD (DOM refs, no re-renders), pause menu, flight hotkeys
src/state/uiStore.ts   screen, pause, camera view
```

**High-frequency data never touches React state.** Serial lines feed `SensorHub`, which stores the latest sample in a plain object. One `requestAnimationFrame` loop runs the control pipeline. The 3D view reads the hub in `useFrame`, and the numeric readouts write to DOM refs. React re-renders only on link-state changes and the 4 Hz diagnostics poll. The flight works the same way: `useFrame` advances the physics and moves the model and camera directly, and the HUD writes its numbers to DOM nodes once per frame.

**Transport choice.** USB serial with fusion on the ESP32 was chosen over raw-data-to-browser fusion. Fusing on the board gives a fixed 200 Hz integration step that browser timers can't guarantee, and it cuts bandwidth to 50 Hz of angles. The cost is reflashing to change filter constants. The browser-side shaping covers everything a player needs to tune. Wi-Fi (WebSocket from the ESP32) fits behind the same `SensorTransport` interface. It adds about 5–30 ms of jittery latency and network setup, so USB stays the default.

Conventions and axis signs: [docs/AXES.md](docs/AXES.md). Wire protocol: [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Interface design

A glass-cockpit look: deep navy and charcoal, white condensed aviation type (Barlow Condensed, with Inter for body text), and one cyan accent for active, selected and target. Green, amber and red are used only for status. Colours are defined as tokens in `src/index.css`, alongside the `.glass` surfaces and lightweight animations, which respect reduced-motion settings.

**Flight HUD** (`src/hud/FlightHud.tsx` and `src/hud/tapes.ts`) is arranged around the screen edges so the aircraft stays clear:

| Where | What |
| --- | --- |
| Top centre | Heading ribbon with digital readout; stall, overspeed and tail-strike warnings; messages |
| Left edge | Airspeed tape with low-speed, normal and caution bands, plus the Easy-flight target speed marker |
| Right edge | Altitude tape with terrain marking, a vertical-speed scale with the target rate, VS and AGL |
| Bottom left | Attitude ball with pitch, bank, AoA and G |
| Bottom centre | The six-pack instruments (toggle with I) |
| Bottom right | Throttle: lever position, engine fill and mode; hold buttons for + / − and brakes (they press the same keys as the keyboard) |
| Top left | Controller status and live turn and climb command; Easy flight, phase and brakes indicators |
| Top right | Toolbar: recenter, camera, instruments, controls card, settings, pause |

All values are live telemetry. The tapes are drawn on canvases once per frame and never re-render React. The layout was checked for overlaps at 780×600, 900×560, 1366×768 and 1920×1080.

## Verification status

**Milestone 4 (tested 2026-10-31):**
- **Aircraft tests (5):** a sane cruise trim and a steady park for every aircraft; the sport plane is more than 10 kt faster and rolls quicker than the trainer; it takes off manually between 58 and 85 kt; Easy flight takes it off with one key, holds its speed and limits a full turn to 35°; switching aircraft resets to the runway.
- **Overall:** 79 tests pass and the production build succeeds.
- **Checked in the browser:**
  - The menu renders, with aircraft stats from the flight model (stall 50 vs 56 kt).
  - The settings dialog saves its choices.
  - Selecting the sport plane persists.
  - Take off downloads the 3D screen, then shows the loading overlay.
- **Not checked here:** the flight scene at High quality with bloom, and the low-wing model in flight. The test pane stopped drawing frames while hidden, so the overlay never lifted there. Needs a look in a normal browser window, along with real frame rates on your machine.

**Milestone 3 (tested 2026-10-31):**
- **Terrain tests:** flat field and approach; hills over 150 m further out; no cliffs; the flight model crashes into a hill instead of flying through it; height above ground is correct.
- **Instrument tests:** airspeed, vertical speed and tachometer scales map the right way; the turn-rate and slip-ball inputs read zero in level flight and turn the right way in a right turn.
- **Overall:** 70 tests pass, the typecheck is clean, and the production build succeeds.
- **Checked by screenshot:** chase view over the hills with the on-screen instruments; cockpit view with all eight gauges matching the HUD values. The overlay originally overlapped the data strip and key card at small window sizes. It now stacks above the strip, and the key card auto-hides after 15 s.
- **Not verified:** frame rate on a normal visible window or a low-end GPU (the test pane was throttled), and how the instruments read at large screen sizes.

**Easy flight (tested 2026-10-30):**
- **Unit tests:** 14 closed-loop tests fly the real model with the assist in the loop:
  - hands-off cruise
  - upset recovery from 40° bank and 12° nose-down, with at most 2 swings
  - gentle turn, and a sharp turn held at the 35° limit with no steady sink
  - climb and descent rates, speed hold, and no stall at full back stick
  - a gentle ground arrival from a held descent
  - hands-off takeoff with no tail strike, the takeoff climb ending at 400 ft
  - ground steering without wing roll, and aborting a takeoff
  - sensor dropout leveling the aircraft
- **In the app:** the app's own session code was driven at 60 Hz with controller-style commands. It took off with one key press and leveled at about 410 ft. A tiny tilt gave a 4° bank, a full tilt 35°, with no overshoot. Letting go leveled the wings in about 3 s, climb and descent worked, and a sensor loss mid-turn leveled out in about 3 s.
- **Bugs found and fixed while tuning:**
  - Auto-rudder too strong: it caused about ±0.5° of wing rocking. Its gain is now half.
  - Gentle tilts couldn't take off. Takeoff is now automatic.
  - Sideways tilt on the runway used the ailerons. It now steers.
  - A steady 155 fpm sink in steep turns.
- **Not tested with the physical MPU-6050:** how the new tilt curve feels in the hand, and whether 8% dead zone and 100 ms smoothing suit your sensor's noise. Tilt in both directions should work, but it hasn't been confirmed in flight.

**Milestone 2, flight (tested 2026-10-30):**

- 18 flight tests pass:
  - math frames and the NED → three.js mapping
  - the stall lift curve, a level-deceleration stall warning at 45–62 kt, and stall recovery
  - trimmed hands-off level flight held for 20 s
  - takeoff: liftoff at 50–72 kt after 200–700 m of ground roll (measured: 65 kt, 277 m)
  - right aileron rolls right and turns right; response is gradual, not instant
  - tail scrape without a crash, and a crash on a dive into the ground
  - a stabilized approach, flare, touchdown and braked stop
  - auto-rudder cuts peak sideslip from more than 8° to under 4°
  - bank assist holds 19–26° for half tilt and levels the wings on release
- Measured performance (offline): top speed 124 kt at full power; climb about 950 fpm at 85 kt; full aileron rolls at about 25°/s.
- In the browser pane, with the simulated motion controller through the real pipeline (simulator → hub → shaping → assists → physics → render):
  - Takeoff and climb to 350 ft at about 750 fpm.
  - With bank assist, a held 22° tilt gave a steady 22° bank turn (heading 360° → 048°). Leveling the controller leveled the wings.
  - Chase and cockpit views both render; the pause menu and assist toggles work.
- Two problems found in browser testing and fixed:
  - Holding a tilt with direct aileron rolled the aircraft inverted. This is correct physics but unusable with a hand-held controller, which is why bank assist now exists.
  - The cockpit view rendered from inside the exterior shell.
- **Not yet verified:** flying with the real MPU-6050 controller (the hardware was verified only on the setup screen), a full keyboard-only circuit at a normal frame rate, and performance on lower-end GPUs. The built-in browser pane ran at about 3 fps while hidden; at that rate the sim runs at about 0.3× real time, because each frame is capped at 0.1 s of physics.

**Milestone 1, tested here (no hardware attached):**

- 29 unit tests pass: protocol parsing and rejection, a firmware-format line, sequence-wrap and restart handling, line framing with split and overlong input, hub `awaiting → live → stale → lost → live` timing, the silent-port hint, recalibration pausing data, dead zone/expo/inversion, the slew limit, the engage ramp, fade on link loss, the ±180° roll wrap, yaw washout, and neutral capture (including rejection).
- In Chrome 152 against the simulator, through the real parser and hub:
  - Recenter recovered the simulated 2.5°/−1.5° desk tilt.
  - Recentering while moving was rejected.
  - With 30% corrupted lines plus 30% drops, the link stayed live and both were counted.
  - Unplugging at full deflection faded the elevator from 1.0 to 0 in about 300 ms.
  - Connecting while tilted held output at 0 through calibration, then ramped in over 1 s.
  - A silent stream went stale, then lost, then recovered.
  - The three.js axis signs were checked numerically.
- The firmware compiles for `esp32:esp32:esp32` with core 3.3.12 (23% flash).

**Tested on hardware (2026-10-29/30: ESP32 DevKit with CH340 on COM8, GY-521 clone, firmware 1.1.2):**

- Stream: 2,102 samples at 50.0 Hz, 0 sequence gaps, 0 malformed, 0 out of range.
- Gyro calibration passes on the first try when resting (0.06–0.50 °/s noise against a 0.8 limit). Pitch/roll noise at rest is ±0.01–0.02°, and yaw drift is 0.27° over 42 s.
- This GY-521 reads gravity 12–16% high near level. The rest-magnitude normalization brings it to 1.00 g, so the accelerometer correction stays active.
- Tipping past vertical stays within the protocol's ±90° pitch (tested on 1.1.1, which had the same fusion code).
- In Edge with Web Serial: connect, calibration, recenter, and all four axis signs (pitch ±, roll ±) were confirmed by hand. The 3D model follows.
- Opening the port usually resets this board (DTR). Once, it didn't. `tools/verify-device.ps1 -Recalibrate` covers that case.

**Known hardware limitations:**

- The accelerometer has a Z-axis offset of about +0.12 g. Rest-magnitude normalization only corrects it near the calibration pose. Far from level (upside down), the accelerometer is ignored and attitude runs on the gyro alone until the controller comes back. A 6-face accelerometer calibration would fix this and was deferred.

**Not yet verified:**

- Unplug/replug auto-reconnect with real USB, and long sessions (thermal drift).
- The tremor-only calibration fallback (both runs calibrated cleanly, so it never triggered).
- Some built-in browser panes show no Web Serial port picker. Use Chrome or Edge.

To check the raw stream without the browser, run `powershell -ExecutionPolicy Bypass -File tools/verify-device.ps1` (it auto-detects the ESP32 port and reports rate, drops, malformed and out-of-range lines, noise and drift; it prints calibration status live).

## Roadmap

1. ✅ **Sensor pipeline and calibration screen**
2. ✅ **Flight dynamics and a flyable scene.** 6-DOF model with stall and ground contact, keyboard fallback, assists, airfield, chase and cockpit cameras, flight-data HUD, pause and restart. Mouse control was deferred.
3. ✅ **Cockpit and world detail.**
   - **Instruments:** the six-pack (airspeed with V-speed arcs, attitude, altimeter, turn coordinator with slip ball, heading, vertical speed), plus a tachometer and warning lights (STALL, LOW ALT, BRAKES, EASY). They're drawn live from flight data (`src/instruments/`), on the cockpit panel and as an on-screen panel in the chase view (I).
   - **Cockpit interior:** yokes and throttle that move with the actual control surfaces, a glareshield, cowling and radio stack.
   - **Aircraft:** position lights, wingtip strobes, wheel fairings, pitot tube, exhaust and landing light.
   - **Terrain:** rolling hills shared by rendering and physics (`src/world/terrain.ts`), so hills are solid. The field and the southern approach stay flat.
   - **Clouds:** shaded cloud bases.
4. ✅ **Menus and polish.**
   - **Start menu:** aircraft selection (Highwing 160 trainer, Lowwing 200 sport, each with its own physics, Easy-flight speeds and look), takeoff or final-approach start, and controller status.
   - **Settings dialog:** graphics quality, effects, FPS counter, Easy flight and assists, on-screen panels. Reachable from the menu and the pause menu.
   - **Loading screen:** shown while the world builds; the page also shows a first-load splash.
   - **Graphics tiers:** Low, Medium and High, set in `src/settings/graphics.ts`. They control resolution, antialiasing, shadows and shadow resolution, terrain mesh detail, trees, clouds and fog distance.
   - **Post-processing (High):** bloom, vignette and ACES tone mapping.
   - **Performance:**
     - The 3D screens load on demand: the menu needs about 105 KB gzipped, and the 3D engine (about 238 KB gzipped) loads on first use.
     - Per-frame allocations were removed from the frame loop.
     - The chase camera stays above hills.
     - An optional FPS counter, plus a one-time hint to lower quality if the frame rate stays under about 32 fps.
