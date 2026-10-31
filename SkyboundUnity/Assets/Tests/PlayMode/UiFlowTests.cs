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

/// <summary>Sets the game up the way each test needs it, with settings that can't reach the player's file.</summary>
public static class TestStart
{
    static void Fresh()
    {
        SettingsStore.persist = false;
        SettingsStore.ResetAll();
        UiStore.ResetAll();
        Recorder.ClearFlightLog();
    }

    public static void InFlight()
    {
        Fresh();
        App.startScreenOverride = AppScreen.Flight;
    }

    public static void AtMenu()
    {
        Fresh();
        App.startScreenOverride = null;
    }
}

/// <summary>The stage 5 flow, without touching the Inspector: menu to flight, pause, settings, controller setup.</summary>
public class UiFlowTests
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

    static IEnumerator LoadAtMenu()
    {
        TestStart.AtMenu();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        Assert.That(App.Instance, Is.Not.Null, "No App: run Skybound > Set Up Flight Scene");
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

    static Button Find(string text)
    {
        return App.Instance.Root.Query<Button>().ToList().FirstOrDefault(b => b.text == text && b.resolvedStyle.display == DisplayStyle.Flex);
    }

    /// <summary>
    /// A click at the element's centre, sent into the panel. It finds its target the way a real one does,
    /// so anything covering the element gets it instead. A batch run has no focused window, and without
    /// one UI Toolkit reads nothing from the mouse device, so that last step is left to a person.
    /// </summary>
    IEnumerator Click(VisualElement element)
    {
        Assert.That(element, Is.Not.Null, "Nothing to click");
        yield return null;
        var at = element.worldBound.center;
        var root = element.panel.visualTree;
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 })) root.SendEvent(down);
        yield return null;
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 })) root.SendEvent(up);
        yield return null;
        yield return null;
    }

    IEnumerator ClickButton(string text)
    {
        var button = Find(text);
        Assert.That(button, Is.Not.Null, $"No button \"{text}\"");
        yield return Click(button);
    }

    static IEnumerator WaitUntilFlying()
    {
        var deadline = Time.realtimeSinceStartup + 30;
        while (!(App.Instance.Current is FlightScreen s && s.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(App.Instance.Current, Is.InstanceOf<FlightScreen>());
        Assert.That(((FlightScreen)App.Instance.Current).Ready, Is.True, "The flight never became ready");
    }

    [UnityTest]
    public IEnumerator TheGameOpensAtTheMenuWithNothingBuiltYet()
    {
        yield return LoadAtMenu();
        Assert.That(App.Instance.Current, Is.InstanceOf<MenuScreen>());
        Assert.That(Object.FindAnyObjectByType<WorldBuilder>().Profile, Is.Null);
        Assert.That(Object.FindAnyObjectByType<WorldBuilder>().transform.childCount, Is.EqualTo(0));
        Assert.That(Find("Take off from runway 36"), Is.Not.Null);
        Assert.That(App.Instance.Root.Q("trainer"), Is.Not.Null);
        Assert.That(App.Instance.Root.Q("sport"), Is.Not.Null);
        // On, or the Editor writes "No cameras rendering" over the menu. Drawing nothing, as nothing is built.
        Assert.That(Camera.main, Is.Not.Null);
        Assert.That(Camera.main.cullingMask, Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator ClickingTakeOffFliesAndEscPausesAndTheMenuComesBack()
    {
        yield return LoadAtMenu();
        yield return ClickButton("Take off from runway 36");
        Assert.That(UiStore.screen, Is.EqualTo(AppScreen.Flight), "The click didn't reach the button");
        yield return WaitUntilFlying();

        // The loading screen is gone once it has faded, so it can't sit over the pause menu's buttons.
        Assert.That(App.Instance.Root.Q(className: "loading__content"), Is.Not.Null);
        yield return new WaitForSeconds(1);
        Assert.That(App.Instance.Root.Q(className: "loading__content"), Is.Null);

        var session = App.Instance.Session;
        Assert.That(session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
        Assert.That(Camera.main.cullingMask, Is.Not.EqualTo(0));
        Assert.That(Object.FindAnyObjectByType<WorldBuilder>().Profile, Is.Not.Null);

        // + takes off, as the key card says.
        yield return Tap(Key.Equals);
        Assert.That(session.easy.takeoff, Is.True);
        yield return new WaitForSeconds(2);
        var moving = session.flight.telemetry.airspeedKt;
        Assert.That(moving, Is.GreaterThan(2));

        // Paused: the aircraft stops, the pause menu shows.
        yield return Tap(Key.Escape);
        Assert.That(UiStore.paused, Is.True);
        var screen = (FlightScreen)App.Instance.Current;
        Assert.That(screen.PauseMenuElement, Is.Not.Null);
        var state = session.flight.state.pos.x;
        yield return new WaitForSeconds(0.3f);
        Assert.That(session.flight.state.pos.x, Is.EqualTo(state));

        yield return ClickButton("Resume");
        Assert.That(UiStore.paused, Is.False);
        yield return null;
        yield return null;
        Assert.That(screen.PauseMenuElement, Is.Null);

        yield return Tap(Key.P);
        yield return Click(screen.PauseMenuElement.Q("Main menu"));
        yield return null;
        Assert.That(UiStore.screen, Is.EqualTo(AppScreen.Menu));
        yield return null;
        Assert.That(App.Instance.Current, Is.InstanceOf<MenuScreen>());
    }

    [UnityTest]
    public IEnumerator ChoosingTheSportFliesTheSport()
    {
        yield return LoadAtMenu();
        yield return Click(App.Instance.Root.Q("sport"));
        Assert.That(SettingsStore.Get().aircraftId, Is.EqualTo("sport"));
        yield return ClickButton("Start on final approach");
        yield return WaitUntilFlying();
        yield return null;
        var session = App.Instance.Session;
        Assert.That(session.flight.ac.id, Is.EqualTo("sport"));
        Assert.That(session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(Object.FindAnyObjectByType<AircraftView>().transform.Find("Exterior/Canopy"), Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator SettingsChangeTheGame()
    {
        yield return LoadAtMenu();
        yield return ClickButton("Settings");
        yield return null;
        var dialog = ((MenuScreen)App.Instance.Current).Settings;
        Assert.That(dialog, Is.Not.Null);

        // Graphics quality: applies to the next world.
        yield return Click(dialog.Root.Query<Button>().ToList().First(b => b.text == "High"));
        Assert.That(SettingsStore.Get().graphics, Is.EqualTo(GraphicsQuality.High));

        // Easy flight off: bank assist comes back into play.
        var switches = dialog.Root.Query<SwitchEl>().ToList();
        Assert.That(switches.Count, Is.EqualTo(7));
        yield return Click(switches[2]);
        Assert.That(SettingsStore.Get().easyFlight, Is.False);

        yield return Tap(Key.Escape);
        Assert.That(UiStore.showSettings, Is.False);
    }

    [UnityTest]
    public IEnumerator TheHudShowsWhatTheAircraftIsDoing()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        var session = App.Instance.Session;
        session.ResetAirborne();
        yield return null;
        yield return null;
        var hud = ((FlightScreen)App.Instance.Current).Hud.Root;
        var texts = hud.Query<Label>().ToList().Select(l => l.text).ToList();
        Assert.That(texts, Does.Contain("AIRBORNE"));
        Assert.That(texts, Does.Contain("EASY FLIGHT"));
        Assert.That(texts, Does.Contain("Keyboard"));
        Assert.That(texts, Does.Contain("IAS · KT"));
        // The tapes draw their numbers: the altitude box reads near 1,000 ft.
        Assert.That(texts, Has.Some.Match(@"^1,0\d0$|^9\d0$"));

        // V hides the tapes, as the panel in front of the pilot has the same numbers.
        var tapes = hud.Q(className: "hud-tape-left");
        Assert.That(tapes.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        yield return Tap(Key.V);
        Assert.That(tapes.style.display.value, Is.EqualTo(DisplayStyle.None));
        yield return Tap(Key.V);

        // A crash shows the crash card, and Restart puts the aircraft back on the runway.
        session.flight.state.pos = new V3(12250 - 100, 13500, -(session.flight.terrain(12250, 13500) - 30));
        session.flight.state.vel = new V3(50, 0, 0);
        session.flight.state.q = FlightMath.FromEuler(0, 0, 0);
        var deadline = Time.realtimeSinceStartup + 20;
        while (session.flight.telemetry.phase != FlightPhase.Crashed && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
        var title = hud.Query<Label>().ToList().First(l => l.text == "CRASHED");
        Assert.That(title.parent.parent.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        yield return ClickButton("Restart");
        Assert.That(session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Ground));
    }

    [UnityTest]
    public IEnumerator ControllerSetupConnectsCalibratesAndTunes()
    {
        yield return LoadAtMenu();
        yield return ClickButton("Connect a controller →");
        yield return null;
        var setup = App.Instance.Current as SetupScreen;
        Assert.That(setup, Is.Not.Null);
        Assert.That(SettingsStore.Get().source, Is.EqualTo(TransportKind.Simulated));

        var link = App.Instance.Link;
        SimulatedTransport.simParams.motion = SimMotion.Still;
        yield return ClickButton("Connect");
        var deadline = Time.realtimeSinceStartup + 5;
        while (!link.hub.IsUsable() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(link.hub.IsUsable(), Is.True, "The simulator never came up");

        // The 3D controller follows the sensor.
        SimulatedTransport.simParams.motion = SimMotion.Manual;
        SimulatedTransport.simParams.targetRoll = 30;
        yield return new WaitForSeconds(1);
        Assert.That(Vector3.Angle(setup.View.Live.up, Vector3.up), Is.GreaterThan(15));
        Assert.That((setup.View.Live.rotation * Vector3.right).y, Is.LessThan(-0.2f), "Roll right should lower the right side");
        SimulatedTransport.simParams.targetRoll = 0;
        SimulatedTransport.simParams.motion = SimMotion.Still;
        yield return new WaitForSeconds(0.5f);

        // Recenter saves the neutral in the settings.
        yield return Tap(Key.R);
        yield return new WaitForSeconds(1.3f);
        Assert.That(SettingsStore.Get().neutral.capturedAt, Is.GreaterThan(0));

        // Sliders change the control settings. A click at the middle of this one isn't its default of 1.
        var sensitivity = App.Instance.Root.Q<SliderEl>("Sensitivity");
        Assert.That(SettingsStore.Get().control.sensitivity, Is.EqualTo(1));
        yield return Click(sensitivity);
        Assert.That(SettingsStore.Get().control.sensitivity, Is.Not.EqualTo(1));

        yield return ClickButton("Disconnect");
        Assert.That(link.hub.GetStatus().link, Is.EqualTo(LinkState.Disconnected));
        SimulatedTransport.simParams.motion = SimMotion.Wave;
    }

    [UnityTest]
    public IEnumerator EverythingInThePauseMenuCanBeClicked()
    {
        yield return LoadAtMenu();
        yield return ClickButton("Take off from runway 36");
        yield return WaitUntilFlying();
        var screen = (FlightScreen)App.Instance.Current;

        // Straight after the loading screen starts to fade: it must not catch the click.
        yield return Tap(Key.Escape);
        yield return null;
        yield return Click(screen.PauseMenuElement.Q("Start on final approach"));
        yield return null;
        Assert.That(App.Instance.Session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Airborne));
        Assert.That(UiStore.paused, Is.False);

        yield return Tap(Key.Escape);
        yield return null;
        yield return Click(screen.PauseMenuElement.Q("Restart on runway"));
        yield return null;
        Assert.That(App.Instance.Session.flight.telemetry.phase, Is.EqualTo(FlightPhase.Ground));

        yield return Tap(Key.Escape);
        yield return null;
        yield return Click(screen.PauseMenuElement.Q("View · Chase"));
        Assert.That(UiStore.view, Is.EqualTo(CameraView.Cockpit));
        UiStore.view = CameraView.Chase;

        yield return Click(screen.PauseMenuElement.Q("Copy flight log"));
        Assert.That(GUIUtility.systemCopyBuffer, Does.StartWith("# skybound flight log"));

        yield return Click(screen.PauseMenuElement.Q("Settings"));
        yield return null;
        Assert.That(UiStore.showSettings, Is.True);
    }

    [UnityTest]
    public IEnumerator TheInstrumentsStopRedrawingWhenNothingShowsThem()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        var panel = InstrumentPanel.Get();
        UiStore.showInstruments = false;
        yield return null;
        var before = panel.version;
        yield return new WaitForSeconds(0.2f);
        Assert.That(panel.version, Is.EqualTo(before));

        UiStore.view = CameraView.Cockpit;
        yield return new WaitForSeconds(0.2f);
        Assert.That(panel.version, Is.GreaterThan(before));
    }

    [UnityTest]
    public IEnumerator TheCockpitPanelShowsTheLiveInstruments()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
        var view = Object.FindAnyObjectByType<AircraftView>();
        yield return null;
        var panel = InstrumentPanel.Get();
        Assert.That(view.Parts.panelSurface.sharedMaterial.GetTexture("_BaseMap"), Is.EqualTo(panel.Texture));
        UiStore.view = CameraView.Cockpit;
        var before = panel.version;
        yield return new WaitForSeconds(0.3f);
        Assert.That(panel.version, Is.GreaterThan(before));

        // Something has been drawn into the panel, not just its plain surface.
        var previous = RenderTexture.active;
        RenderTexture.active = panel.Texture;
        var pixels = new Texture2D(panel.Texture.width, panel.Texture.height, TextureFormat.RGBA32, false);
        pixels.ReadPixels(new Rect(0, 0, panel.Texture.width, panel.Texture.height), 0, 0);
        RenderTexture.active = previous;
        var bright = pixels.GetPixels32().Count(p => p.r > 200 && p.g > 200 && p.b > 200);
        Object.Destroy(pixels);
        Assert.That(bright, Is.GreaterThan(200), "The panel has no white markings on it");
    }
}
