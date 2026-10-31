using NUnit.Framework;
using Skybound;
using UnityEngine;
using UnityEngine.Rendering;

public class GroundTests
{
    [Test]
    public void TheMeshIsBuiltFromTheTerrainFunctionTheFlightModelUses()
    {
        const int segments = 32;
        var mesh = WorldBuilder.BuildGroundMesh(segments);
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(33 * 33));
            Assert.That(mesh.triangles.Length, Is.EqualTo(32 * 32 * 6));
            Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));
            var vertices = mesh.vertices;
            var hills = 0;
            foreach (var v in vertices)
            {
                // x = east, z = north, as Coords maps the flight model's positions.
                var expected = (float)TerrainHeight.At(v.z, v.x);
                Assert.That(v.y, Is.EqualTo(expected).Within(1e-3f));
                if (v.y > 50) hills++;
            }
            Assert.That(hills, Is.GreaterThan(0));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(32000).Within(1));
            Assert.That(mesh.bounds.size.z, Is.EqualTo(32000).Within(1));
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void TheMeshFacesUpwards()
    {
        var mesh = WorldBuilder.BuildGroundMesh(32);
        try
        {
            foreach (var normal in mesh.normals) Assert.That(normal.y, Is.GreaterThan(0.3f));
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void TheLargestGridSizeFits()
    {
        var mesh = WorldBuilder.BuildGroundMesh(400);
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(401 * 401));
            Assert.That(mesh.vertices[401 * 200 + 200].y, Is.EqualTo(0)); // the airfield is flat
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void ColoursShadeByHeightAndSlope()
    {
        var field = WorldBuilder.GroundColor(600, 300);
        var summit = WorldBuilder.GroundColor(12250, 13500); // over 400 m: rock
        Assert.That(field.g, Is.GreaterThan(field.r)); // green around the airfield
        Assert.That(summit.r, Is.GreaterThan(summit.g * 0.9f)); // not green on top
        Assert.That(summit.r, Is.GreaterThan(field.r));
    }

    [Test]
    public void TheTextureHasOneTexelPerVertexAtEachVertexPosition()
    {
        const int segments = 32;
        var texture = WorldBuilder.BuildGroundTexture(segments);
        var mesh = WorldBuilder.BuildGroundMesh(segments);
        try
        {
            Assert.That(texture.width, Is.EqualTo(33));
            Assert.That(texture.height, Is.EqualTo(33));
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            var pixels = texture.GetPixels32();
            for (var i = 0; i < vertices.Length; i += 97)
            {
                Color32 expected = WorldBuilder.GroundColor(vertices[i].z, vertices[i].x).gamma;
                var x = Mathf.FloorToInt(uv[i].x * 33);
                var y = Mathf.FloorToInt(uv[i].y * 33);
                Assert.That(pixels[y * 33 + x], Is.EqualTo(expected), $"vertex {i}");
            }
        }
        finally
        {
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(texture);
        }
    }
}
