using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybound
{
    /// <summary>
    /// Airfield and surroundings. Runway 36/18: 1,200 m × 30 m, threshold of 36 at the origin,
    /// running north (Unity +z, three.js −z). Everything is procedural so the game needs no
    /// imported assets. Scenery numbers are the browser game's, in three.js space.
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        const float TERRAIN_SIZE_M = 32000;
        const float RUNWAY_LENGTH_M = 1200;
        const float RUNWAY_WIDTH_M = 30;
        const float FOG_START_M = 2500;
        const float CLOUD_SORT_INTERVAL_S = 0.25f;
        const float URP_DIFFUSE = 0.96f;

        /// <summary>Towards the sun, in Unity's space.</summary>
        public static readonly Vector3 SUN_DIR = Three.Pos(-0.45f, 0.62f, 0.35f).normalized;

        static readonly Color Meadow = Three.Linear("#6d8a4c");
        static readonly Color Wood = Three.Linear("#3f5c36");
        static readonly Color Dry = Three.Linear("#8f8a62");
        static readonly Color Rock = Three.Linear("#8a8578");

        readonly Owned owned = new Owned();
        GraphicsQuality built;
        Transform cloudTransform;
        Mesh cloudMesh;
        Vector3[] cloudCentres;
        float[] cloudDistances;
        int[] cloudOrder;
        int[] cloudTriangles;
        float nextCloudSort;

        /// <summary>The tier the world is built at.</summary>
        public GraphicsProfile Profile { get; private set; }
        public GraphicsQuality Quality
        {
            get => SettingsStore.Get().graphics;
            set => SettingsStore.SetGraphics(value);
        }

        void Awake()
        {
            if (UiStore.mountScene) Build();
        }

        void OnDestroy()
        {
            owned.DestroyAll();
        }

        void Build()
        {
            built = Quality;
            Profile = GraphicsProfile.For(built);
            SelectQualityLevel(built);

            // The ToneMap pass does the fog, the way three.js does. URP's own would be mixed in too early.
            RenderSettings.fog = false;
            Shader.SetGlobalVector("_SkyboundFog", new Vector4(FOG_START_M, Profile.fogFar));

            Sky();
            Lights();
            Ground(Profile.terrainSegments);
            Runway();
            Trees(Profile.trees);
            Hills();
            Clouds(Profile.clouds);
        }

        void Update()
        {
            if (!UiStore.mountScene) return;
            if (Profile == null) Build();
            else if (Quality != built)
            {
                for (var i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i).gameObject;
                    child.SetActive(false);
                    Destroy(child);
                }
                owned.DestroyAll();
                Build();
            }
        }

        void LateUpdate()
        {
            if (Profile != null) SortClouds();
        }

        /// <summary>The setup menu makes a URP quality level per tier. Without them, the current one stays.</summary>
        public static void SelectQualityLevel(GraphicsQuality quality)
        {
            var level = Array.IndexOf(QualitySettings.names, quality.ToString());
            if (level >= 0 && level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
        }

        // ---------- Random numbers ----------

        /// <summary>The browser game's generator, so scenery lands in the same places.</summary>
        public sealed class Rng
        {
            uint seed;

            public Rng(uint seed)
            {
                this.seed = seed;
            }

            public double Next()
            {
                seed = unchecked(seed * 1664525u + 1013904223u);
                return seed / 4294967296.0;
            }
        }

        // ---------- Sky and lights ----------

        void Sky()
        {
            var material = owned.Add(new Material(Resources.Load<Shader>("Shaders/Sky")));
            material.SetVector("_SunPosition", SUN_DIR * 1000);
            material.SetFloat("_Turbidity", 5);
            material.SetFloat("_Rayleigh", 1.2f);
            material.SetFloat("_MieCoefficient", 0.004f);
            material.SetFloat("_MieDirectionalG", 0.8f);
            RenderSettings.skybox = material;
        }

        /// <summary>
        /// three.js divides diffuse light by π and URP doesn't. URP also takes a non-metal's 4%
        /// reflectance out of its diffuse colour, and three.js doesn't. Dividing both lights by
        /// these gives the same brightness.
        /// </summary>
        void Lights()
        {
            var toUrp = 1 / (Mathf.PI * URP_DIFFUSE);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = HemisphereProbe(Three.Linear("#dfe9f2"), Three.Linear("#5d6b4a"), toUrp);
            // three.js has no environment map here.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0;

            var sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) sun = light;
            }
            if (sun == null) return;
            sun.transform.rotation = Quaternion.LookRotation(-SUN_DIR);
            sun.useColorTemperature = false;
            sun.color = Three.Hex("#fff3df");
            sun.intensity = 2.6f * toUrp;
            sun.shadows = Profile.shadows ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>
        /// three.js's HemisphereLight: sky colour facing up, ground colour facing down, a straight
        /// blend in between. As spherical harmonics that is a constant plus the y term.
        /// </summary>
        public static SphericalHarmonicsL2 HemisphereProbe(Color sky, Color ground, float intensity)
        {
            var probe = new SphericalHarmonicsL2();
            for (var channel = 0; channel < 3; channel++)
            {
                probe[channel, 0] = (sky[channel] + ground[channel]) / 2 * intensity;
                probe[channel, 1] = (sky[channel] - ground[channel]) / 2 * intensity;
            }
            return probe;
        }

        // ---------- Textures ----------

        /// <summary>
        /// The browser game's noise canvas, as bytes with row 0 at the top: per-pixel noise, then
        /// large soft patches so the ground isn't uniform from altitude.
        /// </summary>
        public static Color32[] NoisePixels(int size, Vector3Int colour, double spread, uint seed)
        {
            var r = new Rng(seed);
            var red = new float[size * size];
            var green = new float[size * size];
            var blue = new float[size * size];
            for (var i = 0; i < size * size; i++)
            {
                var n = (r.Next() - 0.5) * spread;
                red[i] = ClampedByte(colour.x + n);
                green[i] = ClampedByte(colour.y + n);
                blue[i] = ClampedByte(colour.z + n * 0.7);
            }
            for (var i = 0; i < 40; i++)
            {
                var patchGreen = r.Next() > 0.5;
                var alpha = (float)(0.05 + r.Next() * 0.08);
                var cx = r.Next() * size;
                var cy = r.Next() * size;
                var radius = 8 + r.Next() * size * 0.25;
                var patch = patchGreen ? new Vector3(70, 95, 45) : new Vector3(120, 130, 70);
                var xMin = Math.Max(0, (int)(cx - radius));
                var xMax = Math.Min(size - 1, (int)(cx + radius));
                var yMin = Math.Max(0, (int)(cy - radius));
                var yMax = Math.Min(size - 1, (int)(cy + radius));
                for (var y = yMin; y <= yMax; y++)
                {
                    for (var x = xMin; x <= xMax; x++)
                    {
                        var dx = x + 0.5 - cx;
                        var dy = y + 0.5 - cy;
                        if (dx * dx + dy * dy > radius * radius) continue;
                        var at = y * size + x;
                        red[at] = Mathf.Round(Mathf.Lerp(red[at], patch.x, alpha));
                        green[at] = Mathf.Round(Mathf.Lerp(green[at], patch.y, alpha));
                        blue[at] = Mathf.Round(Mathf.Lerp(blue[at], patch.z, alpha));
                    }
                }
            }
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)red[i], (byte)green[i], (byte)blue[i], 255);
            return pixels;
        }

        /// <summary>Uint8ClampedArray: rounds to nearest, ties to even, as Math.Round does.</summary>
        static float ClampedByte(double value)
        {
            return (float)Math.Round(Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>A canvas's row 0 is its top, and three.js flips it to v = 1. Unity's row 0 is v = 0.</summary>
        static Color32[] Flipped(Color32[] pixels, int size)
        {
            var flipped = new Color32[pixels.Length];
            for (var y = 0; y < size; y++)
                Array.Copy(pixels, y * size, flipped, (size - 1 - y) * size, size);
            return flipped;
        }

        static Texture2D NewTexture(string name, int size, Color32[] pixels, bool linear, TextureWrapMode wrap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D NoiseTexture(string name, int size, Vector3Int colour, double spread, uint seed)
        {
            return NewTexture(name, size, Flipped(NoisePixels(size, colour, spread, seed), size), false, TextureWrapMode.Repeat);
        }

        /// <summary>
        /// The ground's detail noise. In three.js it multiplies the vertex colours as linear colour.
        /// URP's detail map multiplies by twice the texel, read as linear, so a texel holds half that value.
        /// </summary>
        public static Texture2D GroundDetailTexture()
        {
            const int size = 256;
            var pixels = Flipped(NoisePixels(size, new Vector3Int(150, 150, 150), 30, 7), size);
            for (var i = 0; i < pixels.Length; i++)
            {
                Color linear = ((Color)pixels[i]).linear;
                pixels[i] = new Color(linear.r / 2, linear.g / 2, linear.b / 2, 1);
            }
            return NewTexture("Ground detail", size, pixels, true, TextureWrapMode.Repeat);
        }

        // ---------- Ground ----------

        /// <summary>
        /// Shades by height and slope: meadow in the valleys, darker woodland on the
        /// slopes, dry grass and rock on the tops. Linear colour, mixed as in three.js.
        /// </summary>
        public static Color GroundColor(double north, double east)
        {
            var h = TerrainHeight.At(north, east);
            var dn = TerrainHeight.At(north + 40, east) - h;
            var de = TerrainHeight.At(north, east + 40) - h;
            var slope = Math.Min(1, Math.Sqrt(dn * dn + de * de) / 18);
            var n = Math.Sin(east * 0.0017) * Math.Cos(north * 0.0021) * 0.5 + 0.5; // broad patchiness
            var c = Color.Lerp(Meadow, Wood, (float)Math.Min(1, slope * 1.4 + (h > 25 ? 0.35 : 0) + n * 0.25));
            if (h > 170) c = Color.Lerp(c, Dry, (float)Math.Min(1, (h - 170) / 90));
            if (h > 250 || slope > 0.85) c = Color.Lerp(c, Rock, (float)Math.Min(1, Math.Max((h - 250) / 80, (slope - 0.85) / 0.15)));
            return c;
        }

        static void GridPoint(int i, int j, int segments, out float east, out float north)
        {
            east = -TERRAIN_SIZE_M / 2 + TERRAIN_SIZE_M * i / segments;
            north = -TERRAIN_SIZE_M / 2 + TERRAIN_SIZE_M * j / segments;
        }

        /// <summary>The terrain tile as a grid: x = east, z = north, y = up.</summary>
        public static Mesh BuildGroundMesh(int segments)
        {
            var side = segments + 1;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[side * side];
            for (var j = 0; j < side; j++)
            {
                for (var i = 0; i < side; i++)
                {
                    GridPoint(i, j, segments, out var east, out var north);
                    vertices[j * side + i] = new Vector3(east, (float)TerrainHeight.At(north, east), north);
                    // Texel centres, so each vertex reads exactly its own colour.
                    uv[j * side + i] = new Vector2((i + 0.5f) / side, (j + 0.5f) / side);
                }
            }

            var triangles = new int[segments * segments * 6];
            var t = 0;
            for (var j = 0; j < segments; j++)
            {
                for (var i = 0; i < segments; i++)
                {
                    var corner = j * side + i;
                    var toEast = corner + 1;
                    var toNorth = corner + side;
                    var far = corner + side + 1;
                    triangles[t++] = corner;
                    triangles[t++] = toNorth;
                    triangles[t++] = toEast;
                    triangles[t++] = toEast;
                    triangles[t++] = toNorth;
                    triangles[t++] = far;
                }
            }

            // More than 65,535 vertices at the higher grid sizes.
            var mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// URP Lit ignores vertex colours, so the browser game's per-vertex colours are
        /// baked into a texture with one texel per vertex. It filters the same way.
        /// </summary>
        public static Texture2D BuildGroundTexture(int segments)
        {
            var side = segments + 1;
            var pixels = new Color32[side * side];
            for (var j = 0; j < side; j++)
            {
                for (var i = 0; i < side; i++)
                {
                    GridPoint(i, j, segments, out var east, out var north);
                    pixels[j * side + i] = GroundColor(north, east).gamma;
                }
            }
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, true, false)
            {
                name = "Terrain colours",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void Ground(int segments)
        {
            var material = owned.Add(MaterialKit.LitGround(
                owned.Add(BuildGroundTexture(segments)),
                owned.Add(GroundDetailTexture()),
                900));
            var mesh = owned.Add(BuildGroundMesh(segments));
            Three.Part("Ground", transform, mesh, material, Vector3.zero);

            // Beyond the terrain tile: flat plain under the haze to the horizon
            Three.Part("Plain", transform, owned.Add(Geo.Plane(120000, 120000).ToMesh("Plain")),
                owned.Add(MaterialKit.Lit("#6d8a4c")), Three.Pos(0, -2, 0), Three.Euler(-Mathf.PI / 2, 0, 0));
        }

        // ---------- Runway ----------

        public struct RunwayMark
        {
            public float x, z, w, l;

            public RunwayMark(float x, float z, float w, float l)
            {
                this.x = x;
                this.z = z;
                this.w = w;
                this.l = l;
            }
        }

        public static List<RunwayMark> RunwayMarks()
        {
            const float L = RUNWAY_LENGTH_M;
            const float W = RUNWAY_WIDTH_M;
            var m = new List<RunwayMark>();
            // Centreline: 30 m stripes, 20 m gaps, clear of the threshold areas.
            for (var d = 90f; d < L - 90; d += 50) m.Add(new RunwayMark(0, -(d + 15), 0.9f, 30));
            // Distance from the runway-36 threshold (z = 0) to a marking's centre.
            foreach (var fromThreshold in new[] { 21, L - 21 })
            {
                // Threshold "piano keys": 30 m long, starting 6 m in from each end.
                for (var i = 0; i < 8; i++) m.Add(new RunwayMark((i < 4 ? -1 : 1) * (2.5f + (i % 4) * 3.2f), -fromThreshold, 1.8f, 30));
            }
            // Aiming-point blocks 300 m in from each end.
            foreach (var fromThreshold in new[] { 300, L - 300 })
                for (var s = -1; s <= 1; s += 2) m.Add(new RunwayMark(s * 6, -fromThreshold, 4, 45));
            // Edge lines
            for (var s = -1; s <= 1; s += 2) m.Add(new RunwayMark(s * (W / 2 - 1), -L / 2, 0.9f, L));
            return m;
        }

        void Flat(string name, double width, double length, Material material, float x, float y, float z)
        {
            Three.Part(name, transform, owned.Add(Geo.Plane(width, length).ToMesh(name)), material, Three.Pos(x, y, z), Three.Euler(-Mathf.PI / 2, 0, 0));
        }

        void Runway()
        {
            const float L = RUNWAY_LENGTH_M;
            const float W = RUNWAY_WIDTH_M;
            var asphalt = owned.Add(NoiseTexture("Asphalt", 128, new Vector3Int(64, 66, 68), 22, 3));
            var surface = owned.Add(MaterialKit.LitTextured(asphalt, new Vector2(4, 160), 0.92f));
            Flat("Runway", W, L + 60, surface, 0, 0.03f, -L / 2);

            // One mesh instead of 43.
            var paint = owned.Add(MaterialKit.Lit("#e9e8e2", 0.8f, 0, true));
            var marks = new Geo();
            var faceUp = new Quaternion(-Mathf.Sqrt(0.5f), 0, 0, Mathf.Sqrt(0.5f));
            foreach (var k in RunwayMarks())
                Geo.Plane(k.w, k.l).AppendTo(marks, Matrix4x4.TRS(new Vector3(k.x, 0.06f, k.z), faceUp, Vector3.one));
            Three.Part("Runway marks", transform, owned.Add(marks.ToMesh("Runway marks")), paint, Vector3.zero);

            // Taxiway to the apron and two hangars on the east side
            Flat("Taxiway", 60, 15, surface, 45, 0.03f, -260);
            Flat("Apron", 80, 110, owned.Add(MaterialKit.Lit("#6d6f70", 0.95f, 0, true)), 110, 0.03f, -250);
            Hangar(125, -210, "#a39c8f");
            Hangar(125, -290, "#8b9196");

            // Windsock
            var windsock = Three.Group("Windsock", transform, Three.Pos(-30, 0, -150));
            Three.Part("Pole", windsock, owned.Add(Geo.Cylinder(0.06, 0.06, 6, 8).ToMesh("Pole")), owned.Add(MaterialKit.Lit("#8a8f93")), Three.Pos(0, 3, 0));
            Three.Part("Sock", windsock, owned.Add(Geo.Cone(0.35, 2.2, 12, 1, true).ToMesh("Sock")),
                owned.Add(MaterialKit.DoubleSided(MaterialKit.Lit("#d26a2c"))), Three.Pos(0, 5.8f, 1.1f), Three.Euler(Mathf.PI / 2 + 0.15f, 0, 0));
        }

        void Hangar(float x, float z, string colour)
        {
            var hangar = Three.Group("Hangar", transform, Three.Pos(x, 0, z));
            Three.Part("Building", hangar, owned.Add(Geo.Box(26, 10, 32).ToMesh("Building")), owned.Add(MaterialKit.Lit(colour, 0.85f, 0, true)), Three.Pos(0, 5, 0), null, null, true);
            Three.Part("Door", hangar, owned.Add(Geo.Box(0.1, 8, 26).ToMesh("Door")), owned.Add(MaterialKit.Lit("#3a3e42", 0.6f)), Three.Pos(-13.05f, 4, 0));
        }

        // ---------- Trees ----------

        public struct TreeSpot
        {
            public double x, z, h, ground, turn;
            /// <summary>Crown colour, linear.</summary>
            public Color color;
        }

        /// <summary>Clusters: pick a grove centre, then scatter around it.</summary>
        public static TreeSpot[] PlaceTrees(int count)
        {
            var r = new Rng(42);
            var trees = new TreeSpot[count];
            var n = 0;
            while (n < count)
            {
                var cx = (r.Next() - 0.5) * 9000;
                var cz = (r.Next() - 0.5) * 9000 - 600;
                var groveSize = 20 + Math.Floor(r.Next() * 60);
                for (var i = 0; i < groveSize && n < count; i++)
                {
                    var x = cx + (r.Next() - 0.5) * 260;
                    var z = cz + (r.Next() - 0.5) * 260;
                    // Keep the runway strip, approach paths and apron clear.
                    if (Math.Abs(x) < 160 && z < 900 && z > -2100) continue;
                    var h = 7 + r.Next() * 9;
                    var turn = r.Next() * 6;
                    var hue = 0.25 + r.Next() * 0.07;
                    var saturation = 0.35 + r.Next() * 0.15;
                    var lightness = 0.2 + r.Next() * 0.08;
                    trees[n] = new TreeSpot { x = x, z = z, h = h, ground = TerrainHeight.At(-z, x), turn = turn, color = Hsl(hue, saturation, lightness) };
                    n++;
                }
            }
            return trees;
        }

        /// <summary>three.js's Color.setHSL. Its result is used as linear colour directly.</summary>
        public static Color Hsl(double h, double s, double l)
        {
            var p = l <= 0.5 ? l * (1 + s) : l + s - l * s;
            var q = 2 * l - p;
            return new Color((float)HueToRgb(q, p, h + 1.0 / 3), (float)HueToRgb(q, p, h), (float)HueToRgb(q, p, h - 1.0 / 3));
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * 6 * (2.0 / 3 - t);
            return p;
        }

        /// <summary>
        /// three.js draws these as InstancedMesh with a colour per instance. URP Lit has no
        /// per-instance colour, so all crowns are one mesh, each tree's UVs pointing at its own
        /// texel in a palette texture.
        /// </summary>
        void Trees(int count)
        {
            const int PALETTE_WIDTH = 64;
            var spots = PlaceTrees(count);
            var paletteHeight = (count + PALETTE_WIDTH - 1) / PALETTE_WIDTH;
            var palette = new Texture2D(PALETTE_WIDTH, paletteHeight, TextureFormat.RGBA32, false, false)
            {
                name = "Tree colours",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[PALETTE_WIDTH * paletteHeight];

            var trunk = Geo.Cylinder(0.18, 0.25, 1, 5);
            var crown = Geo.Icosahedron(1);
            var trunks = new Geo();
            var crowns = new Geo();
            for (var n = 0; n < count; n++)
            {
                var t = spots[n];
                var x = (float)t.x;
                var z = (float)t.z;
                var h = (float)t.h;
                var ground = (float)t.ground;
                pixels[n] = t.color.gamma;
                var texel = new Vector2((n % PALETTE_WIDTH + 0.5f) / PALETTE_WIDTH, (n / PALETTE_WIDTH + 0.5f) / paletteHeight);
                trunk.AppendTo(trunks, Matrix4x4.TRS(new Vector3(x, ground + h * 0.22f, z), Quaternion.identity, new Vector3(1, h * 0.44f, 1)));
                var turn = (float)t.turn / 2;
                var rotation = new Quaternion(0, Mathf.Sin(turn), 0, Mathf.Cos(turn));
                crown.AppendTo(crowns, Matrix4x4.TRS(new Vector3(x, ground + h * 0.62f, z), rotation, new Vector3(h * 0.32f, h * 0.75f, h * 0.32f)), texel);
            }
            palette.SetPixels32(pixels);
            palette.Apply();
            owned.Add(palette);

            Three.Part("Tree trunks", transform, owned.Add(trunks.ToMesh("Tree trunks")), owned.Add(MaterialKit.Lit("#4b3a2a")), Vector3.zero, null, null, true);
            Three.Part("Tree crowns", transform, owned.Add(crowns.ToMesh("Tree crowns")), owned.Add(MaterialKit.LitTextured(palette, Vector2.one, 0.95f)), Vector3.zero, null, null, true);
        }

        // ---------- Hills ----------

        public struct HillSpot
        {
            public double x, z, h, w;
        }

        public static HillSpot[] PlaceHills()
        {
            var r = new Rng(9);
            var hills = new HillSpot[46];
            for (var i = 0; i < hills.Length; i++)
            {
                var a = (double)i / 46 * Math.PI * 2 + r.Next() * 0.1;
                var d = 17000 + r.Next() * 8000; // beyond the terrain tile
                var h = 600 + r.Next() * 900;
                var w = 2600 + r.Next() * 3000;
                hills[i] = new HillSpot { x = Math.Cos(a) * d, z = Math.Sin(a) * d - 600, h = h, w = w };
            }
            return hills;
        }

        void Hills()
        {
            var cone = Geo.Cone(1, 1, 7, 1).Flattened();
            var hills = new Geo();
            foreach (var h in PlaceHills())
            {
                var position = new Vector3((float)h.x, (float)(h.h / 2 - 5), (float)h.z);
                cone.AppendTo(hills, Matrix4x4.TRS(position, Quaternion.identity, new Vector3((float)h.w, (float)h.h, (float)(h.w * 0.8))));
            }
            Three.Part("Hills", transform, owned.Add(hills.ToMesh("Hills")), owned.Add(MaterialKit.Lit("#6f8162")), Vector3.zero);
        }

        // ---------- Clouds ----------

        public struct CloudPuff
        {
            public double x, y, z, size, opacity;
            /// <summary>Linear.</summary>
            public Color shade;
        }

        public static List<CloudPuff> PlaceClouds(int count)
        {
            var r = new Rng(17);
            var shaded = Three.Linear("#b9c2cc");
            var puffs = new List<CloudPuff>();
            for (var c = 0; c < count; c++)
            {
                var cx = (r.Next() - 0.5) * 14000;
                var cz = (r.Next() - 0.5) * 14000;
                var cy = 900 + r.Next() * 500;
                var parts = 5 + Math.Floor(r.Next() * 6);
                for (var p = 0; p < parts; p++)
                {
                    var dy = r.Next() * 120;
                    // Shaded bases, bright tops: lower puffs are tinted grey-blue.
                    var shade = Color.Lerp(shaded, Color.white, (float)Math.Min(1, dy / 90));
                    var opacity = 0.6 + r.Next() * 0.25;
                    var size = 220 + r.Next() * 300 + (dy > 60 ? 80 : 0);
                    var x = cx + (r.Next() - 0.5) * 520;
                    var z = cz + (r.Next() - 0.5) * 320;
                    puffs.Add(new CloudPuff { x = x, y = cy + dy, z = z, size = size, opacity = opacity, shade = shade });
                }
            }
            return puffs;
        }

        /// <summary>The soft round sprite. Bytes with row 0 at the top; it's symmetric, so no flip.</summary>
        public static Color32[] CloudPixels()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5 - 64;
                    var dy = y + 0.5 - 64;
                    var t = Mathf.Clamp01((float)((Math.Sqrt(dx * dx + dy * dy) - 4) / 60));
                    Color color = t < 0.5f
                        ? Color.Lerp(new Color(1, 1, 1, 0.95f), new Color(250 / 255f, 250 / 255f, 252 / 255f, 0.55f), t * 2)
                        : Color.Lerp(new Color(250 / 255f, 250 / 255f, 252 / 255f, 0.55f), new Color(245 / 255f, 247 / 255f, 250 / 255f, 0), (t - 0.5f) * 2);
                    pixels[y * size + x] = color;
                }
            }
            return pixels;
        }

        /// <summary>
        /// three.js draws one Sprite per puff and sorts them by distance every frame. Here all puffs
        /// are one mesh, the triangles reordered back to front a few times a second.
        /// </summary>
        void Clouds(int count)
        {
            var puffs = PlaceClouds(count);
            var vertices = new Vector3[puffs.Count * 4];
            var uv = new Vector2[vertices.Length];
            var offsets = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            cloudCentres = new Vector3[puffs.Count];
            cloudDistances = new float[puffs.Count];
            cloudOrder = new int[puffs.Count];
            cloudTriangles = new int[puffs.Count * 6];
            for (var i = 0; i < puffs.Count; i++)
            {
                var p = puffs[i];
                var centre = Three.Pos((float)p.x, (float)p.y, (float)p.z);
                var color = new Color(p.shade.r, p.shade.g, p.shade.b, (float)p.opacity);
                var width = (float)p.size;
                var height = width * 0.55f;
                cloudCentres[i] = centre;
                cloudOrder[i] = i;
                for (var corner = 0; corner < 4; corner++)
                {
                    var u = corner == 1 || corner == 2 ? 1 : 0;
                    var v = corner >= 2 ? 1 : 0;
                    var at = i * 4 + corner;
                    vertices[at] = centre;
                    uv[at] = new Vector2(u, v);
                    offsets[at] = new Vector2((u - 0.5f) * width, (v - 0.5f) * height);
                    colors[at] = color;
                }
            }
            FillCloudTriangles();

            cloudMesh = owned.Add(new Mesh { name = "Clouds", indexFormat = IndexFormat.UInt32 });
            cloudMesh.vertices = vertices;
            cloudMesh.uv = uv;
            cloudMesh.SetUVs(1, offsets);
            cloudMesh.colors = colors;
            cloudMesh.triangles = cloudTriangles;
            cloudMesh.RecalculateBounds();
            // The corners are spread in the shader, so the mesh is bigger than its vertices say.
            var bounds = cloudMesh.bounds;
            bounds.Expand(1300);
            cloudMesh.bounds = bounds;

            var texture = owned.Add(new Texture2D(128, 128, TextureFormat.RGBA32, true, false)
            {
                name = "Cloud",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            });
            texture.SetPixels32(CloudPixels());
            texture.Apply();
            var material = owned.Add(new Material(Resources.Load<Shader>("Shaders/Cloud")));
            material.SetTexture("_BaseMap", texture);
            cloudTransform = Three.Part("Clouds", transform, cloudMesh, material, Vector3.zero).transform;
            nextCloudSort = 0;
        }

        void FillCloudTriangles()
        {
            for (var i = 0; i < cloudOrder.Length; i++)
            {
                var first = cloudOrder[i] * 4;
                var t = i * 6;
                cloudTriangles[t] = first;
                cloudTriangles[t + 1] = first + 1;
                cloudTriangles[t + 2] = first + 2;
                cloudTriangles[t + 3] = first;
                cloudTriangles[t + 4] = first + 2;
                cloudTriangles[t + 5] = first + 3;
            }
        }

        void SortClouds()
        {
            var camera = Camera.main;
            if (cloudTransform == null || camera == null || Time.unscaledTime < nextCloudSort) return;
            nextCloudSort = Time.unscaledTime + CLOUD_SORT_INTERVAL_S;
            var eye = camera.transform.position;
            for (var i = 0; i < cloudOrder.Length; i++)
                cloudDistances[i] = -(cloudCentres[cloudOrder[i]] - eye).sqrMagnitude; // farthest first
            Array.Sort(cloudDistances, cloudOrder);
            FillCloudTriangles();
            cloudMesh.SetTriangles(cloudTriangles, 0, false);
        }
    }
}
