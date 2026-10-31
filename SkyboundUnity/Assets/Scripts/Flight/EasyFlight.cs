using System;
using static Skybound.FlightMath;

namespace Skybound
{
    public class EasyCommand
    {
        /// <summary>-1…1, + = climb / rotate</summary>
        public double pitch;
        /// <summary>-1…1, + = bank right</summary>
        public double roll;
    }

    public struct EasyOutput
    {
        public double elevator, aileron, rudder, throttle;
        /// <summary>For the HUD: what the assist is currently aiming for.</summary>
        public double targetVsFpm, targetBankDeg;
    }

    public static class EASY
    {
        public const double maxBankDeg = 35;
        public const double maxAileron = 0.6; // caps roll rate to a calm ~15°/s
        public const double maxClimbFpm = 1000;
        public const double maxSinkFpm = 1000;
        public const double vsSlewFpmPerS = 700;
        public const double minPitchDeg = -10;
        public const double maxPitchDeg = 15;
        public const double rotatePitchDeg = 10;
        public const double rotateMinKt = 50;
        public const double autoRotateKt = 55;
        public const double takeoffPitchDeg = 7;
        public const double takeoffClimbFpm = 700;
        public const double takeoffEndAglFt = 400;
        public const double groundSteer = 0.5;
        public const double groundSteerDeadzone = 0.15;
        /// <summary>Above this ground speed, tilt no longer steers: the runway heading is held instead.</summary>
        public const double taxiSteerMaxKt = 15;
        public const double stallMarginDeg = 3;
        public const double cruiseKt = 95;
        public const double minTargetKt = 60;
        public const double maxTargetKt = 115;
        public const double takeoffTargetKt = 90;
        public const double bankErrorDeg = 20;
        public const double bankRateDps = 35;
        public const double rudderFullAtSideslipDeg = 10;
        public const double rudderAileronFeedforward = 0.6;
        /// <summary>Pitch error (deg) and pitch rate (deg/s) for full elevator.</summary>
        public const double pitchErrorDeg = 12;
        public const double pitchRateDps = 12;
    }

    /// <summary>
    /// Easy flight: a stability-augmentation layer for first-time pilots.
    ///
    /// It sits between the pilot's commands and the existing flight model. It only
    /// produces ordinary control-surface and throttle commands, so the aircraft
    /// still moves through the same aerodynamics, inertia and damping:
    ///
    ///  - roll command: target bank (±35°), rate-limited ailerons, auto-rudder
    ///  - pitch command: target vertical speed (±1,000 fpm); centred = hold altitude
    ///  - attitude limits: pitch -10…+15°, angle-of-attack (stall) protection,
    ///    gentler sink near the ground
    ///  - auto-throttle holds a target airspeed (throttle keys change the target)
    ///  - an integrator acts as auto-trim, so hands-off flight is steady
    ///
    /// On the ground the wings are held level and sideways tilt steers the nose
    /// wheel. One throttle press starts an automatic take-off: full power, rotate
    /// to 7° at 55 kt (short of the 13° tail-strike angle), then climb at 700 fpm
    /// to 400 ft unless the pilot pushes forward. Pilot input still shapes it.
    /// </summary>
    public class EasyFlight
    {
        /// <summary>Height of the CG above the ground when the wheels touch (ft).</summary>
        const double GEAR_HEIGHT_FT = 3.45;

        public double speedTargetKt;
        /// <summary>Automatic take-off in progress (from the throttle press until 400 ft).</summary>
        public bool takeoff;
        /// <summary>Speeds for the current aircraft (kt).</summary>
        public EasySpeeds speeds = new EasySpeeds
        {
            cruiseKt = EASY.cruiseKt,
            takeoffKt = EASY.takeoffTargetKt,
            minKt = EASY.minTargetKt,
            maxKt = EASY.maxTargetKt,
            rotateKt = EASY.autoRotateKt,
        };

        /// <summary>Heading held during the take-off roll (null when taxiing or airborne).</summary>
        double? rollHeading;
        double vsTarget;
        double vsI;
        double pitchI;
        double throttleI;

        /// <summary>Adopt an aircraft's speeds (cruise, take-off, range, rotation).</summary>
        public void Configure(AircraftDef ac)
        {
            speeds = new EasySpeeds
            {
                cruiseKt = ac.easy.cruiseKt,
                takeoffKt = ac.easy.takeoffKt,
                minKt = ac.easy.minKt,
                maxKt = ac.easy.maxKt,
                rotateKt = ac.easy.rotateKt,
            };
        }

        /// <summary>Start from rest on the runway (idle) or in flight (cruise).</summary>
        public void Reset(bool airborne, double trimElevator = 0, double throttle = 0)
        {
            takeoff = false;
            rollHeading = null;
            speedTargetKt = airborne ? speeds.cruiseKt : 0;
            vsTarget = 0;
            vsI = 0;
            pitchI = airborne ? trimElevator : 0;
            throttleI = airborne ? throttle : 0;
        }

        /// <summary>Throttle keys move the speed target. On the ground the first press means "take off".</summary>
        public void AdjustSpeed(double dir, double dt, bool onGround)
        {
            if (dir == 0) return;
            if (dir > 0 && onGround && speedTargetKt < speeds.takeoffKt)
            {
                speedTargetKt = speeds.takeoffKt;
                takeoff = true;
            }
            else speedTargetKt = Clamp(speedTargetKt + dir * 25 * dt, onGround ? 0 : speeds.minKt, speeds.maxKt);
            if (dir < 0 && onGround && speedTargetKt < speeds.minKt)
            {
                speedTargetKt = 0; // throttle back on the runway = abort / taxi stop
                takeoff = false;
            }
        }

        public EasyOutput Update(Telemetry t, EasyCommand cmd, double dt, double alphaWarnDeg)
        {
            var airborne = t.phase == FlightPhase.Airborne;

            // ---- roll: bank target, gentle aileron, coordinated; on the ground, wings level + steer ----
            double aileron;
            double rudder;
            if (airborne)
            {
                rollHeading = null;
                aileron = Assists.BankAssist(cmd.roll, t, EASY.maxBankDeg, EASY.maxAileron, EASY.bankErrorDeg, EASY.bankRateDps);
                rudder = Assists.AutoRudder(t, aileron, EASY.rudderFullAtSideslipDeg, EASY.rudderAileronFeedforward);
            }
            else
            {
                aileron = Clamp(-t.rollDeg / EASY.bankErrorDeg - t.rollRateDps / EASY.bankRateDps, -EASY.maxAileron, EASY.maxAileron);
                if (t.airspeedKt < EASY.taxiSteerMaxKt)
                {
                    // Taxiing: tilt steers, with an extra dead zone so a slightly-off neutral can't creep.
                    var steer = Clamp(cmd.roll, -1, 1);
                    var dz = EASY.groundSteerDeadzone;
                    rudder = (Math.Abs(steer) <= dz ? 0 : Sign(steer) * ((Math.Abs(steer) - dz) / (1 - dz))) * EASY.groundSteer;
                    rollHeading = null;
                }
                else
                {
                    // Take-off roll: a hand-held controller always wobbles a little, and at speed
                    // any steering from it swerves the aircraft. Hold the heading instead.
                    rollHeading ??= t.headingDeg;
                    var err = ((rollHeading.Value - t.headingDeg + 540) % 360) - 180;
                    rudder = Clamp(err / 6 - t.turnRateDps / 5, -0.6, 0.6);
                }
            }

            // ---- pitch ----
            var agl = Math.Max(0, t.aglFt - GEAR_HEIGHT_FT);
            double targetPitch;
            if (airborne)
            {
                var desiredVs = cmd.pitch >= 0 ? cmd.pitch * EASY.maxClimbFpm : cmd.pitch * EASY.maxSinkFpm;
                // Automatic take-off climb until a safe height, unless the pilot pushes forward.
                if (takeoff)
                {
                    if (agl > EASY.takeoffEndAglFt || cmd.pitch < -0.2) takeoff = false;
                    else desiredVs = Math.Max(desiredVs, EASY.takeoffClimbFpm);
                }
                // Near the ground, limit sink so a held "descend" becomes a gentle arrival.
                if (agl < 60) desiredVs = Math.Max(desiredVs, -Math.Max(150, agl * 7));
                // Ease the target toward the request so stick changes never jerk the nose.
                var step = EASY.vsSlewFpmPerS * dt;
                vsTarget += Clamp(desiredVs - vsTarget, -step, step);

                // Pitch = flight-path angle + angle of attack × cos(bank), plus a correction on VS error.
                // (Banked, part of the angle of attack points sideways, so it raises the nose less.)
                var V = Math.Max(t.airspeedKt * 0.5144, 20);
                var gammaDeg = Math.Asin(Clamp((vsTarget * 0.00508) / V, -0.5, 0.5)) / DEG;
                var vsError = vsTarget - t.verticalSpeedFpm;
                // Slow integral removes the steady sink in turns (and any model mismatch).
                vsI = Clamp(vsI + (vsError / 1500) * dt, -4, 4);
                targetPitch = gammaDeg + t.aoaDeg * Math.Cos(t.rollDeg * DEG) + Clamp(vsError / 300, -5, 5) + vsI;
                // Stall protection: never ask for more angle of attack than the margin allows.
                var aoaLimit = alphaWarnDeg - EASY.stallMarginDeg;
                if (t.aoaDeg > aoaLimit) targetPitch = Math.Min(targetPitch, t.pitchDeg - (t.aoaDeg - aoaLimit) * 1.5);
                targetPitch = Clamp(targetPitch, EASY.minPitchDeg, EASY.maxPitchDeg);
            }
            else
            {
                // Start the airborne VS target at the take-off climb so lift-off doesn't sag.
                vsTarget = takeoff ? EASY.takeoffClimbFpm : 0;
                vsI = 0;
                var pull = Math.Max(0, cmd.pitch);
                if (takeoff && t.airspeedKt >= speeds.rotateKt)
                    targetPitch = cmd.pitch < -0.2 ? 0 : Math.Min(EASY.rotatePitchDeg, EASY.takeoffPitchDeg + pull * 3);
                else targetPitch = t.airspeedKt > EASY.rotateMinKt ? pull * EASY.rotatePitchDeg : 0;
            }

            var pitchError = targetPitch - t.pitchDeg;
            if (airborne) pitchI = Clamp(pitchI + pitchError * 0.015 * dt, -0.4, 0.4);
            else pitchI *= Math.Exp(-dt / 0.5);
            var elevator = Clamp(pitchError / EASY.pitchErrorDeg - t.pitchRateDps / EASY.pitchRateDps + pitchI, -0.7, 0.7);
            // Rolling slowly on the runway: leave the elevator alone.
            if (!airborne && targetPitch == 0 && t.airspeedKt < EASY.rotateMinKt) elevator = 0;

            // ---- auto-throttle ----
            double throttle;
            if (speedTargetKt <= 0)
            {
                throttle = 0;
                throttleI = 0;
            }
            else if (!airborne)
            {
                // Take-off roll: full power until near the target.
                throttle = t.airspeedKt < speedTargetKt - 5 ? 1 : 0.6;
            }
            else
            {
                var err = speedTargetKt - t.airspeedKt;
                throttleI = Clamp(throttleI + err * 0.004 * dt, 0, 1);
                // Climbing costs power, descending gives it back.
                throttle = Clamp(throttleI + err * 0.04 + vsTarget / 2500, 0, 1);
            }

            return new EasyOutput
            {
                elevator = elevator,
                aileron = aileron,
                rudder = rudder,
                throttle = throttle,
                targetVsFpm = airborne ? vsTarget : 0,
                targetBankDeg = airborne ? Clamp(cmd.roll, -1, 1) * EASY.maxBankDeg : 0,
            };
        }
    }
}
