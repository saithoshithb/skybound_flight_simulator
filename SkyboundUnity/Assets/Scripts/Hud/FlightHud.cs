using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// Flight HUD, glass-cockpit style, arranged around the screen edges so the aircraft stays clear:
    /// heading ribbon on top, airspeed tape left, altitude tape and vertical speed right, attitude
    /// ball and throttle in the bottom corners. Everything is drawn from live telemetry once a frame.
    /// </summary>
    public class FlightHud
    {
        const float TAPE_H = 245;
        const float SPEED_W = 84;
        const float VSI_W = 16;
        const float ALT_W = 96;
        const float HDG_W = 435;
        const float HDG_H = 46;
        const float BALL = 112;
        const float WARN_PULSE_S = 0.9f;
        /// <summary>Stands for "nothing to show" in a number that's otherwise never negative.</summary>
        const int NONE = -1;
        /// <summary>Keeps the manual-flying numbers apart from the Easy flight ones in one label.</summary>
        const int MANUAL = 100000;

        static string Signed(double n, int digits = 0)
        {
            return (n >= 0 ? "+" : "−") + Math.Abs(n).ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        static string Thousands(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
        static int Tenths(double v) => (int)Math.Round(v * 10);
        static string SignedTenths(int tenths) => Signed(tenths / 10.0, 1) + "°";
        static string Percent(string[] words, int v)
        {
            // v is the signed percentage: its sign picks the word.
            return v == 0 ? "level" : $"{words[v > 0 ? 1 : 0]} {Math.Abs(v)}%";
        }

        readonly App app;
        readonly FlightScreen screen;
        readonly FlightSession session;
        readonly SensorHub hub;
        readonly Scope scope = new Scope();
        readonly VisualElement host;

        readonly CanvasElement hdgCanvas = NewCanvas(HDG_W, HDG_H);
        readonly CanvasElement speedCanvas = NewCanvas(SPEED_W, TAPE_H);
        readonly CanvasElement altCanvas = NewCanvas(ALT_W, TAPE_H);
        readonly CanvasElement hdgBacking = NewCanvas(HDG_W, HDG_H);
        readonly CanvasElement speedBacking = NewCanvas(SPEED_W, TAPE_H);
        readonly CanvasElement altBacking = NewCanvas(ALT_W, TAPE_H);
        readonly CanvasElement vsiCanvas = NewCanvas(VSI_W, TAPE_H);
        readonly CanvasElement ballCanvas = NewCanvas(BALL, BALL);

        readonly Label hdg, vs, agl, speedSet, pitch, roll, aoa, g, thr, thrMode, easy, fps, phase, brakes, warn, msg, source, cmdTurn, cmdClimb, crashReason;
        readonly VisualElement sourceDot, cmd, thrFill, thrKnob, crash, tapesLeft, tapesRight, attitude, instruments, keyCard;
        readonly IconButton recenter, viewButton, instrumentsButton, helpButton;
        readonly Label keyCardTitle;
        readonly VisualElement keyRows;
        readonly Label keyCardNote;
        bool keyCardEasy;
        bool keyCardBuilt;

        static readonly string[] TURN_WORDS = { "L", "R" };
        static readonly string[] CLIMB_WORDS = { "down", "up" };

        readonly IntLabel hdgText, vsText, aglText, speedSetText, pitchText, rollText, aoaText, gText, thrText, thrModeText, fpsText, cmdTurnText, cmdClimbText;
        string bandsFor = "";
        SpeedBands bands = new SpeedBands { stallKt = 50, normalMaxKt = 115, vneKt = 163 };

        /// <summary>The whole HUD, for tests.</summary>
        public VisualElement Root => host;

        static CanvasElement NewCanvas(float w, float h)
        {
            var canvas = new CanvasElement();
            canvas.style.width = w;
            canvas.style.height = h;
            canvas.style.flexShrink = 0;
            return canvas;
        }

        /// <summary>A canvas over its backing, which is drawn once.</summary>
        static VisualElement Layered(CanvasElement backing, CanvasElement canvas)
        {
            backing.Add(canvas);
            return backing;
        }

        public FlightHud(VisualElement parent, App application, FlightScreen flightScreen)
        {
            app = application;
            screen = flightScreen;
            session = app.Session;
            hub = app.Link.hub;

            host = Ui.Div("fill", "hud-text");
            host.pickingMode = PickingMode.Ignore;
            parent.Add(host);

            // ---------- top-left: controller and status ----------
            sourceDot = Ui.Div("dot");
            source = Ui.Txt("", "semi", "hud-source");
            cmdTurn = Ui.Txt("", "num", "cmd-value");
            cmdClimb = Ui.Txt("", "num", "cmd-value");
            cmd = Ui.Div("glass", "rounded-lg", "hud-cmd").With(Ui.Txt("TURN", "num"), cmdTurn, Ui.Txt("CLIMB", "num", "ml-3"), cmdClimb);
            easy = Ui.Txt("EASY FLIGHT", "display", "chip", "chip--easy");
            phase = Ui.Txt("", "display", "chip", "glass");
            brakes = Ui.Txt("BRAKES", "display", "chip", "chip--brakes");
            fps = Ui.Txt("", "num", "chip", "glass", "chip--fps");
            host.Add(Ui.Div("hud-top-left").With(
                Ui.Div("glass", "rounded-lg", "hud-source-pill").With(sourceDot, source),
                cmd,
                Ui.Div("chips").With(easy, phase, brakes, fps)).Appear());

            // ---------- top-right: toolbar ----------
            recenter = new IconButton(Icon.Recenter, "Recenter controller", "R", () => app.Link.Recenter());
            viewButton = new IconButton(Icon.Camera, "Cockpit view", "V", UiStore.ToggleView);
            instrumentsButton = new IconButton(Icon.Gauge, "Instruments", "I", UiStore.ToggleInstruments);
            helpButton = new IconButton(Icon.Help, "Controls", "H", UiStore.ToggleHelp);
            host.Add(Ui.Div("hud-toolbar").With(
                recenter,
                viewButton,
                instrumentsButton,
                helpButton,
                new IconButton(Icon.Settings, "Settings", null, () => UiStore.SetShowSettings(true)),
                new IconButton(Icon.Pause, "Pause", "Esc", () => UiStore.SetPaused(true))).Appear());

            // Key card: left of the altitude tape so it never covers the instruments
            keyCardTitle = Ui.Txt("", "label");
            keyRows = Ui.Div();
            keyCardNote = Ui.Txt("Bank is limited to 35°, climb and descent to 1,000 fpm, and the aircraft won’t stall.", "key-card__note");
            keyCard = Ui.Div("glass", "rounded-xl", "key-card").With(
                Ui.Div("row-between", "key-card__title").With(keyCardTitle, Ui.Txt("H", "label")),
                keyRows,
                keyCardNote);
            host.Add(keyCard);

            // ---------- top-centre: heading, warnings, messages ----------
            hdg = Ui.Txt("", "num", "hud-hdg");
            warn = Ui.Txt("", "display", "hud-warn");
            msg = Ui.Txt("", "glass", "rounded-lg", "hud-msg");
            host.Add(Ui.Div("hud-top-centre").With(Layered(hdgBacking, hdgCanvas), hdg, warn, msg));
            Tapes.DrawHeadingBacking(hdgBacking.Begin(), HDG_W, HDG_H);
            hdgBacking.End();

            // ---------- left edge: airspeed tape ----------
            speedSet = Ui.Txt("", "num", "hud-speed-set");
            tapesLeft = Ui.Div("hud-tape-left").With(Ui.Txt("IAS · KT", "label", "tape-title"), Layered(speedBacking, speedCanvas), speedSet);
            Tapes.TapeBacking(speedBacking.Begin(), 0, SPEED_W - 10, TAPE_H);
            speedBacking.End();
            host.Add(tapesLeft);

            // ---------- right edge: altitude tape + vertical speed ----------
            vs = Ui.Txt("", "num", "hud-vs");
            agl = Ui.Txt("", "num", "hud-agl");
            tapesRight = Ui.Div("hud-tape-right").With(
                Ui.Txt("ALT · FT", "label", "tape-title"),
                Ui.Div("tape-pair").With(vsiCanvas, Layered(altBacking, altCanvas)),
                Ui.Div("hud-vs-row").With(Ui.Txt("VS", "num", "ink-3"), vs),
                agl);
            host.Add(tapesRight);
            Tapes.TapeBacking(altBacking.Begin(), 0, ALT_W, TAPE_H);
            altBacking.End();

            // ---------- bottom-left: attitude ----------
            pitch = Ui.Txt("", "num", "dd");
            roll = Ui.Txt("", "num", "dd");
            aoa = Ui.Txt("", "num", "dd", "ink-2");
            g = Ui.Txt("", "num", "dd", "ink-2");
            attitude = Ui.Div("hud-attitude").With(ballCanvas, Ui.Div("hud-attitude__table").With(
                Ui.Div("row-between").With(Ui.Txt("PITCH", "num", "ink-3"), pitch),
                Ui.Div("row-between").With(Ui.Txt("BANK", "num", "ink-3"), roll),
                Ui.Div("row-between").With(Ui.Txt("AOA", "num", "ink-3"), aoa),
                Ui.Div("row-between").With(Ui.Txt("G", "num", "ink-3"), g))).Appear();
            host.Add(attitude);

            // ---------- bottom-centre: optional instrument panel ----------
            // The cockpit's instrument texture shown on screen in the chase view: the same texture
            // the 3D panel uses, so the two can never disagree.
            var overlay = Ui.Div("hud-instrument-image");
            overlay.style.backgroundImage = Background.FromRenderTexture(InstrumentPanel.Get().Texture);
            instruments = Ui.Div("hud-instruments").With(Ui.Div("glass", "rounded-xl", "hud-instruments__frame").With(overlay));
            host.Add(instruments);

            // ---------- bottom-right: throttle ----------
            thrFill = Ui.Div("throttle__fill");
            thrKnob = Ui.Div("throttle__knob");
            thr = Ui.Txt("0", "num", "throttle__value");
            thrMode = Ui.Txt("", "num", "throttle__mode");
            var kb = session.keyboard;
            host.Add(Ui.Div("glass", "rounded-xl", "hud-throttle").With(
                Ui.Div("throttle__keys").With(
                    HoldKey(new IconEl(Icon.Plus, 16, Ui.INK_2), "Faster / more throttle (hold)", on => kb.holdFaster = on),
                    HoldKey(new IconEl(Icon.Minus, 16, Ui.INK_2), "Slower / less throttle (hold)", on => kb.holdSlower = on),
                    HoldKey(Ui.Txt("BRK", "display", "throttle__brk"), "Brakes (hold)", on => kb.holdBrake = on)),
                Ui.Div("throttle__track").With(thrFill, thrKnob),
                Ui.Div("throttle__text").With(
                    Ui.Txt("THROTTLE", "label", "tape-title"),
                    Ui.Div("throttle__number").With(thr, Ui.Txt("%", "num", "ink-3", "throttle__percent")),
                    thrMode)).Appear());

            // ---------- crash ----------
            crashReason = Ui.Txt("", "crash__reason");
            crash = Ui.Div("fill", "scrim", "centre").With(
                Ui.Div("glass-strong", "rounded-2xl", "crash").With(
                    Ui.Txt("!", "crash__mark"),
                    Ui.Txt("CRASHED", "display", "crash__title"),
                    crashReason,
                    Ui.Div("crash__buttons").With(
                        Ui.Btn("Restart", screen.RestartOnRunway, "primary"),
                        Ui.Btn("Main menu", () => UiStore.SetScreen(AppScreen.Menu))),
                    Ui.Txt("Backspace to restart · Esc for the pause menu", "crash__hint")));
            host.Add(crash);

            hdgText = new IntLabel(hdg, v => v.ToString("000") + "°");
            vsText = new IntLabel(vs, v => Signed(v));
            aglText = new IntLabel(agl, v => $"{Thousands(v)} AGL");
            speedSetText = new IntLabel(speedSet, v => v >= MANUAL ? $"GS {v - MANUAL}" : v == NONE ? "IDLE" : $"SET {v}");
            pitchText = new IntLabel(pitch, SignedTenths);
            rollText = new IntLabel(roll, SignedTenths);
            aoaText = new IntLabel(aoa, v => (v / 10.0).ToString("F1", CultureInfo.InvariantCulture) + "°");
            gText = new IntLabel(g, v => (v / 100.0).ToString("F2", CultureInfo.InvariantCulture));
            thrText = new IntLabel(thr, NumberText.Whole);
            // Easy flight: the target speed, or NONE. Manual: MANUAL + trim percent, kept positive.
            thrModeText = new IntLabel(thrMode, v => v >= MANUAL / 2 ? (v == MANUAL ? "TRIM 0" : $"TRIM {Signed(v - MANUAL)}") : v == NONE ? "IDLE · PRESS +" : $"SPEED {v} KT");
            fpsText = new IntLabel(fps, v => v > 0 ? $"{v} FPS" : "– FPS");
            cmdTurnText = new IntLabel(cmdTurn, v => Percent(TURN_WORDS, v));
            cmdClimbText = new IntLabel(cmdClimb, v => Percent(CLIMB_WORDS, v));

            // Coarse state: what the browser's React re-render decides.
            scope.Show(recenter, () => session.pilotInfo.source == ControlSource.Motion);
            scope.On(() =>
            {
                var cockpit = UiStore.view == CameraView.Cockpit;
                viewButton.Active = cockpit;
                viewButton.tooltip = cockpit ? "Chase view (V)" : "Cockpit view (V)";
                instrumentsButton.Active = UiStore.showInstruments;
                helpButton.Active = UiStore.showHelp;
                Ui.SetShown(keyCard, UiStore.showHelp);
                Ui.SetShown(tapesLeft, !cockpit);
                Ui.SetShown(tapesRight, !cockpit);
                Ui.SetShown(attitude, !cockpit);
                Ui.SetShown(instruments, UiStore.showInstruments && !cockpit);
                var isEasy = SettingsStore.Get().easyFlight;
                if (!keyCardBuilt || keyCardEasy != isEasy) BuildKeyCard(isEasy);
            });
            scope.On(Loop);
        }

        /// <summary>Hold-to-repeat button that acts like holding a key, so it uses exactly the keyboard's control path.</summary>
        static VisualElement HoldKey(VisualElement content, string label, Action<bool> press)
        {
            var button = Ui.Div("hold-key").With(content);
            button.tooltip = label;
            button.name = label;
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                button.CapturePointer(e.pointerId);
                press(true);
            });
            button.RegisterCallback<PointerUpEvent>(e =>
            {
                button.ReleasePointer(e.pointerId);
                press(false);
            });
            button.RegisterCallback<PointerCaptureOutEvent>(_ => press(false));
            return button;
        }

        void BuildKeyCard(bool isEasy)
        {
            keyCardBuilt = true;
            keyCardEasy = isEasy;
            keyCardTitle.text = (isEasy ? "Easy flight controls" : "Controls").ToUpperInvariant();
            Ui.SetShown(keyCardNote, isEasy);
            keyRows.Clear();
            var rows = isEasy
                ? new[]
                {
                    ("Tilt back · ↓ S", "Climb"),
                    ("Tilt forward · ↑ W", "Descend"),
                    ("Tilt sideways · ← → A D", "Turn"),
                    ("Let go", "Levels out"),
                    ("+ − · PgUp PgDn", "Speed · first + takes off"),
                    ("Tilt sideways · Q E", "Steer on the ground"),
                    ("B", "Brakes"),
                    ("V · I", "Cockpit · instruments"),
                    ("R", "Recenter controller"),
                    ("Esc · P", "Pause"),
                    ("Backspace", "Restart on runway"),
                }
                : new[]
                {
                    ("↓ S · ↑ W", "Pull · push"),
                    ("← → · A D", "Roll"),
                    ("Q · E", "Rudder, nose wheel"),
                    ("+ − · PgUp PgDn", "Throttle"),
                    ("[ ]", "Trim"),
                    ("B", "Brakes"),
                    ("V · I", "Cockpit · instruments"),
                    ("R", "Recenter controller"),
                    ("Esc · P", "Pause"),
                    ("Backspace", "Restart on runway"),
                };
            foreach (var (k, v) in rows)
                keyRows.Add(Ui.Div("row-between", "key-card__row").With(Ui.Txt(k, "key-card__key"), Ui.Txt(v, "key-card__action")));
        }

        void Loop()
        {
            var flight = session.flight;
            var t = flight.telemetry;
            var s = SettingsStore.Get();
            var info = session.pilotInfo;

            if (bandsFor != flight.ac.id)
            {
                bandsFor = flight.ac.id;
                bands = new SpeedBands { stallKt = (float)FlightModel.StallSpeedKt(flight.ac), normalMaxKt = (float)flight.ac.easy.maxKt, vneKt = (float)(flight.ac.vneMs * 1.9438) };
            }
            double? target = s.easyFlight && session.easy.speedTargetKt > 0 ? session.easy.speedTargetKt : (double?)null;
            var cockpit = UiStore.view == CameraView.Cockpit;
            var airborne = t.phase == FlightPhase.Airborne;

            var c = hdgCanvas.Begin();
            Tapes.DrawHeadingTape(c, HDG_W, HDG_H, (float)t.headingDeg);
            hdgCanvas.End();
            if (!cockpit)
            {
                c = speedCanvas.Begin();
                Tapes.DrawSpeedTape(c, SPEED_W, TAPE_H, (float)t.airspeedKt, (float?)target, bands, airborne);
                speedCanvas.End();
                c = altCanvas.Begin();
                Tapes.DrawAltitudeTape(c, ALT_W, TAPE_H, (float)t.altitudeFt, (float)(t.altitudeFt - t.aglFt));
                altCanvas.End();
                c = vsiCanvas.Begin();
                Tapes.DrawVsi(c, VSI_W, TAPE_H, (float)t.verticalSpeedFpm, s.easyFlight && airborne ? (float)info.targetVsFpm : (float?)null);
                vsiCanvas.End();
                c = ballCanvas.Begin();
                Tapes.DrawAttitudeBall(c, BALL, (float)t.pitchDeg, (float)t.rollDeg);
                ballCanvas.End();
            }

            var heading = (int)Math.Round(t.headingDeg, MidpointRounding.AwayFromZero) % 360;
            hdgText.Set(heading == 0 ? 360 : heading);
            vsText.Set((int)Math.Round(t.verticalSpeedFpm / 10) * 10);
            aglText.Set((int)Math.Max(0, Math.Round(t.aglFt)));
            var targetKt = target.HasValue ? (int)Math.Round(target.Value) : NONE;
            speedSetText.Set(target.HasValue ? targetKt : s.easyFlight ? NONE : MANUAL + (int)Math.Round(t.groundSpeedKt));
            pitchText.Set(Tenths(t.pitchDeg));
            rollText.Set(Tenths(t.rollDeg));
            aoaText.Set(Tenths(t.aoaDeg));
            gText.Set((int)Math.Round(t.loadFactor * 100));

            // Throttle lever: fill = engine power actually produced, knob = commanded throttle.
            thrText.Set((int)Math.Round(t.throttle * 100));
            thrFill.style.scale = new Scale(new Vector3(1, (float)t.engine, 1));
            thrKnob.style.bottom = Length.Percent((float)t.throttle * 100);
            thrModeText.Set(s.easyFlight ? targetKt : MANUAL + (int)Math.Round(t.trim * 100));

            Ui.SetShown(easy, s.easyFlight);
            Ui.SetShown(fps, s.showFps);
            if (s.showFps) fpsText.Set((int)Math.Round(Perf.fps));
            Ui.SetShown(phase, t.phase != FlightPhase.Crashed);
            Ui.SetText(phase, t.phase == FlightPhase.Ground ? "ON GROUND" : "AIRBORNE");
            Ui.SetShown(brakes, t.brake > 0 && t.phase == FlightPhase.Ground);
            var warning = t.stalled ? "STALL" : t.overspeed ? "OVERSPEED" : t.tailStrike ? "TAIL STRIKE" : null;
            Ui.SetShown(warn, warning != null);
            if (warning != null)
            {
                Ui.SetText(warn, warning);
                warn.style.opacity = 0.775f + 0.225f * Mathf.Cos(Time.unscaledTime * 2 * Mathf.PI / WARN_PULSE_S);
            }

            var motion = info.source == ControlSource.Motion;
            // Name the real source: the simulator and the ESP32 must never look alike.
            var sourceName = hub.GetStatus().transport == TransportKind.Simulated ? "Simulated controller" : "ESP32 controller";
            var simulated = hub.GetStatus().transport == TransportKind.Simulated;
            Ui.SetText(source, motion ? sourceName : session.controls.engagement > 0 ? (simulated ? "Simulated controller · engaging" : "ESP32 controller · engaging") : session.keyboard.activeDevice ?? "Keyboard");
            sourceDot.EnableInClassList("dot--ok", motion);
            sourceDot.EnableInClassList("dot--idle", !motion);
            // What the controller is asking for right now: makes an off-centre neutral obvious.
            var aileron = session.controls.aileron;
            var pitchCmd = session.controls.elevator;
            cmdTurnText.Set(Math.Abs(aileron) < 0.02 ? 0 : Pct(aileron));
            cmdClimbText.Set(Math.Abs(pitchCmd) < 0.02 ? 0 : Pct(pitchCmd));
            Ui.SetShown(cmd, motion);

            var showMessage = Time.unscaledTimeAsDouble < info.messageUntil;
            Ui.SetShown(msg, showMessage);
            if (showMessage) Ui.SetText(msg, info.message);
            Ui.SetShown(crash, t.phase == FlightPhase.Crashed);
            Ui.SetText(crashReason, t.crashReason ?? "");
        }

        /// <summary>A signed percentage that can't round to zero, since zero means "level" on the HUD.</summary>
        static int Pct(double v)
        {
            var percent = (int)Math.Round(Math.Abs(v) * 100);
            return (v > 0 ? 1 : -1) * Math.Max(percent, 1);
        }

        public void Frame() => scope.Frame();

        public void SetVisible(bool visible) => Ui.SetShown(host, visible);
    }
}
