using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Skybound
{
    /// <summary>The controller pose the pilot calls "hands-off". Degrees, aircraft convention.</summary>
    [Serializable]
    public class Neutral
    {
        public double pitch;
        public double roll;
        public double yaw;
        public double capturedAt;

        public static readonly Neutral ZERO_NEUTRAL = new Neutral();
    }

    public enum NeutralError
    {
        None,
        NoData,
        Moving,
        TooTilted,
        LinkLost,
    }

    public class NeutralResult
    {
        public bool ok;
        public Neutral neutral;
        public double spreadDeg;
        public NeutralError error;
        public string detail;
    }

    public static class NeutralCalibration
    {
        public const double NEUTRAL_CAPTURE_MS = 1000;
        public const int NEUTRAL_MIN_SAMPLES = 20;
        /// <summary>Max standard deviation (deg) of pitch/roll while capturing; more means the hand moved.</summary>
        public const double NEUTRAL_MAX_SPREAD_DEG = 0.8;
        /// <summary>Neutral beyond this tilt is almost certainly a mistake (controller on its side).</summary>
        public const double NEUTRAL_MAX_TILT_DEG = 45;

        const double D2R = Math.PI / 180;

        /// <summary>Angle-safe mean: roll and yaw can straddle ±180°.</summary>
        static double CircularMean(List<double> deg)
        {
            double s = 0;
            double c = 0;
            foreach (var d in deg)
            {
                s += Math.Sin(d * D2R);
                c += Math.Cos(d * D2R);
            }
            return Math.Atan2(s, c) / D2R;
        }

        static double Spread(List<double> deg, double mean)
        {
            double sum = 0;
            foreach (var d in deg)
            {
                var diff = ((d - mean + 180) % 360 + 360) % 360 - 180;
                sum += diff * diff;
            }
            return Math.Sqrt(sum / deg.Count);
        }

        static NeutralResult Fail(NeutralError error, string detail) => new NeutralResult { ok = false, error = error, detail = detail };

        /// <param name="capturedAt">Leave out for the time now, in ms since 1970.</param>
        public static NeutralResult ComputeNeutral(IReadOnlyList<ImuSample> samples, double? capturedAt = null)
        {
            if (samples.Count < NEUTRAL_MIN_SAMPLES)
                return Fail(NeutralError.NoData, $"Only {samples.Count} samples arrived; need {NEUTRAL_MIN_SAMPLES}.");
            var pitches = samples.Select(s => s.pitch).ToList();
            var rolls = samples.Select(s => s.roll).ToList();
            var pitch = pitches.Sum() / pitches.Count;
            var roll = CircularMean(rolls);
            var yaw = CircularMean(samples.Select(s => s.yaw).ToList());
            var worst = Math.Max(Spread(pitches, pitch), Spread(rolls, roll));
            if (worst > NEUTRAL_MAX_SPREAD_DEG)
                return Fail(NeutralError.Moving, $"Controller moved during capture (±{MiniJson.JsToFixed(worst, 1)}°). Hold it still and retry.");
            if (Math.Abs(pitch) > NEUTRAL_MAX_TILT_DEG || Math.Abs(roll) > NEUTRAL_MAX_TILT_DEG)
                return Fail(NeutralError.TooTilted, $"Neutral pose is tilted {MiniJson.JsToFixed(pitch, 0)}° pitch / {MiniJson.JsToFixed(roll, 0)}° roll. Hold it near level, or check the mounting.");
            return new NeutralResult
            {
                ok = true,
                neutral = new Neutral { pitch = pitch, roll = roll, yaw = yaw, capturedAt = capturedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                spreadDeg = worst,
            };
        }

        /// <summary>
        /// Collect samples from the hub for `durationMs`, then validate them. A coroutine,
        /// where the browser game returns a Promise: run it with StartCoroutine, or
        /// `yield return` it from another one, and the result comes through `done`.
        /// </summary>
        public static IEnumerator CaptureNeutral(SensorHub hub, Action<NeutralResult> done, double durationMs = NEUTRAL_CAPTURE_MS)
        {
            var samples = new List<ImuSample>();
            var off = hub.OnSample(s => samples.Add(s.sample));
            var endsAt = hub.Now() + durationMs;
            try
            {
                while (hub.Now() < endsAt) yield return null;
            }
            finally
            {
                off();
            }
            if (!hub.IsUsable())
            {
                done(Fail(NeutralError.LinkLost, "Sensor link dropped during capture."));
                yield break;
            }
            done(ComputeNeutral(samples));
        }
    }
}
