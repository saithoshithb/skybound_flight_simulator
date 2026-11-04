using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>The start menu: pick an aircraft, start a flight, see whether a controller is connected.</summary>
    public class MenuScreen : IScreen
    {
        readonly App app;
        readonly VisualElement root;
        readonly Scope scope = new Scope();
        SettingsDialog settings;

        public SettingsDialog Settings => settings;

        /// <summary>Side-view silhouette: the wing sits above or below the fuselage.</summary>
        class Silhouette : CanvasElement
        {
            const float VIEW_W = 220, VIEW_H = 70;

            readonly AircraftDef ac;

            public Silhouette(AircraftDef aircraft)
            {
                ac = aircraft;
                AddToClassList("silhouette");
                RegisterCallback<GeometryChangedEvent>(e => Paint(e.newRect.width, e.newRect.height));
            }

            void Paint(float w, float h)
            {
                var c = Begin();
                var scale = Mathf.Min(w / VIEW_W, h / VIEW_H);
                c.Translate((w - VIEW_W * scale) / 2, (h - VIEW_H * scale) / 2);
                c.unit = scale;
                DrawSilhouette(c, scale);
                End();
            }

            void DrawSilhouette(Ctx c, float scale)
            {
                var high = ac.look.highWing;
                var body = Three.Hex(ac.look.body);
                var stripe = Three.Hex(ac.look.stripe);
                var accent = Three.Hex(ac.look.accent);
                var canopy = Three.Hex("#26323c");
                var gear = Three.Hex("#8e9398");
                var tire = Three.Hex("#1c1e20");
                // fuselage
                c.FillStyle(body);
                c.BeginPath();
                c.MoveTo(14, 38);
                c.BezierCurveTo(20, 30, 40, 28, 64, 28);
                c.LineTo(150, 31);
                c.BezierCurveTo(175, 32, 192, 33, 204, 30);
                c.LineTo(206, 42);
                c.BezierCurveTo(190, 42, 170, 43, 150, 43);
                c.LineTo(64, 46);
                c.BezierCurveTo(40, 46, 20, 45, 14, 38);
                c.ClosePath();
                c.Fill();
                c.StrokeStyle(stripe);
                c.lineWidth = 2.5f * scale;
                c.BeginPath();
                c.MoveTo(70, 37);
                c.LineTo(196, 36);
                c.Stroke();
                // canopy / cabin
                c.FillStyle(canopy);
                c.BeginPath();
                if (high)
                {
                    c.MoveTo(66, 28);
                    c.LineTo(78, 18);
                    c.LineTo(112, 18);
                    c.LineTo(116, 28);
                }
                else
                {
                    c.MoveTo(70, 29);
                    c.BezierCurveTo(76, 18, 100, 16, 112, 28);
                }
                c.ClosePath();
                c.Fill();
                // wing (seen edge-on)
                c.FillStyle(body);
                c.BeginPath();
                c.RoundRect(72, high ? 14 : 44, 56, 5, 2);
                c.Fill();
                if (high)
                {
                    c.StrokeStyle(body);
                    c.lineWidth = 2 * scale;
                    c.BeginPath();
                    c.MoveTo(84, 44);
                    c.LineTo(96, 19);
                    c.Stroke();
                }
                // tail
                c.FillStyle(stripe);
                c.BeginPath();
                c.MoveTo(182, 31);
                c.LineTo(196, 8);
                c.LineTo(208, 8);
                c.LineTo(204, 31);
                c.ClosePath();
                c.Fill();
                c.FillStyle(body);
                c.FillRect(180, 33, 28, 3);
                // prop and spinner
                c.FillStyle(accent);
                c.BeginPath();
                c.MoveTo(8, 38);
                c.LineTo(14, 34);
                c.LineTo(14, 42);
                c.ClosePath();
                c.Fill();
                c.FillStyle(Three.Hex("#3a3d40"));
                c.BeginPath();
                c.RoundRect(6, 18, 2.5f, 40, 1);
                c.Fill();
                // gear
                c.StrokeStyle(gear);
                c.lineWidth = 2 * scale;
                c.BeginPath();
                c.MoveTo(40, 46);
                c.LineTo(40, 58);
                c.MoveTo(104, 46);
                c.LineTo(110, 58);
                c.Stroke();
                c.FillStyle(tire);
                c.BeginPath();
                c.Arc(40, 60, 4, 0, Mathf.PI * 2);
                c.Fill();
                c.BeginPath();
                c.Arc(110, 60, 4.5f, 0, Mathf.PI * 2);
                c.Fill();
            }
        }

        static VisualElement Stat(string label, string value)
        {
            return Ui.Div("stat").With(Ui.Txt(label.ToUpperInvariant(), "label", "stat__label"), Ui.Txt(value, "num", "stat__value"));
        }

        VisualElement AircraftCard(AircraftDef ac)
        {
            var state = Ui.Txt("", "semi", "aircraft-card__state");
            var card = Ui.Div("glass", "rounded-xl", "aircraft-card").With(
                Ui.Div("row-between").With(Ui.Txt(ac.displayName, "display", "aircraft-card__name"), state),
                new Silhouette(ac),
                Ui.Txt(ac.description, "aircraft-card__description"),
                Ui.Div("aircraft-card__stats").With(
                    Stat("Cruise", $"{ac.easy.cruiseKt:0} kt"),
                    Stat("Stall", $"{Math.Round(FlightModel.StallSpeedKt(ac)):0} kt"),
                    Stat("Power", $"{Math.Round(ac.maxPowerW / 745.7):0} hp"),
                    Stat("Span", $"{ac.span:0.0} m")));
            card.name = ac.id;
            card.AddManipulator(new Clickable(() => SettingsStore.SetAircraftId(ac.id)));
            scope.On(() =>
            {
                var selected = SettingsStore.Get().aircraftId == ac.id;
                card.EnableInClassList("aircraft-card--selected", selected);
                state.EnableInClassList("aircraft-card__state--selected", selected);
                Ui.SetText(state, selected ? "SELECTED" : "SELECT");
            });
            return card;
        }

        public MenuScreen(VisualElement parent, App application)
        {
            root = parent;
            app = application;
            var hub = app.Link.hub;
            bool Live()
            {
                var status = hub.GetStatus();
                return status.Usable;
            }

            // Quiet sky-to-ground backdrop
            root.Add(new BackdropEl(
                new[] { Three.Hex("#0b1626"), Three.Hex("#0a1220"), Three.Hex("#080d16"), Three.Hex("#070b12") },
                new[] { 0f, 0.5f, 0.75f, 1f },
                Three.Hex("#16304f"), new Vector2(0.7f, -0.1f), 0.5f));
            root.Add(Ui.Div("menu-horizon"));

            var fleet = Ui.Div("aircraft-grid");
            foreach (var ac in AircraftCatalog.All)
            {
                var card = AircraftCard(ac);
                fleet.Add(card);
            }

            var flyText = Ui.Txt("", "hint", "mt-3");
            scope.Text(flyText, () => SettingsStore.Get().easyFlight
                ? "Easy flight is on: press + once on the runway to take off, tilt to turn and climb, let go to level out."
                : "Manual flying: full power with +, rotate at about 55 kt. Easy flight can be turned on in Settings.");

            var controllerDot = Ui.Div("dot");
            scope.On(() =>
            {
                var live = Live();
                var simulated = hub.GetStatus().transport == TransportKind.Simulated;
                controllerDot.EnableInClassList("dot--ok", live && !simulated);
                controllerDot.EnableInClassList("dot--warn", live && simulated);
                controllerDot.EnableInClassList("dot--idle", !live);
            });
            var controllerText = Ui.Txt("", "controller-status__text");
            scope.Text(controllerText, () => !Live()
                ? "No controller connected: you can fly with the keyboard"
                : hub.GetStatus().transport == TransportKind.Simulated
                    ? "Simulated controller connected, not your ESP32"
                    : "ESP32 motion controller connected");
            var connect = Ui.Btn("", () => UiStore.SetScreen(AppScreen.Setup), "ghost", "sm");
            connect.AddToClassList("menu-connect");
            scope.Text(connect, () => Live() ? "Recalibrate or tune →" : "Connect a controller →");

            root.Add(Ui.Div("fill", "menu-layout").With(Ui.Div("menu").With(
                Ui.Div("menu__header").With(
                    Ui.Div().With(
                        Ui.Txt("SKYBOUND", "display", "menu__title").Appear(),
                        Ui.Txt("A motion-controlled flight simulator", "menu__subtitle")),
                    HeaderButtons()),
                Ui.Div("menu__main").With(
                    Ui.Div("menu__aircraft").With(Ui.SectionLabel("Aircraft"), fleet),
                    Ui.Div("menu__aside").With(
                        Ui.Div("glass", "rounded-xl", "menu-card").With(
                            Ui.SectionLabel("Fly"),
                            Ui.Btn("Take off from runway 36", () => UiStore.StartFlight(StartPosition.Runway), "primary", "lg"),
                            Ui.Btn("Start on final approach", () => UiStore.StartFlight(StartPosition.Final), "outline", "lg"),
                            flyText).Appear(),
                        Ui.Div("glass", "rounded-xl", "menu-card").With(
                            Ui.SectionLabel("Controller"),
                            Ui.Div("controller-status").With(controllerDot, controllerText),
                            connect).Appear())),
                Ui.Div("menu__footer").With(
                    Ui.Txt("Enter · take off", "menu__footer-text"),
                    Ui.Txt("In flight: Esc pause · V cockpit · I instruments · R recenter", "menu__footer-text")))));
        }

        static VisualElement HeaderButtons()
        {
            var row = Ui.Div("row").With(
                Ui.Btn("Controller setup", () => UiStore.SetScreen(AppScreen.Setup), "ghost"),
                Ui.Btn("Settings", () => UiStore.SetShowSettings(true)));
            // A browser tab has its own close button. A fullscreen game needs one.
            if (!Application.isEditor) row.Add(Ui.Btn("Quit", Application.Quit, "ghost"));
            return row;
        }

        public void Frame()
        {
            var kb = Keyboard.current;
            if (kb != null && !UiStore.showSettings && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                UiStore.StartFlight(StartPosition.Runway);
            scope.Frame();

            if (UiStore.showSettings && settings == null) settings = new SettingsDialog(root);
            else if (!UiStore.showSettings && settings != null)
            {
                settings.Close();
                settings = null;
            }
            settings?.Frame();
        }

        public void Unmount() { }
    }
}
