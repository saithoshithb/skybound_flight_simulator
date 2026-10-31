using System;
using UnityEngine;

namespace Skybound
{
    public enum YawMode
    {
        Off,
        Washout,
        Rate,
    }

    [Serializable]
    public class AxisSettings
    {
        [Tooltip("Controller tilt (deg) that produces full deflection before sensitivity.")]
        [Range(10, 70)] public double maxAngleDeg = 30;
        public bool invert;

        public AxisSettings() { }

        public AxisSettings(double maxAngle, bool inverted = false)
        {
            maxAngleDeg = maxAngle;
            invert = inverted;
        }
    }

    /// <summary>The defaults are tuned for Easy flight: hand tremor (±1–2°) produces nothing, small tilts
    /// give gentle turns, and smoothing hides sensor noise without feeling laggy.</summary>
    [Serializable]
    public class ControlSettings
    {
        public AxisSettings pitch = new AxisSettings(30);
        public AxisSettings roll = new AxisSettings(35);
        public AxisSettings yaw = new AxisSettings(30);
        [Tooltip("Off: rudder from the keyboard only. Washout: a twist gives rudder that fades away. Rate: twist speed gives rudder.")]
        public YawMode yawMode = YawMode.Off;
        [Tooltip("Yaw rate (deg/s) for full rudder in Rate mode.")]
        [Range(20, 300)] public double yawFullRateDps = 90;
        [Tooltip("Seconds for a held twist to wash out to zero rudder in Washout mode.")]
        [Range(0.5f, 10)] public double yawWashoutS = 3;
        [Tooltip("Multiplier applied after the curve. 1 = max angle gives full deflection.")]
        [Range(0.3f, 2)] public double sensitivity = 1;
        [Tooltip("Fraction of the range around neutral that produces no output.")]
        [Range(0, 0.3f)] public double deadzone = 0.08;
        [Tooltip("0 = linear, 1 = fully cubic: finer control near center.")]
        [Range(0, 1)] public double expo = 0.5;
        [Tooltip("Low-pass time constant (ms). Removes hand tremor and sensor noise.")]
        [Range(0, 250)] public double smoothingMs = 100;
        [Tooltip("Max command change per second. Stops instantaneous jumps.")]
        [Range(1, 12)] public double slewPerSec = 3;
    }

    /// <summary>
    /// Turns controller attitude into normalized pilot commands in [-1, 1].
    /// These are control-surface commands (stick position), not aircraft attitude:
    /// the flight model decides how the aircraft responds to them.
    ///
    ///   elevator + = pull / nose-up command   (controller front edge raised)
    ///   aileron  + = roll-right command       (controller right edge lowered)
    ///   rudder   + = yaw-right command        (controller twisted clockwise from above)
    /// </summary>
    public static class ControlMapping
    {
        /// <summary>Seconds to fade sensor authority in after (re)engaging, and out after a dropout.</summary>
        public const double ENGAGE_RAMP_S = 1.0;
        public const double DISENGAGE_RAMP_S = 0.35;

        public static double Clamp(double v, double lo = -1, double hi = 1) => Math.Min(hi, Math.Max(lo, v));
        public static double Wrap180(double d) => (((d + 180) % 360 + 360) % 360) - 180;

        /// <summary>Dead zone that rescales so output still reaches ±1 (no step at the edge).</summary>
        public static double ApplyDeadzone(double x, double dz)
        {
            var a = Math.Abs(x);
            if (a <= dz) return 0;
            return FlightMath.Sign(x) * Math.Min(1, (a - dz) / (1 - dz));
        }

        public static double ApplyExpo(double x, double expo)
        {
            return (1 - expo) * x + expo * x * x * x;
        }

        /// <summary>Static part of the shaping chain: normalized deflection → curve → sensitivity.</summary>
        public static double ShapeAxis(double deltaDeg, AxisSettings axis, ControlSettings s)
        {
            var x = Clamp(deltaDeg / axis.maxAngleDeg);
            var y = Clamp(ApplyExpo(ApplyDeadzone(x, s.deadzone), s.expo) * s.sensitivity);
            return axis.invert ? -y : y;
        }
    }

    /// <summary>
    /// Stateful part: smoothing, slew limiting, yaw washout and engagement ramp.
    /// Call Update once per simulation frame (not per sensor sample) with the
    /// latest sample or null when the link isn't usable.
    /// </summary>
    public class ControlPipeline
    {
        double outElevator, outAileron, outRudder;
        double engagement;
        double? yawRef;
        readonly ControlCommand output = new ControlCommand();

        /// <summary>Raw (pre-smoothing) targets, for the UI.</summary>
        public double targetElevator, targetAileron, targetRudder;

        /// <summary>Drop authority to zero immediately; it ramps back in. Use after recenter/connect.</summary>
        public void Reengage()
        {
            engagement = 0;
            yawRef = null;
        }

        /// <returns>The same object each call, valid until the next.</returns>
        public ControlCommand Update(ImuSample sample, Neutral neutral, ControlSettings s, double dt)
        {
            dt = Math.Min(Math.Max(dt, 0), 0.1);

            if (sample != null)
            {
                engagement = Math.Min(1, engagement + dt / ControlMapping.ENGAGE_RAMP_S);
                var dYaw = YawDelta(sample, neutral, s, dt);
                targetElevator = ControlMapping.ShapeAxis(sample.pitch - neutral.pitch, s.pitch, s);
                targetAileron = ControlMapping.ShapeAxis(ControlMapping.Wrap180(sample.roll - neutral.roll), s.roll, s);
                targetRudder = dYaw;
            }
            else
            {
                // Link stale/lost: fade to neutral instead of freezing the last deflection.
                engagement = Math.Max(0, engagement - dt / ControlMapping.DISENGAGE_RAMP_S);
                targetElevator = targetAileron = targetRudder = 0;
            }

            var alpha = s.smoothingMs > 0 ? 1 - Math.Exp(-(dt * 1000) / s.smoothingMs) : 1;
            var maxStep = s.slewPerSec * dt;
            outElevator = Step(outElevator, targetElevator, alpha, maxStep);
            outAileron = Step(outAileron, targetAileron, alpha, maxStep);
            outRudder = Step(outRudder, targetRudder, alpha, maxStep);
            var e = Smoothstep(engagement);
            output.elevator = outElevator * e;
            output.aileron = outAileron * e;
            output.rudder = outRudder * e;
            output.engagement = e;
            return output;
        }

        static double Step(double current, double target, double alpha, double maxStep)
        {
            var smoothed = current + (target - current) * alpha;
            return current + ControlMapping.Clamp(smoothed - current, -maxStep, maxStep);
        }

        double YawDelta(ImuSample sample, Neutral neutral, ControlSettings s, double dt)
        {
            switch (s.yawMode)
            {
                case YawMode.Rate:
                {
                    // Yaw *rate* is drift-free enough to use directly: twist to turn, hold still to stop.
                    var y = ControlMapping.Clamp(ControlMapping.ApplyDeadzone(ControlMapping.Clamp(sample.yawRate / s.yawFullRateDps), s.deadzone) * s.sensitivity);
                    return s.yaw.invert ? -y : y;
                }
                case YawMode.Washout:
                {
                    // Relative yaw with a high-pass: the reference slowly follows the
                    // controller, so gyro drift and a held twist both decay to zero rudder.
                    if (yawRef == null) yawRef = neutral.capturedAt != 0 ? neutral.yaw : sample.yaw;
                    var delta = ControlMapping.Wrap180(sample.yaw - yawRef.Value);
                    yawRef = ControlMapping.Wrap180(yawRef.Value + delta * (1 - Math.Exp(-dt / Math.Max(0.2, s.yawWashoutS))));
                    return ControlMapping.ShapeAxis(delta, s.yaw, s);
                }
                default:
                    return 0;
            }
        }

        static double Smoothstep(double x) => x * x * (3 - 2 * x);
    }
}
