using System;
using System.IO;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

// Started as a port of src/world/terrain.test.ts. The terrain is now the Blender heightmap, so it no
// longer matches the browser game; these check the rules that keep it flyable instead.
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
    public void HasMountainsFurtherOutAndIsDeterministicAndSmooth()
    {
        double max = 0;
        for (var n = -15000; n <= 15000; n += 250)
        {
            for (var e = -15000; e <= 15000; e += 250) max = Math.Max(max, TerrainHeight.At(n, e));
        }
        Assert.That(max, Is.InRange(1000, 2000));
        Assert.That(TerrainHeight.At(7000, 6000), Is.EqualTo(TerrainHeight.At(7000, 6000)));
        // Mountain slopes, but no cliffs or spikes: 10 m apart never differs by more than 8 m (39°).
        for (var n = 3000; n < 12000; n += 777)
            Assert.That(Math.Abs(TerrainHeight.At(n, 5000) - TerrainHeight.At(n + 10, 5000)), Is.LessThan(8));
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

    [Test]
    public void BetweenHeightmapSamplesTheGroundIsBilinear()
    {
        // 31.25 m apart: halfway between two samples on a line is their mean, well away from the masks.
        const double spacing = 32000.0 / 1024;
        var e0 = -16000 + spacing * 700;
        var n0 = -16000 + spacing * 850;
        var a = TerrainHeight.At(n0, e0);
        var b = TerrainHeight.At(n0, e0 + spacing);
        Assert.That(TerrainHeight.At(n0, e0 + spacing / 2), Is.EqualTo((a + b) / 2).Within(1e-6));
    }
}
