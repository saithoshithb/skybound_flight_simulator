using System;
using System.IO;
using UnityEngine;

namespace Skybound
{
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
                File.WriteAllText(FilePath, JsonUtility.ToJson(Get(), true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skybound] Settings not saved: " + e.Message);
            }
        }

        public static void SetAircraftId(string id) { Get().aircraftId = id; Changed(); }
        public static void SetGraphics(GraphicsQuality q) { Get().graphics = q; Changed(); }
        public static void SetPostFx(bool on) { Get().postFx = on; Changed(); }
        public static void SetShowFps(bool on) { Get().showFps = on; Changed(); }
        public static void SetAutoRudder(bool on) { Get().autoRudder = on; Changed(); }
        public static void SetBankAssist(bool on) { Get().bankAssist = on; Changed(); }
        public static void SetEasyFlight(bool on) { Get().easyFlight = on; Changed(); }
        public static void SetNeutral(Neutral n) { Get().neutral = n; Changed(); }
        public static void SetSource(TransportKind s) { Get().source = s; Changed(); }
        public static void SetLastPort(string port) { Get().lastPort = port; Changed(); }
        /// <summary>Change a value inside control settings, then save.</summary>
        public static void SetControl(Action<ControlSettings> patch) { patch(Get().control); Changed(); }
        public static void ResetControl() { Get().control = new ControlSettings(); Changed(); }
    }
}
