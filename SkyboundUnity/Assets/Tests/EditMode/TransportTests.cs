using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Skybound;
using Debug = UnityEngine.Debug;

/// <summary>A serial port with nothing behind it. The tests push bytes in and read what was written.</summary>
class FakePort : ISerialPort
{
    public readonly ConcurrentQueue<byte[]> incoming = new ConcurrentQueue<byte[]>();
    public readonly ConcurrentQueue<string> written = new ConcurrentQueue<string>();
    public Exception openError;
    public int openDelayMs;
    public volatile Exception readError;
    public volatile bool timeOutOnce;
    public volatile bool isOpen;
    public volatile bool closed;
    byte[] current;
    int offset;

    public void Open()
    {
        if (openDelayMs > 0) Thread.Sleep(openDelayMs);
        if (openError != null) throw openError;
        isOpen = true;
    }

    public void Close()
    {
        isOpen = false;
        closed = true;
    }

    public int BytesToRead
    {
        get
        {
            if (readError != null) throw readError;
            if (timeOutOnce)
            {
                timeOutOnce = false;
                throw new TimeoutException();
            }
            if (current == null || offset >= current.Length)
            {
                if (!incoming.TryDequeue(out current)) return 0;
                offset = 0;
            }
            return current.Length - offset;
        }
    }

    public int Read(byte[] buffer, int at, int count)
    {
        var n = Math.Min(count, current.Length - offset);
        Array.Copy(current, offset, buffer, at, n);
        offset += n;
        return n;
    }

    public void Write(byte[] buffer, int at, int count) => written.Enqueue(Encoding.UTF8.GetString(buffer, at, count));

    public void Say(string text) => incoming.Enqueue(Encoding.UTF8.GetBytes(text));
}

public class SerialTransportTests
{
    const string Ready = "{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"\"}\n";
    const string Imu = "{\"t\":\"imu\",\"s\":1,\"ms\":2,\"p\":3.5,\"r\":0,\"y\":0,\"pr\":0,\"rr\":0,\"yr\":0,\"a\":1}\n";

    FakePort port;
    SensorHub hub;
    SerialTransport transport;

    [SetUp]
    public void Create()
    {
        port = new FakePort();
        hub = new SensorHub();
        transport = new SerialTransport("COM9", _ => port);
    }

    [TearDown]
    public void Close() => hub.Disconnect();

    /// <summary>Runs frames until the condition holds, as the game would, on this thread.</summary>
    void Frames(Func<bool> until, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!until())
        {
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000), "Timed out waiting for: " + what);
            hub.Update();
            Thread.Sleep(1);
        }
    }

    [Test]
    public void OpensReadsSplitLinesAndGoesLiveOnTheMainThread()
    {
        var sampleThread = -1;
        hub.OnSample(_ => sampleThread = Thread.CurrentThread.ManagedThreadId);
        hub.Connect(transport);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Connecting));
        Assert.That(hub.GetStatus().transportLabel, Is.EqualTo("USB serial COM9"));
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");

        port.Say(Ready + Imu.Substring(0, 20));
        port.Say(Imu.Substring(20));
        Frames(() => hub.GetStatus().link == LinkState.Live, "live");
        Assert.That(hub.IsUsable(), Is.True);
        Assert.That(hub.Latest().sample.pitch, Is.EqualTo(3.5));
        Assert.That(sampleThread, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
    }

    [Test]
    public void AnOpenPortThatIsBusyGivesTheHelpfulMessage()
    {
        port.openError = new UnauthorizedAccessException("Access to the port is denied.");
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Disconnected, "failure");
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("Could not open the port. Close Arduino Serial Monitor or any other app using it. (Access to the port is denied.)"));
        Assert.That(hub.ActiveTransport(), Is.Null);
    }

    [Test]
    public void AnyOtherOpenFailureIsPassedOnAsItIs()
    {
        port.openError = new IOException("The system cannot find the file specified.");
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Disconnected, "failure");
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("The system cannot find the file specified."));
    }

    [Test]
    public void AReadErrorMeansTheDeviceIsLost()
    {
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        port.readError = new IOException("A device attached to the system is not functioning.");
        Frames(() => hub.GetStatus().link == LinkState.Disconnected, "loss");
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("Serial device lost"));
        Frames(() => port.closed, "port closed");
    }

    [Test]
    public void ATimeoutIsNotAnError()
    {
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        port.timeOutOnce = true;
        Frames(() => !port.timeOutOnce, "timeout thrown");
        port.Say(Ready + Imu);
        Frames(() => hub.GetStatus().link == LinkState.Live, "live");
    }

    [Test]
    public void CommandsReachThePortWithANewline()
    {
        port.openDelayMs = 200;
        hub.Connect(transport);
        Assert.Throws<InvalidOperationException>(() => hub.Send(Protocol.CMD_CAL), "before it's open");
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        hub.Send(Protocol.CMD_CAL);
        Frames(() => port.written.Count == 1, "written");
        port.written.TryDequeue(out var text);
        Assert.That(text, Is.EqualTo("CAL\n"));
    }

    [Test]
    public void DisconnectClosesThePortAndStopsTheThread()
    {
        var threadsBefore = Process.GetCurrentProcess().Threads.Count;
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        hub.Disconnect();
        Assert.That(port.closed, Is.True, "still open after Disconnect");
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(hub.GetStatus().lastError, Is.Null);
        port.Say(Ready);
        hub.Update();
        Assert.That(hub.GetStatus().device, Is.EqualTo(DeviceState.Unknown));
        Debug.Log($"[Serial] threads before {threadsBefore}, after {Process.GetCurrentProcess().Threads.Count}");
    }

    [Test]
    public void TextSplitInsideACharacterDecodesCorrectly()
    {
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        var bytes = Encoding.UTF8.GetBytes("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"café\"}\n");
        var split = Array.IndexOf(bytes, (byte)0xC3) + 1;
        port.incoming.Enqueue(bytes.Take(split).ToArray());
        port.incoming.Enqueue(bytes.Skip(split).ToArray());
        Frames(() => hub.GetStatus().device == DeviceState.Ready, "status");
        Assert.That(hub.GetStatus().deviceMessage, Is.EqualTo("café"));
    }

    [Test]
    public void ASlowOpenDoesNotHoldUpTheGame()
    {
        port.openDelayMs = 800;
        var watch = Stopwatch.StartNew();
        hub.Connect(transport);
        hub.Update();
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Connecting));
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
    }

    [Test]
    public void DisconnectingDuringASlowOpenStillClosesThePortWhenItOpens()
    {
        port.openDelayMs = 700;
        hub.Connect(transport);
        var watch = Stopwatch.StartNew();
        hub.Disconnect();
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Frames(() => port.closed, "port closed");
        hub.Update();
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
    }

    [Test]
    public void IfTheGameStopsPumpingOnlyTheNewestLinesAreKept()
    {
        hub.Connect(transport);
        Frames(() => hub.GetStatus().link == LinkState.Awaiting, "port open");
        const int Lines = 2500;
        for (var i = 0; i < Lines; i++) port.Say("x\n");
        var watch = Stopwatch.StartNew();
        while (!port.incoming.IsEmpty && watch.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Thread.Sleep(20);
        hub.Update();
        var bad = hub.GetStats().malformed;
        Assert.That(bad, Is.GreaterThan(0));
        Assert.That(bad, Is.LessThanOrEqualTo(2000));
    }

    // ---------- the real System.IO.Ports; opens nothing that exists ----------

    [Test]
    public void TheRealPortListWorks()
    {
        var ports = SerialPorts.List();
        Debug.Log($"[Serial] ports here: {string.Join(", ", ports.Select(p => p.Display))}");
        Assert.That(ports.Select(p => p.name), Is.Unique);
        // USB first.
        Assert.That(ports.Select(p => p.bluetooth), Is.Ordered);
    }

    [Test]
    public void BluetoothPortsAreToldApartByTheirDriver()
    {
        Assert.That(SerialPorts.IsBluetoothDevice(@"\Device\BthModem0"), Is.True);
        Assert.That(SerialPorts.IsBluetoothDevice(@"\Device\Silabser0"), Is.False);
        Assert.That(SerialPorts.IsBluetoothDevice(@"\Device\VCP0"), Is.False);
        Assert.That(SerialPorts.IsBluetoothDevice(@"\Device\USBSER000"), Is.False);
    }

    [Test]
    public void OpeningAPortThatDoesNotExistFailsCleanly()
    {
        const string Missing = "COM250";
        if (SerialPorts.List().Any(p => p.name == Missing)) Assert.Ignore("This machine has a " + Missing);
        var real = new SensorHub();
        real.Connect(new SerialTransport(Missing));
        var watch = Stopwatch.StartNew();
        while (real.GetStatus().link != LinkState.Disconnected)
        {
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000));
            real.Update();
            Thread.Sleep(1);
        }
        Debug.Log($"[Serial] opening {Missing}: {real.GetStatus().lastError}");
        Assert.That(real.GetStatus().lastError, Is.Not.Null.And.Not.Empty);
    }
}

public class SerialSessionTests
{
    double now;
    string[] present;
    FakePort port;
    int opens;
    SensorHub hub;
    SerialSession session;

    [SetUp]
    public void Create()
    {
        now = 0;
        present = new[] { "COM5" };
        opens = 0;
        hub = new SensorHub(() => now);
        session = new SerialSession(hub,
            () => present.Select(p => new SerialPortInfo { name = p }).ToArray(),
            _ =>
            {
                opens++;
                port = new FakePort();
                return port;
            });
    }

    [TearDown]
    public void Close() => hub.Disconnect();

    void Advance(double ms)
    {
        var end = now + ms;
        while (now < end)
        {
            now += 50;
            session.Update();
            hub.Update();
        }
    }

    void WaitForOpen()
    {
        var watch = Stopwatch.StartNew();
        while (hub.GetStatus().link == LinkState.Connecting && watch.ElapsedMilliseconds < 5000)
        {
            hub.Update();
            Thread.Sleep(1);
        }
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
    }

    [Test]
    public void ListsThePorts()
    {
        session.Update();
        Assert.That(session.Ports.Select(p => p.name), Is.EqualTo(new[] { "COM5" }));
        present = new[] { "COM5", "COM7" };
        Advance(1100);
        Assert.That(session.Ports.Length, Is.EqualTo(2));
    }

    [Test]
    public void UnpluggingDisconnectsWithTheReason()
    {
        session.ConnectSerial("COM5");
        WaitForOpen();
        Assert.That(session.CanReconnect(), Is.True);
        present = new string[0];
        Advance(1100);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("USB device was unplugged"));
    }

    [Test]
    public void PluggingBackInReconnectsAfterADelay()
    {
        session.ConnectSerial("COM5");
        WaitForOpen();
        present = new string[0];
        Advance(1100);
        Assert.That(opens, Is.EqualTo(1));
        present = new[] { "COM5" };
        Advance(1000 + 300); // seen by the next reading of the list, not opened yet
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Advance(1000);
        WaitForOpen();
        Assert.That(hub.GetStatus().lastError, Is.Null);
    }

    [Test]
    public void AUserDisconnectStaysDisconnected()
    {
        session.ConnectSerial("COM5");
        WaitForOpen();
        session.Disconnect();
        present = new string[0];
        Advance(1100);
        present = new[] { "COM5" };
        Advance(3000);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
    }

    [Test]
    public void ReconnectReopensTheLastPort()
    {
        Assert.That(session.CanReconnect(), Is.False);
        Assert.Throws<InvalidOperationException>(() => session.Reconnect());
        session.ConnectSerial("COM5");
        WaitForOpen();
        session.Disconnect();
        session.Reconnect();
        WaitForOpen();
        Assert.That(opens, Is.EqualTo(2));
    }

    [Test]
    public void TheSimulatorIsLeftAloneByPortChanges()
    {
        session.ConnectSerial("COM5");
        WaitForOpen();
        session.ConnectSimulated();
        present = new string[0];
        Advance(1100);
        present = new[] { "COM5" };
        Advance(3000);
        Assert.That(hub.GetStatus().transport, Is.EqualTo(TransportKind.Simulated));
    }
}

public class SimulatedTransportTests
{
    double now;
    SensorHub hub;
    SimulatedTransport sim;
    SimParams p;

    [SetUp]
    public void Create()
    {
        now = 1000;
        hub = new SensorHub(() => now);
        sim = new SimulatedTransport(() => now, 42);
        p = SimulatedTransport.simParams;
        p.motion = SimMotion.Still;
        p.targetPitch = p.targetRoll = p.targetYaw = 0;
        p.dropRate = p.malformedRate = 0;
        hub.Connect(sim);
    }

    [TearDown]
    public void Close()
    {
        hub.Disconnect();
        p.motion = SimMotion.Wave;
        p.targetPitch = p.targetRoll = p.targetYaw = 0;
        p.dropRate = p.malformedRate = 0;
    }

    void Advance(double ms)
    {
        var end = now + ms;
        while (now < end)
        {
            now += 1000.0 / 60;
            hub.Update();
        }
    }

    [Test]
    public void SaysHelloCalibratesThenStreamsAt50Hz()
    {
        Assert.That(hub.GetStatus().firmware, Is.EqualTo("simulator/1.0.0"));
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
        Assert.That(hub.GetStatus().device, Is.EqualTo(DeviceState.Calibrating));
        Advance(1400);
        Assert.That(hub.GetStats().samples, Is.EqualTo(0), "streamed during calibration");
        Advance(200);
        Assert.That(hub.GetStatus().device, Is.EqualTo(DeviceState.Ready));
        Assert.That(hub.IsUsable(), Is.True);
        Advance(1100);
        var stats = hub.GetStats();
        Assert.That(stats.rxHz, Is.InRange(48, 52));
        Assert.That(stats.droppedSeq, Is.EqualTo(0));
        Assert.That(stats.malformed, Is.EqualTo(0));
    }

    [Test]
    public void StillSitsOnTheMountingBias()
    {
        Advance(4000);
        var s = hub.Latest().sample;
        Assert.That(s.pitch, Is.EqualTo(p.biasPitchDeg).Within(1.5));
        Assert.That(s.roll, Is.EqualTo(p.biasRollDeg).Within(1.5));
        Assert.That(s.accelG, Is.EqualTo(1).Within(0.1));
    }

    [Test]
    public void ManualEasesTowardsTheTarget()
    {
        p.motion = SimMotion.Manual;
        p.targetPitch = 20;
        p.targetRoll = -30;
        Advance(1600);
        Assert.That(hub.Latest().sample.pitch, Is.LessThan(15), "a hand doesn't teleport");
        Advance(2000);
        var s = hub.Latest().sample;
        Assert.That(s.pitch, Is.EqualTo(20 + p.biasPitchDeg).Within(1.5));
        Assert.That(s.roll, Is.EqualTo(-30 + p.biasRollDeg).Within(1.5));
    }

    [Test]
    public void WaveSweepsBothAxes()
    {
        p.motion = SimMotion.Wave;
        Advance(1600);
        double minRoll = 99, maxRoll = -99, maxPitch = -99;
        for (var i = 0; i < 300; i++)
        {
            Advance(50);
            var s = hub.Latest().sample;
            minRoll = Math.Min(minRoll, s.roll);
            maxRoll = Math.Max(maxRoll, s.roll);
            maxPitch = Math.Max(maxPitch, s.pitch);
        }
        Assert.That(maxRoll, Is.GreaterThan(15));
        Assert.That(minRoll, Is.LessThan(-15));
        Assert.That(maxPitch, Is.GreaterThan(10));
    }

    [Test]
    public void CalRecalibratesLikeTheFirmware()
    {
        Advance(1600);
        Assert.That(hub.IsUsable(), Is.True);
        hub.Send(Protocol.CMD_CAL);
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Awaiting));
        Assert.That(hub.Latest(), Is.Null);
        Advance(1600);
        Assert.That(hub.IsUsable(), Is.True);
    }

    [Test]
    public void AnUnknownCommandIsRejected()
    {
        hub.Send("NOPE");
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("Device rejected ?: unknown command"));
    }

    [Test]
    public void HelloAgainCountsAsARestart()
    {
        hub.Send(Protocol.CMD_HELLO);
        Assert.That(hub.GetStats().restarts, Is.EqualTo(1));
    }

    [Test]
    public void ZeroYawClearsTheDrift()
    {
        p.motion = SimMotion.Manual;
        p.targetYaw = 40;
        p.yawDriftDps = 10;
        Advance(5000);
        Assert.That(hub.Latest().sample.yaw, Is.GreaterThan(40));
        hub.Send(Protocol.CMD_ZEROYAW);
        Assert.That(p.targetYaw, Is.EqualTo(0));
        Advance(2000);
        p.yawDriftDps = 0.3;
        Assert.That(Math.Abs(hub.Latest().sample.yaw), Is.LessThan(25));
    }

    [Test]
    public void UnplugActsLikeAYankedCable()
    {
        Advance(1600);
        sim.Unplug();
        Assert.That(hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(hub.GetStatus().lastError, Is.EqualTo("Simulated cable unplugged"));
    }

    [Test]
    public void FaultInjectionShowsUpInTheCounters()
    {
        p.dropRate = 0.2;
        p.malformedRate = 0.2;
        Advance(6000);
        var stats = hub.GetStats();
        Assert.That(stats.droppedSeq, Is.GreaterThan(5));
        Assert.That(stats.malformed, Is.GreaterThan(5));
        Assert.That(hub.Latest(), Is.Not.Null);
    }

    [Test]
    public void ALongStallDoesNotReplayEverything()
    {
        Advance(1600);
        var before = hub.GetStats().samples;
        now += 60000;
        hub.Update();
        Assert.That(hub.GetStats().samples - before, Is.LessThan(3));
    }
}
