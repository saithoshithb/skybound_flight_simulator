using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Skybound
{
    /// <summary>
    /// Link state, from the game's point of view.
    ///  - Disconnected: no transport open
    ///  - Connecting:   opening the port
    ///  - Awaiting:     port open, no valid attitude yet (device booting or calibrating)
    ///  - Live:         valid samples arriving on time
    ///  - Stale:        samples late (> STALE_MS); controls fade to neutral
    ///  - Lost:         no samples for LOST_MS while the port is still open
    /// </summary>
    public enum LinkState
    {
        Disconnected,
        Connecting,
        Awaiting,
        Live,
        Stale,
        Lost,
    }

    /// <summary>Coarse status, changes a few times a second at most.</summary>
    public class HubStatus
    {
        public LinkState link = LinkState.Disconnected;
        public DeviceState device = DeviceState.Unknown;
        public string deviceMessage = "";
        public TransportKind? transport;
        public string transportLabel = "";
        public string firmware;
        public string lastError;

        public HubStatus Clone() => (HubStatus)MemberwiseClone();

        /// <summary>Samples can drive controls. A fallback calibration (cal_failed) still streams usable data, with a warning.</summary>
        public bool Usable => link == LinkState.Live && (device == DeviceState.Ready || device == DeviceState.CalFailed);

        public bool SameAs(HubStatus o)
        {
            return link == o.link && device == o.device && deviceMessage == o.deviceMessage && transport == o.transport
                && transportLabel == o.transportLabel && firmware == o.firmware && lastError == o.lastError;
        }
    }

    /// <summary>Diagnostics counters, read by polling.</summary>
    public class HubStats
    {
        public int rxHz;
        public double lastSampleAgeMs = double.PositiveInfinity;
        public int samples;
        public double droppedSeq;
        public int malformed;
        public string lastMalformedReason = "";
        public int restarts;
    }

    public class TimedSample
    {
        public ImuSample sample;
        /// <summary>Clock time when the line reached the main thread.</summary>
        public double receivedAt;
    }

    /// <summary>
    /// Owns the transport and is the single source of truth for sensor data.
    /// Consumers either call Latest() once a frame or register an OnSample listener.
    /// Everything here runs on the main thread: a transport with its own thread queues
    /// its lines and hands them over in Pump.
    /// </summary>
    public class SensorHub
    {
        public const double STALE_MS = 250;
        public const double LOST_MS = 2000;
        /// <summary>Port open but not a single line received: probably wrong port/baud or no firmware.</summary>
        public const double SILENT_PORT_MS = 4000;
        const int LOG_LINES = 40;

        static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        /// <summary>Milliseconds since start-up, the stand-in for performance.now().</summary>
        public static double DefaultClock() => Stopwatch.Elapsed.TotalMilliseconds;

        readonly Func<double> now;
        ISensorTransport transport;
        HubStatus status = new HubStatus();
        HubStats stats = new HubStats();
        TimedSample last;
        double? lastSeq;
        readonly Queue<double> rateWindow = new Queue<double>();
        readonly Queue<string> log = new Queue<string>();
        readonly List<Action<TimedSample>> sampleListeners = new List<Action<TimedSample>>();
        readonly List<Action<HubStatus>> statusListeners = new List<Action<HubStatus>>();
        int generation;
        double openedAt;

        public SensorHub(Func<double> clock = null)
        {
            now = clock ?? DefaultClock;
        }

        // ----- public API -----

        public double Now() => now();

        bool helloRequested;

        public HubStatus GetStatus() => status;

        public HubStats GetStats()
        {
            var t = now();
            TrimRateWindow(t);
            return new HubStats
            {
                rxHz = rateWindow.Count,
                lastSampleAgeMs = last != null ? t - last.receivedAt : double.PositiveInfinity,
                samples = stats.samples,
                droppedSeq = stats.droppedSeq,
                malformed = stats.malformed,
                lastMalformedReason = stats.lastMalformedReason,
                restarts = stats.restarts,
            };
        }

        /// <summary>Most recent valid sample, or null. Only meaningful while link is live.</summary>
        public TimedSample Latest() => last;

        /// <summary>True when the newest sample can drive controls.</summary>
        public bool IsUsable() => status.Usable;

        public string[] RecentLines() => log.ToArray();

        /// <returns>Call it to stop listening.</returns>
        public Action OnSample(Action<TimedSample> fn)
        {
            sampleListeners.Add(fn);
            return () => sampleListeners.Remove(fn);
        }

        /// <returns>Call it to stop listening.</returns>
        public Action Subscribe(Action<HubStatus> fn)
        {
            statusListeners.Add(fn);
            return () => statusListeners.Remove(fn);
        }

        public void Connect(ISensorTransport next)
        {
            Disconnect();
            var gen = ++generation;
            transport = next;
            helloRequested = false;
            stats = new HubStats();
            last = null;
            lastSeq = null;
            rateWindow.Clear();
            log.Clear();
            SetStatus(s =>
            {
                s.link = LinkState.Connecting;
                s.device = DeviceState.Unknown;
                s.deviceMessage = "";
                s.transport = next.Kind;
                s.transportLabel = next.Label;
                s.firmware = null;
                s.lastError = null;
            });
            try
            {
                next.Open(new TransportEvents
                {
                    onOpened = () => { if (gen == generation) HandleOpened(next); },
                    onOpenFailed = reason => { if (gen == generation) HandleOpenFailed(reason); },
                    onLine = line => { if (gen == generation) HandleLine(line); },
                    onClose = reason => { if (gen == generation) HandleClose(reason); },
                });
            }
            catch (Exception e)
            {
                if (gen == generation) HandleOpenFailed(e.Message);
            }
        }

        public void Disconnect()
        {
            var t = transport;
            generation++;
            transport = null;
            try
            {
                t?.Close();
            }
            catch (Exception)
            {
                // Already gone: an unplugged port can throw on close.
            }
            last = null;
            if (status.link != LinkState.Disconnected)
            {
                SetStatus(s =>
                {
                    s.link = LinkState.Disconnected;
                    s.device = DeviceState.Unknown;
                    s.deviceMessage = "";
                    s.transport = null;
                });
            }
        }

        public void Send(string cmd)
        {
            if (transport == null) throw new InvalidOperationException("Not connected");
            transport.Send(cmd);
        }

        /// <summary>The active transport, for transport-specific UI (e.g. simulator fault buttons).</summary>
        public ISensorTransport ActiveTransport() => transport;

        /// <summary>
        /// Once a frame: takes in what the transport received, then checks for timeouts. The
        /// browser game does the second part on a 50 ms timer. Lines first, so a long frame
        /// with samples waiting in the queue doesn't count as the controller going quiet.
        /// </summary>
        public void Update()
        {
            transport?.Pump();
            CheckTimeouts();
        }

        // ----- internals -----

        void HandleOpened(ISensorTransport t)
        {
            openedAt = now();
            SetStatus(s =>
            {
                s.link = LinkState.Awaiting;
                s.transportLabel = t.Label;
            });
        }

        void HandleOpenFailed(string reason)
        {
            transport = null;
            SetStatus(s =>
            {
                s.link = LinkState.Disconnected;
                s.transport = null;
                s.lastError = reason;
            });
        }

        void HandleLine(string line)
        {
            log.Enqueue(line);
            if (log.Count > LOG_LINES) log.Dequeue();

            var result = Protocol.ParseLine(line);
            if (!result.ok)
            {
                if (result.reason != "empty line")
                {
                    stats.malformed++;
                    stats.lastMalformedReason = result.reason;
                }
                return;
            }
            switch (result.message)
            {
                case HelloMessage msg:
                    // A hello mid-stream means the board rebooted (e.g. DTR reset or brown-out).
                    if (!string.IsNullOrEmpty(status.firmware)) stats.restarts++;
                    lastSeq = null;
                    SetStatus(s => s.firmware = msg.firmware);
                    if (msg.proto != Protocol.PROTOCOL_VERSION)
                        SetStatus(s => s.lastError = $"Firmware speaks protocol v{MiniJson.JsNumber(msg.proto)}; this app expects v1.");
                    return;
                case StatusMessage msg:
                    SetStatus(s =>
                    {
                        s.device = msg.state;
                        s.deviceMessage = msg.message;
                    });
                    if (msg.state != DeviceState.Ready)
                    {
                        // Never keep driving controls from pre-calibration data.
                        last = null;
                        if (status.link == LinkState.Live || status.link == LinkState.Stale) SetStatus(s => s.link = LinkState.Awaiting);
                    }
                    return;
                case AckMessage msg:
                    if (msg.error != null) SetStatus(s => s.lastError = $"Device rejected {msg.command}: {msg.error}");
                    return;
                case ImuSample msg:
                {
                    var t = now();
                    if (lastSeq != null) stats.droppedSeq += Protocol.SeqGap(lastSeq.Value, msg.seq);
                    lastSeq = msg.seq;
                    stats.samples++;
                    rateWindow.Enqueue(t);
                    // The browser's port open reboots the board, which then says hello. Opening a COM port
                    // doesn't always, so a board that was already running gets asked, once.
                    if (status.firmware == null && !helloRequested)
                    {
                        helloRequested = true;
                        Send(Protocol.CMD_HELLO);
                    }
                    // Older firmware may not send status; treat streaming attitude as ready.
                    if (status.device == DeviceState.Unknown) SetStatus(s => s.device = DeviceState.Ready);
                    if (status.device != DeviceState.Ready && status.device != DeviceState.CalFailed) return;
                    last = new TimedSample { sample = msg, receivedAt = t };
                    if (status.link != LinkState.Live) SetStatus(s => s.link = LinkState.Live);
                    for (var i = sampleListeners.Count - 1; i >= 0; i--) sampleListeners[i](last);
                    return;
                }
            }
        }

        void HandleClose(string reason)
        {
            transport = null;
            last = null;
            SetStatus(s =>
            {
                s.link = LinkState.Disconnected;
                s.device = DeviceState.Unknown;
                s.transport = null;
                s.lastError = reason;
            });
        }

        void CheckTimeouts()
        {
            var t = now();
            TrimRateWindow(t);
            var link = status.link;
            if (link == LinkState.Awaiting && log.Count == 0 && t - openedAt > SILENT_PORT_MS && status.lastError == null)
            {
                SetStatus(s => s.lastError = $"No data received after {SILENT_PORT_MS / 1000} s. Check that the firmware is flashed, the baud rate is 115200, and you picked the ESP32's port.");
                return;
            }
            if (link != LinkState.Live && link != LinkState.Stale && link != LinkState.Lost) return;
            var age = last != null ? t - last.receivedAt : double.PositiveInfinity;
            var next = age > LOST_MS ? LinkState.Lost : age > STALE_MS ? LinkState.Stale : LinkState.Live;
            if (next != link) SetStatus(s => s.link = next);
        }

        void TrimRateWindow(double t)
        {
            while (rateWindow.Count > 0 && t - rateWindow.Peek() > 1000) rateWindow.Dequeue();
        }

        void SetStatus(Action<HubStatus> patch)
        {
            var next = status.Clone();
            patch(next);
            if (next.SameAs(status)) return;
            status = next;
            for (var i = statusListeners.Count - 1; i >= 0; i--) statusListeners[i](next);
        }
    }
}
