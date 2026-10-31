using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>Link diagnostics, read at a human-readable rate (not sensor rate).</summary>
    public static class DiagnosticsPanel
    {
        const double REFRESH_S = 0.25;
        const int RAW_LINES = 7;

        static Label Stat(VisualElement parent, string label)
        {
            var value = Ui.Txt("", "num", "diag__value");
            parent.Add(Ui.Div("diag").With(Ui.SectionLabel(label), value));
            return value;
        }

        static void Tone(Label label, string tone)
        {
            label.EnableInClassList("warn-text", tone == "warn");
            label.EnableInClassList("bad-text", tone == "bad");
        }

        static string Count(double v) => v.ToString("N0", CultureInfo.InvariantCulture);

        public static VisualElement Build(Scope scope, SensorHub hub)
        {
            var grid = Ui.Div("diag-grid");
            var rate = Stat(grid, "Rate");
            var last = Stat(grid, "Last sample");
            var samples = Stat(grid, "Samples");
            var dropped = Stat(grid, "Dropped");
            var malformed = Stat(grid, "Malformed");
            var restarts = Stat(grid, "Restarts");
            var rejected = Ui.Txt("", "ink-3", "small", "mt-3");
            var raw = Ui.Txt("Raw serial lines appear here.", "num", "diag__raw");

            var next = 0.0;
            scope.On(() =>
            {
                if (Time.unscaledTimeAsDouble < next) return;
                next = Time.unscaledTimeAsDouble + REFRESH_S;
                var stats = hub.GetStats();
                var age = stats.lastSampleAgeMs;
                var finite = !double.IsInfinity(age) && !double.IsNaN(age);
                Ui.SetText(rate, $"{stats.rxHz} Hz");
                Tone(rate, stats.samples > 0 && stats.rxHz < 30 ? "warn" : null);
                Ui.SetText(last, finite ? $"{Math.Round(age)} ms" : "—");
                Tone(last, finite && age > 2000 ? "bad" : finite && age > 250 ? "warn" : null);
                Ui.SetText(samples, Count(stats.samples));
                Ui.SetText(dropped, Count(stats.droppedSeq));
                Tone(dropped, stats.droppedSeq > 0 ? "warn" : null);
                Ui.SetText(malformed, Count(stats.malformed));
                Tone(malformed, stats.malformed > 0 ? "warn" : null);
                Ui.SetText(restarts, stats.restarts.ToString());
                Tone(restarts, stats.restarts > 0 ? "warn" : null);
                Ui.SetShown(rejected, !string.IsNullOrEmpty(stats.lastMalformedReason));
                Ui.SetText(rejected, "Last rejected line: " + stats.lastMalformedReason);
                var lines = hub.RecentLines();
                Ui.SetText(raw, lines.Length > 0 ? string.Join("\n", lines.Skip(Math.Max(0, lines.Length - RAW_LINES))) : "Raw serial lines appear here.");
            });
            return Ui.PanelBox("Link diagnostics", null, Ui.Div().With(grid, rejected, raw));
        }
    }
}
