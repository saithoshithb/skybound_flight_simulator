using System;

namespace Skybound
{
    /// <summary>
    /// Terrain height (metres above the field, ≥ 0) as a pure function of
    /// position. Shared by the renderer and the flight model, so hills you see are
    /// hills you can hit. The airfield, its surroundings and the southern approach
    /// to runway 36 are flat; rolling hills rise beyond them.
    ///
    /// Coordinates: north (m) and east (m) from the runway-36 threshold.
    /// </summary>
    public static class TerrainHeight
    {
        public const double flatRadiusM = 1800;
        public const double rampM = 3500;
        public const double maxHillM = 380;

        const double RUNWAY_END_N = 1200;

        // JS wraps to 32 bits with `| 0`, Math.imul and `>>>`; int and uint do the same here.
        static double Hash(long ix, long iy)
        {
            unchecked
            {
                var h = (int)(ix * 374761393L + iy * 668265263L);
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return ((uint)h ^ ((uint)h >> 16)) / 4294967296.0;
            }
        }

        static double ValueNoise(double x, double y)
        {
            var ix = (long)Math.Floor(x);
            var iy = (long)Math.Floor(y);
            var fx = x - ix;
            var fy = y - iy;
            var sx = fx * fx * (3 - 2 * fx);
            var sy = fy * fy * (3 - 2 * fy);
            var a = Hash(ix, iy);
            var b = Hash(ix + 1, iy);
            var c = Hash(ix, iy + 1);
            var d = Hash(ix + 1, iy + 1);
            return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
        }

        static double Fbm(double x, double y)
        {
            double sum = 0;
            var amp = 0.5;
            double freq = 1;
            for (var o = 0; o < 5; o++)
            {
                sum += amp * ValueNoise(x * freq, y * freq);
                freq *= 2.03;
                amp *= 0.5;
            }
            return sum; // ≈ 0…1
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
            if (mask == 0) return 0;
            var @base = Fbm(north / 4200 + 11.3, east / 4200 - 7.1);
            var ridges = 1 - Math.Abs(2 * Fbm(north / 2300 - 3.7, east / 2300 + 5.2) - 1);
            var h = Math.Max(0, @base - 0.32) * 1.5 + ridges * ridges * 0.35;
            return mask * h * maxHillM;
        }
    }
}
