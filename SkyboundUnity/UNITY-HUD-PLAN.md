# Skybound HUD plan

The flight physics files were not changed. The HUD reads the flight through one interface, `IFlightDataProvider`. The menus in your brief come after your feedback on this HUD.

## 1. Project facts

| Item | Value |
| --- | --- |
| Unity | 6000.3.27f1 (6.3 LTS) |
| Render pipeline | URP 17.3 |
| UI system | UI Toolkit (kept, see below) |
| Input | Input System package |
| Target | Windows PC, 16:9, 16:10 and ultrawide |
| Flight data source | `FlightSession` and `FlightModel.telemetry` |

### Why UI Toolkit and not uGUI with TextMeshPro

Every screen in the game (menu, controller setup, settings, pause, HUD) is already UI Toolkit. Adding uGUI would mean two UI systems, two input routes and two ways to style things, for no gain on PC. Your uGUI requirements map across like this:

| Your brief (uGUI) | What it is here |
| --- | --- |
| Canvas Scaler, 1920×1080, match 0.5 | `PanelSettings` Scale With Screen Size, 1280×720, match 0.5 (`App.MakePanelSettings`). Same 16:9 shape, and all layout numbers are sized for 1280×720 |
| Anchors | `left`, `right`, `top` and `bottom` in the stylesheet |
| Separate static and dynamic Canvases | Backings are drawn once on their own layer, and only the numbers and pointers redraw. Moving parts use `usageHints` |
| Raycast Target off | `pickingMode = Ignore` on everything except buttons and the crash card |
| TextMeshPro with tabular numerals | `TabularLabel`: see "No direct equivalent" |
| `ThemeConfig` ScriptableObject | The `:root` and `.hud-theme` rules in `Skybound.uss`: see "No direct equivalent" |
| Cached StringBuilder or `SetText` | `IntLabel`: each number's text is made once, then reused |

## 2. Your script names and where they are

| Your brief | In this project |
| --- | --- |
| `UIManager` | `Scripts/App.cs`, `Scripts/State/UiStore.cs` |
| `HUDController` | `Scripts/Hud/FlightHud.cs` |
| `AirspeedTape`, `AltitudeTape`, `AttitudeIndicator`, `HeadingStrip`, VSI | Drawing functions in `Scripts/Hud/Tapes.cs` |
| `SettingsManager` | `Scripts/Settings/SettingsStore.cs` |
| `IFlightDataProvider` | `Scripts/Hud/IFlightDataProvider.cs` (new) |
| Adapter | `Scripts/Hud/FlightSessionAdapter.cs` (new) |
| Warnings | `Scripts/Hud/HudWarnings.cs` (new) |
| Minimap | `Scripts/Hud/Minimap.cs` (new) |
| Theme colours for code-drawn parts | `Scripts/Hud/HudTheme.cs` (new) |
| Steady digits | `TabularLabel` and `IntLabel` in `Scripts/Hud/NumberText.cs` |

The instruments stay as functions in `Tapes.cs` instead of one script each. They already work and are tested, and splitting them would add no behaviour.

Screen layout is built in code, so there are no prefabs. This is the HUD's hierarchy:

```
UI (GameObject: App + UIDocument)
└── FlightHud host  .hud-theme
    ├── hud-top-left      source, controller command, chips   (fades when idle)
    ├── hud-toolbar       recenter, view, instruments, help, settings, pause   (fades when idle)
    ├── key-card          controls list (H)
    ├── hud-top-centre    heading strip and readout, warning banner, messages
    ├── hud-tape-left     airspeed tape, target speed
    ├── hud-tape-right    vertical speed, altitude tape, VS and AGL
    ├── hud-minimap       moving map, runway, aircraft, heading
    ├── hud-attitude      pitch and bank, attitude ball, AOA and G
    ├── hud-instruments   six-pack overlay (I), replaces the attitude ball
    ├── hud-throttle      hold keys, throttle lever, fuel, flaps, gear
    └── crash card
```

## 3. Editor setup

There is nothing to create or wire in the Inspector. `App` builds the whole HUD when the flight starts, and the scene you already have needs no changes.

1. Switch to Unity and wait for it to compile.
2. Open `Assets/Scenes/Flight.unity` and press Play.
3. Choose a flight from the menu.

**To test in the Editor:**

| Do this | You should see |
| --- | --- |
| Look at the bottom-left | The map, with the runway as a white line. It slides as you fly and the arrow turns with you |
| Press **Tab** three times | Full, then Minimal (tapes and heading), then Off, then Full again. The choice is saved |
| Leave the mouse and keyboard alone for 6 s | Buttons and status fade out and instruments dim. Moving the mouse brings them back |
| Press **I** | The six-pack replaces the attitude ball |
| Dive steeply at the ground | A red PULL UP banner |
| Turn Easy flight off and slow right down | A red STALL banner |

To connect a different flight model, write a class that implements `IFlightDataProvider` and pass it to `new FlightHud(...)` in `FlightScreen.cs`.

## 4. No direct equivalent

| Your brief | Situation | What I did |
| --- | --- | --- |
| **Fuel** | The flight model has no fuel | Estimated in the adapter from real engine power: full tanks last 3 hours at full power. The gauge and LOW FUEL work, but **the engine keeps running at 0%**, because stopping it would mean changing the physics |
| **Flaps** | Not in the flight model | The indicator exists and is tested, but stays hidden while `HasFlaps` is false. A lever that does nothing would mislead |
| **Gear** | Fixed tricycle gear | Shown as FIXED with three green lights. GEAR UP is built and tested, but can't fire on these aircraft |
| **PULL UP** | The model has no terrain warning | Sinking at 1,200 fpm or more with under 12 s to impact. It looks straight down only, so it won't warn about a hill ahead while level |
| **Background blur on panels** | UI Toolkit has no backdrop blur | Glass panels at 75% opacity. A blur would need a URP pass for the pause screen, which I'd do with the menus |
| **Theme ScriptableObject** | Stylesheet variables can't be set from an asset at runtime | One file, `Skybound.uss`, holds the colours. Code-drawn parts read them from it through `HudTheme`, so there's still one place to restyle |
| **Tabular numerals** | Barlow's 1 is 60% as wide as its 0, and Unity's text can't request tabular figures | Each character sits in its own fixed-width cell |
| **Gradients on the attitude ball** | A Painter2D gradient allocates each time it's made | Flat sky and ground colours |
| **Input events for idle** | The controller is always moving | Only mouse and keyboard count as activity, since tilting the controller is flying |

Your menu-screen items stay for the next stage: settings for units, HUD scale and opacity, gamepad navigation, and aircraft and location selection. The HUD shows knots and feet until then.

## 5. Measurements

Built `.exe`, Mono, Direct3D 12, Intel Arc 140V, 1920×1080, VSync off, Medium quality.

| HUD | FPS | Main thread | GPU | Managed allocation |
| --- | --- | --- | --- | --- |
| Before this work (chase view, about the same scene) | 118 | 3.6 ms | 8.0 ms | 3.1 KB/frame |
| Full | 121 | 3.3 ms | 8.0 ms | 0.8 KB/frame |
| Minimal | 120 | 3.0 ms | 8.1 ms | 0.3 KB/frame |
| Off | 121 | 2.4 ms | 7.9 ms | 0.3 KB/frame |
| Cockpit view | 120 | 3.1 ms | 7.9 ms | 0.55 KB/frame |

There was no garbage collection in any 4 s window. **The HUD isn't at zero allocations yet.** Minimal costs no more than Off, which suggests the tapes are now clean, while Full still adds about 0.5 KB/frame that I didn't trace. The 0.3 KB with the HUD off comes from outside the HUD or from the measuring code itself. The drop from 3.1 KB came from three changes: tape labels now fade through opacity instead of colour, each number's text is made once, and the attitude ball uses flat colours.

Checked by screenshot at 1920×1080, 1728×1080 (16:10) and 1720×720 (21:9, the same layout as 3440×1440).

Not checked:

- With the real ESP32 controller
- Warnings in actual hand flying. PULL UP was only triggered by placing the aircraft in a dive
- STALL's new banner in a real stall
- Mouse clicks on the HUD in the built game

## 6. Free assets

None are required. The game already includes Barlow Condensed for numbers and uses Segoe UI from Windows for body text.

| Asset | Where | Why you might want it |
| --- | --- | --- |
| Inter (static TTF) | [Google Fonts](https://fonts.google.com/specimen/Inter), SIL Open Font License | Your choice for text, and the browser game's first choice. It would replace Segoe UI and also work on other platforms |
| Roboto Mono | [Google Fonts](https://fonts.google.com/specimen/Roboto+Mono), Apache 2.0 | Your choice for numbers. It's naturally tabular, but wider than Barlow, so the tape boxes would need widening |
| Aircraft photos or renders | Your own screenshots | Thumbnails for the aircraft cards in the next stage |

To add a font, drop the `.ttf` into `Assets/Resources/Fonts` and change the path in `Fonts` in `Scripts/Instruments/Canvas2D.cs`. If digit widths differ, also change `TabularLabel.DIGIT_EM`.
