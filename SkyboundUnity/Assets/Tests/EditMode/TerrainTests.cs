using System;
using System.IO;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

// Ported test by test from src/world/terrain.test.ts.
public class TerrainTests
{
    static (double n, double e, double h) FindHill()
    {
        (double n, double e, double h) spot = (0, 0, 0);
        for (var n = 4000; n <= 14000; n += 250)
        {
            for (var e = -14000; e <= 14000; e += 250)
            {
                var h = TerrainHeight.At(n, e);
                if (h > spot.h) spot = (n, e, h);
            }
        }
        return spot;
    }

    [Test]
    public void IsFlatPastTheEdgeOfTheDrawnTerrain()
    {
        // Beyond 16 km the world draws a flat plain, so there must be nothing there to hit.
        foreach (var (north, east) in new[] { (0.0, 16000.0), (0.0, 17000.0), (-17000.0, 3000.0), (20000.0, -20000.0) })
            Assert.That(TerrainHeight.At(north, east), Is.EqualTo(0), $"{north}, {east}");
        Assert.That(TerrainHeight.At(0, 12000), Is.GreaterThan(0), "hills well inside the tile are untouched");
    }

    [Test]
    public void IsFlatAroundTheAirfieldAndAlongTheFinalApproach()
    {
        for (var n = -200; n <= 1400; n += 100)
        {
            for (var e = -1500; e <= 1500; e += 250) Assert.That(TerrainHeight.At(n, e), Is.EqualTo(0));
        }
        // Long final starts 4 km south at 300 m: terrain there stays far below.
        for (var n = -6000; n <= 0; n += 250) Assert.That(TerrainHeight.At(n, 0), Is.LessThan(60));
    }

    [Test]
    public void HasRealHillsFurtherOutAndIsDeterministicAndSmooth()
    {
        double max = 0;
        for (var n = -15000; n <= 15000; n += 500)
        {
            for (var e = -15000; e <= 15000; e += 500) max = Math.Max(max, TerrainHeight.At(n, e));
        }
        Assert.That(max, Is.GreaterThan(150));
        Assert.That(TerrainHeight.At(7000, 6000), Is.EqualTo(TerrainHeight.At(7000, 6000)));
        // No cliffs: 10 m apart never differs by more than a few metres.
        for (var n = 3000; n < 12000; n += 777)
            Assert.That(Math.Abs(TerrainHeight.At(n, 5000) - TerrainHeight.At(n + 10, 5000)), Is.LessThan(4));
    }

    [Test]
    public void TheFlightModelCollidesWithHillsAndAglAccountsForThem()
    {
        var spot = FindHill();
        var m = new FlightModel(AircraftCatalog.Trainer, TerrainHeight.At);
        m.ResetAirborne();
        m.state.pos = new V3(spot.n - 300, spot.e, -(spot.h - 30)); // 30 m below the summit, 300 m short
        m.state.vel = new V3(50, 0, 0);
        m.state.q = FromEuler(0, 0, 0);
        var startAgl = m.telemetry.aglFt;
        var input = new PilotInput { throttle = 0.6 };
        for (var i = 0; i < 60 * 12 && m.phase != FlightPhase.Crashed; i++) m.Advance(input, 1.0 / 60);
        Assert.That(m.phase, Is.EqualTo(FlightPhase.Crashed));
        Assert.That(startAgl, Is.LessThan(m.telemetry.altitudeFt)); // AGL below MSL over raised ground
    }

    // --- Compared with data recorded from the TypeScript code ---

    [Serializable] class HeightData { public double[] v; }
    [Serializable] class FileData { public HeightData[] heights; public FlightReferenceTests.FlightData[] flights; }

    static FileData file;

    static FileData Reference()
    {
        if (file == null)
        {
            var path = Path.Combine(Application.dataPath, "Tests/EditMode/Data/reference-terrain.json");
            file = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
        }
        return file;
    }

    static FlightReferenceTests.FlightData Flight(string name) => Array.Find(Reference().flights, f => f.name == name);

    [Test]
    public void MatchesTheTypeScriptHeightsEverywhere()
    {
        var heights = Reference().heights;
        Assert.That(heights.Length, Is.GreaterThan(1600));
        double worst = 0;
        foreach (var point in heights) worst = Math.Max(worst, Math.Abs(TerrainHeight.At(point.v[0], point.v[1]) - point.v[2]));
        Debug.Log($"[Reference] terrain: {heights.Length} heights, worst difference {worst:E2} m");
        Assert.That(worst, Is.LessThan(1e-9));
    }

    [Test]
    public void FlightIntoAHillMatchesTheBrowserGame()
    {
        var spot = FindHill();
        var m = new FlightModel(AircraftCatalog.Trainer, TerrainHeight.At);
        m.ResetAirborne();
        m.state.pos = new V3(spot.n - 300, spot.e, -(spot.h - 30));
        m.state.vel = new V3(50, 0, 0);
        m.state.q = FromEuler(0, 0, 0);
        FlightReferenceTests.Compare(Flight("hill-crash-trainer"), m, new PilotInput { throttle = 0.6 }, 60 * 12);
    }

    [Test]
    public void EasyFlightOverHillsMatchesTheBrowserGame()
    {
        var m = new FlightModel(AircraftCatalog.Trainer, TerrainHeight.At);
        var easy = new EasyFlight();
        var (trim, throttle) = m.ResetAirborne(0, 250, 50);
        m.state.pos = new V3(1500, 0, -250);
        easy.Reset(true, trim, throttle);
        var input = new PilotInput();
        var cmd = new EasyCommand();
        FlightReferenceTests.Compare(Flight("easy-over-hills-trainer"), m, input, 180 * 60, before: f =>
        {
            if (f == 30 * 60) cmd.roll = 0.4;
            if (f == 50 * 60) cmd.roll = 0;
            if (f == 120 * 60) cmd.pitch = -0.6;
            var o = easy.Update(m.telemetry, cmd, 1.0 / 60, m.alphaWarn / DEG);
            input.elevator = o.elevator;
            input.aileron = o.aileron;
            input.rudder = o.rudder;
            input.throttle = o.throttle;
            input.trim = 0;
        });
    }
}
