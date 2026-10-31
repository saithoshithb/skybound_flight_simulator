using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>How controller tilt becomes stick movement.</summary>
    public static class ResponsePanel
    {
        /// <summary>Plot of controller tilt → command for the roll axis with current settings.</summary>
        class CurvePreview : CanvasElement
        {
            const float W = 220, H = 120;
            const int POINTS = 80;

            string drawn;
            float width;

            public CurvePreview()
            {
                AddToClassList("curve");
                RegisterCallback<GeometryChangedEvent>(e =>
                {
                    width = e.newRect.width;
                    drawn = null;
                    Refresh();
                });
            }

            public void Refresh()
            {
                if (width <= 0) return;
                var c = SettingsStore.Get().control;
                var key = $"{c.roll.maxAngleDeg}|{c.deadzone}|{c.expo}|{c.sensitivity}";
                if (key == drawn) return;
                drawn = key;
                Draw(c);
            }

            void Draw(ControlSettings s)
            {
                var c = Begin();
                // Scaled to fit and centred, as an SVG viewBox does.
                c.unit = Mathf.Min(width / W, 1);
                c.Translate((width - W * c.unit) / 2, 0);
                var span = (float)s.roll.maxAngleDeg * 1.25f;
                var unit = c.unit;
                c.lineWidth = 1;
                c.StrokeStyle(Ui.LINE);
                c.BeginPath();
                c.MoveTo(0, H / 2);
                c.LineTo(W, H / 2);
                c.MoveTo(W / 2, 0);
                c.LineTo(W / 2, H);
                c.Stroke();
                float XAt(float deg) => (deg + span) / (2 * span) * W;
                foreach (var d in new[] { -(float)s.roll.maxAngleDeg, (float)s.roll.maxAngleDeg })
                {
                    c.BeginPath();
                    for (float y = 0; y < H; y += 5)
                    {
                        c.MoveTo(XAt(d), y);
                        c.LineTo(XAt(d), y + 2);
                    }
                    c.Stroke();
                }
                var axis = new AxisSettings(s.roll.maxAngleDeg);
                c.StrokeStyle(Ui.ACCENT);
                c.lineWidth = 1.75f * unit;
                c.BeginPath();
                for (var i = 0; i <= POINTS; i++)
                {
                    var deg = -span + 2 * span * i / POINTS;
                    var y = (float)ControlMapping.ShapeAxis(deg, axis, s);
                    var x = (float)i / POINTS * W;
                    var py = H / 2 - y * H / 2.2f;
                    if (i == 0) c.MoveTo(x, py);
                    else c.LineTo(x, py);
                }
                c.Stroke();
                c.Text("command", 4, 11, 9 * unit, Ui.INK_3, TextAlign.Left, Face.Body);
                c.Text($"roll tilt ±{span:0}°", W - 4, H - 8, 9 * unit, Ui.INK_3, TextAlign.Right, Face.Body);
                End();
            }
        }

        static string N(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        public static VisualElement Build(Scope scope)
        {
            ControlSettings C() => SettingsStore.Get().control;
            var curve = new CurvePreview();
            scope.On(curve.Refresh);

            VisualElement Axis(string name, System.Func<ControlSettings, AxisSettings> axis)
            {
                var title = char.ToUpperInvariant(name[0]) + name.Substring(1);
                return Ui.Div("axis").With(
                    Ui.SliderRow(scope, $"{title} tilt for full deflection", () => (float)axis(C()).maxAngleDeg, () => $"{axis(C()).maxAngleDeg:0}°", 10, 70, 1, v => SettingsStore.SetControl(c => axis(c).maxAngleDeg = v)),
                    Ui.SwitchLine(scope, $"Invert {name}", () => axis(C()).invert, v => SettingsStore.SetControl(c => axis(c).invert = v)));
            }

            var yawHint = Ui.Txt("", "hint");
            scope.Text(yawHint, () =>
            {
                switch (C().yawMode)
                {
                    case YawMode.Washout: return "Twisting gives rudder, which fades back to zero while held. This cancels gyro drift.";
                    case YawMode.Rate: return "Rudder follows how fast you twist. Holding still means no rudder, so drift can't build up.";
                    default: return "Recommended. The MPU-6050 has no magnetometer, so yaw angle drifts. The aircraft still turns by banking.";
                }
            });
            var washout = Ui.SliderRow(scope, "Washout time", () => (float)C().yawWashoutS, () => $"{N(C().yawWashoutS, "F1")} s", 0.5f, 10, 0.5f, v => SettingsStore.SetControl(c => c.yawWashoutS = v));
            scope.Show(washout, () => C().yawMode == YawMode.Washout);
            var rate = Ui.SliderRow(scope, "Twist rate for full rudder", () => (float)C().yawFullRateDps, () => $"{C().yawFullRateDps:0}°/s", 20, 300, 5, v => SettingsStore.SetControl(c => c.yawFullRateDps = v));
            scope.Show(rate, () => C().yawMode == YawMode.Rate);
            var invertYaw = Ui.SwitchLine(scope, "Invert yaw", () => C().yaw.invert, v => SettingsStore.SetControl(c => c.yaw.invert = v));
            scope.Show(invertYaw, () => C().yawMode != YawMode.Off);

            return Ui.PanelBox("Response", Ui.Btn("Defaults", SettingsStore.ResetControl, "ghost", "xs"), Ui.Div().With(
                curve,
                Ui.SliderRow(scope, "Sensitivity", () => (float)C().sensitivity, () => $"{N(C().sensitivity, "F2")}×", 0.3f, 2, 0.05f, v => SettingsStore.SetControl(c => c.sensitivity = v)),
                Ui.SliderRow(scope, "Dead zone", () => (float)C().deadzone, () => $"{System.Math.Round(C().deadzone * 100):0}%", 0, 0.3f, 0.01f, v => SettingsStore.SetControl(c => c.deadzone = v), "Range around neutral that produces no command"),
                Ui.SliderRow(scope, "Expo (center softness)", () => (float)C().expo, () => N(C().expo, "F2"), 0, 1, 0.05f, v => SettingsStore.SetControl(c => c.expo = v)),
                Ui.SliderRow(scope, "Smoothing", () => (float)C().smoothingMs, () => $"{C().smoothingMs:0} ms", 0, 250, 5, v => SettingsStore.SetControl(c => c.smoothingMs = v), "Low-pass time constant: removes tremor, adds lag"),
                Ui.SliderRow(scope, "Max rate of change", () => (float)C().slewPerSec, () => $"{N(C().slewPerSec, "F1")} /s", 1, 12, 0.5f, v => SettingsStore.SetControl(c => c.slewPerSec = v), "Full-scale deflections per second"),
                Ui.Div("panel-section").With(Ui.SectionLabel("Axes"), Axis("pitch", c => c.pitch), Axis("roll", c => c.roll)),
                Ui.Div("panel-section").With(
                    Ui.SectionLabel("Yaw / rudder"),
                    new SegmentedEl<YawMode>(scope, () => C().yawMode, v => SettingsStore.SetControl(c => c.yawMode = v),
                        (YawMode.Off, "Off"), (YawMode.Washout, "Twist (washout)"), (YawMode.Rate, "Twist rate")),
                    yawHint,
                    washout,
                    rate,
                    invertYaw)));
        }
    }
}
