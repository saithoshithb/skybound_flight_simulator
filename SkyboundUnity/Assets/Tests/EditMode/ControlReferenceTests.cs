using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

/// <summary>
/// Runs the same inputs through the C# and the TypeScript sensor and control code and compares
/// the results, against Data/reference-control.json (recorded from the browser game's code).
/// The last test goes the whole way: controller lines in, aircraft state out.
/// </summary>
public class ControlReferenceTests
{
    const double Dt = 1.0 / 60;
    const double Tolerance = 1e-9;

#pragma warning disable 649
    [Serializable] class ParseCase { public string line; public bool ok; public string reason; public string kind; public double[] v; public string state; public string message; public string firmware; public string command; public string error; }
    [Serializable] class EncodeCase { public double[] v; public double[] @out; }
    [Serializable] class Values { public double[] v; }
    [Serializable] class FramerCase { public string[] chunks; public string[] lines; public int overflow; }
    [Serializable] class NeutralCase { public double[] v; public bool ok; public string error; public string detail; public double[] neutral; public double spread; }
    [Serializable] class PipelineFrame { public double[] @in; public double[] @out; }
    [Serializable] class PipelineCase { public double[] settings; public int yawMode; public bool[] invert; public double[] neutral; public PipelineFrame[] frames; }
    [Serializable] class LineEvent { public int f; public string l; }
    [Serializable] class LinkChange { public int f; public string link; public string device; }
    [Serializable] class TiltData { public LineEvent[] events; public LinkChange[] links; public double[] neutral; public Values[] controls; public Values[] stats; public FlightReferenceTests.FlightData flight; public string lastMalformedReason; }
    [Serializable] class FileData { public ParseCase[] parse; public EncodeCase[] encode; public Values[] seqGaps; public FramerCase[] framer; public NeutralCase[] neutrals; public PipelineCase[] pipelines; public TiltData tilt; }
#pragma warning restore 649

    static FileData file;

    static FileData Data
    {
        get
        {
            if (file == null)
            {
                var path = Path.Combine(Application.dataPath, "Tests/EditMode/Data/reference-control.json");
                file = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
            }
            return file;
        }
    }

    static string WireName(DeviceState s)
    {
        switch (s)
        {
            case DeviceState.Calibrating: return "calibrating";
            case DeviceState.Ready: return "ready";
            case DeviceState.SensorError: return "sensor_error";
            case DeviceState.MotionDuringCal: return "motion_during_cal";
            case DeviceState.CalFailed: return "cal_failed";
            default: return "unknown";
        }
    }

    [Test]
    public void ParseLineAgreesOnEveryLine()
    {
        Assert.That(Data.parse.Length, Is.GreaterThan(1500));
        var accepted = 0;
        foreach (var c in Data.parse)
        {
            var r = Protocol.ParseLine(c.line);
            Assert.That(r.ok, Is.EqualTo(c.ok), $"{c.line}\nreason: {r.reason}, expected: {c.reason}");
            if (!c.ok)
            {
                Assert.That(r.reason, Is.EqualTo(c.reason), c.line);
                continue;
            }
            accepted++;
            switch (r.message)
            {
                case ImuSample s:
                    Assert.That(c.kind, Is.EqualTo("imu"), c.line);
                    var actual = new[] { s.seq, s.deviceMs, s.pitch, s.roll, s.yaw, s.pitchRate, s.rollRate, s.yawRate, s.accelG };
                    for (var i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(c.v[i]).Within(Tolerance), c.line);
                    break;
                case StatusMessage s:
                    Assert.That(c.kind, Is.EqualTo("status"), c.line);
                    Assert.That(WireName(s.state), Is.EqualTo(c.state), c.line);
                    Assert.That(s.message, Is.EqualTo(c.message), c.line);
                    break;
                case HelloMessage s:
                    Assert.That(c.kind, Is.EqualTo("hello"), c.line);
                    Assert.That(s.firmware, Is.EqualTo(c.firmware), c.line);
                    Assert.That(s.proto, Is.EqualTo(c.v[0]), c.line);
                    Assert.That(s.rateHz, Is.EqualTo(c.v[1]), c.line);
                    break;
                case AckMessage s:
                    Assert.That(c.kind, Is.EqualTo("ack"), c.line);
                    Assert.That(s.command, Is.EqualTo(c.command), c.line);
                    Assert.That(s.error ?? "", Is.EqualTo(c.error), c.line);
                    break;
            }
        }
        Debug.Log($"[Reference] parseLine: {Data.parse.Length} lines agree, {accepted} accepted");
    }

    [Test]
    public void EncodeImuAgreesAfterRounding()
    {
        foreach (var c in Data.encode)
        {
            var line = Protocol.EncodeImu(new ImuSample
            {
                seq = c.v[0], deviceMs = c.v[1], pitch = c.v[2], roll = c.v[3], yaw = c.v[4],
                pitchRate = c.v[5], rollRate = c.v[6], yawRate = c.v[7], accelG = c.v[8],
            });
            var o = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(line);
            var actual = new[] { "s", "ms", "p", "r", "y", "pr", "rr", "yr", "a" }.Select(k => (double)o[k]).ToArray();
            for (var i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(c.@out[i]).Within(Tolerance), line);
        }
    }

    [Test]
    public void SeqGapAgrees()
    {
        foreach (var c in Data.seqGaps) Assert.That(Protocol.SeqGap(c.v[0], c.v[1]), Is.EqualTo(c.v[2]), $"{c.v[0]} → {c.v[1]}");
    }

    [Test]
    public void LineFramerAgreesOnRandomlyChunkedStreams()
    {
        foreach (var c in Data.framer)
        {
            var f = new LineFramer();
            var lines = c.chunks.SelectMany(chunk => f.Push(chunk)).ToArray();
            Assert.That(lines, Is.EqualTo(c.lines));
            Assert.That(f.overflowCount, Is.EqualTo(c.overflow));
        }
    }

    [Test]
    public void ComputeNeutralAgrees()
    {
        foreach (var c in Data.neutrals)
        {
            var samples = Enumerable.Range(0, c.v.Length / 3).Select(i => new ImuSample { pitch = c.v[i * 3], roll = c.v[i * 3 + 1], yaw = c.v[i * 3 + 2] }).ToList();
            var r = NeutralCalibration.ComputeNeutral(samples, 1234);
            Assert.That(r.ok, Is.EqualTo(c.ok), c.detail);
            if (!c.ok)
            {
                var expected = c.error == "no_data" ? NeutralError.NoData : c.error == "moving" ? NeutralError.Moving : NeutralError.TooTilted;
                Assert.That(r.error, Is.EqualTo(expected));
                Assert.That(r.detail, Is.EqualTo(c.detail));
                continue;
            }
            Assert.That(r.neutral.pitch, Is.EqualTo(c.neutral[0]).Within(Tolerance));
            Assert.That(r.neutral.roll, Is.EqualTo(c.neutral[1]).Within(Tolerance));
            Assert.That(r.neutral.yaw, Is.EqualTo(c.neutral[2]).Within(Tolerance));
            Assert.That(r.neutral.capturedAt, Is.EqualTo(c.neutral[3]));
            Assert.That(r.spreadDeg, Is.EqualTo(c.spread).Within(Tolerance));
        }
    }

    [Test]
    public void ControlPipelineAgreesFrameByFrame()
    {
        double worst = 0;
        foreach (var c in Data.pipelines)
        {
            var v = c.settings;
            var s = new ControlSettings
            {
                pitch = new AxisSettings(v[0], c.invert[0]),
                roll = new AxisSettings(v[1], c.invert[1]),
                yaw = new AxisSettings(v[2], c.invert[2]),
                yawMode = c.yawMode == 0 ? YawMode.Off : c.yawMode == 1 ? YawMode.Washout : YawMode.Rate,
                yawFullRateDps = v[3], yawWashoutS = v[4], sensitivity = v[5], deadzone = v[6], expo = v[7], smoothingMs = v[8], slewPerSec = v[9],
            };
            var neutral = new Neutral { pitch = c.neutral[0], roll = c.neutral[1], yaw = c.neutral[2], capturedAt = c.neutral[3] };
            var p = new ControlPipeline();
            for (var f = 0; f < c.frames.Length; f++)
            {
                var i = c.frames[f].@in;
                if (i[6] == 1) p.Reengage();
                var sample = i[0] == 1 ? new ImuSample { pitch = i[1], roll = i[2], yaw = i[3], yawRate = i[4] } : null;
                var o = p.Update(sample, neutral, s, i[5]);
                var actual = new[] { o.elevator, o.aileron, o.rudder, o.engagement, p.targetElevator, p.targetAileron, p.targetRudder };
                for (var k = 0; k < actual.Length; k++)
                {
                    var diff = Math.Abs(actual[k] - c.frames[f].@out[k]);
                    worst = Math.Max(worst, diff);
                    Assert.That(diff, Is.LessThan(Tolerance), $"frame {f}, value {k}");
                }
            }
        }
        Debug.Log($"[Reference] pipeline: {Data.pipelines.Length} runs, worst difference {worst:E2}");
    }

    /// <summary>
    /// A 60 s flight flown by tilting: calibration, recenter, take-off, turns, a dropout, a
    /// recalibration, lost samples, a bad line and a board restart. Same order as a Unity frame.
    /// </summary>
    [Test]
    public void TiltFlightMatchesFromTheWireToTheAircraft()
    {
        var reference = Data.tilt;
        double now = 0;
        var hub = new SensorHub(() => now);
        var transport = new FakeTransport();
        var pipeline = new ControlPipeline();
        var neutral = Neutral.ZERO_NEUTRAL;
        var s = new ControlSettings();
        var frame = -1;
        var links = new System.Collections.Generic.List<LinkChange>();
        hub.Subscribe(st =>
        {
            ControlLoop.ReengageOnReconnect(st, pipeline);
            var name = st.link.ToString().ToLowerInvariant();
            var device = WireName(st.device);
            var last = links.LastOrDefault();
            if (last == null || last.link != name || last.device != device) links.Add(new LinkChange { f = frame, link = name, device = device });
        });
        hub.Connect(transport);

        var m = new FlightModel(AircraftCatalog.Trainer);
        var easy = new EasyFlight();
        easy.Configure(AircraftCatalog.Trainer);
        easy.Reset(false);
        var input = new PilotInput();
        var cmd = new EasyCommand();
        var controls = new ControlCommand();
        var captured = new System.Collections.Generic.List<ImuSample>();
        Action off = null;
        var next = 0;
        var checks = 0;

        FlightReferenceTests.Compare(reference.flight, m, input, 60 * 60,
            before: f =>
            {
                frame = f;
                now = f * 1000.0 / 60;
                while (next < reference.events.Length && reference.events[next].f <= f) transport.Line(reference.events[next++].l);
                hub.Update();
                if (f == 180) off = hub.OnSample(x => captured.Add(x.sample));
                if (f == 240)
                {
                    off();
                    var r = NeutralCalibration.ComputeNeutral(captured, 1);
                    Assert.That(r.ok, Is.True, r.detail);
                    neutral = r.neutral;
                    pipeline.Reengage();
                }
                if (f == 300) easy.AdjustSpeed(1, Dt, true);
                ControlLoop.Frame(hub, pipeline, neutral, s, Dt, controls);
                cmd.pitch = Clamp(controls.elevator, -1, 1);
                cmd.roll = Clamp(controls.aileron, -1, 1);
                var o = easy.Update(m.telemetry, cmd, Dt, m.alphaWarn / DEG);
                input.elevator = o.elevator;
                input.aileron = o.aileron;
                input.rudder = o.rudder;
                input.throttle = o.throttle;
                input.trim = 0;
            },
            each: f =>
            {
                if ((f + 1) % 30 != 0) return;
                var expected = reference.controls[checks].v;
                Assert.That(controls.elevator, Is.EqualTo(expected[0]).Within(Tolerance), $"elevator, frame {f}");
                Assert.That(controls.aileron, Is.EqualTo(expected[1]).Within(Tolerance), $"aileron, frame {f}");
                Assert.That(controls.rudder, Is.EqualTo(expected[2]).Within(Tolerance), $"rudder, frame {f}");
                Assert.That(controls.engagement, Is.EqualTo(expected[3]).Within(Tolerance), $"engagement, frame {f}");
                var st = hub.GetStats();
                var stats = reference.stats[checks].v;
                Assert.That(st.rxHz, Is.EqualTo(stats[0]), $"rxHz, frame {f}");
                Assert.That(double.IsInfinity(st.lastSampleAgeMs) ? -1 : st.lastSampleAgeMs, Is.EqualTo(stats[1]).Within(1e-6), $"age, frame {f}");
                Assert.That(st.samples, Is.EqualTo(stats[2]), $"samples, frame {f}");
                Assert.That(st.droppedSeq, Is.EqualTo(stats[3]), $"dropped, frame {f}");
                Assert.That(st.malformed, Is.EqualTo(stats[4]), $"malformed, frame {f}");
                Assert.That(st.restarts, Is.EqualTo(stats[5]), $"restarts, frame {f}");
                checks++;
            });

        frame = 60 * 60;
        hub.Disconnect();
        Assert.That(checks, Is.EqualTo(reference.controls.Length));
        Assert.That(neutral.pitch, Is.EqualTo(reference.neutral[0]).Within(Tolerance));
        Assert.That(neutral.roll, Is.EqualTo(reference.neutral[1]).Within(Tolerance));
        Assert.That(neutral.yaw, Is.EqualTo(reference.neutral[2]).Within(Tolerance));
        Assert.That(links.Select(l => $"{l.f} {l.link} {l.device}"), Is.EqualTo(reference.links.Select(l => $"{l.f} {l.link} {l.device}")));
        Assert.That(hub.GetStats().lastMalformedReason, Is.EqualTo(reference.lastMalformedReason));
    }
}
