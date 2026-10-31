using System;

namespace Skybound
{
    // Small vector/quaternion helpers for the flight model, in double precision.
    // Frames: world is NED (x north, y east, z down); body is FRD (x forward,
    // y right, z down). Quaternions rotate body -> world.

    [Serializable]
    public struct V3
    {
        public double x, y, z;
        public V3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    [Serializable]
    public struct Quat
    {
        public double w, x, y, z;
        public Quat(double w, double x, double y, double z)
        {
            this.w = w;
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public struct Euler
    {
        public double roll, pitch, yaw;
    }

    public static class FlightMath
    {
        public const double DEG = Math.PI / 180;

        public static V3 Add(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 Sub(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 Scale(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);
        public static double Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Cross(V3 a, V3 b) => new V3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);
        public static double Length(V3 a) => Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);

        public static Quat QMul(Quat a, Quat b) => new Quat(
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w);

        public static Quat QNormalize(Quat q)
        {
            var n = Math.Sqrt(q.w * q.w + q.x * q.x + q.y * q.y + q.z * q.z);
            if (n == 0) n = 1;
            return new Quat(q.w / n, q.x / n, q.y / n, q.z / n);
        }

        /// <summary>Rotate a body vector into the world frame.</summary>
        public static V3 Rotate(Quat q, V3 v)
        {
            // t = 2 * cross(q.xyz, v); v' = v + w*t + cross(q.xyz, t)
            var tx = 2 * (q.y * v.z - q.z * v.y);
            var ty = 2 * (q.z * v.x - q.x * v.z);
            var tz = 2 * (q.x * v.y - q.y * v.x);
            return new V3(
                v.x + q.w * tx + (q.y * tz - q.z * ty),
                v.y + q.w * ty + (q.z * tx - q.x * tz),
                v.z + q.w * tz + (q.x * ty - q.y * tx));
        }

        /// <summary>Rotate a world vector into the body frame.</summary>
        public static V3 RotateInv(Quat q, V3 v) => Rotate(new Quat(q.w, -q.x, -q.y, -q.z), v);

        /// <summary>Aerospace Z-Y-X Euler angles (radians) to quaternion.</summary>
        public static Quat FromEuler(double roll, double pitch, double yaw)
        {
            double cr = Math.Cos(roll / 2), sr = Math.Sin(roll / 2);
            double cp = Math.Cos(pitch / 2), sp = Math.Sin(pitch / 2);
            double cy = Math.Cos(yaw / 2), sy = Math.Sin(yaw / 2);
            return new Quat(
                cr * cp * cy + sr * sp * sy,
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy);
        }

        public static Euler ToEuler(Quat q) => new Euler
        {
            roll = Math.Atan2(2 * (q.w * q.x + q.y * q.z), 1 - 2 * (q.x * q.x + q.y * q.y)),
            pitch = Math.Asin(Math.Max(-1, Math.Min(1, 2 * (q.w * q.y - q.z * q.x)))),
            yaw = Math.Atan2(2 * (q.w * q.z + q.x * q.y), 1 - 2 * (q.y * q.y + q.z * q.z)),
        };

        public static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

        /// <summary>Like JavaScript's Math.sign: unlike Math.Sign, it never throws on NaN.</summary>
        public static double Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : v;
    }
}
