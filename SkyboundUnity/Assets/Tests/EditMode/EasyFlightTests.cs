using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Skybound;
using static Skybound.FlightMath;

// Ported test by test from src/flight/easyFlight.test.ts.
public class EasyFlightTests
{
    struct Sample
    {
        public double bank, pitch, vs, ias, alt, hdg;
        public bool stalled, tail, ground;
    }

    /// <summary>Fly the real flight model with Easy flight in the loop at 60 Hz.</summary>
    class Harness
    {
        public readonly FlightModel m = new FlightModel();
        public readonly EasyFlight easy = new EasyFlight();
        public readonly EasyCommand cmd = new EasyCommand();
        public readonly PilotInput input = new PilotInput();
        public readonly List<Sample> trace = new List<Sample>();

        public Harness(bool airborne = true)
        {
            if (airborne)
            {
                var (trim, throttle) = m.ResetAirborne(4000, 600, 49);
                easy.Reset(true, trim, throttle);
            }
            else easy.Reset(false);
        }

        public Telemetry Fly(double seconds, Action each = null)
        {
            for (var i = 0; i < seconds * 60; i++)
            {
                each?.Invoke();
                var o = easy.Update(m.telemetry, cmd, 1.0 / 60, m.alphaWarn / DEG);
                input.elevator = o.elevator;
                input.aileron = o.aileron;
                input.rudder = o.rudder;
                input.throttle = o.throttle;
                input.trim = 0;
                m.Advance(input, 1.0 / 60);
                var t = m.telemetry;
                trace.Add(new Sample
                {
                    bank = t.rollDeg,
                    pitch = t.pitchDeg,
                    vs = t.verticalSpeedFpm,
                    ias = t.airspeedKt,
                    alt = t.altitudeFt,
                    hdg = t.headingDeg,
                    stalled = t.stalled,
                    tail = t.tailStrike,
                    ground = t.phase == FlightPhase.Ground,
                });
            }
            return m.telemetry;
        }
    }

    static IEnumerable<Sample> Last(IReadOnlyCollection<Sample> a, double seconds) => a.Skip(Math.Max(0, a.Count - (int)Math.Round(seconds * 60)));

    static double Span(IEnumerable<double> xs)
    {
        var list = xs.ToList();
        return list.Max() - list.Min();
    }

    /// <summary>Direction reversals bigger than the deadband: counts real oscillation, ignores numeric dither.</summary>
    static int Reversals(IEnumerable<double> values, double deadband = 0.2)
    {
        var xs = values.ToList();
        var n = 0;
        var dir = 0;
        var extreme = xs[0];
        foreach (var x in xs)
        {
            if (dir >= 0 && x > extreme) extreme = x;
            else if (dir <= 0 && x < extreme) extreme = x;
            if (dir >= 0 && x < extreme - deadband)
            {
                if (dir > 0) n++;
                dir = -1;
                extreme = x;
            }
            else if (dir <= 0 && x > extreme + deadband)
            {
                if (dir < 0) n++;
                dir = 1;
                extreme = x;
            }
        }
        return n;
    }

    static double Centred(double heading) => ((heading + 180) % 360) - 180;

    [Test]
    public void HandsOffHoldsLevelCruiseSpeedAndAltitude()
    {
        var h = new Harness();
        var t = h.Fly(30);
        Assert.That(Math.Abs(t.rollDeg), Is.LessThan(1));
        Assert.That(Math.Abs(t.verticalSpeedFpm), Is.LessThan(100));
        Assert.That(Math.Abs(t.airspeedKt - EASY.cruiseKt), Is.LessThan(5));
        Assert.That(Span(Last(h.trace, 10).Select(s => s.alt)), Is.LessThan(30));
    }

    [Test]
    public void NeutralInputRecoversFromAnUpsetWithoutOscillating()
    {
        var h = new Harness();
        h.m.state.q = FromEuler(40 * DEG, -12 * DEG, 0); // banked 40°, nose 12° down
        var t = h.Fly(20);
        Assert.That(Math.Abs(t.rollDeg), Is.LessThan(2));
        Assert.That(Math.Abs(t.verticalSpeedFpm), Is.LessThan(150));
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(Reversals(h.trace.Select(s => s.bank)), Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void GentleTurnGivesASmallSteadyBankAndHoldsAltitude()
    {
        var h = new Harness();
        h.cmd.roll = 0.25;
        var t = h.Fly(20);
        Assert.That(t.rollDeg, Is.GreaterThan(6));
        Assert.That(t.rollDeg, Is.LessThan(11));
        Assert.That(Span(Last(h.trace, 8).Select(s => s.alt)), Is.LessThan(40));
        Assert.That(Math.Abs(t.sideslipDeg), Is.LessThan(3));
    }

    [Test]
    public void SharpTurnBanksToTheLimitWithoutStallingOrLosingHeight()
    {
        var h = new Harness();
        h.cmd.roll = -1;
        h.Fly(25);
        var turn = h.trace;
        Assert.That(turn.Min(s => s.bank), Is.GreaterThan(-(EASY.maxBankDeg + 3)));
        Assert.That(turn[turn.Count - 1].bank, Is.LessThan(-30));
        Assert.That(turn.Any(s => s.stalled), Is.False);
        Assert.That(turn.Min(s => s.alt), Is.GreaterThan(1968 - 120));
        // No steady sink once established in the turn.
        Assert.That(Math.Abs(turn[turn.Count - 1].vs), Is.LessThan(80));
        Assert.That(Reversals(Last(turn, 15).Select(s => s.bank)), Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void ClimbsAndDescendsAtTheCommandedRateWhileAutoThrottleHoldsSpeed()
    {
        var h = new Harness();
        h.cmd.pitch = 0.5;
        var t = h.Fly(15);
        Assert.That(t.verticalSpeedFpm, Is.GreaterThan(400));
        Assert.That(t.verticalSpeedFpm, Is.LessThan(650));
        Assert.That(t.airspeedKt, Is.GreaterThan(80));
        h.cmd.pitch = -1;
        t = h.Fly(15);
        Assert.That(t.verticalSpeedFpm, Is.LessThan(-850));
        Assert.That(t.verticalSpeedFpm, Is.GreaterThan(-1150));
        Assert.That(t.pitchDeg, Is.GreaterThanOrEqualTo(EASY.minPitchDeg - 0.5));
        Assert.That(t.airspeedKt, Is.LessThan(115));
        h.cmd.pitch = 0;
        t = h.Fly(12);
        Assert.That(Math.Abs(t.verticalSpeedFpm), Is.LessThan(150));
    }

    [Test]
    public void FullBackStickNeverStallsTheAircraft()
    {
        var h = new Harness();
        h.easy.speedTargetKt = EASY.minTargetKt;
        h.cmd.pitch = 1;
        h.Fly(40);
        Assert.That(h.trace.Any(s => s.stalled), Is.False);
        Assert.That(h.trace.Max(s => s.pitch), Is.LessThanOrEqualTo(EASY.maxPitchDeg + 1));
    }

    [Test]
    public void HeldFullDescentNearTheGroundBecomesAGentleArrival()
    {
        var h = new Harness();
        h.m.ResetAirborne(1500, 60, 40);
        h.easy.Reset(true, 0, 0.3);
        h.easy.speedTargetKt = 65;
        h.cmd.pitch = -1;
        var t = h.Fly(60);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(t.crashReason, Is.Null);
    }

    [TestCase(0.0)]
    [TestCase(0.3)]
    [TestCase(1.0)]
    public void TakesOffWithOneThrottlePress(double pull)
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        Assert.That(h.easy.speedTargetKt, Is.EqualTo(EASY.takeoffTargetKt));
        h.cmd.pitch = pull;
        var t = h.Fly(50);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(t.altitudeFt, Is.GreaterThan(350));
        Assert.That(h.trace.Any(s => s.stalled), Is.False);
        Assert.That(h.m.telemetry.crashReason, Is.Null);
        Assert.That(h.trace.Any(s => s.tail), Is.False); // never over-rotates on the runway
        Assert.That(h.trace.Where(s => s.ground).Max(s => s.pitch), Is.LessThan(11));
        Assert.That(h.trace.FindIndex(s => s.alt > 8), Is.GreaterThan(0));
    }

    [Test]
    public void TakeOffClimbEndsAt400FtAndThenHoldsAltitudeHandsOff()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        h.Fly(80);
        Assert.That(h.easy.takeoff, Is.False);
        var alts = Last(h.trace, 10).Select(s => s.alt).ToList();
        Assert.That(alts.Min(), Is.GreaterThan(380));
        Assert.That(Span(alts), Is.LessThan(40));
    }

    [Test]
    public void TaxiingSidewaysTiltSteersWithoutRollingTheWings()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        h.cmd.roll = 0.6;
        var t = h.Fly(2.5);
        Assert.That(t.airspeedKt, Is.LessThan(15));
        Assert.That(t.headingDeg, Is.GreaterThan(2));
        Assert.That(t.headingDeg, Is.LessThan(180));
        Assert.That(h.trace.Max(s => Math.Abs(s.bank)), Is.LessThan(3));
    }

    [Test]
    public void TakeOffRollHoldsItsHeadingWithTheControllerHeldTilted()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        h.cmd.roll = 0.6; // pilot holds a firm sideways tilt the whole roll
        var t = h.Fly(25);
        var fromRoll = h.trace.FindIndex(s => s.ias >= 15);
        var hdgs = h.trace.Skip(fromRoll).Where(s => s.ground).Select(s => Centred(s.hdg));
        Assert.That(Span(hdgs), Is.LessThan(5));
        Assert.That(t.crashReason, Is.Null);
        Assert.That(h.trace.Any(s => !s.ground), Is.True); // still takes off
    }

    [Test]
    public void TakeOffRollStaysStraightWithAWobblingHand()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        var k = 0;
        h.Fly(30, () =>
        {
            k++;
            h.cmd.roll = 0.45 * Math.Sin(k / 23.0) + 0.25 * Math.Sin(k / 7.0); // ±0.7 swaying
        });
        Assert.That(h.trace.Where(s => s.ground).Max(s => Math.Abs(Centred(s.hdg))), Is.LessThan(6));
        Assert.That(h.trace.Any(s => s.tail), Is.False);
        Assert.That(h.trace[h.trace.Count - 1].alt, Is.GreaterThan(20));
    }

    [Test]
    public void ASmallLeftoverTiltDoesNotMakeTheTakeOffRollVeer()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        h.cmd.roll = 0.12;
        h.cmd.pitch = -0.5; // stay on the runway for the check
        var t = h.Fly(12);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(Math.Abs(Centred(t.headingDeg)), Is.LessThan(1));
    }

    [Test]
    public void ThrottlingBackToZeroOnTheRunwayAbortsTheTakeOff()
    {
        var h = new Harness(false);
        h.easy.AdjustSpeed(1, 1.0 / 60, true);
        h.Fly(8);
        for (var i = 0; i < 60 * 4; i++) h.easy.AdjustSpeed(-1, 1.0 / 60, true);
        Assert.That(h.easy.speedTargetKt, Is.EqualTo(0));
        Assert.That(h.easy.takeoff, Is.False);
        h.cmd.pitch = 0;
        var t = h.Fly(10);
        Assert.That(t.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(t.throttle, Is.EqualTo(0));
    }

    [Test]
    public void SensorDropoutLevelsTheAircraft()
    {
        var h = new Harness();
        h.cmd.roll = 0.8;
        h.cmd.pitch = 0.4;
        h.Fly(8);
        // The control pipeline fades a lost sensor to neutral over 0.35 s.
        var k = 1.0;
        h.Fly(0.35, () =>
        {
            k = Math.Max(0, k - 1 / (0.35 * 60));
            h.cmd.roll = 0.8 * k;
            h.cmd.pitch = 0.4 * k;
        });
        var t = h.Fly(15);
        Assert.That(Math.Abs(t.rollDeg), Is.LessThan(2));
        Assert.That(Math.Abs(t.verticalSpeedFpm), Is.LessThan(150));
    }
}
