using System;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

public class BuildingsTests
{
    readonly Owned owned = new Owned();
    GameObject root;

    [TearDown]
    public void CleanUp()
    {
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        owned.DestroyAll();
    }

    [Test]
    public void TheAirfieldAndTheTownAreThere()
    {
        var all = Buildings.All;
        Assert.That(all.Count(b => b.type == "Hangar"), Is.EqualTo(2));
        Assert.That(all.Count(b => b.type == "Control tower"), Is.EqualTo(1));
        Assert.That(all.Count(b => b.type == "Church"), Is.EqualTo(1));
        Assert.That(all.Length, Is.GreaterThan(100));
    }

    [Test]
    public void NothingStandsOnTheRunwayOrItsApproaches()
    {
        // The runway runs north 1,200 m from the origin; keep 2.5 km clear beyond each end.
        for (var n = -2500; n <= 3700; n += 5)
            for (var e = -100; e <= 100; e += 10)
                Assert.That(Buildings.TopAt(n, e), Is.EqualTo(0), $"{n}, {e}");
    }

    [Test]
    public void OverABuildingTheGroundIsItsRoof()
    {
        var tower = Buildings.All.Single(b => b.type == "Control tower");
        Assert.That(Buildings.GroundAt(tower.north, tower.east), Is.EqualTo(tower.ground + 23.8).Within(1e-6));
        Assert.That(Buildings.GroundAt(tower.north + 30, tower.east), Is.EqualTo(TerrainHeight.At(tower.north + 30, tower.east)));
        foreach (var b in Buildings.All)
        {
            // Standing on the lowest ground under it: never above the terrain at its centre.
            Assert.That(b.ground, Is.LessThanOrEqualTo(TerrainHeight.At(b.north, b.east)), b.type);
            // Where one building is solid, the flight model's ground is at least that high.
            for (var dn = -4; dn <= 4; dn += 2)
                for (var de = -4; de <= 4; de += 2)
                    Assert.That(Buildings.GroundAt(b.north + dn, b.east + de), Is.GreaterThanOrEqualTo(Buildings.TopOf(b, b.north + dn, b.east + de)));
        }
    }

    static FlightModel FlyNorthAt(Func<double, double, double> ground, double north, double east, double height)
    {
        var m = new FlightModel(AircraftCatalog.Trainer, ground);
        m.ResetAirborne();
        m.state.pos = new V3(north, east, -height);
        m.state.vel = new V3(50, 0, 0);
        m.state.q = FromEuler(0, 0, 0);
        return m;
    }

    [Test]
    public void FlyingIntoAHangarCrashesButTheTerrainAloneWouldNot()
    {
        var hangar = Buildings.All.Where(b => b.type == "Hangar").OrderBy(b => b.north).First();
        var start = hangar.north - 50; // 50 m short of its centre, 12 m up: under the 13 m roof
        var solid = FlyNorthAt(Buildings.GroundAt, start, hangar.east, hangar.ground + 12);
        var open = FlyNorthAt(TerrainHeight.At, start, hangar.east, hangar.ground + 12);
        var input = new PilotInput { throttle = 0.6 };
        for (var i = 0; i < 90; i++)
        {
            solid.Advance(input, 1.0 / 60);
            open.Advance(input, 1.0 / 60);
        }
        Assert.That(solid.phase, Is.EqualTo(FlightPhase.Crashed));
        Assert.That(open.phase, Is.Not.EqualTo(FlightPhase.Crashed));
    }

    [Test]
    public void EachBuildingIsDrawnWhereItsCollisionIs()
    {
        root = new GameObject("World under test");
        WorldBuilder.BuildBuildings(root.transform, owned);
        Assert.That(root.transform.childCount, Is.EqualTo(Buildings.All.Length));
        for (var i = 0; i < root.transform.childCount; i++)
        {
            var building = root.transform.GetChild(i);
            var b = Buildings.All[i];
            Assert.That(building.GetComponent<LODGroup>().lodCount, Is.EqualTo(2));
            var far = building.Find(b.type + " LOD1").GetComponent<MeshRenderer>();
            Assert.That(far.enabled && far.gameObject.activeSelf, Is.True, $"{b.type}: far model hidden");
            Assert.That(far.sharedMaterials.All(m => !m.name.Contains("#")), Is.True, $"{b.type}: an imported material is left");

            // The drawn model's footprint (x = east, z = north) matches the collision boxes', turned the same way.
            var drawn = far.bounds;
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            for (var e = drawn.min.x - 5; e <= drawn.max.x + 5; e += 0.5f)
            {
                for (var n = drawn.min.z - 5; n <= drawn.max.z + 5; n += 0.5f)
                {
                    if (Buildings.TopOf(b, n, e) <= b.ground) continue;
                    lo = Vector2.Min(lo, new Vector2(e, n));
                    hi = Vector2.Max(hi, new Vector2(e, n));
                }
            }
            var centre = (lo + hi) / 2;
            var size = hi - lo;
            Assert.That(Vector2.Distance(centre, new Vector2(drawn.center.x, drawn.center.z)), Is.LessThan(2), $"{b.type} at {b.north}, {b.east}: drawn at {drawn.center}, solid at {centre}");
            Assert.That(Math.Abs(size.x - drawn.size.x), Is.LessThan(3), $"{b.type}: east-west {drawn.size.x} drawn, {size.x} solid");
            Assert.That(Math.Abs(size.y - drawn.size.z), Is.LessThan(3), $"{b.type}: north-south {drawn.size.z} drawn, {size.y} solid");
        }
    }
}
