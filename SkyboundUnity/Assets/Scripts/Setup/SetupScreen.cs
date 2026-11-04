using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>Motion-controller connection, calibration and tuning.</summary>
    public class SetupScreen : IScreen
    {
        readonly Scope scope = new Scope();
        readonly ControllerView controllerView;
        readonly ConnectionPanel connection;

        public ConnectionPanel Connection => connection;
        public ControllerView View => controllerView;

        public SetupScreen(VisualElement root, App app)
        {
            var hub = app.Link.hub;
            var status = Ui.Txt("", "hint");
            scope.Text(status, () =>
            {
                var st = hub.GetStatus();
                if (!st.Usable) return "No controller: you can fly with the keyboard";
                if (st.transport == TransportKind.Simulated) return "Simulator connected, not your ESP32 (switch Source to ESP32 · USB)";
                return SettingsStore.Get().neutral.capturedAt > 0 ? "ESP32 controller ready" : "Set neutral with Recenter before flying";
            });

            connection = new ConnectionPanel(scope, app);
            controllerView = new ControllerView(scope, hub);
            var simulator = SimulatorPanel.Build(scope, hub);
            scope.Show(simulator, () => SettingsStore.Get().source == TransportKind.Simulated);

            var page = Ui.Scroller("setup__scroll");
            page.Add(Ui.Div("setup__main").With(
                Ui.Div("setup__left").With(connection.Root, simulator),
                Ui.Div("setup__centre").With(controllerView.Root, LiveReadouts.Build(scope, app), DiagnosticsPanel.Build(scope, hub)),
                Ui.Div("setup__right").With(ResponsePanel.Build(scope))));

            root.Add(Ui.Div("fill", "setup").With(
                Ui.Div("setup__header").With(
                    Ui.Div("row-baseline").With(Ui.Txt("SKYBOUND", "semi", "setup__brand"), Ui.Txt("Motion controller setup", "ink-3", "setup__subtitle")),
                    Ui.Div("row").With(
                        status,
                        Ui.Btn("← Menu", () => UiStore.SetScreen(AppScreen.Menu), "ghost"),
                        Ui.Btn("Fly →", () => UiStore.StartFlight(StartPosition.Runway), "primary"))),
                page));
        }

        public void Frame()
        {
            scope.Frame();
        }

        public void Unmount()
        {
            controllerView.Dispose();
        }
    }
}
