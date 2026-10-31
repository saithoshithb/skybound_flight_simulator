using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>The flight: scene on demand, HUD, pause menu, settings dialog, loading screen, and the keys that drive them.</summary>
    public class FlightScreen : IScreen
    {
        /// <summary>Frames rendered before the loading screen lifts (shaders compiled, shadows drawn).</summary>
        const int READY_AFTER_FRAMES = 3;
        const double HELP_HIDES_AFTER_S = 15;
        /// <summary>Just longer than the fade in the stylesheet.</summary>
        const long LOADING_REMOVED_AFTER_MS = 800;

        readonly App app;
        readonly FlightSession session;
        readonly VisualElement root;
        readonly FlightHud hud;
        readonly FlightSessionAdapter flightData;
        readonly VisualElement loading;
        readonly bool startImmediately;
        readonly double helpHidesAt;
        VisualElement pause;
        SettingsDialog settings;
        Scope pauseScope;
        int frames;
        bool ready;
        bool helpTimerDone;

        public FlightHud Hud => hud;
        public bool Ready => ready;
        public VisualElement PauseMenuElement => pause;
        public SettingsDialog Settings => settings;

        public FlightScreen(VisualElement parent, App application, bool skipLoadingScreen)
        {
            root = parent;
            app = application;
            session = app.Session;
            startImmediately = skipLoadingScreen;
            helpHidesAt = Time.unscaledTimeAsDouble + HELP_HIDES_AFTER_S;
            Perf.Reset();

            flightData = new FlightSessionAdapter(session);
            hud = new FlightHud(root, app, this, flightData);
            hud.SetVisible(false);
            loading = LoadingOverlay();
            root.Add(loading);
        }

        public void RestartOnRunway()
        {
            session.ResetOnRunway();
            app.Link.StartAutoRecenter();
        }

        void RestartAirborne()
        {
            session.ResetAirborne();
            app.Link.StartAutoRecenter();
        }

        public void Frame()
        {
            // Mount the heavy 3D scene a frame after the loading screen paints.
            if (!ready)
            {
                if (frames++ == 0 && !startImmediately) return;
                UiStore.mountScene = true;
                hud.Prepare();
                if (startImmediately || frames > READY_AFTER_FRAMES) BecomeReady();
                else return;
            }

            Perf.PerfFrame(Time.unscaledDeltaTime, () =>
            {
                if (SettingsStore.Get().graphics != GraphicsQuality.Low) session.Flash("Low frame rate · try Settings → Graphics: a lower quality", 6000);
            });

            Keys();
            // The key card gets out of the way after a while; H brings it back.
            if (!helpTimerDone && Time.unscaledTimeAsDouble >= helpHidesAt)
            {
                helpTimerDone = true;
                if (UiStore.showHelp) UiStore.ToggleHelp();
            }
            flightData.Tick();
            hud.Frame();
            UpdateOverlays();
        }

        /// <summary>Start where the menu asked, once the world is on screen.</summary>
        void BecomeReady()
        {
            ready = true;
            hud.SetVisible(true);
            // Fades out, then goes. While it fades, the buttons under it must already work.
            loading.AddToClassList("loading--gone");
            loading.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            if (startImmediately) loading.RemoveFromHierarchy();
            else loading.schedule.Execute(loading.RemoveFromHierarchy).ExecuteLater(LOADING_REMOVED_AFTER_MS);
            if (UiStore.start == StartPosition.Final) RestartAirborne();
            else
            {
                RestartOnRunway();
                session.Flash(
                    SettingsStore.Get().easyFlight
                        ? "Runway 36 · press + (or PgUp) once to take off"
                        : "Runway 36 · full power with + or PgUp, rotate at 55 kt",
                    4000);
            }
        }

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.ctrlKey.isPressed || kb.altKey.isPressed) return;
            if (UiStore.showSettings) return; // the dialog handles its own keys
            if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) UiStore.SetPaused(!UiStore.paused);
            if (kb.vKey.wasPressedThisFrame) UiStore.ToggleView();
            if (kb.iKey.wasPressedThisFrame) UiStore.ToggleInstruments();
            if (kb.hKey.wasPressedThisFrame) UiStore.ToggleHelp();
            if (kb.tabKey.wasPressedThisFrame) NextHudMode();
            if (kb.backspaceKey.wasPressedThisFrame)
            {
                RestartOnRunway();
                UiStore.SetPaused(false);
            }
            if (kb.rKey.wasPressedThisFrame) app.Link.Recenter();
        }

        void NextHudMode()
        {
            var next = (HudMode)(((int)SettingsStore.Get().hudMode + 1) % 3);
            SettingsStore.SetHudMode(next);
            session.Flash($"HUD {next.ToString().ToUpperInvariant()} · Tab to change", 1800);
        }

        void UpdateOverlays()
        {
            var showPause = UiStore.paused && !UiStore.showSettings;
            if (showPause && pause == null)
            {
                pauseScope = new Scope();
                pause = PauseMenu(pauseScope);
                root.Add(pause);
            }
            else if (!showPause && pause != null)
            {
                pause.RemoveFromHierarchy();
                pause = null;
                pauseScope = null;
            }
            pauseScope?.Frame();

            if (UiStore.showSettings && settings == null)
            {
                settings = new SettingsDialog(root);
            }
            else if (!UiStore.showSettings && settings != null)
            {
                settings.Close();
                settings = null;
            }
            settings?.Frame();
        }

        static VisualElement MenuItem(Icon icon, Func<string> label, string hint, Action click, Scope scope)
        {
            var labelEl = Ui.Txt("", "menu-item__label");
            scope.Text(labelEl, label);
            var iconEl = new IconEl(icon, 15, Ui.INK_3);
            var row = Ui.Div("menu-item").With(Ui.Div("menu-item__icon").With(iconEl), labelEl);
            if (hint != null) row.Add(Ui.Txt(hint, "num", "menu-item__hint"));
            row.name = label();
            row.AddManipulator(new Clickable(click));
            row.RegisterCallback<PointerEnterEvent>(_ => iconEl.Tint = Ui.ACCENT);
            row.RegisterCallback<PointerLeaveEvent>(_ => iconEl.Tint = Ui.INK_3);
            return row;
        }

        VisualElement PauseMenu(Scope scope)
        {
            string logCopied = null;
            void Resume() => UiStore.SetPaused(false);

            var recenterItem = MenuItem(Icon.Recenter, () => "Recenter controller", "R", app.Link.Recenter, scope);
            scope.Show(recenterItem, () =>
            {
                var hub = app.Link.hub.GetStatus();
                return hub.link == LinkState.Live && hub.device == DeviceState.Ready;
            });

            var resume = Ui.Btn("Resume", Resume, "primary", "lg");
            resume.AddToClassList("pause__resume");
            resume.Insert(0, new IconEl(Icon.Play, 16, Three.Hex("#04121c")));

            return Ui.Div("fill", "scrim", "centre").With(
                Ui.Div("glass-strong", "rounded-2xl", "pause").With(
                    Ui.Div("row-between", "pause__header").With(Ui.Txt("PAUSED", "display", "pause__title"), Ui.Txt("Esc to resume", "num", "ink-3", "small")),
                    resume,
                    Ui.Div("pause__items").With(
                        MenuItem(Icon.Restart, () => "Restart on runway", "Bksp", () => { RestartOnRunway(); Resume(); }, scope),
                        MenuItem(Icon.Approach, () => "Start on final approach", null, () => { RestartAirborne(); Resume(); }, scope),
                        Ui.Div("menu-divider"),
                        MenuItem(Icon.Camera, () => $"View · {(UiStore.view == CameraView.Chase ? "Chase" : "Cockpit")}", "V", UiStore.ToggleView, scope),
                        recenterItem,
                        MenuItem(Icon.Settings, () => "Settings", null, () => UiStore.SetShowSettings(true), scope),
                        MenuItem(Icon.Clipboard, () => logCopied ?? "Copy flight log", null, () =>
                        {
                            GUIUtility.systemCopyBuffer = Recorder.FlightLogCsv();
                            logCopied = "Copied, paste it into the chat";
                        }, scope),
                        Ui.Div("menu-divider"),
                        MenuItem(Icon.Home, () => "Main menu", null, () => UiStore.SetScreen(AppScreen.Menu), scope)))).Appear("fade");
        }

        /// <summary>Shown while the 3D world builds; covers the first frames so nothing pops in.</summary>
        static VisualElement LoadingOverlay()
        {
            var aircraft = AircraftCatalog.ById(SettingsStore.Get().aircraftId);
            var easyOn = SettingsStore.Get().easyFlight;
            var backdrop = new BackdropEl(
                new[] { Three.Hex("#070b12"), Three.Hex("#070b12") }, new[] { 0f, 1f },
                Three.Hex("#0f1a2b"), new Vector2(0.5f, 0.35f), 0.5f);
            var bar = Ui.Div("loading__bar__glow");
            bar.schedule.Execute(() => bar.ToggleInClassList("loading__bar__glow--far")).Every(550);
            return Ui.Div("fill", "centre", "loading").With(
                backdrop,
                Ui.Div("loading__content").With(
                    Ui.Txt("SKYBOUND", "display", "loading__brand"),
                    Ui.Div("loading__icon").With(new IconEl(Icon.Plane, 26, Ui.ACCENT)),
                    Ui.Txt("Preparing the airfield", "loading__title"),
                    Ui.Txt($"{aircraft.displayName.ToUpperInvariant()} · RUNWAY 36", "num", "ink-3", "loading__aircraft"),
                    Ui.Div("loading__bar").With(bar),
                    Ui.Txt(easyOn ? "Tip: hold the controller in your normal position as the flight starts; that pose flies straight." : "Tip: trim with [ and ] to fly hands-off.", "loading__tip")));
        }

        public void Unmount()
        {
            hud.Dispose();
            session.keyboard.holdFaster = session.keyboard.holdSlower = session.keyboard.holdBrake = false;
        }
    }
}
