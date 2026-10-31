using UnityEngine;

namespace Skybound
{
    public enum AppScreen
    {
        Menu,
        Setup,
        Flight,
    }

    public enum StartPosition
    {
        Runway,
        Final,
    }

    /// <summary>Coarse UI state only: changes on user actions, never per frame.</summary>
    public static class UiStore
    {
        public static AppScreen screen = AppScreen.Menu;
        /// <summary>Where the next flight starts (read when the flight screen mounts).</summary>
        public static StartPosition start = StartPosition.Runway;
        public static bool paused;
        public static CameraView view = CameraView.Chase;
        public static bool showHelp = true;
        /// <summary>On-screen instrument panel in the chase view.</summary>
        public static bool showInstruments = true;
        /// <summary>Settings dialog, over the menu or the pause menu.</summary>
        public static bool showSettings;
        /// <summary>The 3D scene is wanted. Set a frame after the loading screen paints.</summary>
        public static bool mountScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetAll()
        {
            screen = AppScreen.Menu;
            start = StartPosition.Runway;
            paused = false;
            view = CameraView.Chase;
            showHelp = true;
            showInstruments = true;
            showSettings = false;
            mountScene = false;
        }

        public static void SetScreen(AppScreen s)
        {
            screen = s;
            paused = false;
            showSettings = false;
        }

        public static void StartFlight(StartPosition startAt)
        {
            screen = AppScreen.Flight;
            start = startAt;
            paused = false;
            showSettings = false;
        }

        public static void SetPaused(bool p) => paused = p;
        public static void ToggleView() => view = view == CameraView.Chase ? CameraView.Cockpit : CameraView.Chase;
        public static void ToggleHelp() => showHelp = !showHelp;
        public static void ToggleInstruments() => showInstruments = !showInstruments;
        public static void SetShowSettings(bool open) => showSettings = open;
    }
}
