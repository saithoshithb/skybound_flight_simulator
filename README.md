# skybound_flight_simulator

A flight simulator flown by tilting an MPU-6050 on an ESP32 over USB serial.

| Folder | What it is |
| --- | --- |
| `skybound-motion/` | The browser game (React, TypeScript, Three.js), plus the ESP32 firmware in `firmware/` and tools in `tools/`. `npm install`, then `npm run dev`. |
| `flight-controller-docs/` | Hardware, axes, protocol and test notes for the controller. |
| `SkyboundUnity/` | The Unity 6 (URP) version. See `UNITY-MIGRATION-PLAN.md`. |

## Running the Unity version

1. Open `SkyboundUnity` from Unity Hub, with Unity 6000.3 LTS.
2. Run **Skybound > Set Up Flight Scene** once.
3. Open `Assets/Scenes/Flight.unity` and press Play. It opens at the main menu.

**Skybound > Build Windows Game** makes a game that runs on its own: `SkyboundUnity/Builds/Windows/SkyboundMotion.exe`.
