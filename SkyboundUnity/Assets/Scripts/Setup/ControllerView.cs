using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// The controller in 3D, tilting with the live sensor. A second camera draws a small model to a
    /// render texture on the screen's layer of its own; the flight camera and world are off here.
    /// </summary>
    public class ControllerView : IDisposable
    {
        const int WIDTH = 900;
        const int HEIGHT = 520;
        /// <summary>Unity's built-in "Ignore Raycast" layer. Nothing else uses it.</summary>
        const int PREVIEW_LAYER = 2;
        const float URP_DIFFUSE = 0.96f;
        const float MIN_DISTANCE = 10, MAX_DISTANCE = 40;
        const float ORBIT_DEG_PER_PX = 0.4f;
        const float ZOOM_PER_WHEEL = 1.5f;

        readonly SensorHub hub;
        readonly Owned owned = new Owned();
        readonly GameObject rig;
        readonly Transform live;
        readonly Transform ghost;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly Light sceneSun;
        readonly AmbientMode savedAmbientMode;
        readonly SphericalHarmonicsL2 savedAmbientProbe;

        float azimuth, elevation, distance;
        bool dragging;
        double lastTime;

        public VisualElement Root { get; }
        public Transform Live => live;

        /// <summary>
        /// Aircraft attitude (deg) to rotation. Aircraft order yaw → pitch → roll. In Unity's space
        /// nose-up pitch is −X, nose-right yaw is +Y, right-wing-down roll is −Z.
        /// </summary>
        public static Quaternion AttitudeToQuaternion(float pitch, float roll, float yaw)
        {
            return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.right) * Quaternion.AngleAxis(-roll, Vector3.forward);
        }

        public ControllerView(Scope scope, SensorHub sensorHub)
        {
            hub = sensorHub;

            rig = new GameObject("Controller preview");
            // The flight's sun and sky light aren't this little scene's. Put back in Dispose.
            sceneSun = RenderSettings.sun != null ? RenderSettings.sun : UnityEngine.Object.FindAnyObjectByType<Light>();
            if (sceneSun != null) sceneSun.enabled = false;
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientProbe = RenderSettings.ambientProbe;
            var toUrp = 1 / (Mathf.PI * URP_DIFFUSE);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = WorldBuilder.HemisphereProbe(Three.Linear("#dfe6ec"), Three.Linear("#1a1d20"), 1.1f * toUrp);

            var sun = new GameObject("Preview light").AddComponent<Light>();
            sun.transform.SetParent(rig.transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 2.2f * toUrp;
            sun.color = Color.white;
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.LookRotation(-Three.Pos(6, 12, 4).normalized);

            ghost = Three.Group("Neutral", rig.transform, Vector3.zero);
            BuildModel(ghost, true);
            live = Three.Group("Live", rig.transform, Vector3.zero);
            BuildModel(live, false);
            BuildGrid(rig.transform);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PREVIEW_LAYER;

            texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Controller preview", antiAliasing = 4 };
            camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform, false);
            camera.fieldOfView = 35;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = NotToneMapped("#0a111c").gamma;
            camera.cullingMask = 1 << PREVIEW_LAYER;
            camera.targetTexture = texture;
            camera.GetUniversalAdditionalCameraData().renderShadows = false;

            var start = Three.Pos(9, 9, 15);
            distance = start.magnitude;
            elevation = Mathf.Asin(start.y / distance) * Mathf.Rad2Deg;
            azimuth = Mathf.Atan2(start.x, start.z) * Mathf.Rad2Deg;
            PlaceCamera();

            var image = Ui.Div("fill", "controller-view__image");
            image.style.backgroundImage = Background.FromRenderTexture(texture);
            image.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                image.CapturePointer(e.pointerId);
            });
            image.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                azimuth += e.deltaPosition.x * ORBIT_DEG_PER_PX;
                elevation = Mathf.Clamp(elevation + e.deltaPosition.y * ORBIT_DEG_PER_PX, -85, 85);
                PlaceCamera();
            });
            image.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                image.ReleasePointer(e.pointerId);
            });
            image.RegisterCallback<WheelEvent>(e =>
            {
                distance = Mathf.Clamp(distance + Mathf.Sign(e.delta.y) * ZOOM_PER_WHEEL, MIN_DISTANCE, MAX_DISTANCE);
                PlaceCamera();
            });

            var legendNeutral = Ui.Txt("", "controller-view__neutral");
            scope.Text(legendNeutral, () => "Outline: neutral" + (SettingsStore.Get().neutral.capturedAt > 0 ? "" : " (not set)"));
            var overlayText = Ui.Txt("", "controller-view__overlay-text");
            var overlay = Ui.Div("fill", "centre", "controller-view__overlay").With(overlayText);
            overlay.pickingMode = PickingMode.Ignore;
            overlayText.pickingMode = PickingMode.Ignore;
            scope.On(() =>
            {
                var text = OverlayText(hub.GetStatus());
                Ui.SetShown(overlay, text != null);
                if (text != null) Ui.SetText(overlayText, text);
            });

            var legend = Ui.Div("controller-view__legend").With(Ui.Txt("Solid: live sensor"), legendNeutral);
            legend.pickingMode = PickingMode.Ignore;
            var help = Ui.Txt("Arrow = forward · amber bar = right side · drag to orbit", "controller-view__help");
            help.pickingMode = PickingMode.Ignore;
            Root = Ui.Div("controller-view").With(image, legend, help, overlay);

            scope.On(Frame);
            lastTime = Time.unscaledTimeAsDouble;
        }

        /// <summary>The renderer tone-maps all of this camera's picture; the browser's background and grid aren't.</summary>
        static Color NotToneMapped(string hex) => ThreeToneMapping.Inverse(Three.Linear(hex));

        static string OverlayText(HubStatus status)
        {
            if (status.link == LinkState.Live && status.device == DeviceState.Ready) return null;
            if (status.link == LinkState.Disconnected) return "Connect a controller or the simulator";
            if (status.device == DeviceState.Calibrating) return "Device is calibrating — keep it still";
            if (status.link == LinkState.Awaiting || status.link == LinkState.Connecting) return "Waiting for sensor data…";
            if (status.link == LinkState.Lost) return "Sensor data stopped";
            return null;
        }

        void PlaceCamera()
        {
            var flat = Mathf.Cos(elevation * Mathf.Deg2Rad);
            var offset = new Vector3(Mathf.Sin(azimuth * Mathf.Deg2Rad) * flat, Mathf.Sin(elevation * Mathf.Deg2Rad), Mathf.Cos(azimuth * Mathf.Deg2Rad) * flat) * distance;
            camera.transform.SetPositionAndRotation(offset, Quaternion.LookRotation(-offset, Vector3.up));
        }

        void Frame()
        {
            var now = Time.unscaledTimeAsDouble;
            var dt = (float)(now - lastTime);
            lastTime = now;
            var s = SettingsStore.Get();
            ghost.localRotation = AttitudeToQuaternion((float)s.neutral.pitch, (float)s.neutral.roll, 0);
            var latest = hub.Latest();
            if (latest == null) return;
            var sample = latest.sample;
            // Yaw has no absolute reference, so only show it relative to neutral, and only when used.
            var yaw = s.control.yawMode == YawMode.Off ? 0 : (float)ControlMapping.Wrap180(sample.yaw - s.neutral.yaw);
            var target = AttitudeToQuaternion((float)sample.pitch, (float)sample.roll, yaw);
            // Display-only smoothing so 50 Hz samples look fluid at 60–144 fps.
            live.localRotation = Quaternion.Slerp(live.localRotation, target, 1 - Mathf.Exp(-dt * 30));
        }

        /// <summary>Breadboard-style controller: base plate, ESP32 dev board, GY-521 breakout, forward arrow. Units: cm.</summary>
        void BuildModel(Transform parent, bool isGhost)
        {
            Mesh Box(double w, double h, double d) => owned.Add(Geo.Box(w, h, d).ToMesh("Box"));
            Material Lit(string hex, float roughness, float metalness = 0) => owned.Add(MaterialKit.Lit(hex, roughness, metalness));

            if (isGhost)
            {
                var outline = owned.Add(MaterialKit.BasicTransparent("#d9a441", 0.06f));
                Three.Part("Base", parent, Box(7, 0.35, 11), outline, Vector3.zero);
                Three.Part("Outline", parent, BoxOutline(7, 0.35f, 11), owned.Add(MaterialKit.Basic("#d9a441")), Vector3.zero);
                return;
            }

            Three.Part("Base", parent, Box(7, 0.35, 11), Lit("#2b2f33", 0.85f), Vector3.zero);
            // ESP32 dev board at the back, USB end toward the pilot
            Three.Part("ESP32", parent, Box(2.8, 0.16, 5.2), Lit("#1c1f22", 0.6f), Three.Pos(0, 0.32f, 2.4f));
            var shield = Lit("#b9bcbf", 0.35f, 0.85f);
            Three.Part("Shield", parent, Box(1.6, 0.25, 1.8), shield, Three.Pos(0, 0.5f, 1.2f));
            Three.Part("USB", parent, Box(0.8, 0.28, 0.6), shield, Three.Pos(0, 0.48f, 4.85f));
            var pins = Lit("#c8a352", 0.3f, 0.9f);
            foreach (var x in new[] { -1.25f, 1.25f })
                Three.Part("Pins", parent, Box(0.18, 0.1, 4.8), pins, Three.Pos(x, 0.26f, 2.4f));
            // GY-521 (MPU-6050) breakout, +X arrow pointing forward
            Three.Part("GY-521", parent, Box(1.6, 0.12, 2.1), Lit("#2a4f8f", 0.55f), Three.Pos(0, 0.3f, -2.6f));
            Three.Part("Chip", parent, Box(0.4, 0.08, 0.4), Lit("#121212", 0.4f), Three.Pos(0, 0.4f, -2.5f));
            // Forward arrow
            Three.Part("Arrow", parent, owned.Add(Geo.Cone(0.55, 1.1, 3).ToMesh("Arrow")), Lit("#e8e4da", 0.7f), Three.Pos(0, 0.2f, -4.7f), Three.Euler(-Mathf.PI / 2, 0, 0));
            // Right-side marker so roll direction is unambiguous
            Three.Part("Right marker", parent, Box(0.3, 0.12, 2), Lit("#d9a441", 0.6f), Three.Pos(3.2f, 0.2f, 0));
        }

        Mesh BoxOutline(float w, float h, float d)
        {
            float x = w / 2, y = h / 2, z = d / 2;
            var corners = new[]
            {
                new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z),
                new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(-x, y, z),
            };
            var mesh = owned.Add(new Mesh { name = "Outline" });
            mesh.SetVertices(corners);
            mesh.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 }, MeshTopology.Lines, 0);
            return mesh;
        }

        /// <summary>The floor grid: cells every 1 cm, heavier lines every 5.</summary>
        void BuildGrid(Transform parent)
        {
            const int HALF = 20;
            var cells = new System.Collections.Generic.List<Vector3>();
            var sections = new System.Collections.Generic.List<Vector3>();
            for (var i = -HALF; i <= HALF; i++)
            {
                var list = i % 5 == 0 ? sections : cells;
                list.Add(new Vector3(i, 0, -HALF));
                list.Add(new Vector3(i, 0, HALF));
                list.Add(new Vector3(-HALF, 0, i));
                list.Add(new Vector3(HALF, 0, i));
            }
            GridLines(parent, cells, "#20252a");
            GridLines(parent, sections, "#2c3238");
        }

        void GridLines(Transform parent, System.Collections.Generic.List<Vector3> points, string hex)
        {
            var mesh = owned.Add(new Mesh { name = "Grid" });
            mesh.SetVertices(points);
            var indices = new int[points.Count];
            for (var i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            var material = owned.Add(MaterialKit.Basic(hex));
            material.SetColor("_BaseColor", NotToneMapped(hex).gamma);
            Three.Part("Grid", parent, mesh, material, Three.Pos(0, -3, 0));
        }

        public void Dispose()
        {
            if (sceneSun != null) sceneSun.enabled = true;
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientProbe = savedAmbientProbe;
            camera.targetTexture = null;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(rig);
            owned.DestroyAll();
        }
    }
}
