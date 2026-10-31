using static Skybound.FlightMath;

namespace Skybound
{
    public static class Assists
    {
        /// <summary>
        /// Bank-angle assist: the roll command sets a target bank angle instead of a
        /// roll rate. A PD law on bank error drives the ailerons, so the aircraft
        /// still rolls only through aileron moments and its own inertia and damping.
        /// This suits a hand-held controller that the pilot naturally holds tilted;
        /// levelling the controller levels the wings. Off on the ground.
        /// </summary>
        public const double MAX_ASSIST_BANK_DEG = 45;

        // 5° drove a ±1.8° sideslip limit cycle (wing rock) through the rudder lag; 10° is clean.
        const double FULL_RUDDER_AT_SIDESLIP_DEG = 10;
        /// <summary>Adverse yaw is proportional to aileron, so lead it rather than wait for sideslip.</summary>
        const double AILERON_FEEDFORWARD = 0.6;

        /// <param name="errorDeg">Bank error (deg) for full aileron.</param>
        /// <param name="rateDps">Roll rate (deg/s) that cancels it.</param>
        public static double BankAssist(double rollCmd, Telemetry t, double maxBankDeg = MAX_ASSIST_BANK_DEG, double maxAileron = 1, double errorDeg = 20, double rateDps = 35)
        {
            if (t.phase != FlightPhase.Airborne) return rollCmd;
            var target = Clamp(rollCmd, -1, 1) * maxBankDeg;
            var error = ((target - t.rollDeg + 540) % 360) - 180;
            return Clamp(error / errorDeg - t.rollRateDps / rateDps, -maxAileron, maxAileron);
        }

        /// <summary>
        /// Auto-coordination ("auto-rudder"): rudder that drives sideslip toward zero,
        /// standing in for the pilot's feet. Without it, aileron alone gives a light
        /// aircraft several degrees of adverse yaw, which feels wrong when the motion
        /// controller has no rudder axis. Inactive on the ground so nose-wheel steering
        /// stays exactly what the pilot commands.
        ///
        /// Sideslip > 0 means the relative wind comes from the right (nose left of the
        /// flight path), which needs right (+) rudder.
        /// </summary>
        public static double AutoRudder(Telemetry t, double aileron, double fullAtSideslipDeg = FULL_RUDDER_AT_SIDESLIP_DEG, double aileronFeedforward = AILERON_FEEDFORWARD)
        {
            if (t.phase != FlightPhase.Airborne || t.airspeedKt < 40) return 0;
            return Clamp(t.sideslipDeg / fullAtSideslipDeg + aileronFeedforward * aileron, -1, 1);
        }
    }
}
