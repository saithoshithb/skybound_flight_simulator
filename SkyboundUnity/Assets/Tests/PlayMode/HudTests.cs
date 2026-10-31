using System.Collections;
using System.Linq;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>A made-up flight, for what the real one can't do yet: fuel, flaps, retractable gear.</summary>
public class FakeFlightData : IFlightDataProvider
{
    public float AirspeedKt { get; set; } = 90;
    public float GroundSpeedKt { get; set; } = 90;
    public float AltitudeFt { get; set; } = 1000;
    public float AglFt { get; set; } = 1000;
    public float HeadingDeg { get; set; } = 90;
    public float VerticalSpeedFpm { get; set; }
    public float PitchDeg { get; set; }
    public float RollDeg { get; set; }
    public float AoaDeg { get; set; } = 3;
    public float LoadFactor { get; set; } = 1;
    public float NorthM { get; set; }
    public float EastM { get; set; }
    public float Throttle { get; set; } = 0.6f;
    public float EnginePower { get; set; } = 0.6f;
    public float Trim { get; set; }
    public float Brake { get; set; }
    public FlightPhase Phase { get; set; } = FlightPhase.Airborne;
    public bool Stalled { get; set; }
    public bool Overspeed { get; set; }
    public bool TailStrike { get; set; }
    public string CrashReason { get; set; }
    public SpeedBands Bands { get; set; } = new SpeedBands { stallKt = 50, normalMaxKt = 115, vneKt = 163 };
    public float TargetSpeedKt { get; set; }
    public float TargetVsFpm { get; set; }
    public bool HasFuel { get; set; }
    public float Fuel { get; set; } = 1;
    public bool HasFlaps { get; set; }
    public float Flaps { get; set; }
    public bool HasRetractableGear { get; set; }
    public bool GearDown { get; set; } = true;
}

public class HudWarningTests
{
    [Test]
    public void NothingIsWrongInNormalFlight()
    {
        Assert.That(HudWarnings.Evaluate(new FakeFlightData()), Is.EqualTo(HudWarning.None));
    }

    [Test]
    public void ANormalApproachDoesNotShoutPullUp()
    {
        // 800 fpm all the way down to the flare.
        for (var agl = 1000f; agl > 0; agl -= 10)
        {
            var d = new FakeFlightData { AglFt = agl, VerticalSpeedFpm = -800 };
            Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "at " + agl + " ft");
        }
    }

    [Test]
    public void SinkingFastNearTheGroundIsPullUp()
    {
        var d = new FakeFlightData { AglFt = 400, VerticalSpeedFpm = -3000 };
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.PullUp));
        d.AglFt = 2000;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "plenty of height left");
        d.AglFt = 400;
        d.Phase = FlightPhase.Ground;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "not on the ground");
    }

    [Test]
    public void TheMostUrgentWarningWins()
    {
        var d = new FakeFlightData { AglFt = 400, VerticalSpeedFpm = -3000, Stalled = true, HasFuel = true, Fuel = 0.05f };
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.PullUp));
        d.VerticalSpeedFpm = 0;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.Stall));
        d.Stalled = false;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.LowFuel));
    }

    [Test]
    public void GearUpOnlyAppliesToRetractableGearComingDownLow()
    {
        var d = new FakeFlightData { AglFt = 300, VerticalSpeedFpm = -500, GearDown = false };
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "fixed gear");
        d.HasRetractableGear = true;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.GearUp));
        d.VerticalSpeedFpm = 500;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "climbing away");
        d.VerticalSpeedFpm = -500;
        d.GearDown = true;
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None), "gear down");
    }

    [Test]
    public void ACrashEndsTheWarnings()
    {
        var d = new FakeFlightData { Stalled = true, Phase = FlightPhase.Crashed };
        Assert.That(HudWarnings.Evaluate(d), Is.EqualTo(HudWarning.None));
    }

    [Test]
    public void ListenersOnlyHearChanges()
    {
        var warnings = new HudWarnings();
        var heard = 0;
        warnings.Changed += _ => heard++;
        var d = new FakeFlightData { Stalled = true };
        warnings.Update(d);
        warnings.Update(d);
        warnings.Update(d);
        Assert.That(heard, Is.EqualTo(1));
        d.Stalled = false;
        warnings.Update(d);
        Assert.That(heard, Is.EqualTo(2));
        Assert.That(warnings.Current, Is.EqualTo(HudWarning.None));
    }

    [Test]
    public void FuelBurnsWithEnginePowerAndStopsAtEmpty()
    {
        var endurance = FlightSessionAdapter.FULL_POWER_ENDURANCE_S;
        Assert.That(FlightSessionAdapter.Burn(1, 1, endurance / 2), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(FlightSessionAdapter.Burn(1, 0.5, endurance / 2), Is.EqualTo(0.75).Within(1e-9));
        Assert.That(FlightSessionAdapter.Burn(1, 0, endurance), Is.EqualTo(1));
        Assert.That(FlightSessionAdapter.Burn(0.1, 1, endurance), Is.EqualTo(0));
    }
}

/// <summary>The HUD in the real scene, with the real flight or a made-up one.</summary>
public class HudTests
{
    Keyboard keyboard;
    InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    InputSettings.BackgroundBehavior savedBackgroundBehavior;

    [SetUp]
    public void SendInputToTheGameWithoutFocus()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    }

    [TearDown]
    public void CleanUp()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    IEnumerator Tap(Key key)
    {
        if (keyboard == null) keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    static IEnumerator Fly()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        App.Instance.Session.ResetAirborne();
        yield return null;
        yield return null;
    }

    static FlightHud Hud => ((FlightScreen)App.Instance.Current).Hud;

    static bool Shown(VisualElement el)
    {
        for (var e = el; e != null; e = e.parent)
            if (e.resolvedStyle.display == DisplayStyle.None) return false;
        return true;
    }

    [UnityTest]
    public IEnumerator EveryColourComesFromTheStylesheet()
    {
        yield return Fly();
        // Styles resolve between frames; a missing colour would have logged a warning and failed this.
        Assert.That(HudTheme.Apply(Hud.Root.customStyle), Is.True);
        Assert.That(HudTheme.Accent, Is.EqualTo(Three.Hex("#4fd6ff")));
    }

    [UnityTest]
    public IEnumerator TabCyclesFullMinimalAndOff()
    {
        yield return Fly();
        var hud = Hud.Root;
        var tape = hud.Q(className: "hud-tape-left");
        var heading = hud.Q(className: "hud-heading");
        var map = hud.Q(className: "minimap");
        var attitude = hud.Q(className: "hud-attitude");
        var engine = hud.Q(className: "hud-throttle");
        var status = hud.Q(className: "hud-top-left");
        var crash = hud.Query<Label>().ToList().First(l => l.text == "CRASHED");
        Assert.That(SettingsStore.Get().hudMode, Is.EqualTo(HudMode.Full));
        Assert.That(new[] { tape, heading, map, attitude, engine, status }.All(Shown), Is.True, "Full shows everything");

        yield return Tap(Key.Tab);
        yield return null;
        Assert.That(SettingsStore.Get().hudMode, Is.EqualTo(HudMode.Minimal));
        Assert.That(Shown(tape) && Shown(heading), Is.True, "Minimal keeps the tapes and heading");
        Assert.That(Shown(map) || Shown(attitude) || Shown(engine) || Shown(status), Is.False, "Minimal drops the rest");

        yield return Tap(Key.Tab);
        yield return null;
        Assert.That(SettingsStore.Get().hudMode, Is.EqualTo(HudMode.Off));
        Assert.That(Shown(tape) || Shown(heading) || Shown(map), Is.False, "Off hides the instruments");

        // Off still says when the aircraft has crashed: the card has the Restart button.
        var session = App.Instance.Session;
        session.flight.state.pos = new V3(12250 - 100, 13500, -(session.flight.terrain(12250, 13500) - 30));
        session.flight.state.vel = new V3(50, 0, 0);
        session.flight.state.q = FlightMath.FromEuler(0, 0, 0);
        var deadline = Time.realtimeSinceStartup + 20;
        while (session.flight.telemetry.phase != FlightPhase.Crashed && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
        yield return null;
        Assert.That(Shown(crash), Is.True);
        session.ResetOnRunway();

        yield return Tap(Key.Tab);
        yield return null;
        Assert.That(SettingsStore.Get().hudMode, Is.EqualTo(HudMode.Full));
        Assert.That(Shown(map), Is.True);
    }

    [UnityTest]
    public IEnumerator TheRealFlightReportsFixedGearAndNoFlaps()
    {
        yield return Fly();
        var hud = Hud.Root;
        var texts = hud.Query<Label>().ToList().Select(l => l.text).ToList();
        Assert.That(texts, Does.Contain("FIXED"));
        var flaps = hud.Query<Label>().ToList().First(l => l.text == "FLAPS");
        var fuel = hud.Query<Label>().ToList().First(l => l.text == "FUEL");
        Assert.That(Shown(flaps), Is.False);
        Assert.That(Shown(fuel), Is.True);
        Assert.That(Hud.Warning, Is.EqualTo(HudWarning.None));
    }

    [UnityTest]
    public IEnumerator AMadeUpFlightDrivesEveryInstrument()
    {
        yield return Fly();
        var fake = new FakeFlightData { HasFuel = true, Fuel = 0.1f, HasFlaps = true, Flaps = 0.5f, HasRetractableGear = true, GearDown = false, HeadingDeg = 123, AirspeedKt = 111 };
        Hud.Data = fake;
        yield return null;
        yield return null;
        var hud = Hud.Root;
        var labels = hud.Query<Label>().ToList();
        var banner = labels.First(l => l.ClassListContains("hud-warn"));
        Assert.That(banner.text, Is.EqualTo("LOW FUEL"));
        Assert.That(Shown(banner), Is.True);
        Assert.That(banner.ClassListContains("hud-warn--caution"), Is.True, "amber");
        Assert.That(labels.Select(l => l.text), Does.Contain("UP"));
        Assert.That(Shown(labels.First(l => l.text == "FLAPS")), Is.True);
        Assert.That(hud.Query<TabularLabel>().ToList().Select(l => l.Text), Does.Contain("123°").And.Contain("10%").And.Contain("50%"));
        Assert.That(hud.Q(className: "sys-bar__fill--low"), Is.Not.Null);

        fake.Stalled = true;
        yield return null;
        yield return null;
        Assert.That(banner.text, Is.EqualTo("STALL"));
        Assert.That(banner.ClassListContains("hud-warn--caution"), Is.False, "red");

        fake.Stalled = false;
        fake.Fuel = 1;
        fake.GearDown = true;
        yield return null;
        yield return null;
        Assert.That(Shown(banner), Is.False);
    }

    [UnityTest]
    public IEnumerator TheMapSlidesWithTheAircraftAndThePlaneTurns()
    {
        yield return Fly();
        var fake = new FakeFlightData { NorthM = 0, EastM = 0, HeadingDeg = 90 };
        Hud.Data = fake;
        yield return null;
        var map = Hud.Map;
        var centre = map.MapOffset;

        // A kilometre north slides the map down; a kilometre east slides it left.
        var unitsPerKm = Minimap.SIZE * 1000 / 8000f;
        fake.NorthM = 1000;
        yield return null;
        Assert.That(map.MapOffset.y - centre.y, Is.EqualTo(unitsPerKm).Within(0.2f));
        fake.NorthM = 0;
        fake.EastM = 1000;
        yield return null;
        Assert.That(map.MapOffset.x - centre.x, Is.EqualTo(-unitsPerKm).Within(0.2f));

        var plane = map.Q(className: "minimap__plane");
        Assert.That(plane.style.rotate.value.angle.ToDegrees(), Is.EqualTo(90).Within(0.5f));
        fake.HeadingDeg = 200;
        yield return null;
        Assert.That(plane.style.rotate.value.angle.ToDegrees(), Is.EqualTo(200).Within(0.5f));
    }

    [UnityTest]
    public IEnumerator ButtonsFadeWhenLeftAloneAndWarningsWakeThem()
    {
        yield return Fly();
        var hud = Hud.Root;
        var deadline = Time.realtimeSinceStartup + 8;
        while (!hud.ClassListContains("hud--idle") && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(hud.ClassListContains("hud--idle"), Is.True, "Never went idle");

        Hud.Data = new FakeFlightData { Stalled = true };
        yield return null;
        yield return null;
        Assert.That(hud.ClassListContains("hud--idle"), Is.False, "A warning has to be seen");
    }

    [UnityTest]
    public IEnumerator DigitsKeepTheirWidthAsTheyCount()
    {
        yield return Fly();
        var fake = new FakeFlightData { HeadingDeg = 111 };
        Hud.Data = fake;
        yield return null;
        yield return null;
        var readout = Hud.Root.Q<TabularLabel>(className: "hud-hdg");
        var width = readout.resolvedStyle.width;
        fake.HeadingDeg = 200;
        yield return null;
        yield return null;
        Assert.That(readout.Text, Is.EqualTo("200°"));
        Assert.That(readout.resolvedStyle.width, Is.EqualTo(width).Within(0.1f));
    }

    [UnityTest]
    public IEnumerator EveryPartOfTheHudStaysOnScreen()
    {
        yield return Fly();
        yield return null;
        var hud = Hud.Root;
        var screen = hud.worldBound;
        foreach (var cls in new[] { "hud-tape-left", "hud-tape-right", "minimap", "hud-throttle", "hud-heading" })
        {
            var box = hud.Q(className: cls).worldBound;
            Assert.That(box.xMin >= screen.xMin && box.xMax <= screen.xMax && box.yMin >= screen.yMin && box.yMax <= screen.yMax, Is.True, cls + " is at " + box);
        }
        // Nothing covers the tapes, whatever the screen's shape.
        Assert.That(hud.Q(className: "minimap").worldBound.Overlaps(hud.Q(className: "hud-tape-left").worldBound), Is.False, "map over speed tape");
        Assert.That(hud.Q(className: "hud-throttle").worldBound.Overlaps(hud.Q(className: "hud-tape-right").worldBound), Is.False, "engine over altitude tape");
    }
}
