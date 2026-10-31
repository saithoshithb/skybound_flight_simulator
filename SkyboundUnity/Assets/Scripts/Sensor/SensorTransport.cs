using System;

namespace Skybound
{
    public enum TransportKind
    {
        Serial,
        Simulated,
    }

    /// <summary>
    /// Where a transport reports to. The TypeScript transport's open() is async and either
    /// resolves or throws; C# has no await across a Unity frame here, so those two outcomes
    /// are callbacks. All of them run on the main thread, from Open or Pump.
    /// </summary>
    public class TransportEvents
    {
        public Action onOpened;
        public Action<string> onOpenFailed;
        public Action<string> onLine;
        /// <summary>The reason is null when we asked it to close.</summary>
        public Action<string> onClose;
    }

    public interface ISensorTransport
    {
        TransportKind Kind { get; }
        string Label { get; }
        void Open(TransportEvents events);
        void Close();
        void Send(string line);
        /// <summary>Called once a frame: deliver whatever arrived since the last call.</summary>
        void Pump();
    }
}
