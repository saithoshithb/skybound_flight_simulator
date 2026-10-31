using NUnit.Framework;
using Skybound;
using UnityEngine;
using static Skybound.FlightMath;

public class CoordsAndInputTests
{
    const float Close = 1e-4f;

    static Vector3 Fwd(Quat q) => Coords.ToUnityRotation(q) * Vector3.forward;
    static Vector3 Right(Quat q) => Coords.ToUnityRotation(q) * Vector3.right;
    static Vector3 Up(Quat q) => Coords.ToUnityRotation(q) * Vector3.up;

    [Test]
    public void MapsPositions()
    {
        Assert.That(Coords.ToUnityPosition(new V3(100, 20, -50)), Is.EqualTo(new Vector3(20, 50, 100)));
    }

    [Test]
    public void HeadingEastPointsTheModelAlongPlusX()
    {
        var f = Fwd(FromEuler(0, 0, 90 * DEG));
        Assert.That(f.x, Is.EqualTo(1).Within(Close));
        Assert.That(f.z, Is.EqualTo(0).Within(Close));
    }

    [Test]
    public void PitchUpRaisesTheNoseAndRightRollLowersTheRightWing()
    {
        Assert.That(Fwd(FromEuler(0, 30 * DEG, 0)).y, Is.EqualTo(0.5f).Within(Close));
        Assert.That(Right(FromEuler(30 * DEG, 0, 0)).y, Is.EqualTo(-0.5f).Within(Close));
    }

    [Test]
    public void AnyAttitudeMatchesTheFlightModelsOwnRotation()
    {
        // Body axes in the flight model's frame, and the same axes in Unity's model frame.
        var forward = new V3(1, 0, 0);
        var right = new V3(0, 1, 0);
        var down = new V3(0, 0, 1);
        double[][] attitudes =
        {
            new[] { 17.0, 33.0, 71.0 },
            new[] { -50.0, -20.0, 200.0 },
            new[] { 120.0, 60.0, -40.0 },
        };
        foreach (var a in attitudes)
        {
            var q = FromEuler(a[0] * DEG, a[1] * DEG, a[2] * DEG);
            AssertSame(Coords.ToUnityPosition(Rotate(q, forward)), Fwd(q));
            AssertSame(Coords.ToUnityPosition(Rotate(q, right)), Right(q));
            AssertSame(Coords.ToUnityPosition(Rotate(q, down)), -Up(q));
        }
    }

    static void AssertSame(Vector3 a, Vector3 b)
    {
        Assert.That(Vector3.Distance(a, b), Is.LessThan(Close), $"{a} vs {b}");
    }

    [Test]
    public void KeyboardAxisEasesTowardTheTargetAndStopsThere()
    {
        Assert.That(KeyboardAxes.Approach(0, 1, 0.1), Is.EqualTo(0.25).Within(1e-9));
        Assert.That(KeyboardAxes.Approach(0.9, 1, 0.1), Is.EqualTo(1));
        Assert.That(KeyboardAxes.Approach(0.5, 0, 0.1), Is.EqualTo(0.1).Within(1e-9));
        Assert.That(KeyboardAxes.Approach(-0.1, 0, 0.1), Is.EqualTo(0));
    }
}
