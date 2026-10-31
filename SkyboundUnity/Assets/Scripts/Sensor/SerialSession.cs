using System;
using System.Linq;

namespace Skybound
{
    /// <summary>
    /// Connection actions the UI calls. Remembers the last serial port so it can
    /// reconnect in one click, and reconnects automatically if that port is plugged
    /// back in after an unexpected loss (not after a user Disconnect).
    ///
    /// The browser reports plugging and unplugging as events. Here nothing does, so the
    /// port list is read once a second and compared with the last reading.
    /// </summary>
    public class SerialSession
    {
        const double POLL_MS = 1000;
        /// <summary>Give the board a moment to enumerate before opening.</summary>
        const double REPLUG_DELAY_MS = 500;

        readonly SensorHub hub;
        readonly Func<SerialPortInfo[]> listPorts;
        readonly Func<string, ISerialPort> portFactory;

        string lastPort;
        bool autoReconnect;
        bool lastPortPresent;
        double nextPollMs;
        double reconnectAtMs = -1;

        /// <summary>The ports from the latest reading, for the UI.</summary>
        public SerialPortInfo[] Ports { get; private set; } = new SerialPortInfo[0];

        /// <param name="ports">Reads the port list; leave out for the real one.</param>
        /// <param name="factory">Makes a port; leave out for a real COM port.</param>
        public SerialSession(SensorHub sensorHub, Func<SerialPortInfo[]> ports = null, Func<string, ISerialPort> factory = null)
        {
            hub = sensorHub;
            listPorts = ports ?? SerialPorts.List;
            portFactory = factory;
        }

        public void ConnectSimulated()
        {
            autoReconnect = false;
            hub.Connect(new SimulatedTransport());
        }

        public void ConnectSerial(string port)
        {
            lastPort = port;
            lastPortPresent = true;
            autoReconnect = true;
            hub.Connect(new SerialTransport(port, portFactory));
        }

        /// <summary>Reopen the last-used port without choosing it again.</summary>
        public void Reconnect()
        {
            if (lastPort == null) throw new InvalidOperationException("No previously used port. Use Connect to choose one.");
            ConnectSerial(lastPort);
        }

        public void Disconnect()
        {
            autoReconnect = false;
            hub.Disconnect();
        }

        public bool CanReconnect() => lastPort != null;

        public string LastPort => lastPort;

        /// <summary>Once a frame.</summary>
        public void Update()
        {
            var t = hub.Now();
            if (t >= nextPollMs)
            {
                nextPollMs = t + POLL_MS;
                Poll(t);
            }
            if (reconnectAtMs >= 0 && t >= reconnectAtMs)
            {
                reconnectAtMs = -1;
                if (autoReconnect && hub.GetStatus().link == LinkState.Disconnected) ConnectSerial(lastPort);
            }
        }

        void Poll(double t)
        {
            Ports = listPorts();
            if (lastPort == null) return;
            var present = Ports.Any(p => p.name == lastPort);
            if (!present && lastPortPresent && hub.ActiveTransport() is SerialTransport active && active.PortName == lastPort) active.Unplugged();
            if (present && !lastPortPresent && autoReconnect && hub.GetStatus().link == LinkState.Disconnected) reconnectAtMs = t + REPLUG_DELAY_MS;
            lastPortPresent = present;
        }
    }
}
