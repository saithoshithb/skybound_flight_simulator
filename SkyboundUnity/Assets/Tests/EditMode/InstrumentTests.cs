using NUnit.Framework;
using Skybound;
using UnityEngine;

/// <summary>The browser game's instruments/gauges.test.ts, plus the settings file and HUD scales.</summary>
public class InstrumentTests
{
    static float Deg(float radians) => radians * Mathf.Rad2Deg;

    [Test]
    public void Airspeed40KtAt12OClockClockwisePegsBelowTheScale()
    {
        Assert.That(Deg(Gauges.AsiAngle(40)), Is.EqualTo(-90).Within(1e-3f));
        Assert.That(Deg(Gauges.AsiAngle(120)), Is.GreaterThan(Deg(Gauges.AsiAngle(100))));
        Assert.That(Gauges.AsiAngle(0), Is.EqualTo(Gauges.AsiAngle(20))); // resting on the stop
    }

    [Test]
    public void VsiLevelPointsAt9OClockClimbSwingsUpDescentDown()
    {
        Assert.That(Deg(Gauges.VsiAngle(0)), Is.EqualTo(180).Within(1e-3f));
        Assert.That(Deg(Gauges.VsiAngle(1000)), Is.GreaterThan(180)); // toward 12 o'clock (270°)
        Assert.That(Deg(Gauges.VsiAngle(-1000)), Is.LessThan(180)); // toward 6 o'clock (90°)
        Assert.That(Gauges.VsiAngle(5000), Is.EqualTo(Gauges.VsiAngle(2000))); // pegged
    }

    [Test]
    public void TachometerReadsIdleAndFullPowerInSensibleRanges()
    {
        Assert.That(Panel.EngineRpm(0, 0), Is.InRange(550, 800));
        Assert.That(Panel.EngineRpm(1, 100), Is.InRange(2400, 2700));
        Assert.That(Gauges.TachAngle(3000), Is.GreaterThan(Gauges.TachAngle(0)));
    }

    [Test]
    public void TapesFadeOutTowardTheEnds()
    {
        Assert.That(Tapes.FadeAt(0), Is.EqualTo(0));
        Assert.That(Tapes.FadeAt(0.08f), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(Tapes.FadeAt(0.5f), Is.EqualTo(1));
        Assert.That(Tapes.FadeAt(1), Is.EqualTo(0));
        Assert.That(Tapes.FadeAt(-0.1f), Is.EqualTo(0));
        Assert.That(Tapes.FadeAt(1.1f), Is.EqualTo(0));
    }

    [Test]
    public void VerticalSpeedScaleIsFineNearZeroAndStopsAt2000()
    {
        Assert.That(Tapes.VsiPos(0), Is.EqualTo(0));
        Assert.That(Tapes.VsiPos(500), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(Tapes.VsiPos(-500), Is.EqualTo(-0.5f).Within(1e-5f));
        Assert.That(Tapes.VsiPos(9000), Is.EqualTo(1).Within(1e-5f));
    }

    [TestCase(0.26f, 0.3f, 2f, 0.05f, 0.3f)]
    [TestCase(1.03f, 0.3f, 2f, 0.05f, 1.05f)]
    [TestCase(5f, 0.3f, 2f, 0.05f, 2f)]
    [TestCase(11.2f, 10f, 70f, 1f, 11f)]
    public void SlidersLandOnTheirSteps(float raw, float min, float max, float step, float expected)
    {
        Assert.That(SliderEl.Quantise(raw, min, max, step), Is.EqualTo(expected).Within(1e-4f));
    }

    [TestCase(1250, "1250", "1,250")]
    [TestCase(0, "0", "0")]
    [TestCase(-20, "-20", "-20")]
    public void NumberTextReadsLikeTheBrowsers(int v, string whole, string thousands)
    {
        Assert.That(NumberText.Whole(v), Is.EqualTo(whole));
        Assert.That(NumberText.Thousands(v), Is.EqualTo(thousands));
        // The same string each time: that's the point of it.
        Assert.That(NumberText.Whole(v), Is.SameAs(NumberText.Whole(v)));
    }

    [Test]
    public void TheHeadingRibbonShowsTwoDigits()
    {
        Assert.That(NumberText.TwoDigit(3), Is.EqualTo("03"));
        Assert.That(NumberText.TwoDigit(33), Is.EqualTo("33"));
    }

    [Test]
    public void TheCatalogFindsAircraftAndFallsBackToTheTrainer()
    {
        Assert.That(AircraftCatalog.ById("sport").id, Is.EqualTo("sport"));
        Assert.That(AircraftCatalog.ById("trainer").id, Is.EqualTo("trainer"));
        Assert.That(AircraftCatalog.ById("removed-in-an-update").id, Is.EqualTo("trainer"));
        Assert.That(AircraftCatalog.All.Length, Is.EqualTo(2));
    }

    [Test]
    public void AnOlderSettingsFileKeepsItsValuesAndGetsDefaultsForTheRest()
    {
        var saved = new SettingsState();
        JsonUtility.FromJsonOverwrite("{\"aircraftId\":\"sport\",\"easyFlight\":false,\"control\":{\"sensitivity\":1.5}}", saved);
        Assert.That(saved.aircraftId, Is.EqualTo("sport"));
        Assert.That(saved.easyFlight, Is.False);
        Assert.That(saved.control.sensitivity, Is.EqualTo(1.5));
        Assert.That(saved.bankAssist, Is.True);
        Assert.That(saved.graphics, Is.EqualTo(GraphicsQuality.Medium));
    }

    [Test]
    public void SettingsSurviveBeingSavedAndLoaded()
    {
        var saved = new SettingsState { aircraftId = "sport", graphics = GraphicsQuality.High, source = TransportKind.Serial, lastPort = "COM8" };
        saved.neutral = new Neutral { pitch = -21.3, roll = -0.9, yaw = 4, capturedAt = 1234 };
        saved.control.yawMode = YawMode.Rate;
        saved.control.roll.invert = true;
        var back = new SettingsState();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(saved), back);
        Assert.That(back.aircraftId, Is.EqualTo("sport"));
        Assert.That(back.graphics, Is.EqualTo(GraphicsQuality.High));
        Assert.That(back.source, Is.EqualTo(TransportKind.Serial));
        Assert.That(back.lastPort, Is.EqualTo("COM8"));
        Assert.That(back.neutral.pitch, Is.EqualTo(-21.3));
        Assert.That(back.neutral.capturedAt, Is.EqualTo(1234));
        Assert.That(back.control.yawMode, Is.EqualTo(YawMode.Rate));
        Assert.That(back.control.roll.invert, Is.True);
    }

    [Test]
    public void AttitudeToQuaternionMatchesTheAircraftConvention()
    {
        // Nose-up pitch raises the front, right-wing-down roll lowers the right side, yaw right turns right.
        Assert.That((ControllerView.AttitudeToQuaternion(30, 0, 0) * Vector3.forward).y, Is.GreaterThan(0.4f));
        Assert.That((ControllerView.AttitudeToQuaternion(0, 30, 0) * Vector3.right).y, Is.LessThan(-0.4f));
        Assert.That((ControllerView.AttitudeToQuaternion(0, 0, 30) * Vector3.forward).x, Is.GreaterThan(0.4f));
    }
}
