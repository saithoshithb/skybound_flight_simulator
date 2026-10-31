using System;
using System.Collections.Generic;
using System.Globalization;

namespace Skybound
{
    /// <summary>
    /// Flight data recorder for diagnosing control problems on real hardware. Keeps the last 60 s at
    /// 5 Hz: what the sensor reported, what the control pipeline commanded, what Easy flight targeted,
    /// and what the aircraft did. Exported as compact CSV the player can paste into a bug report.
    /// </summary>
    public static class Recorder
    {
        const int RATE_HZ = 5;
        const int KEEP = 60 * RATE_HZ;
        const string COLUMNS = "t,link,ageMs,rxHz,sPitch,sRoll,nPitch,nRoll,cPitch,cRoll,engage,kRoll,tBank,bank,hdg,vs,ias,ail,rud,phase";

        static readonly List<string> rows = new List<string>();
        static double clock;
        static double elapsed;

        static string F1(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.0", CultureInfo.InvariantCulture);
        static string F2(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
        static string Whole(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

        public static void RecordFrame(double dt, FlightSession session, SensorHub hub)
        {
            elapsed += dt;
            clock += dt;
            if (clock < 1.0 / RATE_HZ) return;
            clock = 0;
            var latest = hub.Latest();
            var stats = hub.GetStats();
            var neutral = SettingsStore.Get().neutral;
            var t = session.flight.telemetry;
            var controls = session.controls;
            rows.Add(string.Join(",",
                F1(elapsed),
                hub.GetStatus().link.ToString().ToLowerInvariant(),
                Whole(stats.lastSampleAgeMs),
                stats.rxHz.ToString(CultureInfo.InvariantCulture),
                F1(latest != null ? latest.sample.pitch : double.NaN),
                F1(latest != null ? latest.sample.roll : double.NaN),
                F1(neutral.pitch),
                F1(neutral.roll),
                F2(controls.elevator),
                F2(controls.aileron),
                F2(controls.engagement),
                F2(session.keyboard.aileron),
                F1(session.pilotInfo.targetBankDeg),
                F1(t.rollDeg),
                Whole(t.headingDeg),
                Whole(t.verticalSpeedFpm),
                Whole(t.airspeedKt),
                F2(session.pilot.aileron),
                F2(session.pilot.rudder),
                t.phase.ToString().ToLowerInvariant()));
            if (rows.Count > KEEP) rows.RemoveAt(0);
        }

        public static string FlightLogCsv()
        {
            var s = SettingsStore.Get();
            var c = s.control;
            string N(double v) => v.ToString(CultureInfo.InvariantCulture);
            var header = $"# skybound flight log · {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}Z · easy={(s.easyFlight ? "true" : "false")} · deadzone={N(c.deadzone)} expo={N(c.expo)} smooth={N(c.smoothingMs)}ms rollMax={N(c.roll.maxAngleDeg)} invertRoll={(c.roll.invert ? "true" : "false")}";
            return header + "\n" + COLUMNS + (rows.Count > 0 ? "\n" + string.Join("\n", rows) : "");
        }

        public static void ClearFlightLog()
        {
            rows.Clear();
            elapsed = 0;
        }
    }
}
