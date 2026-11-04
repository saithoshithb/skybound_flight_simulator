using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;

public class AircraftVisualsTests
{
    readonly Owned owned = new Owned();
    GameObject root;

    [TearDown]
    public void CleanUp()
    {
        if (root != null) Object.DestroyImmediate(root);
        owned.DestroyAll();
    }

    AircraftParts Build(AircraftDef def)
    {
        root = new GameObject("Aircraft under test");
        return AircraftBuilder.Build(def, root.transform, owned);
    }

    static Vector3 FarthestPoint(Transform surface)
    {
        var filter = surface.GetChild(0).GetComponent<MeshFilter>();
        var best = Vector3.zero;
        foreach (var v in filter.sharedMesh.vertices)
        {
            var world = filter.transform.TransformPoint(v);
            if ((world - surface.position).sqrMagnitude > (best - surface.position).sqrMagnitude) best = world;
        }
        return best;
    }

    static AircraftVisualState State(float elevator = 0, float aileron = 0, float rudder = 0, float engine = 0, float throttle = 0, float rpm = 0, double time = 0.5)
    {
        return new AircraftVisualState
        {
            elevator = elevator,
            aileron = aileron,
            rudder = rudder,
            engine = engine,
            throttle = throttle,
            rpm = rpm,
            maxElevator = 0.5f,
            maxAileron = 0.4f,
            timeS = time,
        };
    }

    [Test]
    public void TheTrainerHasAHighWingCabinAndStruts()
    {
        Build(AircraftCatalog.Trainer);
        var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        Assert.That(names, Does.Contain("Cabin"));
        Assert.That(names.Count(n => n.StartsWith("Strut")), Is.EqualTo(2));
        Assert.That(names, Does.Not.Contain("Canopy"));
        Assert.That(root.transform.Find("Wing").localPosition.y, Is.EqualTo(1).Within(1e-5f));
    }

    [Test]
    public void TheSportHasALowWingAndABubbleCanopy()
    {
        Build(AircraftCatalog.Sport);
        var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        Assert.That(names, Does.Contain("Canopy"));
        Assert.That(names, Does.Contain("Turtle deck"));
        Assert.That(names, Does.Not.Contain("Strut"));
        Assert.That(names, Does.Not.Contain("Roof"));
        Assert.That(root.transform.Find("Wing").localPosition.y, Is.EqualTo(-0.32f).Within(1e-5f));
    }

    [Test]
    public void TheNoseIsForwardAndTheWheelsSitAtTheGearContactPoints()
    {
        var def = AircraftCatalog.Trainer;
        var parts = Build(def);
        Assert.That(parts.prop.localPosition.z, Is.GreaterThan(2)); // Unity +z is forward
        Assert.That(parts.elevator.localPosition.z, Is.LessThan(-5));
        Assert.That(parts.aileronRight.localPosition.x, Is.GreaterThan(3)); // not mirrored: Unity +x is right

        var wheels = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Wheel")).ToList();
        Assert.That(wheels.Count, Is.EqualTo(3));
        foreach (var leg in def.gear.Take(3)) // the last contact point is the tail, which has no wheel
        {
            // FRD: x forward, y right. The model keeps the wheel at the leg's x and z.
            var found = wheels.Any(w => Mathf.Abs(w.localPosition.z - (float)leg.pos.x) < 1e-4f && Mathf.Abs(w.localPosition.x - (float)leg.pos.y) < 1e-4f);
            Assert.That(found, Is.True, $"no wheel at {leg.pos.x}, {leg.pos.y}");
        }
    }

    [Test]
    public void BothAircraftAreTheBlenderModels()
    {
        foreach (var def in AircraftCatalog.All)
        {
            var parts = Build(def);
            Assert.That(root.transform.Find("Exterior/Fuselage").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(5000), def.name);
            Assert.That(parts.cockpit.activeSelf, Is.False);
            // The imported material was swapped for the game's (which draws after the tone-mapping pass).
            Assert.That(parts.panelSurface.sharedMaterial.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
            CleanUp();
        }
    }

    [Test]
    public void TheInstrumentPanelFacesThePilotWhereTheCodeBuiltOneWas()
    {
        foreach (var def in AircraftCatalog.All)
        {
            var parts = Build(def);
            var filter = parts.panelSurface.GetComponent<MeshFilter>();
            var centre = root.transform.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center));
            // Three.Pos(-0.2, 0.42, -1.02) plus the 0.0265 offset, tilted back 0.2 rad
            Assert.That(Vector3.Distance(centre, new Vector3(-0.2f, 0.42f, 0.994f)), Is.LessThan(0.02f), $"{def.name}: {centre}");
            var normal = root.transform.InverseTransformDirection(filter.transform.TransformDirection(filter.sharedMesh.normals[0]));
            Assert.That(normal.z, Is.LessThan(-0.9f), $"{def.name}: faces {normal}"); // towards the pilot, behind it
            // The yoke's wheel is drawn at its pivot, not somewhere else in the cabin.
            var yoke = parts.yokeLeft.GetComponent<MeshFilter>();
            var wheel = root.transform.InverseTransformPoint(yoke.transform.TransformPoint(yoke.sharedMesh.bounds.center));
            Assert.That(Vector3.Distance(wheel, parts.yokeLeft.localPosition), Is.LessThan(0.25f), $"{def.name}: yoke drawn at {wheel}");
            CleanUp();
        }
    }

    [Test]
    public void TheSportsWheelsSitAtItsGearContactPoints()
    {
        var def = AircraftCatalog.Sport;
        var parts = Build(def);
        Assert.That(parts.aileronRight.localPosition.x, Is.GreaterThan(2));
        var wheels = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Wheel")).ToList();
        foreach (var leg in def.gear.Take(3))
            Assert.That(wheels.Any(w => Mathf.Abs(w.localPosition.z - (float)leg.pos.x) < 1e-4f && Mathf.Abs(w.localPosition.x - (float)leg.pos.y) < 1e-4f), Is.True, $"no wheel at {leg.pos.x}, {leg.pos.y}");
    }

    [Test]
    public void FacesPointTheWayTheirNormalsDo()
    {
        // In Unity a triangle's front is clockwise: Cross(b − a, c − a) along the normal.
        foreach (var geo in new[] { Geo.Box(1, 2, 3), Geo.Cylinder(1, 1, 2, 8), Geo.Sphere(1, 16, 10), Geo.Plane(1, 1), Geo.Circle(1, 8), Geo.Icosahedron(1), AircraftBuilder.FuselageGeometry() })
        {
            var mesh = geo.ToMesh("Under test");
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var face = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                if (face.sqrMagnitude < 1e-10f) continue; // a triangle collapsed at a pole
                Assert.That(Vector3.Dot(face, normals[triangles[i]]), Is.GreaterThan(0), $"triangle {i}");
            }
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void ElevatorDownMovesTheTrailingEdgeDown()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(elevator: 0.3f), 0);
        Assert.That(FarthestPoint(parts.elevator).y, Is.LessThan(parts.elevator.position.y - 0.1f));
    }

    [Test]
    public void RollRightRaisesTheRightAileronAndLowersTheLeft()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(aileron: 0.3f), 0);
        var rightEnd = parts.aileronRight.GetChild(0).TransformPoint(0, 0, -0.18f);
        var leftEnd = parts.aileronLeft.GetChild(0).TransformPoint(0, 0, -0.18f);
        Assert.That(rightEnd.y, Is.GreaterThan(parts.aileronRight.position.y + 0.05f));
        Assert.That(leftEnd.y, Is.LessThan(parts.aileronLeft.position.y - 0.05f));
    }

    [Test]
    public void RudderRightMovesTheTrailingEdgeRight()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(rudder: 0.3f), 0);
        var trailing = parts.rudder.GetChild(0).TransformPoint(0, 0, -0.2f);
        Assert.That(trailing.x, Is.GreaterThan(parts.rudder.position.x + 0.05f));
    }

    [Test]
    public void ThePropellerTurnsAtTheEngineSpeed()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(rpm: 600), 0.01f); // 10 rev/s for 0.01 s: 36°
        Assert.That(Quaternion.Angle(parts.prop.localRotation, Quaternion.identity), Is.EqualTo(36).Within(0.01f));
    }

    [Test]
    public void TheDiscThickensWithTheEngine()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(engine: 0), 0);
        Assert.That(parts.discMaterial.GetColor("_BaseColor").a, Is.EqualTo(0.08f).Within(1e-5f));
        AircraftBuilder.SyncAircraft(parts, State(engine: 1), 0);
        Assert.That(parts.discMaterial.GetColor("_BaseColor").a, Is.EqualTo(0.22f).Within(1e-5f));
    }

    [Test]
    public void YokesAndThrottleFollowTheControls()
    {
        var parts = Build(AircraftCatalog.Trainer);
        AircraftBuilder.SyncAircraft(parts, State(elevator: -0.5f, aileron: 0.4f, throttle: 1), 0); // full pull, full right roll
        // three.js z −0.62 + 0.07: towards the pilot is Unity −z.
        Assert.That(parts.yokeLeft.localPosition.z, Is.EqualTo(0.55f).Within(1e-5f));
        Assert.That(parts.yokeRight.localPosition.z, Is.EqualTo(0.55f).Within(1e-5f));
        // Roll right turns the top of the yoke to the right, seen from the seat.
        Assert.That((parts.yokeLeft.localRotation * Vector3.up).x, Is.GreaterThan(0.5f));
        // Pushed in = full power: further forward.
        Assert.That(parts.throttleKnob.localPosition.z, Is.EqualTo(0.97f).Within(1e-5f));
    }

    [TestCase(0.0, true)]
    [TestCase(0.049, true)]
    [TestCase(0.1, false)]
    [TestCase(0.16, true)]
    [TestCase(0.21, false)]
    [TestCase(1.0, false)]
    [TestCase(1.21, true)]
    public void StrobesDoubleFlashEvery1Point2Seconds(double time, bool lit)
    {
        Assert.That(AircraftBuilder.StrobeFlash(time), Is.EqualTo(lit));
    }

    [Test]
    public void EngineRpmMatchesTheBrowserGame()
    {
        Assert.That(Panel.EngineRpm(0, 0), Is.EqualTo(650));
        Assert.That(Panel.EngineRpm(1, 0), Is.EqualTo(2500));
        Assert.That(Panel.EngineRpm(1, 200), Is.EqualTo(2650)); // the windmill term tops out at 150
        Assert.That(Panel.EngineRpm(0.5, 50), Is.EqualTo(650 + 925 + 60 * 0.65).Within(1e-9));
    }

    // Expected values from three.js's formula, evaluated separately in JavaScript.
    [TestCase(1f, 1f, 1f, 0.75102f, 0.75102f, 0.75101f)]
    [TestCase(0.5f, 0.25f, 0.1f, 0.54795f, 0.30347f, 0.11577f)]
    public void ToneMappingFollowsThreeJs(float r, float g, float b, float er, float eg, float eb)
    {
        var mapped = ThreeToneMapping.Aces(new Color(r, g, b));
        Assert.That(mapped.r, Is.EqualTo(er).Within(1e-4f));
        Assert.That(mapped.g, Is.EqualTo(eg).Within(1e-4f));
        Assert.That(mapped.b, Is.EqualTo(eb).Within(1e-4f));
    }

    [Test]
    public void TheGroundDetailAveragesAboutTheBrowserGamesGrey()
    {
        var pixels = WorldBuilder.NoisePixels(256, new Vector3Int(150, 150, 150), 30, 7);
        var mean = pixels.Average(p => (double)p.r);
        // Noise around 150, pulled down a little by the soft dark patches.
        Assert.That(mean, Is.InRange(125, 150));
        Assert.That(pixels.Select(p => p.r).Distinct().Count(), Is.GreaterThan(20));
    }

    [Test]
    public void TheCloudSpriteFadesToNothingAtItsEdge()
    {
        var pixels = WorldBuilder.CloudPixels();
        Assert.That(pixels[0].a, Is.EqualTo(0));
        Assert.That(pixels[64 * 128 + 64].a, Is.EqualTo(242).Within(2)); // 0.95 at the centre
        Assert.That(pixels[64 * 128 + 98].a, Is.EqualTo(140).Within(3)); // 0.55 halfway out
    }

    [TestCase(GraphicsQuality.Low, 160, 900, 16, 14000f, false, false)]
    [TestCase(GraphicsQuality.Medium, 280, 1800, 28, 19000f, true, false)]
    [TestCase(GraphicsQuality.High, 400, 2600, 38, 22000f, true, true)]
    public void GraphicsTiersMatchTheBrowserGame(GraphicsQuality quality, int segments, int trees, int clouds, float fogFar, bool shadows, bool postFx)
    {
        var profile = GraphicsProfile.For(quality);
        Assert.That(profile.terrainSegments, Is.EqualTo(segments));
        Assert.That(profile.trees, Is.EqualTo(trees));
        Assert.That(profile.clouds, Is.EqualTo(clouds));
        Assert.That(profile.fogFar, Is.EqualTo(fogFar));
        Assert.That(profile.shadows, Is.EqualTo(shadows));
        Assert.That(profile.postFx, Is.EqualTo(postFx));
    }
}
