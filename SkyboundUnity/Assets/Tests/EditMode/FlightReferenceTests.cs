using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

/// <summary>
/// Flies the same scenarios as the TypeScript flight model and compares the two,
/// value by value, against Data/reference-flights.json (recorded from the browser
/// game's code). This is the proof that the port flies the same.
/// </summary>
public class FlightReferenceTests
{
    const double Dt = 1.0 / 60;
    const int SampleEvery = 30;
    /// <summary>Allowed difference, relative to the value's size (with a floor of 1).</summary>
    const double Tolerance = 1e-6;

    static readonly string[] Names =
    {
        "time", "pos.n", "pos.e", "pos.d", "vel.n", "vel.e", "vel.d", "q.w", "q.x", "q.y", "q.z", "p", "q", "r",
        "engine", "surf.elevator", "surf.aileron", "surf.rudder",
        "airspeedKt", "altitudeFt", "aglFt", "headingDeg", "verticalSpeedFpm", "pitchDeg", "rollDeg",
        "aoaDeg", "sideslipDeg", "loadFactor", "lateralG", "turnRateDps",
        "wheelsOnGround", "stalled", "tailStrike", "phase",
    };

    [Serializable] internal class SampleData { public double[] v; }
    [Serializable] internal class FlightData { public string name; public string crashReason; public SampleData[] samples; }
    [Serializable] class FileData { public FlightData[] flights; }

    static FileData file;

    static FlightData Load(string name)
    {
        if (file == null)
        {
            var path = Path.Combine(Application.dataPath, "Tests/EditMode/Data/reference-flights.json");
            file = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
        }
        return file.flights.Single(f => f.name == name);
    }

    static double[] Sample(FlightModel m)
    {
        var s = m.state;
        var t = m.telemetry;
        return new[]
        {
            t.timeS,
            s.pos.x, s.pos.y, s.pos.z, s.vel.x, s.vel.y, s.vel.z, s.q.w, s.q.x, s.q.y, s.q.z, s.w.x, s.w.y, s.w.z,
            s.engine, s.surfaces.elevator, s.surfaces.aileron, s.surfaces.rudder,
            t.airspeedKt, t.altitudeFt, t.aglFt, t.headingDeg, t.verticalSpeedFpm, t.pitchDeg, t.rollDeg,
            t.aoaDeg, t.sideslipDeg, t.loadFactor, t.lateralG, t.turnRateDps,
            t.wheelsOnGround, t.stalled ? 1 : 0, t.tailStrike ? 1 : 0, (double)t.phase,
        };
    }

    /// <summary>Same order as the generator: `before` runs ahead of each frame, `each` after it.</summary>
    static void Compare(string name, FlightModel m, PilotInput input, int frames, Action<int> each = null, Action<int> before = null)
    {
        Compare(Load(name), m, input, frames, each, before);
    }

    internal static void Compare(FlightData reference, FlightModel m, PilotInput input, int frames, Action<int> each = null, Action<int> before = null)
    {
        var name = reference.name;
        var index = 0;
        double worst = 0;
        string worstAt = "";

        void Check()
        {
            var actual = Sample(m);
            var expected = reference.samples[index].v;
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < actual.Length; i++)
            {
                var error = Math.Abs(actual[i] - expected[i]) / Math.Max(1, Math.Abs(expected[i]));
                if (error > worst)
                {
                    worst = error;
                    worstAt = $"{Names[i]} at t={expected[0]:0.0}s (expected {expected[i]}, got {actual[i]})";
                }
            }
            index++;
        }

        Check();
        for (var f = 0; f < frames; f++)
        {
            before?.Invoke(f);
            m.Advance(input, Dt);
            each?.Invoke(f);
            if ((f + 1) % SampleEvery == 0) Check();
        }
        Assert.That(index, Is.EqualTo(reference.samples.Length));
        Assert.That(m.crashReason ?? "", Is.EqualTo(reference.crashReason));
        Debug.Log($"[Reference] {name}: {index} samples × {Names.Length} values, worst difference {worst:E2}{(worstAt == "" ? "" : ": " + worstAt)}");
        Assert.That(worst, Is.LessThan(Tolerance), worstAt);
    }

    static void ManualTakeoff(string name, AircraftDef ac, double rotateKt, double pull)
    {
        var m = new FlightModel(ac);
        var input = new PilotInput { throttle = 1 };
        Compare(name, m, input, 40 * 60, each: _ =>
        {
            var t = m.telemetry;
            input.elevator = t.airspeedKt > rotateKt ? (t.pitchDeg > 8 ? 0.05 : pull) : 0;
        });
    }

    static void FlyEasy(FlightModel m, EasyFlight easy, EasyCommand cmd, PilotInput input)
    {
        var o = easy.Update(m.telemetry, cmd, Dt, m.alphaWarn / DEG);
        input.elevator = o.elevator;
        input.aileron = o.aileron;
        input.rudder = o.rudder;
        input.throttle = o.throttle;
        input.trim = 0;
    }

    static void EasyFlightScenario(string name, AircraftDef ac)
    {
        var m = new FlightModel(ac);
        var easy = new EasyFlight();
        easy.Configure(ac);
        easy.Reset(false);
        easy.AdjustSpeed(1, Dt, true);
        var input = new PilotInput();
        var cmd = new EasyCommand();
        Compare(name, m, input, 90 * 60, before: f =>
        {
            if (f == 60 * 60) cmd.roll = -1;
            if (f == 80 * 60)
            {
                cmd.roll = 0.3;
                cmd.pitch = 0.5;
            }
            FlyEasy(m, easy, cmd, input);
        });
    }

    [Test] public void ManualTakeoffTrainer() => ManualTakeoff("manual-takeoff-trainer", AircraftCatalog.Trainer, 55, 0.35);
    [Test] public void ManualTakeoffSport() => ManualTakeoff("manual-takeoff-sport", AircraftCatalog.Sport, 62, 0.3);
    [Test] public void EasyFlightTrainer() => EasyFlightScenario("easy-flight-trainer", AircraftCatalog.Trainer);
    [Test] public void EasyFlightSport() => EasyFlightScenario("easy-flight-sport", AircraftCatalog.Sport);

    [Test]
    public void ScriptedManeuvers()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        var (trim, throttle) = m.ResetAirborne(4000, 600, 50);
        var input = new PilotInput { trim = trim, throttle = throttle };
        Compare("scripted-maneuvers-trainer", m, input, 30 * 60, before: f =>
        {
            input.aileron = 0.4 * Math.Sin(f / 90.0);
            input.rudder = 0.2 * Math.Sin(f / 130.0);
            input.elevator = 0.15 * Math.Sin(f / 70.0);
        });
    }

    [Test]
    public void StallAndRecovery()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        var (trim, _) = m.ResetAirborne(4000, 1200, 45);
        var input = new PilotInput { trim = trim, throttle = 0, elevator = 1 };
        Compare("stall-recovery-trainer", m, input, 18 * 60, before: f =>
        {
            if (f == 12 * 60)
            {
                input.throttle = 1;
                input.elevator = -0.2;
            }
        });
    }

    [Test]
    public void TaxiAndBrake()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        var input = new PilotInput { throttle = 0.5, rudder = 0.5 };
        Compare("taxi-brake-trainer", m, input, 14 * 60, before: f =>
        {
            if (f == 8 * 60)
            {
                input.throttle = 0;
                input.rudder = 0;
                input.brake = 1;
            }
        });
    }

    [Test]
    public void DiveAndCrash()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        var (trim, _) = m.ResetAirborne(500, 40, 50);
        Compare("dive-crash-trainer", m, new PilotInput { trim = trim, throttle = 1, elevator = -1 }, 10 * 60);
    }

    [Test]
    public void EasyArrival()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        var easy = new EasyFlight();
        m.ResetAirborne(1500, 60, 40);
        easy.Reset(true, 0, 0.3);
        easy.speedTargetKt = 65;
        var input = new PilotInput();
        var cmd = new EasyCommand { pitch = -1 };
        Compare("easy-arrival-trainer", m, input, 60 * 60, before: _ => FlyEasy(m, easy, cmd, input));
    }
}
