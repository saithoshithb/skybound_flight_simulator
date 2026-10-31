using System;
using System.Collections.Generic;

namespace Skybound
{
    public enum DeviceState
    {
        /// <summary>Nothing heard from the device yet (the hub's own state, never on the wire).</summary>
        Unknown,
        Calibrating,
        Ready,
        SensorError,
        MotionDuringCal,
        CalFailed,
    }

    public abstract class DeviceMessage { }

    /// <summary>Fused attitude sample. Angles in degrees, aircraft convention (see docs/AXES.md).</summary>
    public class ImuSample : DeviceMessage
    {
        public double seq;
        public double deviceMs;
        /// <summary>+ nose up</summary>
        public double pitch;
        /// <summary>+ right wing down</summary>
        public double roll;
        /// <summary>+ nose right; relative to power-on / last ZEROYAW, drifts over time</summary>
        public double yaw;
        public double pitchRate;
        public double rollRate;
        public double yawRate;
        /// <summary>Accelerometer magnitude in g (≈1 at rest).</summary>
        public double accelG;
    }

    public class StatusMessage : DeviceMessage
    {
        public DeviceState state;
        public string message;
    }

    public class HelloMessage : DeviceMessage
    {
        public string firmware;
        public double proto;
        public double rateHz;
    }

    public class AckMessage : DeviceMessage
    {
        public string command;
        public string error;
    }

    public struct ParseResult
    {
        public bool ok;
        public DeviceMessage message;
        public string reason;
    }

    /// <summary>
    /// Wire protocol v1: newline-delimited JSON from the controller. See docs/PROTOCOL.md.
    /// Every line is validated here; nothing downstream sees unvalidated data.
    /// </summary>
    public static class Protocol
    {
        public const int PROTOCOL_VERSION = 1;
        public const int MAX_LINE_LENGTH = 512;

        public const string CMD_CAL = "CAL";
        public const string CMD_ZEROYAW = "ZEROYAW";
        public const string CMD_HELLO = "HELLO";

        const double UINT32_MAX = 0xffffffffL;
        const double UINT32_SPAN = 0x100000000L;

        static ParseResult Fail(string reason) => new ParseResult { ok = false, reason = reason };
        static ParseResult Ok(DeviceMessage message) => new ParseResult { ok = true, message = message };

        /// <returns>The error text, or null with the value in <paramref name="value"/>.</returns>
        static string Num(Dictionary<string, object> o, string key, double min, double max, out double value)
        {
            value = 0;
            if (!(MiniJson.Get(o, key) is double v) || double.IsNaN(v) || double.IsInfinity(v)) return $"\"{key}\" is not a finite number";
            if (v < min || v > max) return $"\"{key}\"={MiniJson.JsNumber(v)} outside [{MiniJson.JsNumber(min)}, {MiniJson.JsNumber(max)}]";
            value = v;
            return null;
        }

        static string Str(Dictionary<string, object> o, string key, int maxLen = 120)
        {
            return MiniJson.Get(o, key) is string v && v.Length <= maxLen ? v : null;
        }

        static bool TryParseState(string state, out DeviceState parsed)
        {
            switch (state)
            {
                case "calibrating": parsed = DeviceState.Calibrating; return true;
                case "ready": parsed = DeviceState.Ready; return true;
                case "sensor_error": parsed = DeviceState.SensorError; return true;
                case "motion_during_cal": parsed = DeviceState.MotionDuringCal; return true;
                case "cal_failed": parsed = DeviceState.CalFailed; return true;
                default: parsed = DeviceState.Unknown; return false;
            }
        }

        public static ParseResult ParseLine(string raw)
        {
            var line = MiniJson.JsTrim(raw);
            if (line.Length == 0) return Fail("empty line");
            if (line.Length > MAX_LINE_LENGTH) return Fail("line too long");
            if (line[0] != '{') return Fail("not a JSON object");
            object data;
            try
            {
                data = MiniJson.Parse(line);
            }
            catch (FormatException)
            {
                return Fail("invalid JSON");
            }
            var o = (Dictionary<string, object>)data; // a line starting with { can only parse to an object

            var type = MiniJson.Get(o, "t");
            switch (type as string)
            {
                case "imu":
                {
                    var s = new ImuSample();
                    var error = Num(o, "s", 0, UINT32_MAX, out s.seq)
                        ?? Num(o, "ms", 0, UINT32_MAX, out s.deviceMs)
                        ?? Num(o, "p", -90.5, 90.5, out s.pitch)
                        ?? Num(o, "r", -180.5, 180.5, out s.roll)
                        ?? Num(o, "y", -180.5, 180.5, out s.yaw)
                        ?? Num(o, "pr", -2000, 2000, out s.pitchRate)
                        ?? Num(o, "rr", -2000, 2000, out s.rollRate)
                        ?? Num(o, "yr", -2000, 2000, out s.yawRate)
                        ?? Num(o, "a", 0, 16, out s.accelG);
                    if (error != null) return Fail(error);
                    if (s.seq != Math.Floor(s.seq)) return Fail("\"s\" is not an integer");
                    return Ok(s);
                }
                case "status":
                {
                    var state = Str(o, "state");
                    if (string.IsNullOrEmpty(state) || !TryParseState(state, out var parsed))
                        return Fail($"unknown status \"{MiniJson.JsString(MiniJson.Get(o, "state"))}\"");
                    return Ok(new StatusMessage { state = parsed, message = Str(o, "msg") ?? "" });
                }
                case "hello":
                {
                    var firmware = Str(o, "fw");
                    var protoError = Num(o, "proto", 1, 1000, out var proto);
                    var rateError = Num(o, "rate", 1, 1000, out var rateHz);
                    if (string.IsNullOrEmpty(firmware)) return Fail("\"fw\" missing");
                    if (protoError != null) return Fail(protoError);
                    if (rateError != null) return Fail(rateError);
                    return Ok(new HelloMessage { firmware = firmware, proto = proto, rateHz = rateHz });
                }
                case "ack":
                {
                    var command = Str(o, "cmd");
                    if (string.IsNullOrEmpty(command)) return Fail("\"cmd\" missing");
                    var error = Str(o, "err");
                    return Ok(new AckMessage { command = command, error = string.IsNullOrEmpty(error) ? null : error });
                }
                default:
                    return Fail($"unknown message type \"{MiniJson.JsString(type)}\"");
            }
        }

        /// <summary>Serialize a sample exactly as the firmware does. Used by the simulator and tests.</summary>
        public static string EncodeImu(ImuSample s)
        {
            return "{\"t\":\"imu\",\"s\":" + MiniJson.JsNumber(s.seq) + ",\"ms\":" + MiniJson.JsNumber(s.deviceMs)
                + ",\"p\":" + MiniJson.JsToFixed(s.pitch, 2) + ",\"r\":" + MiniJson.JsToFixed(s.roll, 2) + ",\"y\":" + MiniJson.JsToFixed(s.yaw, 2)
                + ",\"pr\":" + MiniJson.JsToFixed(s.pitchRate, 1) + ",\"rr\":" + MiniJson.JsToFixed(s.rollRate, 1) + ",\"yr\":" + MiniJson.JsToFixed(s.yawRate, 1)
                + ",\"a\":" + MiniJson.JsToFixed(s.accelG, 3) + "}";
        }

        /// <summary>Number of samples lost between two sequence numbers (handles uint32 wrap).</summary>
        public static double SeqGap(double prev, double next)
        {
            var diff = (next - prev + UINT32_SPAN) % UINT32_SPAN;
            // A huge forward jump (or going backwards) means the device restarted, not loss.
            return diff == 0 || diff > 10000 ? 0 : diff - 1;
        }
    }
}
