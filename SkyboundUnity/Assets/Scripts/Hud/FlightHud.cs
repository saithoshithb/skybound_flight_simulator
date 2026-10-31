using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// Flight HUD, glass-cockpit style, arranged around the screen edges so the aircraft stays clear:
    /// heading ribbon on top, airspeed tape left, altitude tape and vertical speed right, minimap
    /// bottom-left, attitude bottom-centre, throttle and systems bottom-right. Everything is drawn
    /// once a frame from what IFlightDataProvider reports. Tab cycles Full, Minimal and Off; buttons
    /// and status fade out when the mouse and keyboard have been left alone.
    /// </summary>
    public class FlightHud
    {
        const float TAPE_H = 245;
        const float SPEED_W = 84;
        const float VSI_W = 16;
        const float ALT_W = 96;
        const float HDG_W = 435;
        const float HDG_H = 46;
        const float BALL = 128;
        const float WARN_PULSE_S = 0.9f;
        const float IDLE_AFTER_S = 6;
        /// <summary>Stands for "nothing to show" in a number that's otherwise never negative.</summary>
        const int NONE = -1;
        /// <summary>Keeps the manual-flying numbers apart from the Easy flight ones in one label.</summary>
        const int MANUAL = 100000;

        static string Signed(double n, int digits = 0)
        {
            return (n >= 0 ? "+" : "−") + Math.Abs(n).ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        static string Thousands(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
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
        readonly HudWarnings warnings = new HudWarnings();
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

        readonly TabularLabel hdg = new TabularLabel(17, "hud-hdg");
        readonly TabularLabel vs = new TabularLabel(13, "hud-vs");
        readonly TabularLabel agl = new TabularLabel(12, "hud-agl");
        readonly TabularLabel pitch = new TabularLabel(13, "dd");
        readonly TabularLabel roll = new TabularLabel(13, "dd");
        readonly TabularLabel aoa = new TabularLabel(13, "dd");
        readonly TabularLabel g = new TabularLabel(13, "dd");
        readonly TabularLabel thr = new TabularLabel(30, "throttle__value");
        readonly TabularLabel fuel = new TabularLabel(12);
        readonly TabularLabel flaps = new TabularLabel(12);
        readonly Label speedSet, thrMode, easy, fps, phase, brakes, warn, msg, source, cmdTurn, cmdClimb, crashReason, gearText;
        readonly VisualElement sourceDot, cmd, thrFill, thrKnob, crash, heading, tapesLeft, tapesRight, attitude, instruments, keyCard;
        readonly VisualElement topLeft, toolbar, minimapBox, systems, fuelRow, flapsRow, fuelFill, flapsFill;
        readonly VisualElement[] gearDots = { Ui.Dot("ok"), Ui.Dot("ok"), Ui.Dot("ok") };
        readonly Minimap minimap = new Minimap();
        readonly IconButton recenter, viewButton, instrumentsButton, helpButton;
        readonly Label keyCardTitle;
        readonly VisualElement keyRows;
        readonly Label keyCardNote;
        bool keyCardEasy;
        bool keyCardBuilt;

        static readonly string[] TURN_WORDS = { "L", "R" };
        static readonly string[] CLIMB_WORDS = { "down", "up" };

        readonly IntLabel hdgText, vsText, aglText, speedSetText, pitchText, rollText, aoaText, gText, thrText, thrModeText, fpsText, cmdTurnText, cmdClimbText, fuelText, flapsText;
        bool idle;
        float lastActivity;

        /// <summary>The whole HUD, for tests.</summary>
        public VisualElement Root => host;
        public Minimap Map => minimap;
        public HudWarning Warning => warnings.Current;
        /// <summary>Where every number on the HUD comes from. Tests swap it for made-up flights.</summary>
        public IFlightDataProvider Data { get; set; }

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

        public FlightHud(VisualElement parent, App application, FlightScreen flightScreen, IFlightDataProvider flightData)
        {
            app = application;
            screen = flightScreen;
            session = app.Session;
            hub = app.Link.hub;
            Data = flightData;

            host = Ui.Div("fill", "hud-text", "hud-theme");
            parent.Add(host);
            host.RegisterCallback<CustomStyleResolvedEvent>(_ => ApplyTheme());

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
            topLeft = Ui.Div("hud-top-left", "hud-chrome").With(
                Ui.Div("glass", "rounded-lg", "hud-source-pill").With(sourceDot, source),
                cmd,
                Ui.Div("chips").With(easy, phase, brakes, fps));
            host.Add(topLeft.Appear());

            // ---------- top-right: toolbar ----------
            recenter = new IconButton(Icon.Recenter, "Recenter controller", "R", () => app.Link.Recenter());
            viewButton = new IconButton(Icon.Camera, "Cockpit view", "V", UiStore.ToggleView);
            instrumentsButton = new IconButton(Icon.Gauge, "Instruments", "I", UiStore.ToggleInstruments);
            helpButton = new IconButton(Icon.Help, "Controls", "H", UiStore.ToggleHelp);
            toolbar = Ui.Div("hud-toolbar", "hud-chrome").With(
                recenter,
                viewButton,
                instrumentsButton,
                helpButton,
                new IconButton(Icon.Settings, "Settings", null, () => UiStore.SetShowSettings(true)),
                new IconButton(Icon.Pause, "Pause", "Esc", () => UiStore.SetPaused(true)));
            host.Add(toolbar.Appear());

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
            warn = Ui.Txt("", "display", "hud-warn");
            msg = Ui.Txt("", "glass", "rounded-lg", "hud-msg");
            heading = Ui.Div("hud-heading", "hud-instrument").With(Layered(hdgBacking, hdgCanvas), hdg);
            host.Add(Ui.Div("hud-top-centre").With(heading, warn, msg));
            warnings.Changed += ShowWarning;

            // ---------- left edge: airspeed tape ----------
            speedSet = Ui.Txt("", "num", "hud-speed-set");
            tapesLeft = Ui.Div("hud-tape-left", "hud-instrument").With(Ui.Txt("IAS · KT", "label", "tape-title"), Layered(speedBacking, speedCanvas), speedSet);
            host.Add(tapesLeft);

            // ---------- right edge: altitude tape + vertical speed ----------
            tapesRight = Ui.Div("hud-tape-right", "hud-instrument").With(
                Ui.Txt("ALT · FT", "label", "tape-title"),
                Ui.Div("tape-pair").With(vsiCanvas, Layered(altBacking, altCanvas)),
                Ui.Div("hud-vs-row").With(Ui.Txt("VS", "num", "ink-3"), vs),
                Ui.Div("hud-agl-row").With(agl, Ui.Txt("AGL", "num")));
            host.Add(tapesRight);

            // ---------- bottom-left: minimap ----------
            minimapBox = Ui.Div("hud-minimap", "hud-instrument").With(minimap);
            host.Add(minimapBox.Appear());

            // ---------- bottom-centre: attitude ----------
            attitude = Ui.Div("hud-attitude", "hud-instrument").With(
                Ui.Div("glass", "rounded-lg", "hud-attitude__table").With(
                    Ui.Div("row-between").With(Ui.Txt("PITCH", "num", "ink-2"), pitch),
                    Ui.Div("row-between").With(Ui.Txt("BANK", "num", "ink-2"), roll)),
                ballCanvas,
                Ui.Div("glass", "rounded-lg", "hud-attitude__table").With(
                    Ui.Div("row-between").With(Ui.Txt("AOA", "num", "ink-2"), aoa),
                    Ui.Div("row-between").With(Ui.Txt("G", "num", "ink-2"), g)));
            host.Add(attitude.Appear());

            // ---------- bottom-centre: optional instrument panel, in the attitude ball's place ----------
            // The cockpit's instrument texture shown on screen in the chase view: the same texture
            // the 3D panel uses, so the two can never disagree.
            var overlay = Ui.Div("hud-instrument-image");
            overlay.style.backgroundImage = Background.FromRenderTexture(InstrumentPanel.Get().Texture);
            instruments = Ui.Div("hud-instruments").With(Ui.Div("glass", "rounded-xl", "hud-instruments__frame").With(overlay));
            host.Add(instruments);

            // ---------- bottom-right: throttle ----------
            thrFill = Ui.Div("throttle__fill");
            thrKnob = Ui.Div("throttle__knob");
            thrMode = Ui.Txt("", "num", "throttle__mode");
            fuelFill = Ui.Div("sys-bar__fill");
            flapsFill = Ui.Div("sys-bar__fill");
            gearText = Ui.Txt("", "num", "ink-2");
            fuelRow = SystemRow("FUEL", fuel, fuelFill);
            flapsRow = SystemRow("FLAPS", flaps, flapsFill);
            systems = Ui.Div("hud-systems").With(
                fuelRow,
                flapsRow,
                Ui.Div("sys-row").With(Ui.Div("row-between").With(
                    Ui.Txt("GEAR", "label", "sys-title"),
                    Ui.Div("sys-gear").With(gearText, gearDots[0], gearDots[1], gearDots[2]))));
            var kb = session.keyboard;
            host.Add(Ui.Div("glass", "rounded-xl", "hud-throttle", "hud-instrument").With(
                Ui.Div("throttle__keys").With(
                    HoldKey(new IconEl(Icon.Plus, 16, Ui.INK_2), "Faster / more throttle (hold)", on => kb.holdFaster = on),
                    HoldKey(new IconEl(Icon.Minus, 16, Ui.INK_2), "Slower / less throttle (hold)", on => kb.holdSlower = on),
                    HoldKey(Ui.Txt("BRK", "display", "throttle__brk"), "Brakes (hold)", on => kb.holdBrake = on)),
                Ui.Div("throttle__track").With(thrFill, thrKnob),
                Ui.Div("throttle__text").With(
                    Ui.Txt("THROTTLE", "label", "tape-title"),
                    Ui.Div("throttle__number").With(thr, Ui.Txt("%", "num", "ink-3", "throttle__percent")),
                    thrMode),
                systems).Appear());

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
            aglText = new IntLabel(agl, Thousands);
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
            fuelText = new IntLabel(fuel, v => v + "%");
            flapsText = new IntLabel(flaps, v => v + "%");

            IgnorePointerExceptOnControls(host);
            lastActivity = Time.unscaledTime;
            UiStore.PausedChanged += OnPausedChanged;

            // Coarse state: what the browser's React re-render decides.
            scope.Show(recenter, () => session.pilotInfo.source == ControlSource.Motion);
            scope.On(() =>
            {
                var cockpit = UiStore.view == CameraView.Cockpit;
                var mode = SettingsStore.Get().hudMode;
                var full = mode == HudMode.Full;
                var on = mode != HudMode.Off;
                viewButton.Active = cockpit;
                viewButton.tooltip = cockpit ? "Chase view (V)" : "Cockpit view (V)";
                instrumentsButton.Active = UiStore.showInstruments;
                helpButton.Active = UiStore.showHelp;
                Ui.SetShown(keyCard, UiStore.showHelp && on);
                Ui.SetShown(topLeft, full);
                Ui.SetShown(toolbar, on);
                Ui.SetShown(heading, on);
                Ui.SetShown(tapesLeft, !cockpit && on);
                Ui.SetShown(tapesRight, !cockpit && on);
                Ui.SetShown(minimapBox, full);
                Ui.SetShown(systems.parent, full);
                Ui.SetShown(attitude, !cockpit && full && !UiStore.showInstruments);
                Ui.SetShown(instruments, UiStore.showInstruments && !cockpit && full);
                Ui.SetShown(warn, warnings.Current != HudWarning.None && on);
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

        /// <summary>A named bar with its percentage, for fuel and flaps.</summary>
        static VisualElement SystemRow(string title, TabularLabel value, VisualElement fill)
        {
            return Ui.Div("sys-row").With(
                Ui.Div("row-between").With(Ui.Txt(title, "label", "sys-title"), Ui.Div("sys-value").With(value)),
                Ui.Div("sys-bar").With(fill));
        }

        /// <summary>Only what can be clicked takes part in picking, and the crash card blocks what's behind it.</summary>
        void IgnorePointerExceptOnControls(VisualElement el)
        {
            if (el == crash || el is IconButton || el.ClassListContains("hold-key")) return;
            el.pickingMode = PickingMode.Ignore;
            foreach (var child in el.Children()) IgnorePointerExceptOnControls(child);
        }

        void ApplyTheme()
        {
            if (!HudTheme.Apply(host.customStyle)) Debug.LogWarning("[Skybound] A HUD colour is missing from the .hud-theme rule in Skybound.uss");
            DrawBackings();
            minimap.DrawFixedParts();
        }

        /// <summary>These never change while flying, so they're drawn when the theme arrives and left alone.</summary>
        void DrawBackings()
        {
            Tapes.DrawHeadingBacking(hdgBacking.Begin(), HDG_W, HDG_H);
            hdgBacking.End();
            Tapes.TapeBacking(speedBacking.Begin(), 0, SPEED_W - 10, TAPE_H);
            speedBacking.End();
            Tapes.TapeBacking(altBacking.Begin(), 0, ALT_W, TAPE_H);
            altBacking.End();
        }

        void ShowWarning(HudWarning warning)
        {
            lastActivity = Time.unscaledTime;
            Ui.SetText(warn, HudWarnings.Text(warning));
            warn.EnableInClassList("hud-warn--caution", HudWarnings.IsCaution(warning));
        }

        void OnPausedChanged(bool paused) => lastActivity = Time.unscaledTime;

        /// <summary>Tilting the controller is flying, not using the screen, so only the mouse and keyboard wake the buttons.</summary>
        void UpdateIdle()
        {
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            var active = UiStore.paused || warnings.Current != HudWarning.None
                || mouse != null && (mouse.delta.ReadValue() != Vector2.zero || mouse.leftButton.isPressed)
                || keys != null && keys.anyKey.isPressed;
            if (active) lastActivity = Time.unscaledTime;
            var nowIdle = Time.unscaledTime - lastActivity > IDLE_AFTER_S;
            if (nowIdle == idle) return;
            idle = nowIdle;
            host.EnableInClassList("hud--idle", idle);
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
                    ("Tab", "HUD full · minimal · off"),
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
                    ("Tab", "HUD full · minimal · off"),
                    ("R", "Recenter controller"),
                    ("Esc · P", "Pause"),
                    ("Backspace", "Restart on runway"),
                };
            foreach (var (k, v) in rows)
                keyRows.Add(Ui.Div("row-between", "key-card__row").With(Ui.Txt(k, "key-card__key"), Ui.Txt(v, "key-card__action")));
        }

        void Loop()
        {
            var d = Data;
            var s = SettingsStore.Get();
            var info = session.pilotInfo;

            warnings.Update(d);
            UpdateIdle();

            float? target = s.easyFlight && d.TargetSpeedKt > 0 ? d.TargetSpeedKt : (float?)null;
            var cockpit = UiStore.view == CameraView.Cockpit;
            var airborne = d.Phase == FlightPhase.Airborne;
            var full = s.hudMode == HudMode.Full;
            var hudOn = s.hudMode != HudMode.Off;

            if (hudOn)
            {
                var c = hdgCanvas.Begin();
                Tapes.DrawHeadingTape(c, HDG_W, HDG_H, d.HeadingDeg);
                hdgCanvas.End();
            }
            if (hudOn && !cockpit)
            {
                var c = speedCanvas.Begin();
                Tapes.DrawSpeedTape(c, SPEED_W, TAPE_H, d.AirspeedKt, target, d.Bands, airborne);
                speedCanvas.End();
                c = altCanvas.Begin();
                Tapes.DrawAltitudeTape(c, ALT_W, TAPE_H, d.AltitudeFt, d.AltitudeFt - d.AglFt);
                altCanvas.End();
                c = vsiCanvas.Begin();
                Tapes.DrawVsi(c, VSI_W, TAPE_H, d.VerticalSpeedFpm, s.easyFlight && airborne ? d.TargetVsFpm : (float?)null);
                vsiCanvas.End();
            }
            if (full && !cockpit && !UiStore.showInstruments)
            {
                var c = ballCanvas.Begin();
                Tapes.DrawAttitudeBall(c, BALL, d.PitchDeg, d.RollDeg);
                ballCanvas.End();
            }
            if (full) minimap.Frame(d.NorthM, d.EastM, d.HeadingDeg);

            var headingDeg = (int)Math.Round((double)d.HeadingDeg, MidpointRounding.AwayFromZero) % 360;
            hdgText.Set(headingDeg == 0 ? 360 : headingDeg);
            vsText.Set((int)Math.Round(d.VerticalSpeedFpm / 10) * 10);
            aglText.Set((int)Math.Max(0, Math.Round((double)d.AglFt)));
            var targetKt = target.HasValue ? (int)Math.Round((double)target.Value) : NONE;
            speedSetText.Set(target.HasValue ? targetKt : s.easyFlight ? NONE : MANUAL + (int)Math.Round((double)d.GroundSpeedKt));
            pitchText.Set(Tenths(d.PitchDeg));
            rollText.Set(Tenths(d.RollDeg));
            aoaText.Set(Tenths(d.AoaDeg));
            gText.Set((int)Math.Round(d.LoadFactor * 100.0));

            // Throttle lever: fill = engine power actually produced, knob = commanded throttle.
            thrText.Set((int)Math.Round(d.Throttle * 100.0));
            thrFill.style.scale = new Scale(new Vector3(1, d.EnginePower, 1));
            thrKnob.style.bottom = Length.Percent(d.Throttle * 100);
            thrModeText.Set(s.easyFlight ? targetKt : MANUAL + (int)Math.Round(d.Trim * 100.0));

            Ui.SetShown(fuelRow, d.HasFuel);
            if (d.HasFuel)
            {
                fuelText.Set(Mathf.CeilToInt(d.Fuel * 100));
                fuelFill.style.scale = new Scale(new Vector3(d.Fuel, 1, 1));
                fuelFill.EnableInClassList("sys-bar__fill--low", d.Fuel <= HudWarnings.LOW_FUEL);
            }
            Ui.SetShown(flapsRow, d.HasFlaps);
            if (d.HasFlaps)
            {
                flapsText.Set(Mathf.RoundToInt(d.Flaps * 100));
                flapsFill.style.scale = new Scale(new Vector3(d.Flaps, 1, 1));
            }
            Ui.SetText(gearText, d.HasRetractableGear ? (d.GearDown ? "DOWN" : "UP") : "FIXED");
            foreach (var dot in gearDots)
            {
                dot.EnableInClassList("dot--ok", d.GearDown);
                dot.EnableInClassList("dot--idle", !d.GearDown);
            }

            Ui.SetShown(easy, s.easyFlight);
            Ui.SetShown(fps, s.showFps);
            if (s.showFps) fpsText.Set((int)Math.Round(Perf.fps));
            Ui.SetShown(phase, d.Phase != FlightPhase.Crashed);
            Ui.SetText(phase, d.Phase == FlightPhase.Ground ? "ON GROUND" : "AIRBORNE");
            Ui.SetShown(brakes, d.Brake > 0 && d.Phase == FlightPhase.Ground);
            if (warnings.Current != HudWarning.None) warn.style.opacity = 0.775f + 0.225f * Mathf.Cos(Time.unscaledTime * 2 * Mathf.PI / WARN_PULSE_S);

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
            Ui.SetShown(crash, d.Phase == FlightPhase.Crashed);
            Ui.SetText(crashReason, d.CrashReason ?? "");
        }

        /// <summary>A signed percentage that can't round to zero, since zero means "level" on the HUD.</summary>
        static int Pct(double v)
        {
            var percent = (int)Math.Round(Math.Abs(v) * 100);
            return (v > 0 ? 1 : -1) * Math.Max(percent, 1);
        }

        public void Frame() => scope.Frame();

        public void SetVisible(bool visible) => Ui.SetShown(host, visible);

        /// <summary>Heavy one-time work, done while the loading screen is up.</summary>
        public void Prepare() => minimap.Prepare();

        public void Dispose()
        {
            UiStore.PausedChanged -= OnPausedChanged;
        }
    }
}
