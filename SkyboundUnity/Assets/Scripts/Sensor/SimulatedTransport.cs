using System;

namespace Skybound
{
    public enum SimMotion
    {
        Wave,
        Manual,
        Still,
    }

    /// <summary>Live-tunable simulator parameters. Mutated directly by the UI.</summary>
    public class SimParams
    {
        public SimMotion motion = SimMotion.Wave;
        /// <summary>Manual target attitude in degrees, aircraft convention.</summary>
        public double targetPitch, targetRoll, targetYaw;
        /// <summary>Gaussian noise on angles, degrees (1σ).</summary>
        public double noiseDeg = 0.25;
        /// <summary>Yaw gyro drift, deg/s, as a real MPU-6050 shows without a magnetometer.</summary>
        public double yawDriftDps = 0.3;
        /// <summary>Fraction of samples silently dropped (0–1).</summary>
        public double dropRate;
        /// <summary>Fraction of lines corrupted (0–1).</summary>
        public double malformedRate;
        /// <summary>Simulated mounting offset: the "desk" isn't level.</summary>
        public double biasPitchDeg = 2.5, biasRollDeg = -1.5;
    }

    /// <summary>
    /// Stands in for the ESP32 when no hardware is attached. It speaks the exact
    /// line protocol (including hello/status/ack, calibration delay and command
    /// handling) so the parser, hub and calibration code run unmodified.
    ///
    /// The browser game runs it from setInterval and setTimeout; here Pump works out
    /// from the clock which ticks are due.
    /// </summary>
    public class SimulatedTransport : ISensorTransport
    {
        const double RATE_HZ = 50;
        const double CAL_MS = 1500;
        const double CATCH_UP_LIMIT_MS = 1000;
        const string HELLO = "{\"t\":\"hello\",\"fw\":\"simulator/1.0.0\",\"proto\":1,\"rate\":50}";

        public static readonly SimParams simParams = new SimParams();

        public TransportKind Kind => TransportKind.Simulated;
        public string Label => "Simulated controller";

        readonly Func<double> now;
        readonly Random random;
        TransportEvents events;
        double seq;
        double startMs;
        double nextTickMs;
        double calEndsMs = -1;
        bool calibrating = true;
        double attPitch, attRoll, attYaw;
        double prevPitch, prevRoll, prevYaw;
        double yawDrift;

        public SimulatedTransport(Func<double> clock = null, int? seed = null)
        {
            now = clock ?? SensorHub.DefaultClock;
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public void Open(TransportEvents transportEvents)
        {
            events = transportEvents;
            startMs = now();
            nextTickMs = startMs + 1000 / RATE_HZ;
            Emit(HELLO);
            BeginCalibration();
            events.onOpened();
        }

        public void Close()
        {
            var e = events;
            events = null;
            e?.onClose(null);
        }

        /// <summary>Fault injection: behave like a yanked USB cable.</summary>
        public void Unplug()
        {
            var e = events;
            events = null;
            e?.onClose("Simulated cable unplugged");
        }

        public void Send(string line)
        {
            var cmd = MiniJson.JsTrim(line);
            if (cmd == Protocol.CMD_CAL)
            {
                Emit("{\"t\":\"ack\",\"cmd\":\"CAL\"}");
                BeginCalibration();
            }
            else if (cmd == Protocol.CMD_ZEROYAW)
            {
                yawDrift = 0;
                simParams.targetYaw = 0;
                Emit("{\"t\":\"ack\",\"cmd\":\"ZEROYAW\"}");
            }
            else if (cmd == Protocol.CMD_HELLO)
            {
                Emit(HELLO);
            }
            else
            {
                Emit("{\"t\":\"ack\",\"cmd\":\"?\",\"err\":\"unknown command\"}");
            }
        }

        public void Pump()
        {
            var t = now();
            if (calEndsMs >= 0 && t >= calEndsMs)
            {
                calEndsMs = -1;
                calibrating = false;
                yawDrift = 0;
                Emit("{\"t\":\"status\",\"state\":\"ready\",\"msg\":\"gyro bias calibrated\"}");
            }
            // After a long stall (Editor paused, say) carry on from now instead of replaying it all.
            if (t - nextTickMs > CATCH_UP_LIMIT_MS) nextTickMs = t;
            while (events != null && t >= nextTickMs)
            {
                Tick(nextTickMs);
                nextTickMs += 1000 / RATE_HZ;
            }
        }

        void BeginCalibration()
        {
            calibrating = true;
            Emit("{\"t\":\"status\",\"state\":\"calibrating\",\"msg\":\"hold the controller still\"}");
            calEndsMs = now() + CAL_MS;
        }

        void Emit(string line)
        {
            events?.onLine(line);
        }

        double Gauss()
        {
            return Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        }

        static double Wrap180(double d) => (((d + 180) % 360 + 360) % 360) - 180;

        void Tick(double tickMs)
        {
            // Like the firmware, no attitude is streamed while calibrating.
            if (calibrating) return;
            var p = simParams;
            var dt = 1 / RATE_HZ;
            var tSec = (tickMs - startMs) / 1000;
            var targetPitch = p.targetPitch;
            var targetRoll = p.targetRoll;
            var targetYaw = p.targetYaw;
            if (p.motion == SimMotion.Wave)
            {
                targetPitch = 18 * Math.Sin(tSec * 0.7);
                targetRoll = 28 * Math.Sin(tSec * 0.45 + 1);
                targetYaw = 0;
            }
            else if (p.motion == SimMotion.Still)
            {
                targetPitch = targetRoll = targetYaw = 0;
            }

            // A hand doesn't teleport: ease toward the target like a real wrist.
            var k = 1 - Math.Exp(-dt * 8);
            attPitch += (targetPitch - attPitch) * k;
            attRoll += (targetRoll - attRoll) * k;
            attYaw += Wrap180(targetYaw - attYaw) * k;
            yawDrift += p.yawDriftDps * dt;

            var pitch = Math.Max(-90, Math.Min(90, attPitch + p.biasPitchDeg + Gauss() * p.noiseDeg));
            var roll = Wrap180(attRoll + p.biasRollDeg + Gauss() * p.noiseDeg);
            var yaw = Wrap180(attYaw + yawDrift + Gauss() * p.noiseDeg * 0.5);
            var pitchRate = (pitch - prevPitch) / dt;
            var rollRate = Wrap180(roll - prevRoll) / dt;
            var yawRate = Wrap180(yaw - prevYaw) / dt;
            prevPitch = pitch;
            prevRoll = roll;
            prevYaw = yaw;

            var sequence = seq++;
            if (random.NextDouble() < p.dropRate) return;
            var line = Protocol.EncodeImu(new ImuSample
            {
                seq = sequence,
                deviceMs = Math.Floor(tSec * 1000 + 0.5),
                pitch = pitch,
                roll = roll,
                yaw = yaw,
                pitchRate = pitchRate,
                rollRate = rollRate,
                yawRate = yawRate,
                accelG = 1 + Gauss() * 0.01,
            });
            if (random.NextDouble() < p.malformedRate) line = line.Substring(0, (int)Math.Floor(random.NextDouble() * line.Length));
            Emit(line);
        }
    }
}
