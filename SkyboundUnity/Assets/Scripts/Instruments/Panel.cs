using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    public struct PanelState
    {
        public float rpm;
        public bool stall, brakes, easyFlight, lowAltitude;
    }

    /// <summary>
    /// The instrument panel as one texture: the six-pack (airspeed, attitude, altimeter / turn
    /// coordinator, heading, vertical speed) plus a tachometer and warning lights. The same texture
    /// is the cockpit panel and the optional on-screen panel, so both always show identical values.
    /// It is a UI panel of its own, drawing into a render texture instead of onto the screen.
    /// </summary>
    public class InstrumentPanel
    {
        public const int PANEL_W = 1024;
        public const int PANEL_H = 512;
        const float R = 116;
        static readonly float[] COL = { 128, 384, 640 };
        static readonly float[] ROW = { 128, 384 };
        const float RIGHT_X = 896;
        const int SPECKS = 4000;

        static readonly Color SURFACE = Three.Hex("#25282b");
        static readonly Color LIGHT_OFF = Three.Hex("#1a1c1d");
        static readonly Color LIGHT_EDGE = Three.Hex("#3a3d40");
        static readonly Color TEXT_ON = Three.Hex("#111111");
        static readonly Color TEXT_OFF = Three.Hex("#55585b");
        static readonly Color THR_TEXT = Three.Hex("#9a9b97");
        static readonly (string name, Color color)[] LIGHTS =
        {
            ("STALL", Three.Hex("#e0483a")),
            ("LOW ALT", Three.Hex("#e0a43a")),
            ("BRAKES", Three.Hex("#e0a43a")),
            ("EASY", Three.Hex("#5fb37a")),
        };

        static InstrumentPanel panel;

        readonly GameObject host;
        readonly CanvasElement faces = new CanvasElement();
        readonly CanvasElement dynamicParts = new CanvasElement();
        readonly CanvasElement counter = new CanvasElement();
        readonly CanvasElement needles = new CanvasElement();
        readonly bool[] lightStates = new bool[4];
        readonly string[] thrTexts = new string[101];

        public RenderTexture Texture { get; }

        /// <summary>Bumped on every redraw.</summary>
        public int version;

        /// <summary>App-wide panel, created on first use. A scene change destroys the host, so that's checked too.</summary>
        public static InstrumentPanel Get()
        {
            if (panel == null || panel.host == null)
            {
                panel?.Texture.Release();
                panel = new InstrumentPanel();
            }
            return panel;
        }

        InstrumentPanel()
        {
            Texture = new RenderTexture(PANEL_W, PANEL_H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Instrument panel",
                useMipMap = true,
                autoGenerateMips = true,
                anisoLevel = 8,
            };

            // Inactive until it has its settings, so the document's root exists as soon as it wakes.
            host = new GameObject("Instrument panel");
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = App.MakePanelSettings(Texture);
            host.SetActive(true);

            var root = doc.rootVisualElement;
            root.style.width = PANEL_W;
            root.style.height = PANEL_H;
            root.style.backgroundColor = SURFACE;
            root.style.backgroundImage = Specks();
            foreach (var layer in new[] { faces, dynamicParts, counter, needles })
            {
                layer.style.position = Position.Absolute;
                layer.style.left = 0;
                layer.style.top = 0;
                layer.style.width = PANEL_W;
                layer.style.height = PANEL_H;
                root.Add(layer);
            }
            DrawStatic(faces.Begin());
            faces.End();
        }

        /// <summary>Panel surface: matte, slightly textured dark grey.</summary>
        static Texture2D Specks()
        {
            var pixels = new Color32[PANEL_W * PANEL_H];
            var random = new System.Random(7);
            for (var i = 0; i < SPECKS; i++)
            {
                var x = random.Next(PANEL_W - 1);
                var y = random.Next(PANEL_H - 1);
                var alpha = (byte)(random.NextDouble() * 0.025 * 255);
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                        pixels[(y + dy) * PANEL_W + x + dx] = new Color32(255, 255, 255, alpha);
            }
            var texture = new Texture2D(PANEL_W, PANEL_H, TextureFormat.RGBA32, false) { name = "Panel specks" };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void DrawStatic(Ctx c)
        {
            Gauges.DrawAirspeedFace(c, COL[0], ROW[0], R);
            Gauges.DrawAltimeterFace(c, COL[2], ROW[0], R);
            Gauges.DrawTurnCoordinatorFace(c, COL[0], ROW[1], R);
            Gauges.DrawVsiFace(c, COL[2], ROW[1], R);
            Gauges.DrawTachFace(c, RIGHT_X, ROW[0], R * 0.92f);
        }

        /// <summary>Redraw from telemetry. Cheap enough to run every frame.</summary>
        public void Draw(Telemetry t, PanelState s)
        {
            var c = dynamicParts.Begin();
            Gauges.DrawAttitude(c, COL[1], ROW[0], R, (float)t.pitchDeg, (float)t.rollDeg);
            Gauges.DrawTurnCoordinatorDynamic(c, COL[0], ROW[1], R, (float)t.turnRateDps, (float)t.lateralG);
            Gauges.DrawHeading(c, COL[1], ROW[1], R, (float)t.headingDeg);
            DrawAnnunciators(c, t, s);
            dynamicParts.End();

            c = counter.Begin();
            Gauges.DrawAltimeterCounter(c, COL[2], ROW[0], R, (float)t.altitudeFt);
            counter.End();

            c = needles.Begin();
            Gauges.DrawAirspeedNeedle(c, COL[0], ROW[0], R, (float)t.airspeedKt);
            Gauges.DrawAltimeterNeedles(c, COL[2], ROW[0], R, (float)t.altitudeFt);
            Gauges.DrawVsiNeedle(c, COL[2], ROW[1], R, (float)t.verticalSpeedFpm);
            Gauges.DrawTachNeedle(c, RIGHT_X, ROW[0], R * 0.92f, s.rpm);
            needles.End();
            version++;
        }

        void DrawAnnunciators(Ctx c, Telemetry t, PanelState s)
        {
            lightStates[0] = s.stall;
            lightStates[1] = s.lowAltitude;
            lightStates[2] = s.brakes;
            lightStates[3] = s.easyFlight;
            var x0 = RIGHT_X - 92;
            var y0 = ROW[1] - 92;
            for (var i = 0; i < LIGHTS.Length; i++)
            {
                var on = lightStates[i];
                var x = x0 + (i % 2) * 96;
                var y = y0 + (i / 2) * 64;
                c.FillStyle(on ? LIGHTS[i].color : LIGHT_OFF);
                c.FillRect(x, y, 88, 48);
                c.StrokeStyle(LIGHT_EDGE);
                c.lineWidth = 3;
                c.StrokeRect(x, y, 88, 48);
                c.Text(LIGHTS[i].name, x + 44, y + 25, 17, on ? TEXT_ON : TEXT_OFF);
            }
            // Throttle position readout under the lights.
            var percent = (int)Math.Round(t.throttle * 100);
            c.Text(thrTexts[percent] ?? (thrTexts[percent] = $"THR {percent}%"), RIGHT_X, y0 + 152, 18, THR_TEXT);
        }
    }

    public static class Panel
    {
        /// <summary>Engine speed the tachometer shows: idle ~650 rpm, full power ~2,600.</summary>
        public static double EngineRpm(double engine, double airspeedKt)
        {
            return 650 + engine * 1850 + Math.Min(150, airspeedKt * 1.2) * (0.3 + engine * 0.7);
        }
    }
}
