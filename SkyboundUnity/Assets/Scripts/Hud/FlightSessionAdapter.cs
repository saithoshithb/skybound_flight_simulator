using System;

namespace Skybound
{
    /// <summary>
    /// Connects the existing flight code to the HUD. It only reads, so the flight model stays exactly
    /// as it was.
    ///
    /// The flight model has no fuel, flaps or retractable gear. Gear is reported as what it really
    /// is, fixed and down, and flaps as absent. Fuel is an estimate made here from the engine's real
    /// power output: it empties the gauge and raises LOW FUEL, but the engine knows nothing about it
    /// and keeps running at zero.
    /// </summary>
    public sealed class FlightSessionAdapter : IFlightDataProvider
    {
        /// <summary>How long full tanks would last at full power.</summary>
        public const double FULL_POWER_ENDURANCE_S = 3 * 3600;
        const double MS_TO_KT = 1.9438;

        readonly FlightSession session;
        double fuel = 1;
        double fuelClock;
        string bandsFor = "";
        SpeedBands bands;

        public FlightSessionAdapter(FlightSession flightSession)
        {
            session = flightSession;
        }

        Telemetry T => session.flight.telemetry;

        /// <summary>Once a frame. The model's clock goes back to 0 on a restart, which refills the tanks.</summary>
        public void Tick()
        {
            var now = T.timeS;
            fuel = now < fuelClock ? 1 : Burn(fuel, T.engine, now - fuelClock);
            fuelClock = now;
        }

        public static double Burn(double fuelLeft, double enginePower, double seconds)
        {
            return Math.Max(0, fuelLeft - enginePower * seconds / FULL_POWER_ENDURANCE_S);
        }

        public float AirspeedKt => (float)T.airspeedKt;
        public float GroundSpeedKt => (float)T.groundSpeedKt;
        public float AltitudeFt => (float)T.altitudeFt;
        public float AglFt => (float)T.aglFt;
        public float HeadingDeg => (float)T.headingDeg;
        public float VerticalSpeedFpm => (float)T.verticalSpeedFpm;
        public float PitchDeg => (float)T.pitchDeg;
        public float RollDeg => (float)T.rollDeg;
        public float AoaDeg => (float)T.aoaDeg;
        public float LoadFactor => (float)T.loadFactor;
        public float NorthM => (float)session.flight.state.pos.x;
        public float EastM => (float)session.flight.state.pos.y;
        public float Throttle => (float)T.throttle;
        public float EnginePower => (float)T.engine;
        public float Trim => (float)T.trim;
        public float Brake => (float)T.brake;
        public FlightPhase Phase => T.phase;
        public bool Stalled => T.stalled;
        public bool Overspeed => T.overspeed;
        public bool TailStrike => T.tailStrike;
        public string CrashReason => T.crashReason;

        public SpeedBands Bands
        {
            get
            {
                var ac = session.flight.ac;
                if (bandsFor != ac.id)
                {
                    bandsFor = ac.id;
                    bands = new SpeedBands { stallKt = (float)FlightModel.StallSpeedKt(ac), normalMaxKt = (float)ac.easy.maxKt, vneKt = (float)(ac.vneMs * MS_TO_KT) };
                }
                return bands;
            }
        }

        public float TargetSpeedKt => SettingsStore.Get().easyFlight ? (float)session.easy.speedTargetKt : 0;
        public float TargetVsFpm => (float)session.pilotInfo.targetVsFpm;

        public bool HasFuel => true;
        public float Fuel => (float)fuel;
        public bool HasFlaps => false;
        public float Flaps => 0;
        public bool HasRetractableGear => false;
        public bool GearDown => true;
    }
}
