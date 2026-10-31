using System;
using System.Globalization;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// Source, connect and calibrate. The browser's Connect opens its own port chooser; a desktop game
    /// has none, so the ports are listed here, with the USB ones first.
    /// </summary>
    public class ConnectionPanel
    {
        enum Busy
        {
            None,
            Calibrate,
            Recenter,
        }

        public static string LinkLabel(LinkState link)
        {
            switch (link)
            {
                case LinkState.Connecting: return "Connecting…";
                case LinkState.Awaiting: return "Waiting for sensor";
                case LinkState.Live: return "Live";
                case LinkState.Stale: return "Data late";
                case LinkState.Lost: return "No data";
                default: return "Not connected";
            }
        }

        static string LinkTone(LinkState link)
        {
            switch (link)
            {
                case LinkState.Live: return "ok";
                case LinkState.Connecting:
                case LinkState.Awaiting:
                case LinkState.Stale: return "warn";
                case LinkState.Lost: return "bad";
                default: return "idle";
            }
        }

        public static string DeviceLabel(DeviceState device)
        {
            switch (device)
            {
                case DeviceState.Calibrating: return "Calibrating gyro — hold still";
                case DeviceState.Ready: return "Ready";
                case DeviceState.SensorError: return "Sensor error";
                case DeviceState.MotionDuringCal: return "Moved during calibration, retrying";
                case DeviceState.CalFailed: return "Calibration failed (using previous bias)";
                default: return "—";
            }
        }

        readonly App app;
        readonly SensorHub hub;
        readonly SerialSession serial;
        readonly VisualElement ports = Ui.Div("port-list");
        Busy busy;
        string note;
        bool noteOk;
        SerialPortInfo[] portsShown;
        string chosenShown;

        public VisualElement Root { get; }

        /// <summary>The port Connect will open: the one chosen before, or the first USB one.</summary>
        string ChosenPort()
        {
            var last = SettingsStore.Get().lastPort;
            string firstUsb = null;
            foreach (var p in serial.Ports)
            {
                if (p.name == last) return last;
                if (firstUsb == null && !p.bluetooth) firstUsb = p.name;
            }
            return firstUsb;
        }

        public ConnectionPanel(Scope scope, App application)
        {
            app = application;
            var link = app.Link;
            hub = link.hub;
            serial = link.serial;
            bool Connected() => hub.GetStatus().link != LinkState.Disconnected && hub.GetStatus().link != LinkState.Connecting;
            bool Usable() => hub.IsUsable();

            var linkDot = Ui.Div("dot");
            var linkText = Ui.Txt("", "semi", "link-text");
            scope.On(() =>
            {
                var state = hub.GetStatus().link;
                Ui.SetText(linkText, LinkLabel(state));
                var tone = LinkTone(state);
                linkDot.EnableInClassList("dot--ok", tone == "ok");
                linkDot.EnableInClassList("dot--warn", tone == "warn");
                linkDot.EnableInClassList("dot--bad", tone == "bad");
                linkDot.EnableInClassList("dot--idle", tone == "idle");
            });

            var source = new SegmentedEl<TransportKind>(scope, () => SettingsStore.Get().source, v =>
                {
                    if (!Connected()) SettingsStore.SetSource(v);
                },
                (TransportKind.Serial, "ESP32 · USB"), (TransportKind.Simulated, "Simulated"));

            var noPorts = Ui.Txt("No COM ports found. Plug in the ESP32.", "bad-text");
            scope.Show(ports, () => SettingsStore.Get().source == TransportKind.Serial && !Connected());
            scope.Show(noPorts, () => SettingsStore.Get().source == TransportKind.Serial && !Connected() && serial.Ports.Length == 0);
            scope.On(RefreshPorts);

            var connect = Ui.Btn("Connect", Connect, "primary");
            scope.Text(connect, () => hub.GetStatus().link == LinkState.Connecting ? "Connecting…" : "Connect");
            scope.On(() => connect.SetEnabled(busy == Busy.None && hub.GetStatus().link != LinkState.Connecting && (SettingsStore.Get().source != TransportKind.Serial || ChosenPort() != null)));
            scope.Show(connect, () => !Connected());
            var disconnect = Ui.Btn("Disconnect", serial.Disconnect, "danger");
            scope.Show(disconnect, Connected);

            var reconnect = Ui.Btn("Reconnect", serial.Reconnect);
            bool ShowReconnect() => SettingsStore.Get().source == TransportKind.Serial && !Connected() && serial.CanReconnect();
            scope.Show(reconnect, ShowReconnect);
            scope.On(() => reconnect.SetEnabled(busy == Busy.None));
            var calibrate = Ui.Btn("Calibrate gyro", OnCalibrate);
            calibrate.tooltip = "Re-measure gyro bias on the device (hold still)";
            scope.Show(calibrate, () => !ShowReconnect());
            scope.On(() => calibrate.SetEnabled(Connected() && busy == Busy.None && hub.GetStatus().device != DeviceState.Calibrating));

            var recenter = Ui.Btn("", OnRecenter);
            recenter.AddToClassList("connection__wide");
            recenter.tooltip = "Capture the current pose as neutral (shortcut: R)";
            scope.Text(recenter, () => busy == Busy.Recenter ? "Hold still…" : "Recenter  ·  R");
            scope.On(() => recenter.SetEnabled(Usable() && busy == Busy.None));

            // R = recenter, a hands-free-ish shortcut while holding the controller.
            scope.On(() =>
            {
                var kb = Keyboard.current;
                if (kb != null && kb.rKey.wasPressedThisFrame && Usable() && busy == Busy.None) OnRecenter();
            });

            var device = Ui.Txt("", "dd-right");
            scope.On(() =>
            {
                var st = hub.GetStatus();
                Ui.SetText(device, DeviceLabel(st.device) + (st.device == DeviceState.SensorError && !string.IsNullOrEmpty(st.deviceMessage) ? "\n" + st.deviceMessage : ""));
                device.EnableInClassList("bad-text", st.device == DeviceState.SensorError);
            });
            var port = Ui.Txt("", "dd-right");
            scope.Text(port, () => string.IsNullOrEmpty(hub.GetStatus().transportLabel) ? "—" : hub.GetStatus().transportLabel);
            var firmware = Ui.Txt("", "num", "dd-right");
            scope.Text(firmware, () => hub.GetStatus().firmware ?? "—");
            var neutral = Ui.Txt("", "num", "dd-right");
            scope.On(() =>
            {
                var n = SettingsStore.Get().neutral;
                Ui.SetText(neutral, n.capturedAt != 0 ? $"{Fixed1(n.pitch)}° / {Fixed1(n.roll)}°" : "not set");
                neutral.EnableInClassList("warn-text", n.capturedAt == 0);
            });
            var clear = Ui.Txt("clear", "link-button");
            clear.AddManipulator(new Clickable(() => SettingsStore.SetNeutral(new Neutral())));
            scope.Show(clear, () => SettingsStore.Get().neutral.capturedAt != 0);

            var noteEl = Ui.Txt("", "note");
            scope.On(() =>
            {
                var text = note ?? hub.GetStatus().lastError;
                Ui.SetShown(noteEl, !string.IsNullOrEmpty(text));
                if (string.IsNullOrEmpty(text)) return;
                Ui.SetText(noteEl, text);
                var ok = note != null && noteOk;
                noteEl.EnableInClassList("note--ok", ok);
                noteEl.EnableInClassList("note--bad", !ok);
            });

            Root = Ui.PanelBox("Connection", Ui.Div("row").With(linkDot, linkText), Ui.Div().With(
                Ui.SectionLabel("Source"),
                source,
                ports,
                noPorts,
                Ui.Div("connection__buttons").With(connect, disconnect, reconnect, calibrate, recenter),
                Ui.Div("connection__table").With(
                    Ui.Div("row-between").With(Ui.Txt("Device", "ink-3", "small"), device),
                    Ui.Div("row-between").With(Ui.Txt("Port", "ink-3", "small"), port),
                    Ui.Div("row-between").With(Ui.Txt("Firmware", "ink-3", "small"), firmware),
                    Ui.Div("row-between").With(Ui.Txt("Neutral", "ink-3", "small"), Ui.Div("row").With(neutral, clear))),
                noteEl));
        }

        static string Fixed1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        void RefreshPorts()
        {
            var chosen = ChosenPort();
            if (serial.Ports == portsShown && chosen == chosenShown) return;
            portsShown = serial.Ports;
            chosenShown = chosen;
            ports.Clear();
            foreach (var p in serial.Ports)
            {
                var name = p.name;
                var button = Ui.Btn(p.Display, () => SettingsStore.SetLastPort(name), "ghost", "sm");
                button.AddToClassList("port");
                button.EnableInClassList("port--chosen", name == chosen);
                ports.Add(button);
            }
        }

        void Connect()
        {
            note = null;
            if (SettingsStore.Get().source == TransportKind.Simulated)
            {
                serial.ConnectSimulated();
                return;
            }
            var port = ChosenPort();
            if (port == null) return;
            SettingsStore.SetLastPort(port);
            serial.ConnectSerial(port);
        }

        void OnCalibrate()
        {
            note = null;
            hub.Send(Protocol.CMD_CAL);
            note = "Gyro calibration started on the device. Keep the controller still for ~2 s.";
            noteOk = true;
        }

        void OnRecenter()
        {
            note = null;
            busy = Busy.Recenter;
            app.Link.StartCoroutine(NeutralCalibration.CaptureNeutral(hub, r =>
            {
                busy = Busy.None;
                if (!r.ok)
                {
                    note = r.detail;
                    noteOk = false;
                    return;
                }
                app.Link.SetNeutral(r.neutral);
                note = $"Neutral set at {Fixed1(r.neutral.pitch)}° pitch, {Fixed1(r.neutral.roll)}° roll.";
                noteOk = true;
            }));
        }
    }
}
