namespace Skybound
{
    /// <summary>
    /// Everything the HUD knows about the flight. The HUD reads nothing else from the simulation, so
    /// it can be driven by a test or another flight model through this one interface.
    /// Units are the ones printed on the instruments: knots, feet, feet per minute, degrees.
    /// </summary>
    public interface IFlightDataProvider
    {
        float AirspeedKt { get; }
        float GroundSpeedKt { get; }
        float AltitudeFt { get; }
        /// <summary>Height above the terrain directly below.</summary>
        float AglFt { get; }
        /// <summary>0–360.</summary>
        float HeadingDeg { get; }
        float VerticalSpeedFpm { get; }
        float PitchDeg { get; }
        float RollDeg { get; }
        float AoaDeg { get; }
        /// <summary>In g.</summary>
        float LoadFactor { get; }

        /// <summary>Metres north of the runway 36 threshold, for the minimap.</summary>
        float NorthM { get; }
        /// <summary>Metres east of the runway 36 threshold, for the minimap.</summary>
        float EastM { get; }

        /// <summary>Lever position, 0–1.</summary>
        float Throttle { get; }
        /// <summary>Power the engine is actually producing, 0–1.</summary>
        float EnginePower { get; }
        /// <summary>−1 to 1.</summary>
        float Trim { get; }
        /// <summary>0–1.</summary>
        float Brake { get; }

        FlightPhase Phase { get; }
        bool Stalled { get; }
        bool Overspeed { get; }
        bool TailStrike { get; }
        string CrashReason { get; }

        SpeedBands Bands { get; }
        /// <summary>Easy flight's target speed, or 0 when there isn't one.</summary>
        float TargetSpeedKt { get; }
        /// <summary>Easy flight's target climb or descent.</summary>
        float TargetVsFpm { get; }

        /// <summary>False hides the fuel gauge and its warning.</summary>
        bool HasFuel { get; }
        /// <summary>0–1.</summary>
        float Fuel { get; }

        /// <summary>False hides the flap indicator.</summary>
        bool HasFlaps { get; }
        /// <summary>0–1.</summary>
        float Flaps { get; }

        /// <summary>False means fixed gear: always down, and GEAR UP can't happen.</summary>
        bool HasRetractableGear { get; }
        bool GearDown { get; }
    }
}
