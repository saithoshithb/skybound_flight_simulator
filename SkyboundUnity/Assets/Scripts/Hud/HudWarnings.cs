using System;

namespace Skybound
{
    /// <summary>In order of priority: the first that applies is the one on screen.</summary>
    public enum HudWarning
    {
        None,
        PullUp,
        Stall,
        Overspeed,
        GearUp,
        LowFuel,
        TailStrike,
    }

    /// <summary>
    /// Works out which warning applies and tells whoever listens only when that changes. It has to
    /// look at the flight every frame, since the model has no events of its own; the banner doesn't.
    /// </summary>
    public sealed class HudWarnings
    {
        /// <summary>A normal approach sinks at 500–800 fpm, and Easy flight stops at 1,000.</summary>
        public const float PULL_UP_MIN_SINK_FPM = 1200;
        public const float PULL_UP_SECONDS_TO_IMPACT = 12;
        public const float GEAR_UP_BELOW_FT = 500;
        public const float LOW_FUEL = 0.15f;

        public HudWarning Current { get; private set; }

        public event Action<HudWarning> Changed;

        public void Update(IFlightDataProvider d)
        {
            var now = Evaluate(d);
            if (now == Current) return;
            Current = now;
            Changed?.Invoke(now);
        }

        public static HudWarning Evaluate(IFlightDataProvider d)
        {
            if (d.Phase == FlightPhase.Crashed) return HudWarning.None;
            var airborne = d.Phase == FlightPhase.Airborne;
            if (airborne && SinkingIntoTheGround(d)) return HudWarning.PullUp;
            if (d.Stalled) return HudWarning.Stall;
            if (d.Overspeed) return HudWarning.Overspeed;
            if (airborne && d.HasRetractableGear && !d.GearDown && d.VerticalSpeedFpm < 0 && d.AglFt < GEAR_UP_BELOW_FT) return HudWarning.GearUp;
            if (d.HasFuel && d.Fuel <= LOW_FUEL) return HudWarning.LowFuel;
            if (d.TailStrike) return HudWarning.TailStrike;
            return HudWarning.None;
        }

        static bool SinkingIntoTheGround(IFlightDataProvider d)
        {
            var sink = -d.VerticalSpeedFpm;
            return sink >= PULL_UP_MIN_SINK_FPM && d.AglFt / (sink / 60) < PULL_UP_SECONDS_TO_IMPACT;
        }

        public static string Text(HudWarning w)
        {
            switch (w)
            {
                case HudWarning.PullUp: return "PULL UP";
                case HudWarning.Stall: return "STALL";
                case HudWarning.Overspeed: return "OVERSPEED";
                case HudWarning.GearUp: return "GEAR UP";
                case HudWarning.LowFuel: return "LOW FUEL";
                case HudWarning.TailStrike: return "TAIL STRIKE";
                default: return "";
            }
        }

        /// <summary>Amber rather than red: act soon, not now.</summary>
        public static bool IsCaution(HudWarning w) => w == HudWarning.GearUp || w == HudWarning.LowFuel || w == HudWarning.TailStrike;
    }
}
