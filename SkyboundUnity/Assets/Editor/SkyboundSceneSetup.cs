using System.Linq;
using Skybound;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Builds the aircraft assets and wires up the Flight scene. Safe to run more than once.</summary>
public static class SkyboundSceneSetup
{
    const string TrainerPath = "Assets/Data/Trainer.asset";
    const string SportPath = "Assets/Data/Sport.asset";
    const string FlightScenePath = "Assets/Scenes/Flight.unity";
    const string OldMenuScenePath = "Assets/Scenes/Menu.unity";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string ToneMapFeatureName = "Three.js tone mapping";
    const string ComposerFeatureName = "Three.js effects end";
    const float SHADOW_DISTANCE_M = 100;

    [MenuItem("Skybound/Set Up Flight Scene")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[Skybound] Stop Play mode first, then run Set Up Flight Scene.");
            return;
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var trainer = EnsureAsset(TrainerPath, AircraftCatalog.CreateTrainer);
        var sport = EnsureAsset(SportPath, AircraftCatalog.CreateSport);
        EnsureMaterialTemplates();
        EnsureQualityLevels();
        EnsureRendererFeatures();

        var scene = EditorSceneManager.OpenScene(FlightScenePath, OpenSceneMode.Single);

        // Earlier stages' on-screen readout and controller panel, now replaced by the real UI.
        foreach (var rootObject in scene.GetRootGameObjects())
            foreach (var t in rootObject.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        var session = EnsureComponent<FlightSession>(FindOrCreate("Flight Session"));
        var serialized = new SerializedObject(session);
        var fleet = serialized.FindProperty("fleet");
        fleet.arraySize = 2;
        fleet.GetArrayElementAtIndex(0).objectReferenceValue = trainer;
        fleet.GetArrayElementAtIndex(1).objectReferenceValue = sport;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnsureComponent<ControllerLink>(FindOrCreate("Controller"));
        EnsureComponent<App>(FindOrCreate("UI"));

        EnsureComponent<WorldBuilder>(FindOrCreate("World"));
        EnsureComponent<AircraftView>(FindOrCreate("Aircraft"));

        var camera = Camera.main;
        if (camera == null) throw new System.InvalidOperationException("The Flight scene has no Main Camera.");
        EnsureComponent<ChaseCamera>(camera.gameObject);
        EnsureComponent<FlightEffects>(camera.gameObject);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // The menu is part of the one scene now, as it's one page in the browser game.
        AssetDatabase.DeleteAsset(OldMenuScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(FlightScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[Skybound] Flight scene is ready. Press Play.");
    }

    static AircraftDef EnsureAsset(string path, System.Func<AircraftDef> create)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AircraftDef>(path);
        if (existing != null) return existing;
        var asset = create();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>Saved materials, so a build keeps the URP Lit shader and the variants the scene uses.</summary>
    static void EnsureMaterialTemplates()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var lit = EnsureMaterial("Assets/Resources/" + MaterialKit.LitTemplatePath + ".mat", shader);
        MaterialKit.TurnOffEnvironmentReflections(lit);
        EditorUtility.SetDirty(lit);
        var unshadowed = EnsureMaterial("Assets/Resources/" + MaterialKit.UnshadowedTemplatePath + ".mat", shader);
        MaterialKit.TurnOffEnvironmentReflections(unshadowed);
        MaterialKit.TurnOffShadowReceiving(unshadowed);
        EditorUtility.SetDirty(unshadowed);
        var ground = EnsureMaterial("Assets/Resources/" + MaterialKit.GroundTemplatePath + ".mat", shader);
        MaterialKit.TurnOffEnvironmentReflections(ground);
        MaterialKit.TurnOnDetailMap(ground);
        EditorUtility.SetDirty(ground);
    }

    static Material EnsureMaterial(string path, Shader shader)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// One URP asset and quality level per graphics tier. There's no script API for adding
    /// quality levels, so this edits the settings file the way the Project Settings window does.
    /// </summary>
    static void EnsureQualityLevels()
    {
        var assets = new[]
        {
            EnsureRenderAsset("Low", 0.75f, 1, false, 512, false),
            EnsureRenderAsset("Medium", 1, 4, true, 1024, false),
            EnsureRenderAsset("High", 1, 4, true, 2048, true),
        };
        var names = new[] { "Low", "Medium", "High" };
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var levels = settings.FindProperty("m_QualitySettings");
        while (levels.arraySize < names.Length) levels.InsertArrayElementAtIndex(levels.arraySize - 1);
        while (levels.arraySize > names.Length) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
        for (var i = 0; i < names.Length; i++)
        {
            var level = levels.GetArrayElementAtIndex(i);
            level.FindPropertyRelative("name").stringValue = names[i];
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
            level.FindPropertyRelative("excludedTargetPlatforms").ClearArray();
            // One frame per screen refresh, as the browser's requestAnimationFrame gives.
            level.FindPropertyRelative("vSyncCount").intValue = 1;
        }
        settings.FindProperty("m_CurrentQuality").intValue = 1;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static Object EnsureRenderAsset(string tier, float renderScale, int msaa, bool shadows, int shadowResolution, bool hdrGrading)
    {
        var path = $"Assets/Settings/{tier}_RPAsset.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) == null && !AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset", path))
            throw new System.InvalidOperationException("Couldn't copy PC_RPAsset to " + path);
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        var serialized = new SerializedObject(asset);
        serialized.FindProperty("m_RenderScale").floatValue = renderScale;
        serialized.FindProperty("m_MSAA").intValue = msaa;
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = shadows;
        serialized.FindProperty("m_MainLightShadowmapResolution").intValue = shadowResolution;
        // The browser's shadow box is ±40 m around the aircraft. URP's is measured from the camera.
        serialized.FindProperty("m_ShadowDistance").floatValue = SHADOW_DISTANCE_M;
        serialized.FindProperty("m_ShadowCascadeCount").intValue = 2;
        // With effects on, tone mapping comes after URP's post-processing, which mustn't clip at 1 first.
        serialized.FindProperty("m_ColorGradingMode").intValue = hdrGrading ? (int)ColorGradingMode.HighDynamicRange : (int)ColorGradingMode.LowDynamicRange;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return asset;
    }

    /// <summary>
    /// three.js's tone mapping and fog as renderer features, on the renderer all three tiers share.
    /// The template's ambient occlusion goes off: the browser game has none, and it darkens the ground.
    /// </summary>
    static void EnsureRendererFeatures()
    {
        var material = EnsureMaterial("Assets/Resources/Skybound/ToneMap.mat", Resources.Load<Shader>("Shaders/ToneMap"));
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        foreach (var feature in renderer.rendererFeatures)
            if (feature is ScreenSpaceAmbientOcclusion) feature.SetActive(false);

        EnsureFullScreenFeature(renderer, ToneMapFeatureName, material, 0, FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents, ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        EnsureFullScreenFeature(renderer, ComposerFeatureName, material, 1, FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing, ScriptableRenderPassInput.Color);
        EditorUtility.SetDirty(renderer);
    }

    static void EnsureFullScreenFeature(UniversalRendererData renderer, string name, Material material, int pass, FullScreenPassRendererFeature.InjectionPoint injectionPoint, ScriptableRenderPassInput requirements)
    {
        var feature = renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == name);
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            var serialized = new SerializedObject(renderer);
            var features = serialized.FindProperty("m_RendererFeatures");
            var map = serialized.FindProperty("m_RendererFeatureMap");
            features.InsertArrayElementAtIndex(features.arraySize);
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(map.arraySize);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        feature.injectionPoint = injectionPoint;
        feature.requirements = requirements;
        feature.fetchColorBuffer = true;
        feature.passMaterial = material;
        feature.passIndex = pass;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
    }

    static GameObject FindOrCreate(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go : new GameObject(name);
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
}
