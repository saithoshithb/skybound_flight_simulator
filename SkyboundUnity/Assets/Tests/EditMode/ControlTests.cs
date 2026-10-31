using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Skybound;

public class ControlTests
{
    static readonly ControlSettings S = new ControlSettings();

    static ImuSample Sample(double pitch = 0, double roll = 0, double yaw = 0) => new ImuSample { pitch = pitch, roll = roll, yaw = yaw, accelG = 1 };

    static ControlCommand Run(ControlPipeline p, ImuSample s, double seconds, Neutral neutral = null, ControlSettings settings = null)
    {
        neutral = neutral ?? Neutral.ZERO_NEUTRAL;
        settings = settings ?? S;
        var o = p.Update(s, neutral, settings, 0);
        for (double t = 0; t < seconds; t += 1.0 / 60) o = p.Update(s, neutral, settings, 1.0 / 60);
        return o;
    }

    [Test]
    public void DeadZoneIsContinuousAndStillReachesFullScale()
    {
        Assert.That(ControlMapping.ApplyDeadzone(0.05, 0.1), Is.EqualTo(0));
        Assert.That(ControlMapping.ApplyDeadzone(0.1001, 0.1), Is.EqualTo(0).Within(1e-3));
        Assert.That(ControlMapping.ApplyDeadzone(1, 0.1), Is.EqualTo(1));
        Assert.That(ControlMapping.ApplyDeadzone(-1, 0.1), Is.EqualTo(-1));
    }

    [Test]
    public void MapsMaxAngleToFullDeflectionRespectsSignAndInversion()
    {
        Assert.That(ControlMapping.ShapeAxis(30, S.pitch, S), Is.EqualTo(1).Within(0.01));
        Assert.That(ControlMapping.ShapeAxis(-60, S.pitch, S), Is.EqualTo(-1));
        Assert.That(ControlMapping.ShapeAxis(15, S.pitch, S), Is.GreaterThan(0));
        Assert.That(ControlMapping.ShapeAxis(15, new AxisSettings(30, true), S), Is.LessThan(0));
        Assert.That(ControlMapping.ShapeAxis(1, S.pitch, S), Is.EqualTo(0)); // inside dead zone
    }

    [Test]
    public void EasyFlightTiltCurveIgnoresTremorAndIsProgressive()
    {
        Assert.That(ControlMapping.ShapeAxis(2, S.roll, S), Is.EqualTo(0)); // hand tremor
        var small = ControlMapping.ShapeAxis(8, S.roll, S);
        var medium = ControlMapping.ShapeAxis(18, S.roll, S);
        Assert.That(small, Is.GreaterThan(0));
        Assert.That(small, Is.LessThan(0.12)); // ≈ 3–4° of bank with the 35° limit
        Assert.That(medium, Is.GreaterThan(small * 3)); // progressive, not linear
        Assert.That(ControlMapping.ShapeAxis(35, S.roll, S), Is.EqualTo(1).Within(0.01));
    }

    [Test]
    public void RampsInInsteadOfJumpingOnFirstSample()
    {
        var p = new ControlPipeline();
        var first = p.Update(Sample(30), Neutral.ZERO_NEUTRAL, S, 1.0 / 60);
        Assert.That(first.elevator, Is.LessThan(0.01));
        var later = Run(p, Sample(30), 2);
        Assert.That(later.engagement, Is.EqualTo(1));
        Assert.That(later.elevator, Is.EqualTo(1).Within(0.01));
    }

    [Test]
    public void NeverChangesFasterThanTheSlewLimit()
    {
        var p = new ControlPipeline();
        Run(p, Sample(), 2);
        var prev = p.Update(Sample(0, -40), Neutral.ZERO_NEUTRAL, S, 1.0 / 60).aileron;
        for (var i = 0; i < 60; i++)
        {
            var a = p.Update(Sample(0, -40), Neutral.ZERO_NEUTRAL, S, 1.0 / 60).aileron;
            Assert.That(Math.Abs(a - prev), Is.LessThanOrEqualTo(S.slewPerSec / 60 + 1e-9));
            prev = a;
        }
    }

    [Test]
    public void FadesToNeutralWhenTheLinkDrops()
    {
        var p = new ControlPipeline();
        Run(p, Sample(30), 2);
        var o = Run(p, null, 1);
        Assert.That(o.engagement, Is.EqualTo(0));
        Assert.That(o.elevator, Is.EqualTo(0));
    }

    [Test]
    public void SubtractsNeutralWrappingRollAcross180()
    {
        var p = new ControlPipeline();
        var n = new Neutral { pitch = 5, roll = 179, yaw = 0, capturedAt = 1 };
        var o = Run(p, Sample(5, -179), 2, n);
        Assert.That(o.elevator, Is.EqualTo(0));
        Assert.That(o.aileron, Is.EqualTo(0)); // 2° apart, inside dead zone
    }

    [Test]
    public void WashoutYawDecaysAHeldTwistToZero()
    {
        var p = new ControlPipeline();
        var s = new ControlSettings { yawMode = YawMode.Washout, yawWashoutS = 1 };
        var n = new Neutral { capturedAt = 1 };
        var o = p.Update(Sample(), n, s, 0);
        for (var i = 0; i < 90; i++) o = p.Update(Sample(), n, s, 1.0 / 60);
        for (var i = 0; i < 20; i++) o = p.Update(Sample(0, 0, 30), n, s, 1.0 / 60);
        Assert.That(o.rudder, Is.GreaterThan(0.3));
        for (var i = 0; i < 600; i++) o = p.Update(Sample(0, 0, 30), n, s, 1.0 / 60);
        Assert.That(Math.Abs(o.rudder), Is.LessThan(0.05));
    }

    static List<ImuSample> Still(int n, double p = 2, double r = -1)
    {
        return Enumerable.Range(0, n).Select(i => Sample(p + (i % 2) * 0.1, r)).ToList();
    }

    [Test]
    public void AveragesAStillCapture()
    {
        var r = NeutralCalibration.ComputeNeutral(Still(50));
        Assert.That(r.ok, Is.True);
        Assert.That(r.neutral.pitch, Is.EqualTo(2.05).Within(0.005));
        Assert.That(r.neutral.capturedAt, Is.GreaterThan(1.7e12)); // ms since 1970, set to now
    }

    [Test]
    public void AveragesRollAcrossTheSeam()
    {
        var samples = Enumerable.Range(0, 40).Select(i => Sample(0, i % 2 == 1 ? 179.5 : -179.5)).ToList();
        var r = NeutralCalibration.ComputeNeutral(samples, 0);
        // Too tilted to accept, but the mean must be ~180, not ~0.
        Assert.That(r.ok, Is.False);
        Assert.That(r.error, Is.EqualTo(NeutralError.TooTilted));
    }

    [Test]
    public void RejectsMovementMissingDataAndExtremeTilt()
    {
        var moving = Enumerable.Range(0, 50).Select(i => Sample(i * 0.5)).ToList();
        Assert.That(NeutralCalibration.ComputeNeutral(moving).error, Is.EqualTo(NeutralError.Moving));
        Assert.That(NeutralCalibration.ComputeNeutral(Still(5)).error, Is.EqualTo(NeutralError.NoData));
        Assert.That(NeutralCalibration.ComputeNeutral(Still(50, 60)).error, Is.EqualTo(NeutralError.TooTilted));
    }

    // ---------- captureNeutral, the coroutine that replaces a Promise ----------

    static string Imu(double seq, double pitch) => Protocol.EncodeImu(new ImuSample { seq = seq, pitch = pitch, accelG = 1 });

    class Capture
    {
        public double now;
        public SensorHub hub;
        public FakeTransport t = new FakeTransport();
        public NeutralResult result;
        public IEnumerator routine;

        public Capture()
        {
            hub = new SensorHub(() => now);
            hub.Connect(t);
            t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
            t.Line(Imu(0, 3));
            routine = NeutralCalibration.CaptureNeutral(hub, r => result = r);
        }

        /// <summary>Feeds 50 Hz samples while running the coroutine frame by frame.</summary>
        public void Run(double ms, double pitch = 3, int stopAfterSamples = int.MaxValue)
        {
            var seq = 1;
            for (double t0 = 0; t0 < ms; t0 += 20)
            {
                now += 20;
                if (seq <= stopAfterSamples) t.Line(Imu(seq++, pitch));
                hub.Update();
                if (!routine.MoveNext()) return;
            }
        }
    }

    [Test]
    public void CaptureCollectsForASecondThenReportsTheNeutral()
    {
        var c = new Capture();
        c.Run(500);
        Assert.That(c.result, Is.Null, "finished early");
        c.Run(600);
        Assert.That(c.result.ok, Is.True, c.result.detail);
        Assert.That(c.result.neutral.pitch, Is.EqualTo(3).Within(1e-9));
    }

    [Test]
    public void CaptureReportsALostLink()
    {
        var c = new Capture();
        c.Run(1100, stopAfterSamples: 20);
        Assert.That(c.result.ok, Is.False);
        Assert.That(c.result.error, Is.EqualTo(NeutralError.LinkLost));
        Assert.That(c.result.detail, Is.EqualTo("Sensor link dropped during capture."));
    }

    [Test]
    public void CaptureStopsListeningWhenItEnds()
    {
        var c = new Capture();
        c.Run(1100);
        var result = c.result;
        c.t.Line(Imu(999, 40));
        Assert.That(c.result, Is.SameAs(result));
        Assert.That(c.hub.Latest().sample.pitch, Is.EqualTo(40));
    }
}
