using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    public interface IScreen
    {
        void Frame();
        void Unmount();
    }

    /// <summary>
    /// The browser game's App.tsx: shows the menu, setup or flight screen. It's one page there and one
    /// scene here, so the sensor link stays connected across screens. World and aircraft are only
    /// built and drawn for a flight.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(UIDocument))]
    public class App : MonoBehaviour
    {
        const int REFERENCE_WIDTH = 1280;
        const int REFERENCE_HEIGHT = 720;

        /// <summary>Tests start straight in a flight, without the menu or loading screen.</summary>
        public static AppScreen? startScreenOverride;

        UIDocument document;
        VisualElement root;
        Camera mainCamera;
        int flightCullingMask;
        CameraClearFlags flightClearFlags;
        bool? cameraDrawsFlight;
        IScreen current;
        AppScreen mounted;

        public static App Instance { get; private set; }
        public FlightSession Session { get; private set; }
        public ControllerLink Link { get; private set; }
        public IScreen Current => current;
        public VisualElement Root => root;

        /// <summary>Every UI panel: scaled as if the window were 1280×720, the size the browser layout is sized for here.</summary>
        public static PanelSettings MakePanelSettings(RenderTexture target = null)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = target != null ? target.name : "Skybound UI";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/SkyboundTheme");
            if (target != null)
            {
                settings.targetTexture = target;
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.scale = 1;
                settings.clearColor = true;
                settings.colorClearValue = Color.clear;
            }
            else
            {
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                settings.referenceResolution = new Vector2Int(REFERENCE_WIDTH, REFERENCE_HEIGHT);
                settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                settings.match = 0.5f;
            }
            return settings;
        }

        void Awake()
        {
            Instance = this;
            Session = FindAnyObjectByType<FlightSession>();
            Link = FindAnyObjectByType<ControllerLink>();
            mainCamera = Camera.main;
            flightCullingMask = mainCamera.cullingMask;
            flightClearFlags = mainCamera.clearFlags;
            if (startScreenOverride.HasValue)
            {
                UiStore.screen = startScreenOverride.Value;
                UiStore.mountScene = UiStore.screen == AppScreen.Flight;
            }
            WorldBuilder.SelectQualityLevel(SettingsStore.Get().graphics);
            ApplyCamera();

            document = GetComponent<UIDocument>();
            document.panelSettings = MakePanelSettings();
        }

        // In a build the document's root only exists once its OnEnable has run, which is after this Awake.
        void Start()
        {
            root = document.rootVisualElement;
            root.AddToClassList("fill");
            root.style.unityFontDefinition = Fonts.Body;
            Mount();
        }

        void Update()
        {
            if (UiStore.screen != mounted) Mount();
            current.Frame();
            ApplyCamera();
            SettingsStore.Tick();
        }

        /// <summary>
        /// Off a flight the camera stays on but draws nothing, behind the opaque screens. With no
        /// camera at all, the Editor writes "No cameras rendering" across the Game view.
        /// </summary>
        void ApplyCamera()
        {
            var flying = UiStore.screen == AppScreen.Flight && UiStore.mountScene;
            if (flying == cameraDrawsFlight) return;
            cameraDrawsFlight = flying;
            mainCamera.cullingMask = flying ? flightCullingMask : 0;
            mainCamera.clearFlags = flying ? flightClearFlags : CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
        }

        void Mount()
        {
            current?.Unmount();
            root.Clear();
            mounted = UiStore.screen;
            switch (mounted)
            {
                case AppScreen.Flight: current = new FlightScreen(root, this, startScreenOverride.HasValue); break;
                case AppScreen.Setup: current = new SetupScreen(root, this); break;
                default: current = new MenuScreen(root, this); break;
            }
        }

        void OnDisable()
        {
            SettingsStore.Flush();
            current?.Unmount();
            current = null;
        }

        void OnApplicationQuit()
        {
            SettingsStore.Flush();
        }

#if !UNITY_EDITOR
        /// <summary>Pause automatically when the window loses focus so the aircraft doesn't fly unattended.</summary>
        void OnApplicationFocus(bool focused)
        {
            if (!focused && UiStore.screen == AppScreen.Flight) UiStore.SetPaused(true);
        }
#endif
    }
}
