using System.Collections;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>The batch equivalent of pressing Play, connecting the simulated controller and flying by tilting it.</summary>
public class ControllerSceneTests
{
    Keyboard keyboard;
    InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    InputSettings.BackgroundBehavior savedBackgroundBehavior;
    FlightSession session;
    ControllerLink link;
    SimParams sim;

    [SetUp]
    public void SendInputToTheGameWithoutFocus()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        sim = SimulatedTransport.simParams;
        sim.motion = SimMotion.Still;
        sim.targetPitch = sim.targetRoll = sim.targetYaw = 0;
    }

    [TearDown]
    public void CleanUp()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        sim.motion = SimMotion.Wave;
        sim.targetPitch = sim.targetRoll = sim.targetYaw = 0;
    }

    IEnumerator LoadFlightScene()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        session = Object.FindAnyObjectByType<FlightSession>();
        link = Object.FindAnyObjectByType<ControllerLink>();
        Assert.That(link, Is.Not.Null, "No ControllerLink: run Skybound > Set Up Flight Scene");
    }

    IEnumerator ConnectSimulated()
    {
        link.serial.ConnectSimulated();
        var deadline = Time.realtimeSinceStartup + 5;
        while (!link.hub.IsUsable() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(link.hub.IsUsable(), Is.True, "The simulator never came up");
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

    [UnityTest]
    public IEnumerator StartsWithNoControllerAndKeyboardFlying()
    {
        yield return LoadFlightScene();
        Assert.That(link.hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(session.controls.engagement, Is.EqualTo(0));
        Assert.That(session.pilotInfo.source, Is.EqualTo(ControlSource.Keyboard));
    }

    [UnityTest]
    public IEnumerator TiltingRightBanksTheAircraftRight()
    {
        yield return LoadFlightScene();
        session.ResetAirborne();
        yield return ConnectSimulated();

        // Recenter on the "desk", which isn't level.
        yield return Tap(Key.R);
        yield return new WaitForSeconds(1.2f);
        Assert.That(link.neutral.pitch, Is.EqualTo(sim.biasPitchDeg).Within(0.5));
        Assert.That(link.neutral.roll, Is.EqualTo(sim.biasRollDeg).Within(0.5));
        Assert.That(session.controls.engagement, Is.LessThan(0.9), "Recentering should ramp the controller back in");

        sim.motion = SimMotion.Manual;
        sim.targetRoll = 30;
        yield return new WaitForSeconds(4);
        Assert.That(session.controls.engagement, Is.EqualTo(1));
        Assert.That(session.controls.aileron, Is.GreaterThan(0.3));
        Assert.That(session.pilotInfo.source, Is.EqualTo(ControlSource.Motion));
        Assert.That(session.flight.telemetry.rollDeg, Is.GreaterThan(5), "The bank doesn't reach the aircraft");

        sim.targetRoll = -30;
        yield return new WaitForSeconds(6);
        Assert.That(session.controls.aileron, Is.LessThan(-0.3));
        Assert.That(session.flight.telemetry.rollDeg, Is.LessThan(-5));

        // The cable comes out: controls fade away and the keyboard is flying again.
        link.serial.Disconnect();
        yield return new WaitForSeconds(0.6f);
        Assert.That(session.controls.engagement, Is.EqualTo(0));
        Assert.That(session.controls.aileron, Is.EqualTo(0));
        Assert.That(session.pilotInfo.source, Is.EqualTo(ControlSource.Keyboard));
    }

    [UnityTest]
    public IEnumerator RWithNoControllerSaysSo()
    {
        yield return LoadFlightScene();
        yield return Tap(Key.R);
        Assert.That(session.pilotInfo.message, Is.EqualTo("No live motion controller to recenter"));
    }

    [UnityTest]
    public IEnumerator BackspaceRecentersOnWhateverPoseIsHeld()
    {
        yield return LoadFlightScene();
        yield return ConnectSimulated();
        sim.motion = SimMotion.Manual;
        sim.targetPitch = 10;
        yield return new WaitForSeconds(1.5f);

        yield return Tap(Key.Backspace);
        Assert.That(session.pilotInfo.message, Does.StartWith("Hold the controller"));
        yield return new WaitForSeconds(2.4f);
        Assert.That(link.neutral.pitch, Is.EqualTo(10 + sim.biasPitchDeg).Within(0.5));
        Assert.That(session.pilotInfo.message, Does.StartWith("Neutral set"));
    }

    [UnityTest]
    public IEnumerator HoldingAPoseMeansNoTurnAfterRecentering()
    {
        yield return LoadFlightScene();
        session.ResetAirborne();
        yield return ConnectSimulated();
        sim.motion = SimMotion.Manual;
        sim.targetRoll = 20;
        yield return new WaitForSeconds(1.5f);
        link.Recenter();
        yield return new WaitForSeconds(2.5f);
        Assert.That(System.Math.Abs(session.controls.aileron), Is.LessThan(0.02));
    }

    [UnityTest]
    public IEnumerator DisablingTheLinkClosesTheConnection()
    {
        yield return LoadFlightScene();
        yield return ConnectSimulated();
        link.enabled = false;
        Assert.That(link.hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        Assert.That(link.hub.ActiveTransport(), Is.Null);
    }
}
