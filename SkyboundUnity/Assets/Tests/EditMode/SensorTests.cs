using System;
using System.Collections.Generic;
using NUnit.Framework;
using Skybound;

/// <summary>A transport the test feeds by hand, as in the browser game's sensor.test.ts.</summary>
class FakeTransport : ISensorTransport
{
    public TransportEvents events;
    public readonly List<string> sent = new List<string>();
    public TransportKind Kind => TransportKind.Simulated;
    public string Label => "fake";
    public void Open(TransportEvents e)
    {
        events = e;
        e.onOpened();
    }
    public void Close() => events?.onClose(null);
    public void Send(string line) => sent.Add(line);
    public void Pump() { }
    public void Line(string line) => events?.onLine(line);
}

public class SensorTests
{
    // The exact line format the firmware's sendImu() printf produces.
    const string FirmwareLine = "{\"t\":\"imu\",\"s\":42,\"ms\":12345,\"p\":-3.25,\"r\":10.50,\"y\":-170.00,\"pr\":1.5,\"rr\":-2.0,\"yr\":0.3,\"a\":1.002}";

    static string Imu(double seq, double pitch = 0)
    {
        return Protocol.EncodeImu(new ImuSample { seq = seq, deviceMs = seq * 20, pitch = pitch, accelG = 1 });
    }

    [Test]
    public void ParsesAFirmwareImuLine()
    {
        var r = Protocol.ParseLine(FirmwareLine);
        Assert.That(r.ok, Is.True);
        var s = (ImuSample)r.message;
        Assert.That(s.seq, Is.EqualTo(42));
        Assert.That(s.deviceMs, Is.EqualTo(12345));
        Assert.That(s.pitch, Is.EqualTo(-3.25));
        Assert.That(s.roll, Is.EqualTo(10.5));
        Assert.That(s.yaw, Is.EqualTo(-170));
        Assert.That(s.pitchRate, Is.EqualTo(1.5));
        Assert.That(s.rollRate, Is.EqualTo(-2));
        Assert.That(s.yawRate, Is.EqualTo(0.3));
        Assert.That(s.accelG, Is.EqualTo(1.002));
    }

    [Test]
    public void ParsesStatusHelloAndAck()
    {
        Assert.That(((StatusMessage)Protocol.ParseLine("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"ok\"}").message).state, Is.EqualTo(DeviceState.Ready));
        var hello = (HelloMessage)Protocol.ParseLine("{\"t\":\"hello\",\"fw\":\"mpu-flight/1.0.0\",\"proto\":1,\"rate\":50}").message;
        Assert.That(hello.firmware, Is.EqualTo("mpu-flight/1.0.0"));
        Assert.That(hello.rateHz, Is.EqualTo(50));
        Assert.That(((AckMessage)Protocol.ParseLine("{\"t\":\"ack\",\"cmd\":\"?\",\"err\":\"unknown command\"}").message).error, Is.EqualTo("unknown command"));
    }

    [TestCase("", "empty line")]
    [TestCase("garbage", "not a JSON object")]
    [TestCase("{\"t\":\"imu\",\"s\":1", "invalid JSON")]
    [TestCase("[1,2]", "not a JSON object")]
    [TestCase("{\"t\":\"nope\"}", "unknown message type")]
    [TestCase("{\"t\":\"status\",\"state\":\"flying\"}", "unknown status")]
    public void RejectsBadLines(string line, string reason)
    {
        var r = Protocol.ParseLine(line);
        Assert.That(r.ok, Is.False);
        Assert.That(r.reason, Does.Contain(reason));
    }

    [Test]
    public void RejectsOutOfRangeAndNonNumericFields()
    {
        Assert.That(Protocol.ParseLine(FirmwareLine.Replace("\"p\":-3.25", "\"p\":95")).ok, Is.False);
        Assert.That(Protocol.ParseLine(FirmwareLine.Replace("\"a\":1.002", "\"a\":\"1\"")).ok, Is.False);
        Assert.That(Protocol.ParseLine(FirmwareLine.Replace("\"s\":42", "\"s\":4.2")).ok, Is.False);
        Assert.That(Protocol.ParseLine(FirmwareLine.Replace(",\"yr\":0.3", "")).ok, Is.False);
    }

    [Test]
    public void RoundTripsEncodeImu()
    {
        var s = (ImuSample)Protocol.ParseLine(Protocol.EncodeImu(new ImuSample { seq = 7, deviceMs = 1, pitch = 1.234, roll = -5, accelG = 1 })).message;
        Assert.That(s.seq, Is.EqualTo(7));
        Assert.That(s.pitch, Is.EqualTo(1.23));
    }

    [Test]
    public void SeqGapCountsLossesAndHandlesWrapAndRestarts()
    {
        Assert.That(Protocol.SeqGap(1, 2), Is.EqualTo(0));
        Assert.That(Protocol.SeqGap(1, 5), Is.EqualTo(3));
        Assert.That(Protocol.SeqGap(0xffffffffL, 0), Is.EqualTo(0));
        Assert.That(Protocol.SeqGap(0xfffffffeL, 1), Is.EqualTo(2));
        Assert.That(Protocol.SeqGap(5000, 3), Is.EqualTo(0)); // device rebooted
    }

    [Test]
    public void FramerReassemblesLinesSplitAcrossChunks()
    {
        var f = new LineFramer();
        Assert.That(f.Push("{\"a\":1}\n{\"b\""), Is.EqualTo(new[] { "{\"a\":1}" }));
        Assert.That(f.Push(":2}\r\n\n"), Is.EqualTo(new[] { "{\"b\":2}", "" }));
    }

    [Test]
    public void FramerDiscardsOverlongLinesWithoutGrowing()
    {
        var f = new LineFramer();
        Assert.That(f.Push(new string('x', 600)), Is.Empty);
        Assert.That(f.Push(new string('y', 600)), Is.Empty);
        Assert.That(f.Push("tail\nok\n"), Is.EqualTo(new[] { "ok" }));
        Assert.That(f.overflowCount, Is.EqualTo(1));
    }

    [Test]
    public void ALoneSurrogateEscapeIsKeptAsItIs()
    {
        Assert.That(((AckMessage)Protocol.ParseLine("{\"t\":\"ack\",\"cmd\":\"\\ud83d\"}").message).command, Is.EqualTo(new string((char)0xD83D, 1)));
    }

    [Test]
    public void JsNumberPrintsLikeJavaScript()
    {
        Assert.That(MiniJson.JsNumber(95), Is.EqualTo("95"));
        Assert.That(MiniJson.JsNumber(-0.0), Is.EqualTo("0"));
        Assert.That(MiniJson.JsNumber(0.1), Is.EqualTo("0.1"));
        Assert.That(MiniJson.JsNumber(4294967295), Is.EqualTo("4294967295"));
        Assert.That(MiniJson.JsNumber(1e21), Is.EqualTo("1e+21"));
        Assert.That(MiniJson.JsNumber(1e20), Is.EqualTo("100000000000000000000"));
        Assert.That(MiniJson.JsNumber(0.000001), Is.EqualTo("0.000001"));
        Assert.That(MiniJson.JsNumber(1.5e-7), Is.EqualTo("1.5e-7"));
        Assert.That(MiniJson.JsNumber(-90.5), Is.EqualTo("-90.5"));
    }

    // ---------- SensorHub ----------

    double now;
    SensorHub hub;
    FakeTransport t;

    [SetUp]
    public void SetUpHub()
    {
        now = 0;
        hub = new SensorHub(() => now);
        t = new FakeTransport();
        hub.Connect(t);
    }

    void Advance(double ms)
    {
        now += ms;
        hub.Update();
    }

    [Test]
    public void WaitsForCalibrationBeforeGoingLive()
    {
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
        t.Line("{\"t\":\"status\",\"state\":\"calibrating\",\"msg\":\"\"}");
        t.Line(Imu(0));
        Assert.That(hub.Latest(), Is.Null);
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(1, 5));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Live));
        Assert.That(hub.IsUsable(), Is.True);
        Assert.That(hub.Latest().sample.pitch, Is.EqualTo(5));
    }

    [Test]
    public void AFallbackCalibrationStillFlies()
    {
        t.Line("{\"t\":\"status\",\"state\":\"cal_failed\",\"msg\":\"no fully still sample\"}");
        t.Line(Imu(0, 5));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Live));
        Assert.That(hub.IsUsable(), Is.True);
        Assert.That(hub.GetStatus().Usable, Is.True);
    }

    [Test]
    public void GoesStaleThenLostWhenSamplesStopAndRecovers()
    {
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(0));
        Advance(300);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Stale));
        Assert.That(hub.IsUsable(), Is.False);
        Advance(2000);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Lost));
        t.Line(Imu(1));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Live));
    }

    [Test]
    public void CountsMalformedLinesAndSequenceGaps()
    {
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(0));
        t.Line("{\"t\":\"imu\",\"s\":");
        t.Line(Imu(4));
        var st = hub.GetStats();
        Assert.That(st.malformed, Is.EqualTo(1));
        Assert.That(st.droppedSeq, Is.EqualTo(3));
    }

    [Test]
    public void DropsToDisconnectedWhenTheTransportDies()
    {
        t.events.onClose("USB device was unplugged");
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("USB device was unplugged"));
        Assert.That(hub.Latest(), Is.Null);
    }

    [Test]
    public void FlagsASilentPort()
    {
        Advance(4100);
        Assert.That(hub.GetStatus().lastError, Does.Contain("No data received after 4 s"));
    }

    [Test]
    public void StopsUsingDataWhenTheDeviceRecalibrates()
    {
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(0));
        t.Line("{\"t\":\"status\",\"state\":\"calibrating\",\"msg\":\"\"}");
        Assert.That(hub.IsUsable(), Is.False);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
    }

    [Test]
    public void AHelloMidStreamCountsAsARestartAndAWrongProtocolIsReported()
    {
        t.Line("{\"t\":\"hello\",\"fw\":\"a\",\"proto\":1,\"rate\":50}");
        Assert.That(hub.GetStats().restarts, Is.EqualTo(0));
        t.Line("{\"t\":\"hello\",\"fw\":\"a\",\"proto\":2,\"rate\":50}");
        Assert.That(hub.GetStats().restarts, Is.EqualTo(1));
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("Firmware speaks protocol v2; this app expects v1."));
    }

    [Test]
    public void LinesFromAnOldTransportAreIgnored()
    {
        var old = t;
        var fresh = new FakeTransport();
        hub.Connect(fresh);
        old.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        old.events.onClose("gone");
        Assert.That(hub.GetStatus().device, Is.EqualTo(DeviceState.Unknown));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
    }

    [Test]
    public void ListenersStopWhenUnsubscribedAndStatusOnlyFiresOnChange()
    {
        var samples = 0;
        var statuses = 0;
        var offSample = hub.OnSample(_ => samples++);
        hub.Subscribe(_ => statuses++);
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(0));
        var seen = statuses;
        t.Line(Imu(1));
        Assert.That(samples, Is.EqualTo(2));
        Assert.That(statuses, Is.EqualTo(seen));
        offSample();
        t.Line(Imu(2));
        Assert.That(samples, Is.EqualTo(2));
    }

    [Test]
    public void SendWithNoTransportThrows()
    {
        hub.Send(Protocol.CMD_HELLO);
        Assert.That(t.sent, Is.EqualTo(new[] { "HELLO" }));
        hub.Disconnect();
        Assert.Throws<InvalidOperationException>(() => hub.Send(Protocol.CMD_HELLO));
    }

    [Test]
    public void KeepsAShortLogOfRecentLines()
    {
        for (var i = 0; i < 50; i++) t.Line("x" + i);
        var lines = hub.RecentLines();
        Assert.That(lines.Length, Is.EqualTo(40));
        Assert.That(lines[39], Is.EqualTo("x49"));
    }

    [Test]
    public void ALongFrameWithSamplesQueuedIsNotADropout()
    {
        t.Line("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}");
        t.Line(Imu(0));
        now += 1000; // a hitch; the sample below was already waiting
        t.Line(Imu(1));
        hub.Update();
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Live));
    }
}
