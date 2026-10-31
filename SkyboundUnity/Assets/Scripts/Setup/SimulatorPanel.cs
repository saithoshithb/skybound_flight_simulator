using System;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>Drives the simulated controller and injects faults to exercise the pipeline.</summary>
    public static class SimulatorPanel
    {
        public static VisualElement Build(Scope scope, SensorHub hub)
        {
            var p = SimulatedTransport.simParams;
            var manual = Ui.Div().With(
                Ui.SliderRow(scope, "Pitch", () => (float)p.targetPitch, () => $"{p.targetPitch:0}°", -60, 60, 1, v => p.targetPitch = v),
                Ui.SliderRow(scope, "Roll", () => (float)p.targetRoll, () => $"{p.targetRoll:0}°", -80, 80, 1, v => p.targetRoll = v),
                Ui.SliderRow(scope, "Yaw twist", () => (float)p.targetYaw, () => $"{p.targetYaw:0}°", -60, 60, 1, v => p.targetYaw = v));
            scope.Show(manual, () => p.motion == SimMotion.Manual);

            var unplug = Ui.Btn("Simulate cable unplug", () => (hub.ActiveTransport() as SimulatedTransport)?.Unplug(), "danger", "sm");
            scope.On(() => unplug.SetEnabled(hub.ActiveTransport() is SimulatedTransport && hub.GetStatus().link != LinkState.Disconnected));

            return Ui.PanelBox("Simulated controller", Ui.Txt("no hardware needed", "ink-3", "tiny"), Ui.Div().With(
                new SegmentedEl<SimMotion>(scope, () => p.motion, v => p.motion = v,
                    (SimMotion.Wave, "Sweep"), (SimMotion.Manual, "Manual"), (SimMotion.Still, "Still")),
                manual,
                Ui.Div("panel-section").With(
                    Ui.SectionLabel("Faults"),
                    Ui.SliderRow(scope, "Sensor noise", () => (float)p.noiseDeg, () => $"±{p.noiseDeg:0.00}°", 0, 3, 0.05f, v => p.noiseDeg = v),
                    Ui.SliderRow(scope, "Yaw drift", () => (float)p.yawDriftDps, () => $"{p.yawDriftDps:0.0}°/s", 0, 3, 0.1f, v => p.yawDriftDps = v),
                    Ui.SliderRow(scope, "Dropped packets", () => (float)p.dropRate, () => $"{Math.Round(p.dropRate * 100):0}%", 0, 0.9f, 0.01f, v => p.dropRate = v),
                    Ui.SliderRow(scope, "Corrupted lines", () => (float)p.malformedRate, () => $"{Math.Round(p.malformedRate * 100):0}%", 0, 0.5f, 0.01f, v => p.malformedRate = v),
                    unplug)));
        }
    }
}
