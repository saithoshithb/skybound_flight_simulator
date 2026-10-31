using System;
using System.Linq;
using NUnit.Framework;
using Skybound;
using static Skybound.FlightMath;

// Ported test by test from src/flight/aircraftTypes.test.ts.
public class AircraftTypeTests
{
    static readonly AircraftDef[] All = { AircraftCatalog.Trainer, AircraftCatalog.Sport };

    static Telemetry Run(FlightModel m, PilotInput input, double s, Action each = null)
    {
        for (var i = 0; i < s * 60; i++)
        {
            each?.Invoke();
            m.Advance(input, 1.0 / 60);
        }
        return m.telemetry;
    }

    [Test]
    public void EveryAircraftHasAUniqueIdSaneTrimAndSitsStillWhenParked()
    {
        Assert.That(All.Select(a => a.id).Distinct().Count(), Is.EqualTo(All.Length));
        foreach (var ac in All)
        {
            var t = FlightModel.TrimLevel(ac, ac.easy.cruiseKt * 0.5144, 600);
            Assert.That(t.alpha / DEG, Is.GreaterThan(-2));
            Assert.That(t.alpha / DEG, Is.LessThan(8));
            Assert.That(t.throttle, Is.LessThan(0.95));
            var p = Run(new FlightModel(ac), new PilotInput { brake = 1 }, 4);
            Assert.That(p.phase, Is.EqualTo(FlightPhase.Ground));
            Assert.That(p.groundSpeedKt, Is.LessThan(0.3));
        }
    }

    [Test]
    public void TheSportPlaneIsFasterAndRollsQuickerThanTheTrainer()
    {
        double Top(AircraftDef ac)
        {
            var m = new FlightModel(ac);
            var (trim, _) = m.ResetAirborne(4000, 600, ac.easy.cruiseKt * 0.5144);
            var input = new PilotInput { throttle = 1, trim = trim };
            Run(m, input, 90, () => input.elevator = Math.Max(-1, Math.Min(1, -m.telemetry.verticalSpeedFpm / 600 - m.state.w.y * 2)));
            return m.telemetry.airspeedKt;
        }
        double RollRate(AircraftDef ac)
        {
            var m = new FlightModel(ac);
            var (trim, throttle) = m.ResetAirborne(4000, 600, 55);
            Run(m, new PilotInput { trim = trim, throttle = throttle, aileron = 1 }, 1);
            return m.telemetry.rollRateDps;
        }
        Assert.That(Top(AircraftCatalog.Sport), Is.GreaterThan(Top(AircraftCatalog.Trainer) + 10));
        Assert.That(RollRate(AircraftCatalog.Sport), Is.GreaterThan(RollRate(AircraftCatalog.Trainer)));
    }

    [Test]
    public void TheSportPlaneTakesOffManuallyWithFullPowerAndAGentlePull()
    {
        var m = new FlightModel(AircraftCatalog.Sport);
        var input = new PilotInput { throttle = 1 };
        double liftoff = 0;
        Run(m, input, 45, () =>
        {
            var t = m.telemetry;
            input.elevator = t.airspeedKt > 62 ? (t.pitchDeg > 8 ? 0.05 : 0.3) : 0;
            if (liftoff == 0 && t.phase == FlightPhase.Airborne && t.altitudeFt > 3) liftoff = t.airspeedKt;
        });
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(liftoff, Is.GreaterThan(58));
        Assert.That(liftoff, Is.LessThan(85));
    }

    [Test]
    public void EasyFlightFliesTheSportPlaneToo()
    {
        var sport = AircraftCatalog.Sport;
        var m = new FlightModel(sport);
        var easy = new EasyFlight();
        easy.Configure(sport);
        easy.Reset(false);
        easy.AdjustSpeed(1, 1.0 / 60, true);
        Assert.That(easy.speedTargetKt, Is.EqualTo(sport.easy.takeoffKt));
        var input = new PilotInput();
        var cmd = new EasyCommand();
        bool stalled = false, tail = false;
        Telemetry Fly(double s) => Run(m, input, s, () =>
        {
            var o = easy.Update(m.telemetry, cmd, 1.0 / 60, m.alphaWarn / DEG);
            input.elevator = o.elevator;
            input.aileron = o.aileron;
            input.rudder = o.rudder;
            input.throttle = o.throttle;
            input.trim = 0;
            stalled |= m.telemetry.stalled;
            tail |= m.telemetry.tailStrike;
        });
        Fly(80);
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(m.telemetry.crashReason, Is.Null);
        Assert.That(Math.Abs(m.telemetry.airspeedKt - sport.easy.takeoffKt), Is.LessThan(8));
        cmd.roll = 1;
        var t = Fly(20);
        Assert.That(t.rollDeg, Is.GreaterThan(30));
        Assert.That(t.rollDeg, Is.LessThan(38));
        Assert.That(stalled, Is.False);
        Assert.That(tail, Is.False);
    }

    [Test]
    public void SwitchingAircraftResetsToTheRunwayWithTheNewType()
    {
        var m = new FlightModel(AircraftCatalog.Trainer);
        m.ResetAirborne();
        m.SetAircraft(AircraftCatalog.Sport);
        Assert.That(m.ac.id, Is.EqualTo("sport"));
        Assert.That(m.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
    }
}
