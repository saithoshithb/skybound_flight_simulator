using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybound
{
    /// <summary>
    /// Ports of the three.js geometry classes the scene uses, vertex for vertex, in three.js
    /// space. <see cref="ToMesh"/> converts the result to Unity's space. Negating z only swaps the
    /// way the two cameras look, so a triangle stays counter-clockwise on screen. Unity's front
    /// faces are clockwise, so the triangle order is reversed there.
    /// </summary>
    public sealed class Geo
    {
        public readonly List<Vector3> position = new List<Vector3>();
        public readonly List<Vector3> normal = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> index = new List<int>();

        void Vertex(double x, double y, double z, double nx, double ny, double nz, double u, double v)
        {
            position.Add(new Vector3((float)x, (float)y, (float)z));
            normal.Add(new Vector3((float)nx, (float)ny, (float)nz));
            uv.Add(new Vector2((float)u, (float)v));
        }

        void Triangle(int a, int b, int c)
        {
            index.Add(a);
            index.Add(b);
            index.Add(c);
        }

        // ---------- BoxGeometry ----------

        public static Geo Box(double width, double height, double depth)
        {
            var g = new Geo();
            g.BuildPlane(2, 1, 0, -1, -1, depth, height, width); // px
            g.BuildPlane(2, 1, 0, 1, -1, depth, height, -width); // nx
            g.BuildPlane(0, 2, 1, 1, 1, width, depth, height); // py
            g.BuildPlane(0, 2, 1, 1, -1, width, depth, -height); // ny
            g.BuildPlane(0, 1, 2, 1, -1, width, height, depth); // pz
            g.BuildPlane(0, 1, 2, -1, -1, width, height, -depth); // nz
            return g;
        }

        void BuildPlane(int u, int v, int w, int udir, int vdir, double width, double height, double depth)
        {
            var first = position.Count;
            var p = new double[3];
            var n = new double[3];
            for (var iy = 0; iy < 2; iy++)
            {
                var y = iy * height - height / 2;
                for (var ix = 0; ix < 2; ix++)
                {
                    var x = ix * width - width / 2;
                    p[u] = x * udir;
                    p[v] = y * vdir;
                    p[w] = depth / 2;
                    n[u] = 0;
                    n[v] = 0;
                    n[w] = depth > 0 ? 1 : -1;
                    Vertex(p[0], p[1], p[2], n[0], n[1], n[2], ix, 1 - iy);
                }
            }
            var a = first;
            var b = first + 2;
            var c = first + 3;
            var d = first + 1;
            Triangle(a, b, d);
            Triangle(b, c, d);
        }

        // ---------- CylinderGeometry, ConeGeometry ----------

        public static Geo Cone(double radius, double height, int radialSegments, int heightSegments = 1, bool openEnded = false)
        {
            return Cylinder(0, radius, height, radialSegments, heightSegments, openEnded);
        }

        public static Geo Cylinder(double radiusTop, double radiusBottom, double height, int radialSegments, int heightSegments = 1, bool openEnded = false)
        {
            var g = new Geo();
            var halfHeight = height / 2;
            var slope = (radiusBottom - radiusTop) / height;
            var grid = new int[heightSegments + 1][];
            for (var y = 0; y <= heightSegments; y++)
            {
                grid[y] = new int[radialSegments + 1];
                var v = (double)y / heightSegments;
                var radius = v * (radiusBottom - radiusTop) + radiusTop;
                for (var x = 0; x <= radialSegments; x++)
                {
                    var u = (double)x / radialSegments;
                    var theta = u * Math.PI * 2;
                    var sin = Math.Sin(theta);
                    var cos = Math.Cos(theta);
                    var length = Math.Sqrt(sin * sin + slope * slope + cos * cos);
                    grid[y][x] = g.position.Count;
                    g.Vertex(radius * sin, -v * height + halfHeight, radius * cos, sin / length, slope / length, cos / length, u, 1 - v);
                }
            }
            for (var x = 0; x < radialSegments; x++)
            {
                for (var y = 0; y < heightSegments; y++)
                {
                    var a = grid[y][x];
                    var b = grid[y + 1][x];
                    var c = grid[y + 1][x + 1];
                    var d = grid[y][x + 1];
                    if (radiusTop > 0 || y != 0) g.Triangle(a, b, d);
                    if (radiusBottom > 0 || y != heightSegments - 1) g.Triangle(b, c, d);
                }
            }
            if (!openEnded)
            {
                if (radiusTop > 0) g.CylinderCap(true, radiusTop, halfHeight, radialSegments);
                if (radiusBottom > 0) g.CylinderCap(false, radiusBottom, halfHeight, radialSegments);
            }
            return g;
        }

        void CylinderCap(bool top, double radius, double halfHeight, int radialSegments)
        {
            var sign = top ? 1 : -1;
            var centerStart = position.Count;
            for (var x = 1; x <= radialSegments; x++)
                Vertex(0, halfHeight * sign, 0, 0, sign, 0, 0.5, 0.5);
            var centerEnd = position.Count;
            for (var x = 0; x <= radialSegments; x++)
            {
                var theta = (double)x / radialSegments * Math.PI * 2;
                var cos = Math.Cos(theta);
                var sin = Math.Sin(theta);
                Vertex(radius * sin, halfHeight * sign, radius * cos, 0, sign, 0, cos * 0.5 + 0.5, sin * 0.5 * sign + 0.5);
            }
            for (var x = 0; x < radialSegments; x++)
            {
                var c = centerStart + x;
                var i = centerEnd + x;
                if (top) Triangle(i, i + 1, c);
                else Triangle(i + 1, i, c);
            }
        }

        // ---------- SphereGeometry ----------

        public static Geo Sphere(double radius, int widthSegments, int heightSegments, double phiStart = 0, double phiLength = Math.PI * 2, double thetaStart = 0, double thetaLength = Math.PI)
        {
            var g = new Geo();
            var thetaEnd = Math.Min(thetaStart + thetaLength, Math.PI);
            var grid = new int[heightSegments + 1][];
            for (var iy = 0; iy <= heightSegments; iy++)
            {
                grid[iy] = new int[widthSegments + 1];
                var v = (double)iy / heightSegments;
                var uOffset = 0.0;
                if (iy == 0 && thetaStart == 0) uOffset = 0.5 / widthSegments;
                else if (iy == heightSegments && thetaEnd == Math.PI) uOffset = -0.5 / widthSegments;
                for (var ix = 0; ix <= widthSegments; ix++)
                {
                    var u = (double)ix / widthSegments;
                    var x = -radius * Math.Cos(phiStart + u * phiLength) * Math.Sin(thetaStart + v * thetaLength);
                    var y = radius * Math.Cos(thetaStart + v * thetaLength);
                    var z = radius * Math.Sin(phiStart + u * phiLength) * Math.Sin(thetaStart + v * thetaLength);
                    var length = Math.Sqrt(x * x + y * y + z * z);
                    grid[iy][ix] = g.position.Count;
                    g.Vertex(x, y, z, x / length, y / length, z / length, u + uOffset, 1 - v);
                }
            }
            for (var iy = 0; iy < heightSegments; iy++)
            {
                for (var ix = 0; ix < widthSegments; ix++)
                {
                    var a = grid[iy][ix + 1];
                    var b = grid[iy][ix];
                    var c = grid[iy + 1][ix];
                    var d = grid[iy + 1][ix + 1];
                    if (iy != 0 || thetaStart > 0) g.Triangle(a, b, d);
                    if (iy != heightSegments - 1 || thetaEnd < Math.PI) g.Triangle(b, c, d);
                }
            }
            return g;
        }

        // ---------- CircleGeometry, PlaneGeometry ----------

        public static Geo Circle(double radius, int segments)
        {
            var g = new Geo();
            g.Vertex(0, 0, 0, 0, 0, 1, 0.5, 0.5);
            for (var s = 0; s <= segments; s++)
            {
                var angle = (double)s / segments * Math.PI * 2;
                var x = radius * Math.Cos(angle);
                var y = radius * Math.Sin(angle);
                g.Vertex(x, y, 0, 0, 0, 1, (x / radius + 1) / 2, (y / radius + 1) / 2);
            }
            for (var i = 1; i <= segments; i++) g.Triangle(i, i + 1, 0);
            return g;
        }

        public static Geo Plane(double width, double height)
        {
            var g = new Geo();
            for (var iy = 0; iy < 2; iy++)
            {
                var y = iy * height - height / 2;
                for (var ix = 0; ix < 2; ix++)
                    g.Vertex(ix * width - width / 2, -y, 0, 0, 0, 1, ix, 1 - iy);
            }
            g.Triangle(0, 2, 1);
            g.Triangle(2, 3, 1);
            return g;
        }

        // ---------- IcosahedronGeometry (detail 0) ----------

        /// <summary>Flat-shaded: three.js gives each face its own three vertices at detail 0. No UVs.</summary>
        public static Geo Icosahedron(double radius)
        {
            var t = (1 + Math.Sqrt(5)) / 2;
            double[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] faces = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var g = new Geo();
            var scale = radius / Math.Sqrt(1 + t * t);
            for (var i = 0; i < faces.Length; i += 3)
            {
                // three.js subdivides each face a, b, c and pushes them as b, c, a.
                foreach (var corner in new[] { faces[i + 1], faces[i + 2], faces[i] })
                    g.position.Add(new Vector3((float)(v[corner * 3] * scale), (float)(v[corner * 3 + 1] * scale), (float)(v[corner * 3 + 2] * scale)));
            }
            g.ComputeFaceNormals();
            return g;
        }

        // ---------- LatheGeometry ----------

        /// <summary>Points are (radius, y). Only the fuselage uses this, and it recomputes the normals, so they start empty.</summary>
        public static Geo Lathe(IReadOnlyList<Vector2> points, int segments)
        {
            var g = new Geo();
            for (var i = 0; i <= segments; i++)
            {
                var phi = i * (1.0 / segments) * (Math.PI * 2);
                var sin = Math.Sin(phi);
                var cos = Math.Cos(phi);
                for (var j = 0; j < points.Count; j++)
                    g.Vertex(points[j].x * sin, points[j].y, points[j].x * cos, 0, 0, 0, (double)i / segments, (double)j / (points.Count - 1));
            }
            for (var i = 0; i < segments; i++)
            {
                for (var j = 0; j < points.Count - 1; j++)
                {
                    var a = j + i * points.Count;
                    var b = a + points.Count;
                    var c = a + points.Count + 1;
                    var d = a + 1;
                    g.Triangle(a, b, d);
                    g.Triangle(c, d, b);
                }
            }
            return g;
        }

        // ---------- BufferGeometry ----------

        public Geo RotateX(double angle)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            for (var i = 0; i < position.Count; i++)
            {
                position[i] = RotatedX(position[i], cos, sin);
                normal[i] = RotatedX(normal[i], cos, sin);
            }
            return this;
        }

        static Vector3 RotatedX(Vector3 v, double cos, double sin)
        {
            return new Vector3(v.x, (float)(v.y * cos - v.z * sin), (float)(v.y * sin + v.z * cos));
        }

        /// <summary>Indexed geometry: each vertex gets the sum of its faces' area-weighted normals.</summary>
        public Geo ComputeVertexNormals()
        {
            var sum = new Vector3[position.Count];
            for (var i = 0; i < index.Count; i += 3)
            {
                var a = index[i];
                var b = index[i + 1];
                var c = index[i + 2];
                var face = Vector3.Cross(position[c] - position[b], position[a] - position[b]);
                sum[a] += face;
                sum[b] += face;
                sum[c] += face;
            }
            normal.Clear();
            foreach (var n in sum) normal.Add(n.normalized);
            return this;
        }

        void ComputeFaceNormals()
        {
            normal.Clear();
            for (var i = 0; i < position.Count; i += 3)
            {
                var face = Vector3.Cross(position[i + 2] - position[i + 1], position[i] - position[i + 1]).normalized;
                normal.Add(face);
                normal.Add(face);
                normal.Add(face);
            }
        }

        /// <summary>The same shape with a separate vertex per face corner, as a material's flatShading looks. Drops UVs.</summary>
        public Geo Flattened()
        {
            var g = new Geo();
            foreach (var i in index) g.position.Add(position[i]);
            g.ComputeFaceNormals();
            return g;
        }

        /// <summary>Adds this shape to a combined one, standing in for InstancedMesh and many separate meshes.</summary>
        public void AppendTo(Geo target, Matrix4x4 transform, Vector2? uvForAll = null)
        {
            var first = target.position.Count;
            var forNormals = transform.inverse.transpose;
            for (var i = 0; i < position.Count; i++)
            {
                target.position.Add(transform.MultiplyPoint3x4(position[i]));
                target.normal.Add(forNormals.MultiplyVector(normal[i]).normalized);
                if (uvForAll.HasValue) target.uv.Add(uvForAll.Value);
                else if (uv.Count > 0) target.uv.Add(uv[i]);
            }
            if (index.Count == 0)
                for (var i = 0; i < position.Count; i++) target.index.Add(first + i);
            else
                foreach (var i in index) target.index.Add(first + i);
        }

        public Mesh ToMesh(string name)
        {
            var vertices = new Vector3[position.Count];
            var normals = new Vector3[position.Count];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(position[i].x, position[i].y, -position[i].z);
                normals[i] = new Vector3(normal[i].x, normal[i].y, -normal[i].z);
            }
            var triangles = new int[index.Count > 0 ? index.Count : vertices.Length];
            for (var i = 0; i < triangles.Length; i += 3)
            {
                triangles[i] = index.Count > 0 ? index[i] : i;
                triangles[i + 1] = index.Count > 0 ? index[i + 2] : i + 2;
                triangles[i + 2] = index.Count > 0 ? index[i + 1] : i + 1;
            }
            var mesh = new Mesh { name = name, indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            if (uv.Count > 0) mesh.SetUVs(0, uv);
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
