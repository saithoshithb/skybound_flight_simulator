using System;
using System.IO;
using UnityEngine;

namespace Skybound
{
    public enum HudMode
    {
        Full,
        Minimal,
        Off,
    }

    /// <summary>What's saved. A new field keeps its default when an older file doesn't have it.</summary>
    [Serializable]
    public class SettingsState
    {
        /// <summary>Selected aircraft (see AircraftCatalog).</summary>
        public string aircraftId = "trainer";
        public GraphicsQuality graphics = GraphicsQuality.Medium;
        /// <summary>Bloom and vignette (High quality only).</summary>
        public bool postFx = true;
        public bool showFps;
        public ControlSettings control = new ControlSettings();
        public Neutral neutral = new Neutral();
        public TransportKind source = TransportKind.Simulated;
        /// <summary>The browser remembers its chosen port itself; here the settings do.</summary>
        public string lastPort = "";
        /// <summary>Flight assist: automatic rudder coordination while airborne.</summary>
        public bool autoRudder = true;
        /// <summary>Flight assist: roll input sets a bank angle instead of a roll rate.</summary>
        public bool bankAssist = true;
        /// <summary>
        /// Easy flight: stabilised, forgiving flying for first-time pilots: tilt sets bank and climb
        /// rate, auto-throttle holds speed, limits prevent upsets. Includes bank assist and
        /// auto-rudder regardless of those toggles.
        /// </summary>
        public bool easyFlight = true;
        public HudMode hudMode = HudMode.Full;
    }

    /// <summary>
    /// The browser's settings store: same fields and setters, kept in a JSON file instead of
    /// localStorage. Saves are held back until a slider has stopped moving. Reading or writing can
    /// fail (a locked or damaged file); settings then live in memory only.
    /// </summary>
    public static class SettingsStore
    {
        const double SAVE_DELAY_S = 0.5;

        static SettingsState state;
        static bool dirty;
        static double saveAt;

        /// <summary>Off in tests, so a test run can't overwrite the player's settings.</summary>
        public static bool persist = true;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static SettingsState Get()
        {
            if (state == null) state = Load();
            return state;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetLoadedState()
        {
            state = null;
            dirty = false;
            persist = true;
        }

        static SettingsState Load()
        {
            var loaded = new SettingsState();
            if (!persist) return loaded;
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), loaded);
                FitToRanges(loaded.control);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skybound] Settings not loaded: " + e.Message);
                loaded = new SettingsState();
            }
            return loaded;
        }

        /// <summary>Back to defaults, without saving.</summary>
        public static void ResetAll()
        {
            state = new SettingsState();
            dirty = false;
        }

        static void Changed()
        {
            dirty = true;
            saveAt = Time.realtimeSinceStartupAsDouble + SAVE_DELAY_S;
        }

        /// <summary>Once a frame.</summary>
        public static void Tick()
        {
            if (dirty && Time.realtimeSinceStartupAsDouble >= saveAt) Flush();
        }

        public static void Flush()
        {
            if (!dirty) return;
            dirty = false;
            if (!persist) return;
            try
            {
                WriteSafely(FilePath, JsonUtility.ToJson(Get(), true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skybound] Settings not saved: " + e.Message);
            }
        }

        /// <summary>Writes beside the file, then swaps it in, so a crash mid-save leaves the old file whole.</summary>
        public static void WriteSafely(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>A hand-edited file can hold values the sliders never allow: a 0° max angle divides by zero.</summary>
        public static void FitToRanges(ControlSettings c)
        {
            var d = new ControlSettings();
            foreach (var (axis, def) in new[] { (c.pitch, d.pitch), (c.roll, d.roll), (c.yaw, d.yaw) })
                axis.maxAngleDeg = Fit(axis.maxAngleDeg, 10, 70, def.maxAngleDeg);
            c.yawFullRateDps = Fit(c.yawFullRateDps, 20, 300, d.yawFullRateDps);
            c.yawWashoutS = Fit(c.yawWashoutS, 0.5, 10, d.yawWashoutS);
            c.sensitivity = Fit(c.sensitivity, 0.3, 2, d.sensitivity);
            c.deadzone = Fit(c.deadzone, 0, 0.3, d.deadzone);
            c.expo = Fit(c.expo, 0, 1, d.expo);
            c.smoothingMs = Fit(c.smoothingMs, 0, 250, d.smoothingMs);
            c.slewPerSec = Fit(c.slewPerSec, 1, 12, d.slewPerSec);
        }

        static double Fit(double v, double min, double max, double fallback) => double.IsNaN(v) ? fallback : Math.Min(max, Math.Max(min, v));

        public static void SetAircraftId(string id) { Get().aircraftId = id; Changed(); }
        public static void SetGraphics(GraphicsQuality q) { Get().graphics = q; Changed(); }
        public static void SetPostFx(bool on) { Get().postFx = on; Changed(); }
        public static void SetShowFps(bool on) { Get().showFps = on; Changed(); }
        public static void SetAutoRudder(bool on) { Get().autoRudder = on; Changed(); }
        public static void SetBankAssist(bool on) { Get().bankAssist = on; Changed(); }
        public static void SetEasyFlight(bool on) { Get().easyFlight = on; Changed(); }
        public static void SetHudMode(HudMode mode) { Get().hudMode = mode; Changed(); }
        public static void SetNeutral(Neutral n) { Get().neutral = n; Changed(); }
        public static void SetSource(TransportKind s) { Get().source = s; Changed(); }
        public static void SetLastPort(string port) { Get().lastPort = port; Changed(); }
        /// <summary>Change a value inside control settings, then save.</summary>
        public static void SetControl(Action<ControlSettings> patch) { patch(Get().control); Changed(); }
        public static void ResetControl() { Get().control = new ControlSettings(); Changed(); }
    }
}
