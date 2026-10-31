using System;
using NUnit.Framework;
using Skybound;
using static Skybound.FlightMath;

// Ported test by test from src/flight/flight.test.ts.
public class FlightModelTests
{
    const double CloseTo2 = 0.005; // vitest toBeCloseTo(x) default precision

    static Telemetry Run(FlightModel m, PilotInput input, double seconds, Action<double> each = null)
    {
        for (double t = 0; t < seconds; t += 1.0 / 60)
        {
            m.Advance(input, 1.0 / 60);
            each?.Invoke(t);
        }
        return m.telemetry;
    }

    static double TurnedFrom(double heading, double heading0) => ((heading - heading0 + 540) % 360) - 180;

    [Test]
    public void EulerRoundTripAndFrameDirections()
    {
        var e = ToEuler(FromEuler(10 * DEG, 20 * DEG, 30 * DEG));
        Assert.That(e.roll / DEG, Is.EqualTo(10).Within(CloseTo2));
        Assert.That(e.pitch / DEG, Is.EqualTo(20).Within(CloseTo2));
        Assert.That(e.yaw / DEG, Is.EqualTo(30).Within(CloseTo2));
        // Yaw 90° points the nose east; pitch up points it at -z (up in NED).
        Assert.That(Rotate(FromEuler(0, 0, 90 * DEG), new V3(1, 0, 0)).y, Is.EqualTo(1).Within(CloseTo2));
        Assert.That(Rotate(FromEuler(0, 30 * DEG, 0), new V3(1, 0, 0)).z, Is.EqualTo(-0.5).Within(CloseTo2));
    }

    [Test]
    public void LiftBreaksDownPastTheStallAngle()
    {
        var m = new FlightModel();
        var trainer = AircraftCatalog.Trainer;
        var clMax = FlightModel.LiftCoefficient(trainer, m.alphaCLmax);
        Assert.That(clMax, Is.GreaterThan(1.4));
        Assert.That(clMax, Is.LessThan(1.7));
        Assert.That(m.alphaCLmax / DEG, Is.GreaterThan(14));
        Assert.That(m.alphaCLmax / DEG, Is.LessThan(18));
        Assert.That(FlightModel.LiftCoefficient(trainer, 5 * DEG), Is.EqualTo(0.31 + 5.143 * 5 * DEG).Within(CloseTo2));
        Assert.That(FlightModel.LiftCoefficient(trainer, 26 * DEG), Is.LessThan(clMax * 0.4));
    }

    [Test]
    public void TrimGivesPlausibleCruiseNumbers()
    {
        var t = FlightModel.TrimLevel(AircraftCatalog.Trainer, 50, 300);
        Assert.That(t.alpha / DEG, Is.GreaterThan(0));
        Assert.That(t.alpha / DEG, Is.LessThan(6));
        Assert.That(t.throttle, Is.GreaterThan(0.3));
        Assert.That(t.throttle, Is.LessThan(0.9));
    }

    [Test]
    public void SitsStillOnTheRunwayWithoutBouncingOrCreeping()
    {
        var m = new FlightModel();
        var t = Run(m, new PilotInput { brake = 1 }, 5);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(t.groundSpeedKt, Is.LessThan(0.2));
        Assert.That(Math.Abs(t.pitchDeg), Is.LessThan(2));
        Assert.That(Math.Abs(t.verticalSpeedFpm), Is.LessThan(5));
        Assert.That(t.wheelsOnGround, Is.EqualTo(3));
    }

    [Test]
    public void HoldsTrimmedLevelFlightHandsOff()
    {
        var m = new FlightModel();
        var (trim, throttle) = m.ResetAirborne(4000, 300, 50);
        var start = m.telemetry.altitudeFt;
        var t = Run(m, new PilotInput { trim = trim, throttle = throttle }, 20);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(Math.Abs(t.altitudeFt - start), Is.LessThan(60));
        Assert.That(Math.Abs(t.airspeedKt - 97), Is.LessThan(5));
        Assert.That(Math.Abs(t.rollDeg), Is.LessThan(1));
    }

    [Test]
    public void TakesOffWithFullPowerAndAGentlePull()
    {
        var m = new FlightModel();
        double liftoffKt = 0, groundRollM = 0;
        var input = new PilotInput { throttle = 1 };
        Run(m, input, 45, _ =>
        {
            var t = m.telemetry;
            input.elevator = t.airspeedKt > 55 ? 0.35 : 0;
            if (liftoffKt == 0 && t.phase == FlightPhase.Airborne && t.altitudeFt > 3)
            {
                liftoffKt = t.airspeedKt;
                groundRollM = m.state.pos.x - 60;
            }
            // Ease the pull once climbing so the test doesn't stall it.
            if (t.pitchDeg > 8) input.elevator = 0.05;
        });
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(liftoffKt, Is.GreaterThan(50));
        Assert.That(liftoffKt, Is.LessThan(72));
        Assert.That(groundRollM, Is.GreaterThan(200));
        Assert.That(groundRollM, Is.LessThan(700));
        Assert.That(m.telemetry.verticalSpeedFpm, Is.GreaterThan(200));
    }

    [Test]
    public void RollsRightWithRightAileronAndTurnsRightWhenBanked()
    {
        var m = new FlightModel();
        var (trim, throttle) = m.ResetAirborne(4000, 600, 50);
        Run(m, new PilotInput { trim = trim, throttle = throttle, aileron = 0.4 }, 1.5);
        Assert.That(m.telemetry.rollDeg, Is.GreaterThan(10));
        var heading0 = m.telemetry.headingDeg;
        Run(m, new PilotInput { trim = trim, throttle = throttle }, 5);
        Assert.That(TurnedFrom(m.telemetry.headingDeg, heading0), Is.GreaterThan(5));
    }

    [Test]
    public void AutoRudderKeepsAnAileronOnlyTurnCoordinated()
    {
        (double maxBeta, double minHeadingDelta) Fly(bool assist)
        {
            var m = new FlightModel();
            var (trim, _) = m.ResetAirborne(4000, 600, 51);
            var input = new PilotInput { trim = trim, throttle = 1, aileron = 0.5 };
            double maxBeta = 0, minHeadingDelta = 0;
            Run(m, input, 3, _ =>
            {
                input.rudder = assist ? Assists.AutoRudder(m.telemetry, input.aileron) : 0;
                maxBeta = Math.Max(maxBeta, Math.Abs(m.telemetry.sideslipDeg));
                minHeadingDelta = Math.Min(minHeadingDelta, ((m.telemetry.headingDeg + 180) % 360) - 180);
            });
            return (maxBeta, minHeadingDelta);
        }
        var off = Fly(false);
        var on = Fly(true);
        Assert.That(off.maxBeta, Is.GreaterThan(8)); // adverse yaw without feet
        Assert.That(on.maxBeta, Is.LessThan(4));
        Assert.That(on.minHeadingDelta, Is.GreaterThan(-4)); // nose no longer swings left first
    }

    [Test]
    public void BankAssistHeldTiltGivesASteadyTurnAndLevellingLevelsTheWings()
    {
        var m = new FlightModel();
        var (trim, throttle) = m.ResetAirborne(4000, 600, 50);
        var input = new PilotInput { trim = trim, throttle = throttle };
        var cmd = 0.5; // half tilt: 22.5° target
        double maxBank = 0;
        void Fly(double s) => Run(m, input, s, _ =>
        {
            input.aileron = Assists.BankAssist(cmd, m.telemetry);
            input.rudder = Assists.AutoRudder(m.telemetry, input.aileron);
            // Hold altitude roughly, like a pilot adding back pressure in the turn.
            input.elevator = Clamp(-m.telemetry.verticalSpeedFpm / 1500, -0.3, 0.4);
            maxBank = Math.Max(maxBank, m.telemetry.rollDeg);
        });
        var h0 = m.telemetry.headingDeg;
        Fly(8);
        Assert.That(m.telemetry.rollDeg, Is.GreaterThan(19));
        Assert.That(m.telemetry.rollDeg, Is.LessThan(26));
        Assert.That(maxBank, Is.LessThan(28)); // little overshoot
        Assert.That((m.telemetry.headingDeg - h0 + 360) % 360, Is.GreaterThan(15)); // turning right
        cmd = 0;
        Fly(6);
        Assert.That(Math.Abs(m.telemetry.rollDeg), Is.LessThan(3));
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
    }

    [Test]
    public void StallWarningComesAtATrainerLikeSpeedInASlowDeceleration()
    {
        var m = new FlightModel();
        var (trim, _) = m.ResetAirborne(4000, 1200, 45);
        var input = new PilotInput { trim = trim, throttle = 0 };
        double warnKt = 0;
        Run(m, input, 90, _ =>
        {
            var vs = m.telemetry.verticalSpeedFpm;
            input.elevator = Math.Max(-1, Math.Min(1, input.elevator + (-vs / 400 - m.state.w.y * 3) * 0.004));
            if (warnKt == 0 && m.telemetry.stalled) warnKt = m.telemetry.airspeedKt;
        });
        Assert.That(warnKt, Is.GreaterThan(45));
        Assert.That(warnKt, Is.LessThan(62));
    }

    [Test]
    public void StallsWhenHeldNoseHighAtIdleThenRecoversWhenReleased()
    {
        var m = new FlightModel();
        var (trim, _) = m.ResetAirborne(4000, 1200, 45);
        var stalled = false;
        Run(m, new PilotInput { trim = trim, throttle = 0, elevator = 1 }, 12, _ => stalled |= m.telemetry.stalled);
        Assert.That(stalled, Is.True);
        Run(m, new PilotInput { trim = trim, throttle = 1, elevator = -0.2 }, 6);
        Assert.That(m.telemetry.stalled, Is.False);
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
    }

    [Test]
    public void RespondsThroughDynamicsNotInstantly()
    {
        var m = new FlightModel();
        var (trim, throttle) = m.ResetAirborne(4000, 600, 50);
        var p0 = m.telemetry.pitchDeg;
        Run(m, new PilotInput { trim = trim, throttle = throttle, elevator = 1 }, 1.0 / 30);
        Assert.That(Math.Abs(m.telemetry.pitchDeg - p0), Is.LessThan(0.5));
    }

    [Test]
    public void OverRotatingOnTheRunwayScrapesTheTailWithoutEndingTheFlight()
    {
        var m = new FlightModel();
        var input = new PilotInput { throttle = 1 };
        var scraped = false;
        Run(m, input, 25, _ =>
        {
            input.elevator = m.telemetry.airspeedKt > 50 ? 1 : 0;
            scraped |= m.telemetry.tailStrike;
        });
        Assert.That(scraped, Is.True);
        Assert.That(m.telemetry.phase, Is.Not.EqualTo(FlightPhase.Crashed));
    }

    [Test]
    public void LandsGentlyFromAStabilisedDescentAndBrakesToAStop()
    {
        var m = new FlightModel();
        var (trim, _) = m.ResetAirborne(800, 25, 36);
        var input = new PilotInput { trim = trim, throttle = 0.25 };
        double? touchdownFpm = null;
        Run(m, input, 60, _ =>
        {
            var t = m.telemetry;
            var agl = -m.state.pos.z - 1.05;
            // Descend at 400 fpm, flare to 100 fpm in the last 3 m, idle and brake once down.
            // Like a pilot: vertical-speed error picks a pitch attitude, elevator holds that attitude.
            var targetVs = agl > 3 ? -400 : -100;
            if (t.phase == FlightPhase.Airborne)
            {
                var targetPitch = Clamp(1 + (targetVs - t.verticalSpeedFpm) / 250, -5, 9);
                input.elevator = Clamp((targetPitch - t.pitchDeg) / 12 - (m.state.w.y / DEG) / 15, -0.5, 0.5);
            }
            else
            {
                touchdownFpm ??= t.verticalSpeedFpm;
                input.throttle = 0;
                input.elevator = 0;
                if (t.groundSpeedKt < 50) input.brake = 1;
            }
        });
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(touchdownFpm, Is.Not.Null);
        Assert.That(m.telemetry.groundSpeedKt, Is.LessThan(1));
        Assert.That(m.telemetry.wheelsOnGround, Is.EqualTo(3));
    }

    [Test]
    public void CrashesOnASteepDiveIntoTheGround()
    {
        var m = new FlightModel();
        var (trim, _) = m.ResetAirborne(500, 40, 50);
        var t = Run(m, new PilotInput { trim = trim, throttle = 1, elevator = -1 }, 10);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Crashed));
        Assert.That(t.crashReason, Is.Not.Null.And.Not.Empty);
    }
}
