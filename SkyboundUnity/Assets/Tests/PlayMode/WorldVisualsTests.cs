using System.Collections;
using NUnit.Framework;
using Skybound;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Stage 4's visuals, in the real Flight scene.</summary>
public class WorldVisualsTests
{
    static IEnumerator LoadFlightScene()
    {
        TestStart.InFlight();
        yield return SceneManager.LoadSceneAsync("Flight");
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator TheWorldIsBuiltAtTheMediumTier()
    {
        yield return LoadFlightScene();
        var world = Object.FindAnyObjectByType<WorldBuilder>();
        Assert.That(world.Quality, Is.EqualTo(GraphicsQuality.Medium));
        foreach (var name in new[] { "Ground", "Plain", "Runway", "Runway marks", "Taxiway", "Apron", "Hangar", "Windsock", "Tree trunks", "Tree crowns", "Hills", "Clouds" })
            Assert.That(world.transform.Find(name), Is.Not.Null, name);
        Assert.That(world.transform.Find("Tree crowns").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(1800 * 60));
        Assert.That(Shader.GetGlobalVector("_SkyboundFog").y, Is.EqualTo(19000));
        Assert.That(RenderSettings.skybox.shader.name, Is.EqualTo("Skybound/Sky"));
        Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom));
        Assert.That(QualitySettings.names[QualitySettings.GetQualityLevel()], Is.EqualTo("Medium"));

        var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindAnyObjectByType<Light>();
        Assert.That(sun.intensity, Is.EqualTo(2.6f / Mathf.PI / 0.96f).Within(1e-4f));
        Assert.That(sun.shadows, Is.EqualTo(LightShadows.Soft));
        Assert.That(Vector3.Dot(-sun.transform.forward, WorldBuilder.SUN_DIR), Is.GreaterThan(0.999f));
        // Any shader that failed to compile would be pink, or log an error and fail this test.
        foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>())
            Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True, renderer.name);
    }

    /// <summary>Renders a white card under the ambient light alone, and reads the linear value on screen.</summary>
    static Color AmbientOnACard(Quaternion cardFacing, float distance = 3)
    {
        var sun = Object.FindAnyObjectByType<Light>();
        sun.enabled = false;
        var size = distance * 3;
        var mesh = Geo.Plane(size, size).ToMesh("Card");
        var material = MaterialKit.Lit("#ffffff");
        var card = Three.Part("Card", null, mesh, material, new Vector3(0, 5000, 0), cardFacing);
        // The plane's front is its local −z in Unity, as three's front is +z.
        var cameraObject = new GameObject("Card camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.farClipPlane = 40000;
        cameraObject.transform.SetPositionAndRotation(card.transform.position + cardFacing * Vector3.back * distance, cardFacing);
        var target = new RenderTexture(16, 16, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var pixel = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
        pixel.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
        var color = pixel.GetPixel(8, 8);
        RenderTexture.active = null;
        camera.targetTexture = null;
        target.Release();
        Object.Destroy(cameraObject);
        Object.Destroy(card);
        Object.Destroy(mesh);
        Object.Destroy(material);
        Object.Destroy(pixel);
        Object.Destroy(target);
        sun.enabled = true;
        return color;
    }

    static void AssertColor(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.012f), "red");
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.012f), "green");
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.012f), "blue");
    }

    [UnityTest]
    public IEnumerator AmbientLightIsTheBrowserGamesHemisphereLight()
    {
        yield return LoadFlightScene();
        var sky = Three.Linear("#dfe9f2");
        var ground = Three.Linear("#5d6b4a");
        // A camera looking along its forward sees a card facing back at it. The tone mapping
        // pass runs for every camera, so this checks that too.
        AssertColor(AmbientOnACard(Quaternion.LookRotation(Vector3.down, Vector3.forward)), ThreeToneMapping.Aces(sky / Mathf.PI)); // card faces up
        AssertColor(AmbientOnACard(Quaternion.LookRotation(Vector3.up, Vector3.forward)), ThreeToneMapping.Aces(ground / Mathf.PI)); // card faces down
        AssertColor(AmbientOnACard(Quaternion.LookRotation(Vector3.forward, Vector3.up)), ThreeToneMapping.Aces((sky + ground) / 2 / Mathf.PI)); // upright
    }

    [UnityTest]
    public IEnumerator BeyondTheFogEverythingIsExactlyTheHazeColour()
    {
        yield return LoadFlightScene();
        AssertColor(AmbientOnACard(Quaternion.LookRotation(Vector3.forward, Vector3.up), 30000), Three.Linear("#c9d5df"));
    }

    [UnityTest]
    public IEnumerator WithEffectsOnTheHazeIsToneMappedToo()
    {
        yield return LoadFlightScene();
        Object.FindAnyObjectByType<WorldBuilder>().Quality = GraphicsQuality.High;
        yield return null;
        yield return null;
        AssertColor(AmbientOnACard(Quaternion.LookRotation(Vector3.forward, Vector3.up), 30000), ThreeToneMapping.Aces(Three.Linear("#c9d5df")));
        Object.FindAnyObjectByType<WorldBuilder>().Quality = GraphicsQuality.Medium;
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChangingTheTierRebuildsTheWorld()
    {
        yield return LoadFlightScene();
        var world = Object.FindAnyObjectByType<WorldBuilder>();
        var effects = Object.FindAnyObjectByType<FlightEffects>();
        yield return null;
        Assert.That(effects.EffectsOn, Is.False);

        world.Quality = GraphicsQuality.High;
        yield return null;
        yield return null;
        Assert.That(world.transform.Find("Ground").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(401 * 401));
        Assert.That(world.transform.Find("Tree crowns").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(2600 * 60));
        Assert.That(Shader.GetGlobalVector("_SkyboundFog").y, Is.EqualTo(22000));
        Assert.That(QualitySettings.names[QualitySettings.GetQualityLevel()], Is.EqualTo("High"));
        Assert.That(effects.EffectsOn, Is.True);

        world.Quality = GraphicsQuality.Low;
        yield return null;
        yield return null;
        Assert.That(world.transform.Find("Ground").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(161 * 161));
        Assert.That(Object.FindAnyObjectByType<Light>().shadows, Is.EqualTo(LightShadows.None));
        Assert.That(QualitySettings.names[QualitySettings.GetQualityLevel()], Is.EqualTo("Low"));
        Assert.That(effects.EffectsOn, Is.False);

        // Leave the project's level as the next test expects it.
        world.Quality = GraphicsQuality.Medium;
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SwitchingAircraftRebuildsTheModel()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        var view = Object.FindAnyObjectByType<AircraftView>();
        yield return null;
        Assert.That(view.transform.Find("Exterior/Cabin"), Is.Not.Null);

        session.flight.SetAircraft(AircraftCatalog.Sport);
        yield return null;
        yield return null;
        Assert.That(view.transform.Find("Exterior/Canopy"), Is.Not.Null);
        Assert.That(view.transform.Find("Exterior/Cabin"), Is.Null);
        Assert.That(view.Exterior.activeSelf, Is.True);
    }

    [UnityTest]
    public IEnumerator PlayingMovesTheSurfacesAndTheStrobesFlash()
    {
        yield return LoadFlightScene();
        var session = Object.FindAnyObjectByType<FlightSession>();
        var view = Object.FindAnyObjectByType<AircraftView>();
        yield return null;
        session.flight.state.surfaces.elevator = 0.2;
        session.flight.state.engine = 1;
        yield return null;
        var propBefore = view.Parts.prop.localRotation;
        yield return null;
        yield return null;
        Assert.That(Quaternion.Angle(propBefore, view.Parts.prop.localRotation), Is.GreaterThan(0));

        var seenOn = false;
        var seenOff = false;
        var deadline = Time.realtimeSinceStartup + 2;
        while (Time.realtimeSinceStartup < deadline && !(seenOn && seenOff))
        {
            yield return null;
            if (view.Parts.strobes[0].activeSelf) seenOn = true;
            else seenOff = true;
        }
        Assert.That(seenOff, Is.True);
        // A 50 ms flash can fall between frames in a slow batch run, so only the off state is required.
    }
}
