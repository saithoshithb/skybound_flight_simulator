using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;

/// <summary>
/// Compares the ported geometry and scenery with Data/reference-world.json, which was recorded
/// from real three.js and the browser game's own layout code.
/// </summary>
public class WorldReferenceTests
{
    [Serializable]
    class GeometryData
    {
        public string name;
        public float[] position;
        public float[] normal;
        public float[] uv;
        public int[] index;
    }

    [Serializable]
    class Row
    {
        public double[] v;
    }

    [Serializable]
    class TreeSet
    {
        public int count;
        public Row[] trees;
    }

    [Serializable]
    class CloudSet
    {
        public int count;
        public Row[] puffs;
    }

    [Serializable]
    class NamedColor
    {
        public string hex;
        public float[] v;
    }

    [Serializable]
    class FileData
    {
        public GeometryData[] geometries;
        public TreeSet[] trees;
        public Row[] hills;
        public CloudSet[] clouds;
        public Row[] marks;
        public NamedColor[] colors;
    }

    static FileData file;

    static FileData File
    {
        get
        {
            if (file == null)
            {
                var path = Path.Combine(Application.dataPath, "Tests/EditMode/Data/reference-world.json");
                file = JsonUtility.FromJson<FileData>(System.IO.File.ReadAllText(path));
            }
            return file;
        }
    }

    static GeometryData Reference(string name) => File.geometries.First(g => g.name == name);

    static void AssertSame(string name, Geo geo)
    {
        var expected = Reference(name);
        Assert.That(geo.position.Count * 3, Is.EqualTo(expected.position.Length), name + " vertex count");
        Assert.That(geo.normal.Count * 3, Is.EqualTo(expected.normal.Length), name + " normal count");
        Assert.That(geo.index, Is.EqualTo(expected.index), name + " triangles");
        for (var i = 0; i < geo.position.Count; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                Assert.That(geo.position[i][c], Is.EqualTo(expected.position[i * 3 + c]).Within(1e-5f), $"{name} position {i}.{c}");
                Assert.That(geo.normal[i][c], Is.EqualTo(expected.normal[i * 3 + c]).Within(1e-4f), $"{name} normal {i}.{c}");
            }
        }
        // The icosahedron's UVs aren't used, and aren't ported.
        if (geo.uv.Count == 0) return;
        Assert.That(geo.uv.Count * 2, Is.EqualTo(expected.uv.Length), name + " uv count");
        for (var i = 0; i < geo.uv.Count; i++)
        {
            Assert.That(geo.uv[i].x, Is.EqualTo(expected.uv[i * 2]).Within(1e-5f), $"{name} uv {i}.x");
            Assert.That(geo.uv[i].y, Is.EqualTo(expected.uv[i * 2 + 1]).Within(1e-5f), $"{name} uv {i}.y");
        }
    }

    [Test] public void Box() => AssertSame("box", Geo.Box(1.5, 2.5, 3.5));
    [Test] public void Cylinder() => AssertSame("cylinder", Geo.Cylinder(0.035, 0.035, 2, 8));
    [Test] public void TaperedCylinder() => AssertSame("tapered", Geo.Cylinder(0.18, 0.25, 1, 5));
    [Test] public void Cone() => AssertSame("cone", Geo.Cone(0.2, 0.42, 18));
    [Test] public void OpenCone() => AssertSame("openCone", Geo.Cone(0.35, 2.2, 12, 1, true));
    [Test] public void HillCone() => AssertSame("hillCone", Geo.Cone(1, 1, 7, 1));
    [Test] public void Sphere() => AssertSame("sphere", Geo.Sphere(1, 16, 10));
    [Test] public void Dome() => AssertSame("dome", Geo.Sphere(1, 24, 14, 0, Math.PI * 2, 0, Math.PI / 2));
    [Test] public void Circle() => AssertSame("circle", Geo.Circle(0.95, 32));
    [Test] public void Plane() => AssertSame("plane", Geo.Plane(3, 4));
    [Test] public void Icosahedron() => AssertSame("icosahedron", Geo.Icosahedron(1));
    [Test] public void Fuselage() => AssertSame("fuselage", AircraftBuilder.FuselageGeometry());

    static void AssertRow(double actual, double expected, string what)
    {
        Assert.That(actual, Is.EqualTo(expected).Within(Math.Abs(expected) * 1e-9 + 1e-9), what);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void TreesLandWhereTheBrowserGamePutsThem(int set)
    {
        var expected = File.trees[set];
        var trees = WorldBuilder.PlaceTrees(expected.count);
        Assert.That(trees.Length, Is.EqualTo(expected.count));
        foreach (var row in expected.trees)
        {
            var v = row.v;
            var n = (int)v[0];
            var t = trees[n];
            AssertRow(t.x, v[1], $"tree {n} x");
            AssertRow(t.z, v[2], $"tree {n} z");
            AssertRow(t.h, v[3], $"tree {n} h");
            // The browser's ground is its noise hills; ours is the Blender heightmap, so trees stand on that.
            Assert.That(t.ground, Is.EqualTo(TerrainHeight.At(-t.z, t.x)), $"tree {n} ground");
            AssertRow(Math.Sin(t.turn / 2), v[5], $"tree {n} turn sine");
            AssertRow(Math.Cos(t.turn / 2), v[6], $"tree {n} turn cosine");
            Assert.That(t.color.r, Is.EqualTo(v[7]).Within(1e-6), $"tree {n} red");
            Assert.That(t.color.g, Is.EqualTo(v[8]).Within(1e-6), $"tree {n} green");
            Assert.That(t.color.b, Is.EqualTo(v[9]).Within(1e-6), $"tree {n} blue");
        }
    }

    [Test]
    public void HillsLandWhereTheBrowserGamePutsThem()
    {
        var hills = WorldBuilder.PlaceHills();
        Assert.That(hills.Length, Is.EqualTo(File.hills.Length));
        for (var i = 0; i < hills.Length; i++)
        {
            var v = File.hills[i].v;
            AssertRow(hills[i].x, v[0], $"hill {i} x");
            AssertRow(hills[i].z, v[1], $"hill {i} z");
            AssertRow(hills[i].h, v[2], $"hill {i} h");
            AssertRow(hills[i].w, v[3], $"hill {i} w");
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void CloudsLandWhereTheBrowserGamePutsThem(int set)
    {
        var expected = File.clouds[set];
        var puffs = WorldBuilder.PlaceClouds(expected.count);
        Assert.That(puffs.Count, Is.EqualTo(expected.puffs.Length));
        for (var i = 0; i < puffs.Count; i++)
        {
            var v = expected.puffs[i].v;
            AssertRow(puffs[i].x, v[0], $"puff {i} x");
            AssertRow(puffs[i].y, v[1], $"puff {i} y");
            AssertRow(puffs[i].z, v[2], $"puff {i} z");
            AssertRow(puffs[i].size, v[3], $"puff {i} size");
            AssertRow(puffs[i].opacity, v[4], $"puff {i} opacity");
            Assert.That(puffs[i].shade.r, Is.EqualTo(v[5]).Within(1e-3), $"puff {i} red");
            Assert.That(puffs[i].shade.g, Is.EqualTo(v[6]).Within(1e-3), $"puff {i} green");
            Assert.That(puffs[i].shade.b, Is.EqualTo(v[7]).Within(1e-3), $"puff {i} blue");
        }
    }

    [Test]
    public void RunwayMarksMatch()
    {
        var marks = WorldBuilder.RunwayMarks();
        Assert.That(marks.Count, Is.EqualTo(File.marks.Length));
        for (var i = 0; i < marks.Count; i++)
        {
            var v = File.marks[i].v;
            Assert.That(marks[i].x, Is.EqualTo(v[0]).Within(1e-4), $"mark {i} x");
            Assert.That(marks[i].z, Is.EqualTo(v[1]).Within(1e-4), $"mark {i} z");
            Assert.That(marks[i].w, Is.EqualTo(v[2]).Within(1e-4), $"mark {i} w");
            Assert.That(marks[i].l, Is.EqualTo(v[3]).Within(1e-4), $"mark {i} l");
        }
    }

    [Test]
    public void ColoursConvertToLinearAsThreeDoes()
    {
        foreach (var c in File.colors)
        {
            var linear = Three.Linear(c.hex);
            Assert.That(linear.r, Is.EqualTo(c.v[0]).Within(1e-3f), c.hex + " red");
            Assert.That(linear.g, Is.EqualTo(c.v[1]).Within(1e-3f), c.hex + " green");
            Assert.That(linear.b, Is.EqualTo(c.v[2]).Within(1e-3f), c.hex + " blue");
        }
    }
}
