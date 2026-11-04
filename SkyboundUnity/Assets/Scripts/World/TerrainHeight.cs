using System;
using UnityEngine;

namespace Skybound
{
    /// <summary>
    /// Terrain height (metres above the field, ≥ 0) as a pure function of
    /// position. Shared by the renderer and the flight model, so hills you see are
    /// hills you can hit. The shape is a heightmap made in Blender (Tools/blender/terrain.py);
    /// on top of it the airfield, its surroundings and the southern approach to runway 36
    /// are kept flat, and the tile's edge fades out, whatever was sculpted there.
    ///
    /// Coordinates: north (m) and east (m) from the runway-36 threshold.
    /// </summary>
    public static class TerrainHeight
    {
        public const double flatRadiusM = 1800;
        public const double rampM = 3500;
        /// <summary>About the tallest peak: the minimap shades from the field up to here.</summary>
        public const double maxHillM = 1500;

        const double RUNWAY_END_N = 1200;
        /// <summary>Half the drawn terrain tile. Past it the world draws a flat plain, so the hills fade out before it.</summary>
        const double TILE_EDGE_M = 16000;
        const double EDGE_FADE_M = 2000;

        /// <summary>Heightmap: SIDE × SIDE 16-bit heights, rows south to north, columns west to east.</summary>
        const int SIDE = 1025;
        const double SPACING_M = 2 * TILE_EDGE_M / (SIDE - 1);
        const double UNIT_M = 2000.0 / 65535;
        static ushort[] heights;

        static ushort[] Heights()
        {
            if (heights != null) return heights;
            var asset = Resources.Load<TextAsset>("Terrain/heightmap");
            if (asset == null) throw new InvalidOperationException("Resources/Terrain/heightmap.bytes is missing: export it with Tools/blender/terrain.py");
            var bytes = asset.bytes;
            if (bytes.Length != SIDE * SIDE * 2) throw new InvalidOperationException($"The heightmap is {bytes.Length} bytes, not {SIDE * SIDE * 2}");
            var loaded = new ushort[SIDE * SIDE];
            Buffer.BlockCopy(bytes, 0, loaded, 0, bytes.Length); // little-endian, as written
            return heights = loaded;
        }

        /// <summary>Bilinear between the four nearest heightmap samples.</summary>
        static double Sample(double north, double east)
        {
            var map = Heights();
            var x = Math.Min(SIDE - 1.000001, Math.Max(0, (east + TILE_EDGE_M) / SPACING_M));
            var y = Math.Min(SIDE - 1.000001, Math.Max(0, (north + TILE_EDGE_M) / SPACING_M));
            var i = (int)x;
            var j = (int)y;
            var fx = x - i;
            var fy = y - j;
            var at = j * SIDE + i;
            double a = map[at], b = map[at + 1], c = map[at + SIDE], d = map[at + SIDE + 1];
            return (a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy) * UNIT_M;
        }

        static double Smoothstep(double a, double b, double x)
        {
            var t = Math.Min(1, Math.Max(0, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Distance (m) from a point to the runway centreline segment.</summary>
        static double DistToRunway(double n, double e)
        {
            var cn = Math.Min(RUNWAY_END_N, Math.Max(0, n));
            return Math.Sqrt((n - cn) * (n - cn) + e * e);
        }

        public static double At(double north, double east)
        {
            var mask = Smoothstep(flatRadiusM, flatRadiusM + rampM, DistToRunway(north, east));
            // Keep a wide, low corridor for the long final to runway 36 (south of the field).
            if (north < 0) mask *= 0.15 + 0.85 * Smoothstep(900, 2600, Math.Abs(east));
            mask *= 1 - Smoothstep(TILE_EDGE_M - EDGE_FADE_M, TILE_EDGE_M, Math.Max(Math.Abs(north), Math.Abs(east)));
            if (mask == 0) return 0;
            return mask * Sample(north, east);
        }
    }
}
