using System.Collections;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>The batch equivalent of opening the Flight scene and pressing Play.</summary>
public class FlightSceneTests
{
    Keyboard keyboard;
    InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    InputSettings.BackgroundBehavior savedBackgroundBehavior;

    // A batch run has no focused Game view, and the Input System drops keyboard events without one.
    [SetUp]
    public void SendInputToTheGameWithoutFocus()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    }

    [TearDown]
    public void RemoveKeyboard()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    static IEnumerator LoadFlightScene()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Flight"));
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SceneStartsParkedOnTheRunwayWithEverythingWired()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        Assert.That(session, Is.Not.Null, "No FlightSession: run Skybound > Set Up Flight Scene");
        Assert.That(session.flight.ac.id, Is.EqualTo("trainer"));
        Assert.That(session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(Object.FindAnyObjectByType<WorldBuilder>(), Is.Not.Null);
        Assert.That(Object.FindAnyObjectByType<App>(), Is.Not.Null);

        // Any error logged while these frames run fails the test.
        for (var i = 0; i < 30; i++) yield return null;

        var view = Object.FindAnyObjectByType<AircraftView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(view.transform.childCount, Is.GreaterThan(0));
        Assert.That(view.transform.position.z, Is.EqualTo(60).Within(0.5f)); // 60 m past the threshold
        Assert.That(view.transform.position.y, Is.EqualTo(1).Within(0.2f));

        var camera = Camera.main;
        Assert.That(camera.GetComponent<ChaseCamera>(), Is.Not.Null);
        Assert.That(camera.transform.position.z, Is.LessThan(view.transform.position.z)); // behind the aircraft
        Assert.That(camera.transform.position.y, Is.GreaterThan(view.transform.position.y));
    }

    [UnityTest]
    public IEnumerator PressingPlusStartsTheTakeOffRoll()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        var view = Object.FindAnyObjectByType<AircraftView>();

        keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Equals));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());

        Assert.That(session.easy.takeoff, Is.True, "The + key didn't reach Easy flight");
        Assert.That(session.easy.speedTargetKt, Is.GreaterThanOrEqualTo(session.flight.ac.easy.takeoffKt));

        yield return new WaitForSeconds(3);
        Assert.That(session.flight.telemetry.airspeedKt, Is.GreaterThan(5));
        Assert.That(view.transform.position.z, Is.GreaterThan(61)); // the box itself has moved north
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
    public IEnumerator TheGroundIsBuiltFromTheTerrainTheFlightModelUses()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        Assert.That(session.flight.terrain(12250, 13500), Is.GreaterThan(400));
        Assert.That(session.flight.terrain(600, 0), Is.EqualTo(0));

        var ground = GameObject.Find("Ground");
        Assert.That(ground, Is.Not.Null);
        var mesh = ground.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh.vertexCount, Is.EqualTo(281 * 281));
        Assert.That(mesh.bounds.max.y, Is.GreaterThan(300));
        // The ToneMap pass does the fog; WorldVisualsTests checks a rendered pixel.
        Assert.That(RenderSettings.fog, Is.False);
        Assert.That(Shader.GetGlobalVector("_SkyboundFog").x, Is.EqualTo(2500));
    }

    [UnityTest]
    public IEnumerator VSwitchesToTheCockpitAndBack()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        var aircraft = Object.FindAnyObjectByType<AircraftView>();
        var camera = Camera.main;
        Assert.That(aircraft.Exterior.activeSelf, Is.True);
        Assert.That(aircraft.Cockpit.activeSelf, Is.False);
        Assert.That(camera.fieldOfView, Is.EqualTo(55).Within(0.01f));

        yield return Tap(Key.V);
        Assert.That(session.view, Is.EqualTo(CameraView.Cockpit));
        yield return new WaitForSeconds(0.7f);
        Assert.That(camera.fieldOfView, Is.EqualTo(68).Within(0.01f));
        Assert.That(aircraft.Exterior.activeSelf, Is.False);
        Assert.That(aircraft.Cockpit.activeSelf, Is.True);
        var eye = aircraft.transform.position + aircraft.transform.rotation * ChaseCamera.EYE;
        Assert.That(Vector3.Distance(camera.transform.position, eye), Is.LessThan(0.01f));
        Assert.That(Quaternion.Angle(camera.transform.rotation, aircraft.transform.rotation * ChaseCamera.EYE_TILT), Is.LessThan(0.1f));

        yield return Tap(Key.V);
        Assert.That(session.view, Is.EqualTo(CameraView.Chase));
        yield return new WaitForSeconds(0.7f);
        Assert.That(camera.fieldOfView, Is.EqualTo(55).Within(0.01f));
        Assert.That(aircraft.Exterior.activeSelf, Is.True);
        Assert.That(aircraft.Cockpit.activeSelf, Is.False);
        Assert.That(Vector3.Distance(camera.transform.position, aircraft.transform.position), Is.GreaterThan(10));
    }

    [UnityTest]
    public IEnumerator FlyingIntoAHillCrashesAndBackspaceRestarts()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        var flight = session.flight;
        var camera = Camera.main;
        session.ResetAirborne();
        // The highest hill: 30 m below its summit, 300 m short, flying at it.
        const double north = 12250, east = 13500;
        flight.state.pos = new V3(north - 300, east, -(flight.terrain(north, east) - 30));
        flight.state.vel = new V3(50, 0, 0);
        flight.state.q = FlightMath.FromEuler(0, 0, 0);

        var deadline = Time.realtimeSinceStartup + 20;
        while (flight.telemetry.phase != FlightPhase.Crashed && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            var at = camera.transform.position;
            Assert.That(at.y, Is.GreaterThan(flight.terrain(at.z, at.x)), "The camera went under the hillside");
        }
        Assert.That(flight.telemetry.phase, Is.EqualTo(FlightPhase.Crashed));
        Assert.That(flight.telemetry.crashReason, Is.Not.Null.And.Not.Empty);

        yield return Tap(Key.Backspace);
        Assert.That(flight.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
        var aircraft = Object.FindAnyObjectByType<AircraftView>();
        Assert.That(aircraft.transform.position.z, Is.EqualTo(60).Within(1));
        // The camera cuts back to the runway instead of gliding 18 km.
        Assert.That(Vector3.Distance(camera.transform.position, aircraft.transform.position), Is.LessThan(40));
    }
}
