using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace Skybound
{
    /// <summary>The parts of a serial port the transport uses; tests supply their own.</summary>
    public interface ISerialPort
    {
        void Open();
        void Close();
        int BytesToRead { get; }
        int Read(byte[] buffer, int offset, int count);
        void Write(byte[] buffer, int offset, int count);
    }

    /// <summary>A real COM port. Same settings the browser opens it with: 115200 8N1, DTR and RTS asserted.</summary>
    public class SystemSerialPort : ISerialPort
    {
        readonly SerialPort port;

        public SystemSerialPort(string name)
        {
            port = new SerialPort(name, SerialTransport.SERIAL_BAUD)
            {
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 100,
                WriteTimeout = 500,
            };
        }

        public void Open() => port.Open();
        public void Close() => port.Close();
        public int BytesToRead => port.BytesToRead;
        public int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count);
        public void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count);
    }

    public struct SerialPortInfo
    {
        public string name;
        /// <summary>A Bluetooth link rather than a USB cable: never the ESP32 on USB, and slow to open.</summary>
        public bool bluetooth;

        public string Display => bluetooth ? name + " (Bluetooth)" : name;
    }

    /// <summary>
    /// The stand-in for the browser's port chooser, which names each device. All Windows
    /// tells .NET is "COM5", so the device list in the registry is used to mark Bluetooth
    /// links, which otherwise look just like the ESP32. USB ports come first.
    /// </summary>
    public static class SerialPorts
    {
        public static SerialPortInfo[] List()
        {
            var bluetooth = BluetoothPortNames();
            return SerialPort.GetPortNames()
                .Distinct()
                .Select(name => new SerialPortInfo { name = name, bluetooth = bluetooth.Contains(name) })
                .OrderBy(p => p.bluetooth)
                .ThenBy(p => PortNumber(p.name))
                .ThenBy(p => p.name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <param name="devicePath">The registry's name for the port's driver, e.g. \Device\BthModem0.</param>
        public static bool IsBluetoothDevice(string devicePath)
        {
            return devicePath.IndexOf("bth", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int PortNumber(string name)
        {
            var digits = Regex.Match(name, @"\d+$");
            return digits.Success && int.TryParse(digits.Value, out var n) ? n : int.MaxValue;
        }

        static HashSet<string> BluetoothPortNames()
        {
            var names = new HashSet<string>();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (key == null) return names;
                    foreach (var device in key.GetValueNames())
                        if (IsBluetoothDevice(device)) names.Add(key.GetValue(device, "").ToString());
                }
            }
            catch (Exception)
            {
                // Only a label: without it the port just isn't marked.
            }
#endif
            return names;
        }
    }

    /// <summary>
    /// USB serial, replacing the browser's Web Serial API.
    ///
    /// Opening the port toggles DTR/RTS, which resets most ESP32 dev boards. The
    /// firmware then re-runs gyro calibration, so SensorHub waits for a "ready"
    /// status before treating data as usable.
    ///
    /// Web Serial never blocks the page; System.IO.Ports does, and opening a Bluetooth
    /// port can take many seconds. So one background thread owns the port: it opens,
    /// reads and writes it, and queues everything for Pump on the main thread. It reads
    /// only what is already waiting, so a read returns at once however this Mono build
    /// treats its timeouts.
    /// </summary>
    public class SerialTransport : ISensorTransport
    {
        public const int SERIAL_BAUD = 115200;
        const int IDLE_SLEEP_MS = 2;
        const int CLOSE_WAIT_MS = 300;
        /// <summary>About 40 s of data: only ever reached while the game is stopped (a paused Editor) and the thread isn't.</summary>
        const int MAX_QUEUED_LINES = 2000;

        enum EventKind
        {
            Opened,
            OpenFailed,
            Line,
            Closed,
        }

        struct QueuedEvent
        {
            public EventKind kind;
            public string text;
        }

        public TransportKind Kind => TransportKind.Serial;
        public string Label { get; }
        public string PortName { get; }

        readonly Func<string, ISerialPort> portFactory;
        readonly ConcurrentQueue<QueuedEvent> inbox = new ConcurrentQueue<QueuedEvent>();
        readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
        TransportEvents events;
        Thread thread;
        volatile bool closing;
        volatile bool opened;

        /// <param name="factory">Makes the port; leave out for a real COM port.</param>
        public SerialTransport(string portName, Func<string, ISerialPort> factory = null)
        {
            PortName = portName;
            Label = "USB serial " + portName;
            portFactory = factory ?? (name => new SystemSerialPort(name));
        }

        public void Open(TransportEvents transportEvents)
        {
            events = transportEvents;
            thread = new Thread(Run) { IsBackground = true, Name = "Skybound serial " + PortName };
            thread.Start();
        }

        public void Pump()
        {
            while (events != null && inbox.TryDequeue(out var e))
            {
                switch (e.kind)
                {
                    case EventKind.Opened:
                        events.onOpened();
                        break;
                    case EventKind.OpenFailed:
                    {
                        var failed = events;
                        events = null;
                        failed.onOpenFailed(e.text);
                        break;
                    }
                    case EventKind.Line:
                        events.onLine(e.text);
                        break;
                    case EventKind.Closed:
                        Finish(e.text);
                        break;
                }
            }
        }

        public void Send(string line)
        {
            if (!opened || closing) throw new InvalidOperationException("Serial port is not open");
            outbox.Enqueue(Encoding.UTF8.GetBytes(line + "\n"));
        }

        /// <summary>The port dropped out of the system's list (Windows gives a read no reliable error for this).</summary>
        public void Unplugged()
        {
            Finish("USB device was unplugged");
        }

        public void Close()
        {
            closing = true;
            // The thread closes the port on its way out. If it's still stuck opening one, it
            // does that when the open returns, and being a background thread it can't keep
            // the Editor or the game from exiting meanwhile.
            thread?.Join(CLOSE_WAIT_MS);
            Finish(null);
        }

        void Finish(string reason)
        {
            closing = true;
            var e = events;
            events = null;
            e?.onClose(reason);
        }

        void Queue(EventKind kind, string text = null)
        {
            inbox.Enqueue(new QueuedEvent { kind = kind, text = text });
        }

        static string OpenErrorMessage(Exception e)
        {
            return e is UnauthorizedAccessException || Regex.IsMatch(e.Message, "already open|failed to open|denied|in use|busy", RegexOptions.IgnoreCase)
                ? $"Could not open the port. Close Arduino Serial Monitor or any other app using it. ({e.Message})"
                : e.Message;
        }

        void Run()
        {
            ISerialPort port;
            try
            {
                port = portFactory(PortName);
                port.Open();
            }
            catch (Exception e)
            {
                Queue(EventKind.OpenFailed, OpenErrorMessage(e));
                return;
            }

            try
            {
                if (closing) return;
                opened = true;
                Queue(EventKind.Opened);
                ReadLoop(port);
            }
            finally
            {
                opened = false;
                try
                {
                    port.Close();
                }
                catch (Exception)
                {
                    // Unplugged ports throw on close.
                }
            }
        }

        void ReadLoop(ISerialPort port)
        {
            var framer = new LineFramer();
            var decoder = Encoding.UTF8.GetDecoder();
            var bytes = new byte[1024];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            while (!closing)
            {
                try
                {
                    while (outbox.TryDequeue(out var command)) Write(port, command);

                    var available = port.BytesToRead;
                    if (available <= 0)
                    {
                        Thread.Sleep(IDLE_SLEEP_MS);
                        continue;
                    }
                    var read = port.Read(bytes, 0, Math.Min(available, bytes.Length));
                    if (read <= 0) continue;
                    var count = decoder.GetChars(bytes, 0, read, chars, 0);
                    foreach (var line in framer.Push(new string(chars, 0, count))) QueueLine(line);
                }
                catch (TimeoutException)
                {
                    // Nothing there after all.
                }
                catch (Exception)
                {
                    if (!closing) Queue(EventKind.Closed, "Serial device lost");
                    return;
                }
            }
        }

        static void Write(ISerialPort port, byte[] command)
        {
            try
            {
                port.Write(command, 0, command.Length);
            }
            catch (TimeoutException)
            {
                Debug.LogWarning("[Skybound] The controller didn't accept a command in time.");
            }
        }

        void QueueLine(string line)
        {
            if (inbox.Count >= MAX_QUEUED_LINES) inbox.TryDequeue(out _); // the hub counts the gap from the sequence numbers
            Queue(EventKind.Line, line);
        }
    }
}
