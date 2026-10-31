# Skybound Motion → Unity 6 (URP) migration plan

Source: `skybound-motion/` (7,742 lines of TypeScript/TSX, React + Three.js + Zustand, 79 tests), runs in the browser, talks to an ESP32 over Web Serial.
Target: a new Unity 6 LTS project using URP, C#, opened from Unity Hub, Windows desktop build.

## 1. What this project actually contains (differences from the generic brief)

- **No JSON config files, GLSL shaders, models, textures or audio.** A search of the repo found only `package.json`, `tsconfig.json` and the lockfile as JSON, and no asset files at all. Aircraft stats are TypeScript constants (`flight/aircraft.ts`), and the aircraft and world are built from code. So there is nothing to import, and nothing to convert from JSON; "data" means porting `aircraft.ts`.
- **Flight physics are custom, not engine physics.** `FlightModel` already has its own 240 Hz fixed-step integrator, spring-damper gear, stall model and crash detection. Port it as is. **Do not use Rigidbody forces or Unity colliders**; they would lose the tuning that took several rounds of testing with your controller. Terrain contact stays `terrainHeight(north, east)`, shared by physics and rendering.
- **The firmware does not change at all.** Unity reads the same NDJSON lines at 115200 baud.

## 2. Module map

Effort: **S** mechanical port, **M** port with some rework, **L** rewrite for Unity's systems.

| TypeScript | Unity / C# | Attaches to | Effort |
| --- | --- | --- | --- |
| `flight/math.ts` | `Flight/FlightMath.cs`: same function names on `V3`/`Quat` structs of `double` | Nothing (plain C#) | S |
| `flight/aircraft.ts` | `Flight/AircraftDef.cs` (ScriptableObject), assets `Data/Trainer.asset`, `Data/Sport.asset` | Referenced by `FlightSession` | S |
| `flight/dynamics.ts` | `Flight/FlightModel.cs`: plain class, same `advance()`, `step()`, `groundForces()` | Nothing | S |
| `flight/assists.ts`, `easyFlight.ts` | `Flight/Assists.cs`, `Flight/EasyFlight.cs` | Nothing | S |
| `flight/session.ts` | `Flight/FlightSession.cs` MonoBehaviour: `Update()` runs `updatePilot()` then `flight.advance()` | `FlightSession` GameObject | S |
| `flight/recorder.ts` | `Flight/Recorder.cs`, CSV written with `StreamWriter` | Nothing | S |
| `world/terrain.ts` | `World/TerrainHeight.cs`, same function | Nothing | S |
| `world/coords.ts` | `World/Coords.cs`, see section 4 | Nothing | S |
| `control/mapping.ts`, `controlLoop.ts` | `Control/ControlMapping.cs`, `ControlLoop.cs` | `FlightSession` calls it | S |
| `calibration/neutral.ts` | `Calibration/Neutral.cs`, Promise → coroutine | Nothing | S |
| `sensor/protocol.ts`, `lineFramer.ts`, `sensorHub.ts` | `Sensor/Protocol.cs`, `LineFramer.cs`, `SensorHub.cs` | `SensorHub` GameObject | M |
| `sensor/transport/webSerial.ts` | `Sensor/SerialTransport.cs`: `System.IO.Ports` on a background thread, lines queued to the main thread | Nothing | M |
| `sensor/transport/simulated.ts` | `Sensor/SimulatedTransport.cs` | Nothing | S |
| `input/keyboard.ts` | `Input/KeyboardAxes.cs` with Input System actions; gamepad and joystick bindings added | `FlightSession` | S |
| `settings/settingsStore.ts`, `state/uiStore.ts` | `Settings/SettingsStore.cs`, `UiStore.cs`: C# class + events, saved as JSON | Nothing | S |
| `settings/graphics.ts` | `Settings/GraphicsProfile.cs` + URP quality levels | Nothing | S |
| `world/World.tsx` | `World/WorldBuilder.cs`: procedural `Mesh` for ground and runway, instanced trees, clouds | `World` GameObject | L |
| `world/Aircraft.tsx` | `World/AircraftBuilder.cs`: procedural meshes, control surfaces, strobes, cockpit | `Aircraft` GameObject | L |
| `world/FlightScene.tsx` | `World/ChaseCamera.cs`, `World/AircraftView.cs`, `World/FlightEffects.cs` (bloom), `Skybound/ToneMap` renderer features (tone mapping, fog, vignette) | Main Camera, `Aircraft` | M |
| `instruments/gauges.ts`, `panel.ts` | `Instruments/Gauges.cs`, `Panel.cs`, on `Canvas2D.cs`: a UI panel drawing into a `RenderTexture` | Created at run time; shown on the cockpit mesh and the HUD | L |
| `hud/tapes.ts`, `perf.ts` | `Hud/Tapes.cs`, `Perf.cs` | Nothing | M |
| `App.tsx` | `App.cs` | `UI` GameObject, with a `UIDocument` | S |
| `hud/FlightHud.tsx`, `FlightScreen.tsx` | `Hud/FlightHud.cs`, `FlightScreen.cs`: C# functions in place of JSX, styled by `Resources/UI/Skybound.uss` | Built by `App` | L |
| `menu/*`, `setup/*`, `ui/*`, `index.css` | `Menu/*`, `Setup/*`, `Ui/Ui.cs`, `Resources/UI/Skybound.uss`, one file per component | Built by `App` | L |
| `setup/ControllerView.tsx` | `Setup/ControllerView.cs`: second camera → `RenderTexture` shown in the setup screen | Created by the setup screen | M |
| `*.test.ts` | Unity Test Framework EditMode tests, ported test by test | `Tests/EditMode` | M |

Requested mappings that don't apply: **JSON → data classes** (no JSON exists), **audio** (the game has none; engine sound would be a new feature), **Rigidbody** (not recommended, see section 1).

## 3. Project setup

```
Assets/
  Scripts/{Flight,Control,Sensor,Calibration,Input,Settings,World,Instruments,Hud}/
  Scenes/Flight.unity   (the only scene: the browser game is one page, and the menu is part of it)
  Prefabs/  Materials/  Data/  UI/  Fonts/
  Tests/EditMode/
```

Packages: Universal RP (template), Input System, UI Toolkit (built in), Test Framework (built in). Newtonsoft JSON is not needed, because `JsonUtility` covers saved settings.

Player settings: **Api Compatibility Level = .NET Framework** (needed for `System.IO.Ports`), Active Input Handling = Input System.

## 4. Coordinates

Physics stays NED/FRD in `double`. Only the rendering boundary converts, as `coords.ts` does now. Unity is left-handed, with x east, y up, z north:

```
position:   unity = (east, -down, north)
quaternion: unity(x, y, z, w) = (-q.y, q.z, -q.x, q.w)   // q = (w, x, y, z) NED
```

Hand-checked: yaw right, pitch up and roll right each come out with the right sign. An EditMode test will lock these in, as `coords.test.ts` does today.

## 5. Stages and how to test each

| Stage | Contents | Test in Unity |
| --- | --- | --- |
| 0 | Install the Editor, create the URP project, settings, folders | Project opens from Unity Hub, empty scene plays |
| 1 | Math, aircraft, flight model, assists, Easy flight, keyboard, ported tests | Test Runner passes. `Flight.unity`, Play: a placeholder box takes off with **+**, turns and climbs, with live numbers on screen |
| 2 | Terrain, plain ground, chase camera, cockpit camera | Fly over hills; flying into one crashes; **V** switches view |
| 3 | Sensor hub, serial and simulated transports, control mapping, neutral capture | Connect the ESP32 by COM port and fly the box by tilting, including recenter |
| 4 | Real aircraft and world visuals, URP effects, quality levels | Compare against the browser game side by side |
| 5 | HUD, gauges, cockpit panel, menu, settings, controller setup | Full flow from menu to landing, without touching the Inspector |
| 6 | Windows build, performance pass | Run the `.exe` outside the Editor |

**Accuracy check for stages 1 and 3:** the TypeScript version will generate reference flights (input sequence → expected state over time) saved as test data, and the C# tests must match them. That shows the port flies the same rather than only looking similar.

## 6. No direct Unity equivalent

| Web feature | Replacement | Note |
| --- | --- | --- |
| Web Serial and the browser port picker | `System.IO.Ports` on a background thread, and a list from `SerialPort.GetPortNames()` with Bluetooth ports marked from the registry | **Controller needs a desktop build: a Unity WebGL build can't reach the COM port** |
| Auto-reconnect on replug | Poll the port list every second | Simple |
| `JSON.parse` for the wire protocol | `MiniJson`, a small strict parser | `JsonUtility` can't tell a missing or mistyped field from zero, and the protocol must reject both |
| Number formatting in protocol messages | `MiniJson.JsNumber`, `JsToFixed` | Mono's `"R"` and `"F2"` round differently from JavaScript |
| `async` transport open | `onOpened` and `onOpenFailed` callbacks, delivered from `Pump()` on the main thread | Opening a port can block for seconds |
| React, JSX, hooks | C# functions that build elements, and `Scope`: closures that bring each element up to date once a frame | Replaced the component re-rendering. No UXML, since the JSX was code too |
| Zustand stores | Static `SettingsStore` and `UiStore`, read directly | Same fields and setters |
| Tailwind, CSS | `Skybound.uss`, with a theme file of its own instead of Unity's default | Sizes are fixed for a 1280×720 layout and the whole UI scales with the window, in place of `clamp()` and `vw`. **Letter spacing is in hundredths of an em in USS, not pixels** |
| Glass blur (`backdrop-filter`) | Slightly more opaque panels and scrims | **USS has no blur.** The scene is sharp behind dialogs |
| CSS shadows and glows | Left out | USS has no `box-shadow` |
| Canvas 2D (tapes, gauges) | `Ctx` in `Canvas2D.cs`: the same calls, recorded and replayed through `Painter2D` | Lets the drawing code port line by line |
| Canvas `clip()` | Shapes worked out inside the circle (`FillBelowChord`, `LineInCircle`) | Painter2D can't clip |
| Canvas `destination-in` fade on the tapes | Each thing drawn takes its alpha from where it sits | Painter2D has no masking |
| Rotated canvas text | Pooled `Label`s, moved and rotated each frame | Text always sits above its own layer's shapes, so the altimeter needles are on a layer above its counter |
| Two-colour `FillGradient` | Goes through a `Gradient` object when translucent | **Unity's two-colour calls ignore alpha** |
| Inter Variable | Segoe UI, the browser's own second choice, from Windows | Its package has only a web font Unity can't read. Semibold is bold here: a second system font mixed up text between elements. Other systems fall back to Arial |
| Barlow Condensed | `Resources/Fonts`, unpacked from the browser game's own package, with kerning removed | Unity's text engine drops letter spacing on kerned pairs |
| The browser's serial port chooser | The ports listed in the Connection panel, USB first, last choice saved | |
| `visibilitychange` auto-pause | `OnApplicationFocus`, in builds only | In the Editor, clicking another window would pause it |
| Clipboard, falling back to a file download | `GUIUtility.systemCopyBuffer` | Always works, so no fallback |
| drei `RoundedBox`, `Edges`, `Grid`, `OrbitControls` | Plain boxes, line meshes and a small drag-to-orbit | Setup screen preview only |
| Mouse input with no focused window | Real clicks reach the UI only with the Game view focused | Batch tests send events into the panel instead, so the mouse device itself is a manual check |
| The browser tab's close button | A **Quit** button on the menu, in builds only | A fullscreen game needs one. Alt+F4 also works |
| `requestAnimationFrame` | VSync on in all three quality levels | Otherwise the game runs as fast as it can, for no benefit |
| Port open rebooting the board, which then says hello | The hub asks with `HELLO` once, if samples arrive with no version yet | Opening a COM port doesn't always reboot it |
| three.js ACES tone mapping | `Skybound/ToneMap` full-screen renderer feature | URP's ACES is a different curve that greys the sky, so URP's own tone mapping stays off |
| three.js fog, mixed after tone mapping | Done per pixel in the same pass and in the transparent shaders; Unity's fog stays off | URP mixes fog before tone mapping and couldn't match both near and far distances |
| `@react-three/postprocessing` on High | The browser's order is kept: fog, then URP Bloom, then the browser's vignette formula and tone mapping in a final full-screen pass | Bloom is the one approximation: URP's blur and blending differ, so the scatter is fitted by eye and by measurement. Grass stays about 10–18 levels darker than the browser on High |
| `toneMapped={false}` | Drawn after the tone mapping pass (`MaterialKit.BasicNotToneMapped`) | Stage 5's panel and displays should use this |
| `InstancedMesh` per-instance colours | One merged tree mesh with a palette texture and per-tree UV | URP Lit ignores vertex and instance colours. Merged meshes get a look in the stage 6 performance pass |
| Vertex-coloured ground | Colours baked into a texture | Same reason |
| Sprites | One billboard mesh per cloud field, spread in the vertex shader and sorted 4 times a second | Direct enough |
| Per-mesh `receiveShadow` | Per-material in URP: `Skybound/Lit`, `LitUnshadowed`, `Ground` templates | Saved as assets so builds keep the shader variants |
| Shadow box ±40 m around the aircraft | URP shadow distance of 100 m from the camera | URP measures from the camera |
| Blending in sRGB (no effects) | Transparent objects blend in linear | No framebuffer read in plain URP. Clouds and the prop disc look slightly fainter on Low and Medium |
| three.js GGX specular | URP's approximate specular | Ground is about 5–9 levels darker than the browser at grazing angles. Left as it is |
| `localStorage` | JSON file in `Application.persistentDataPath` | Direct; settings start fresh |
| Promises, `setTimeout` | Coroutines | Direct |
| `prefers-reduced-motion` | Not ported | Unity can't read the OS preference. The UI's few animations are short fades |

## 7. Stage 6 results

Measured in the built `.exe`, not the Editor: Mono, Direct3D 12, Intel Arc 140V, 1920×1080 window, flying over the airfield. Frame rates are with VSync off, to show the real cost.

| Screen | FPS | Frame | Main thread | GPU |
| --- | --- | --- | --- | --- |
| Menu | 105 | 9.5 ms | 2.6 ms | 9.2 ms |
| Controller setup | 84 | 11.9 ms | 4.1 ms | 9.3 ms |
| Low | 266 | 3.8 ms | 2.8 ms | 1.3 ms |
| Medium | 118 | 8.4 ms | 3.6 ms | 8.0 ms |
| Medium, cockpit | 118 | 8.5 ms | 3.3 ms | 8.0 ms |
| High | 99 | 10.1 ms | 4.3 ms | 9.6 ms |
| Medium, VSync on (the shipped setting) | 59 | 16.8 ms | 6.1 ms | 9.8 ms |

All three tiers are well inside the 16.6 ms budget for 60 FPS on this machine. A world builds in under 1 s.

What changed because of these numbers:

- VSync on, since the game otherwise ran at 170 FPS at 720p for nothing.
- URP's post-processing pass only runs when there's bloom to draw.
- Instruments only redraw when the cockpit or the on-screen panel shows them.
- The loading screen is removed once faded. It stayed on top and would have caught clicks meant for the pause menu.

What was left alone, because the numbers don't call for it:

- The trees stay one merged mesh. The whole world's GPU cost is 8 ms.
- 3 KB of garbage per frame remains in the chase view, down only slightly from 3.4 KB after the HUD's own strings and arrays were removed, so most of it comes from redrawing the tapes. Not traced further. It causes a collection about every 5 s, with no matching spike in frame time. The cockpit view, without tapes, makes 0.2 KB.
- An occasional single frame reaches about 35 ms at 1080p. Not traced further.

Build: **Skybound > Build Windows Game** writes `Builds/Windows/SkyboundMotion.exe`. Only the Mono scripting backend is installed; IL2CPP would need its module added in Unity Hub. Share the whole `Builds/Windows` folder, without any `*_DoNotShip` folder.

## 8. Risks

- **Size:** the flight and sensor code ports quickly, but the UI and procedural visuals are a real rewrite. Roughly 3,000 lines of React/canvas UI and 900 lines of scene code have to be rebuilt.
- **Feel:** matching physics doesn't guarantee matching feel. Frame timing, camera and serial latency differ, so tilt response may need retuning with your hand on the controller.
- **Visuals:** URP lighting won't match Three.js exactly, so stage 4 aims for the same look, not identical pixels.
- **The browser game stays as is.** Nothing in `skybound-motion/` gets changed.
