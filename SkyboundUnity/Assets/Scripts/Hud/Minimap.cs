using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// North-up moving map: a contour map of the terrain, baked once, slides under a fixed aircraft
    /// that turns with the heading. Only transforms change in flight, so nothing gets redrawn.
    /// </summary>
    public class Minimap : VisualElement
    {
        public const float SIZE = 160;
        /// <summary>The terrain tile, in metres. Past its edge the map is just the panel behind it.</summary>
        const float MAP_SPAN_M = 32000;
        /// <summary>What the circle shows, edge to edge.</summary>
        const float VIEW_SPAN_M = 8000;
        const int TEXELS = 512;
        const double CONTOUR_M = 40;
        const float RUNWAY_LENGTH_M = 1200;
        const float RUNWAY_WIDTH = 3;
        const float PLANE_SIZE = 24;
        /// <summary>Layout units a move has to reach before the map's transform is touched.</summary>
        const float MOVE_STEP = 0.1f;
        const float TURN_STEP_DEG = 0.25f;

        static readonly float MAP_SIZE = SIZE * MAP_SPAN_M / VIEW_SPAN_M;
        static Texture2D baked;

        readonly VisualElement map = Ui.Div("minimap__map");
        readonly CanvasElement face = new CanvasElement();
        readonly CanvasElement plane = new CanvasElement();
        readonly TabularLabel heading = new TabularLabel(13, "minimap__hdg");
        readonly IntLabel headingText;
        Vector2 shownAt = new Vector2(float.NaN, float.NaN);
        float shownHeading = float.NaN;

        /// <summary>Where the map has slid to, for tests.</summary>
        public Vector2 MapOffset => shownAt;

        public Minimap()
        {
            AddToClassList("glass");
            AddToClassList("minimap");
            map.style.width = MAP_SIZE;
            map.style.height = MAP_SIZE;
            map.usageHints = UsageHints.DynamicTransform;
            var runway = Ui.Div("minimap__runway");
            var scale = MAP_SIZE / MAP_SPAN_M;
            runway.style.left = MAP_SIZE / 2 - RUNWAY_WIDTH / 2;
            runway.style.top = MAP_SIZE / 2 - RUNWAY_LENGTH_M * scale;
            runway.style.width = RUNWAY_WIDTH;
            runway.style.height = RUNWAY_LENGTH_M * scale;
            map.Add(runway);
            Add(map);

            face.AddToClassList("minimap__face");
            face.style.width = SIZE;
            face.style.height = SIZE;
            Add(face);

            plane.AddToClassList("minimap__plane");
            plane.style.width = PLANE_SIZE;
            plane.style.height = PLANE_SIZE;
            plane.usageHints = UsageHints.DynamicTransform;
            Add(plane);

            Add(heading);
            headingText = new IntLabel(heading, v => v.ToString("000") + "°");
            DrawFixedParts();
        }

        /// <summary>Ring, north mark and the aircraft: they only change with the theme.</summary>
        public void DrawFixedParts()
        {
            var c = face.Begin();
            c.StrokeStyle(HudTheme.Dim);
            c.lineWidth = 1;
            c.BeginPath();
            c.Arc(SIZE / 2, SIZE / 2, SIZE / 4, 0, Mathf.PI * 2);
            c.Stroke();
            c.Text("N", SIZE / 2, 12, 14, HudTheme.Accent, TextAlign.Center, Face.Display);
            face.End();

            c = plane.Begin();
            c.FillStyle(HudTheme.Accent);
            c.BeginPath();
            c.MoveTo(PLANE_SIZE / 2, 0);
            c.LineTo(PLANE_SIZE - 3, PLANE_SIZE - 2);
            c.LineTo(PLANE_SIZE / 2, PLANE_SIZE - 7);
            c.LineTo(3, PLANE_SIZE - 2);
            c.ClosePath();
            c.Fill();
            plane.End();
        }

        /// <summary>The map costs a moment to bake, so it's done while the loading screen is up. Once per run.</summary>
        public void Prepare()
        {
            if (baked == null) baked = Bake();
            map.style.backgroundImage = Background.FromTexture2D(baked);
        }

        public void Frame(float northM, float eastM, float headingDeg)
        {
            var scale = MAP_SIZE / MAP_SPAN_M;
            var at = new Vector2(SIZE / 2 - (MAP_SIZE / 2 + eastM * scale), SIZE / 2 - (MAP_SIZE / 2 - northM * scale));
            if (!(Mathf.Abs(at.x - shownAt.x) < MOVE_STEP && Mathf.Abs(at.y - shownAt.y) < MOVE_STEP))
            {
                shownAt = at;
                map.style.translate = new Translate(at.x, at.y);
            }
            if (!(Mathf.Abs(Mathf.DeltaAngle(headingDeg, shownHeading)) < TURN_STEP_DEG))
            {
                shownHeading = headingDeg;
                plane.style.rotate = new Rotate(Angle.Degrees(headingDeg));
            }
            var whole = Mathf.RoundToInt(headingDeg) % 360;
            headingText.Set(whole == 0 ? 360 : whole);
        }

        /// <summary>Height tints with contour lines, from the same terrain function the aircraft can hit.</summary>
        static Texture2D Bake()
        {
            var heights = new double[TEXELS * TEXELS];
            var step = MAP_SPAN_M / TEXELS;
            for (var row = 0; row < TEXELS; row++)
            {
                var north = -MAP_SPAN_M / 2 + (row + 0.5f) * step;
                for (var col = 0; col < TEXELS; col++)
                {
                    var east = -MAP_SPAN_M / 2 + (col + 0.5f) * step;
                    heights[row * TEXELS + col] = TerrainHeight.At(north, east);
                }
            }
            var pixels = new Color32[TEXELS * TEXELS];
            for (var row = 0; row < TEXELS; row++)
            {
                for (var col = 0; col < TEXELS; col++)
                {
                    var h = heights[row * TEXELS + col];
                    var right = heights[row * TEXELS + Mathf.Min(col + 1, TEXELS - 1)];
                    var above = heights[Mathf.Min(row + 1, TEXELS - 1) * TEXELS + col];
                    var band = System.Math.Floor(h / CONTOUR_M);
                    var contour = band != System.Math.Floor(right / CONTOUR_M) || band != System.Math.Floor(above / CONTOUR_M);
                    var tint = Color.Lerp(HudTheme.MapLow, HudTheme.MapHigh, (float)(h / TerrainHeight.maxHillM));
                    pixels[row * TEXELS + col] = contour ? Color.Lerp(tint, HudTheme.MapContour, 0.55f) : tint;
                }
            }
            var texture = new Texture2D(TEXELS, TEXELS, TextureFormat.RGBA32, false)
            {
                name = "Minimap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
