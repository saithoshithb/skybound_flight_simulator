using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>Live attitude and commands, updated once a frame.</summary>
    public static class LiveReadouts
    {
        static string Fmt(double v, int digits = 1)
        {
            return (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        static VisualElement AngleReadout(Scope scope, string name, string hint, Func<ImuSample, Neutral, (double rel, double abs, double rate)> read, SensorHub hub)
        {
            var rel = Ui.Txt("—", "num", "angle__value");
            var abs = Ui.Txt("raw —", "num", "ink-3", "angle__detail");
            var rate = Ui.Txt("—°/s", "num", "ink-3", "angle__detail");
            scope.On(() =>
            {
                var latest = hub.Latest();
                if (latest == null)
                {
                    Ui.SetText(rel, "—");
                    Ui.SetText(abs, "raw —");
                    Ui.SetText(rate, "—°/s");
                    return;
                }
                var (r, a, d) = read(latest.sample, SettingsStore.Get().neutral);
                Ui.SetText(rel, Fmt(r) + "°");
                Ui.SetText(abs, "raw " + Fmt(a) + "°");
                Ui.SetText(rate, Fmt(d, 0) + "°/s");
            });
            return Ui.Div("angle").With(
                Ui.Div("row-between").With(Ui.SectionLabel(name), Ui.Txt(hint, "ink-3", "tiny")),
                rel,
                Ui.Div("row-between").With(abs, rate));
        }

        static VisualElement CommandBar(Scope scope, string name, Func<double> value, Func<double> target)
        {
            var fill = Ui.Div("command-bar__fill");
            var tick = Ui.Div("command-bar__target");
            var number = Ui.Txt("0.00", "num", "command-bar__value");
            scope.On(() =>
            {
                var v = (float)value();
                fill.style.scale = new Scale(new Vector3(v, 1, 1));
                tick.style.left = Length.Percent(50 + (float)target() * 50);
                Ui.SetText(number, Fmt(v, 2));
            });
            return Ui.Div("command-bar").With(
                Ui.Txt(name, "command-bar__name"),
                Ui.Div("command-bar__track").With(Ui.Div("command-bar__centre"), fill, tick),
                number);
        }

        public static VisualElement Build(Scope scope, App app)
        {
            var hub = app.Link.hub;
            var controls = app.Session.controls;
            var pipeline = app.Link.pipeline;

            var accel = Ui.Txt("", "num", "ink-3", "small");
            accel.tooltip = "Accelerometer magnitude (≈1 g at rest)";
            scope.On(() =>
            {
                var latest = hub.Latest();
                Ui.SetText(accel, latest != null ? latest.sample.accelG.ToString("F2", CultureInfo.InvariantCulture) + " g" : "—");
            });

            var stick = Ui.Div("stick__dot");
            var stickArea = Ui.Div("stick").With(Ui.Div("stick__h"), Ui.Div("stick__v"), stick);
            scope.On(() => stick.style.translate = new Translate(Length.Percent((float)controls.aileron * 400), Length.Percent((float)-controls.elevator * 400)));

            var engage = Ui.Txt("0%", "num", "ink-3", "command-bar__value");
            var engageBar = Ui.Div("authority__fill");
            scope.On(() =>
            {
                engageBar.style.scale = new Scale(new Vector3((float)controls.engagement, 1, 1));
                Ui.SetText(engage, Math.Round(controls.engagement * 100).ToString("0") + "%");
            });

            var wrap = Ui.Div().With(
                Ui.Div("angles").With(
                    AngleReadout(scope, "Pitch", "+ nose up", (s, n) => (s.pitch - n.pitch, s.pitch, s.pitchRate), hub),
                    AngleReadout(scope, "Roll", "+ right down", (s, n) => (ControlMapping.Wrap180(s.roll - n.roll), s.roll, s.rollRate), hub),
                    AngleReadout(scope, "Yaw", "relative, drifts", (s, n) => (ControlMapping.Wrap180(s.yaw - n.yaw), s.yaw, s.yawRate), hub)),
                Ui.Div("commands").With(
                    Ui.Div("commands__bars").With(
                        CommandBar(scope, "Elevator", () => controls.elevator, () => pipeline.targetElevator),
                        CommandBar(scope, "Aileron", () => controls.aileron, () => pipeline.targetAileron),
                        CommandBar(scope, "Rudder", () => controls.rudder, () => pipeline.targetRudder),
                        Ui.Div("command-bar").With(
                            Ui.Txt("Authority", "ink-3", "command-bar__name"),
                            Ui.Div("authority__track").With(engageBar),
                            engage)),
                    // Virtual stick: where the pilot's "yoke" is after all shaping
                    stickArea),
                Ui.Txt("Big numbers are relative to neutral. Grey bars are the shaped command sent to the aircraft; the amber tick is the unsmoothed target.", "hint", "mt-3"));
            scope.On(() => wrap.style.opacity = hub.IsUsable() ? 1 : 0.45f);
            return Ui.PanelBox("Live attitude & commands", accel, wrap);
        }
    }
}
